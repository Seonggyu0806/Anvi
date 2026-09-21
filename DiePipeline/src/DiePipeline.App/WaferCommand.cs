using System.Diagnostics;
using System.Text;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using OpenCvSharp;

namespace DiePipeline.App;

/// <summary>
/// 웨이퍼 한 장을 통째로 돌린다 — 격자를 만들고, die 마다 레시피를 돌리고,
/// 결함을 웨이퍼 좌표로 모아 보여준다.
///
/// <para>★ 지금까지 만든 조각이 <b>처음으로 한 줄로 이어지는 자리</b>다.
/// <see cref="WaferRunner"/> 는 이미지를 안 만지게 만들어 뒀다 — 자리만 계산하고
/// <b>바깥에서 받은 검사 함수</b>에 넘긴다. 테스트에서 가짜가 들어갔던 그 자리를
/// 여기서 <b>진짜 주방</b>(레시피 실행)으로 채운다.</para>
///
/// <para>★ 원본은 <b>통째로 읽지 않는다.</b> die 하나를 볼 때 그 칸과 이웃 칸만 뜯어 온다.
/// 수백 MB 원본이어도 한 번에 메모리에 올라가는 건 몇 MB 뿐이다.</para>
/// </summary>
public static class WaferCommand
{
    public static int Run(string[] argv)
    {
        Args options = Args.Parse(argv);

        NodeRegistry registry = CvBackend.CreateRegistry();

        // ★ 레시피는 die 마다 다시 만들지 않고 한 번만 컴파일한다.
        //   노드는 파라미터만 들고 결과는 칠판에 놓는다 — 상태가 없으니 N die 에 그대로 재사용된다.
        //   die 가 수백 개면 이 차이가 그대로 드러난다.
        Recipe e1Recipe = Load(options.E1Path, registry);
        IReadOnlyList<IPipelineNode> e1Nodes = registry.CreateAll(e1Recipe);

        IReadOnlyList<IPipelineNode>? e0Nodes = null;

        if (options.E0Path is not null)
        {
            e0Nodes = registry.CreateAll(Load(options.E0Path, registry));
        }

        // ★ 레시피가 골든 몇 장을 전제하는지 읽어낸다 — 사람이 명령줄에서 맞춰 넣지 않아도 되게.
        //   이웃 고르는 법(명령행)과 합치는 법(레시피)은 짝인데 정하는 자리가 떨어져 있다.
        GoldenContract.Requirement need = GoldenContract.Of(e1Recipe);

        using IImageSource source = ImageSource.Open(options.ImagePath);

        DieGrid grid = DieGridBuilder.Build(options.Spec);

        PrintHeader(options, source, grid, need);

        WaferRunOptions runOptions = new()
        {
            Strategy = options.Strategy,
            GoldenWanted = options.GoldenWanted,
            ImageWidth = source.Width,
            ImageHeight = source.Height,
            MinimumGoldens = need.Minimum,
        };

        Stopwatch clock = Stopwatch.StartNew();

        WaferRunResult run = WaferRunner.Run(
            grid,
            (roi, goldenRois) => Inspect(source, e1Nodes, roi, goldenRois),
            e0Nodes is null ? null : roi => Inspect(source, e0Nodes, roi, []),
            runOptions);

        clock.Stop();

        // 모으기 → 좌표 변환. 정렬은 아직 안 붙었으니 항등 정렬이다.
        WaferDefectList found = AbsoluteTransform.Transform(
            DefectCollector.Collect(run.Dies), grid, WaferAlignment.Identity);

        // 샘플링은 맨 끝이다 — 검사를 덜 하는 게 아니라 다 하고 결과만 추린다.
        WaferDefectList defects = WaferSampler.Sample(found, options.Sampling);

        PrintMap(grid, run);
        PrintDefects(defects);
        PrintSummary(run, found, defects, clock.Elapsed.TotalMilliseconds);

        // ★ 실패가 있으면 결함 유무보다 먼저 알린다.
        //   "결함 0개" 로 보이는데 사실은 안 본 것 — 그게 제일 위험한 상태다.
        //
        // ★★ 끝값은 샘플링 "전"(found) 기준이다.
        //   끝값은 사람이 아니라 <b>스크립트</b>가 읽는다. 샘플링은 화면에 몇 개를 띄울지일 뿐인데,
        //   그걸로 끝값을 정하면 "--sample-count 10 으로 잘랐더니 결함 5000개짜리 웨이퍼가
        //   통과로 찍히는" 일이 생긴다. 화면은 줄여도 <b>판정은 못 줄인다.</b>
        return run.Failures.Any() ? 4
            : found.Count == 0 ? 0
            : 3;
    }

    /// <summary>
    /// die 한 칸을 실제로 읽어 레시피를 돌린다 — <b>러너가 부르는 "검사 함수"</b>다.
    ///
    /// <para>★ 우리가 읽은 <see cref="Mat"/> 은 우리가 놓는다.
    /// <c>SetInput</c> 은 소유권을 안 가져가고, <c>ReleaseAll</c> 은 노드가 칠판에 올린 것만 놓는다.
    /// 이렇게 나눈 덕에 <b>같은 원본을 다음 die 에서 또 쓸 수 있다.</b></para>
    /// </summary>
    private static DefectList Inspect(
        IImageSource source,
        IReadOnlyList<IPipelineNode> nodes,
        Roi roi,
        IReadOnlyList<Roi> goldenRois)
    {
        // 이 칸만 파일에서 뜯어 온다. 나머지 수백 MB 는 건드리지 않는다.
        using Mat test = source.ReadRegion(roi);

        List<Mat> goldens = new(goldenRois.Count);

        try
        {
            foreach (Roi golden in goldenRois)
            {
                goldens.Add(source.ReadRegion(golden));
            }

            PipelineContext context = new();
            context.SetInput(ReservedContext.Roi, test);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)goldens);

            try
            {
                return new PipelineEngine().Run(nodes, context).Defects;
            }
            finally
            {
                // 한 die = 한 번 정리. 안 하면 die 수만큼 쌓인다.
                context.ReleaseAll();
            }
        }
        finally
        {
            foreach (Mat golden in goldens)
            {
                golden.Dispose();
            }
        }
    }

    /// <summary>
    /// 레시피를 읽고 검사까지 마쳐 돌려준다.
    ///
    /// ★ 노드를 여기서 안 만드는 이유 — 부르는 쪽이 <b>레시피 자체</b>를 봐야 한다.
    ///   골든을 몇 장 요구하는지(<see cref="GoldenContract"/>)는 레시피에서 읽어내기 때문이다.
    /// </summary>
    private static Recipe Load(string path, NodeRegistry registry)
    {
        Recipe recipe = RecipeLoader.Parse(File.ReadAllText(path));

        // ★ 돌리기 전에 통째로 검사한다.
        //   die 30개를 돈 뒤에 "레시피가 틀렸다" 를 알면 30번 헛돈 것이다.
        RecipeValidator.EnsureValid(recipe, registry);

        return recipe;
    }

    private static void PrintHeader(
        Args options, IImageSource source, DieGrid grid, GoldenContract.Requirement need)
    {
        int e0 = grid.Dies.Count(d => d.Zone == WaferZone.E0);

        Console.WriteLine($"이미지  {options.ImagePath}");
        Console.WriteLine($"        {source.Width}×{source.Height}");
        Console.WriteLine($"격자    {grid.Cols}×{grid.Rows} = {grid.Count} die · "
                          + $"피치 {grid.Pitch.X:F0}×{grid.Pitch.Y:F0} · die {grid.DieWidth}×{grid.DieHeight}");
        Console.WriteLine($"존      E1 {grid.Count - e0}개 · E0 {e0}개");
        Console.WriteLine($"레시피  E1  {options.E1Path}");
        Console.WriteLine($"        E0  {options.E0Path ?? "(없음 — E0 die 가 있으면 멈춘다)"}");
        Console.WriteLine($"이웃    {Describe(options.Strategy)}"
                          + (options.Strategy == NeighborStrategy.Nearest
                              ? $" · 최대 {options.GoldenWanted}장"
                              : string.Empty));
        Console.WriteLine($"골든    최소 {need.Minimum}장 — {need.Reason}");
        Console.WriteLine($"샘플링  {Describe(options.Sampling)}");

        if (need.Warning is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ {need.Warning}");
        }

        Console.WriteLine();
    }

    /// <summary>웨이퍼 맵 — 한 칸이 die 하나다. 어디가 몰려 있는지는 <b>지도로 봐야</b> 보인다.</summary>
    private static void PrintMap(DieGrid grid, WaferRunResult run)
    {
        Console.WriteLine("웨이퍼 맵   숫자=결함 수 · `.`=깨끗함 · `-`=E0 · `×`=검사 실패");
        Console.WriteLine();

        for (int row = 0; row < grid.Rows; row++)
        {
            StringBuilder line = new($"  r{row,-3}");

            for (int col = 0; col < grid.Cols; col++)
            {
                // 러너가 row-major 순서를 보장한다 — 그래서 이 계산이 성립한다.
                DieInspection die = run.Dies[(row * grid.Cols) + col];

                string cell =
                    die.Failed ? "×"
                    : die.Defects.Count > 0 ? die.Defects.Count.ToString()
                    : die.Die.Zone == WaferZone.E0 ? "-"
                    : ".";

                line.Append(cell.PadLeft(3));
            }

            Console.WriteLine(line);
        }

        Console.WriteLine();
    }

    private static void PrintDefects(WaferDefectList defects)
    {
        if (defects.Count == 0)
        {
            return;
        }

        Console.WriteLine($"결함 {defects.Count}개");

        foreach (WaferDefect defect in defects.Items)
        {
            Console.WriteLine(
                $"  die(c{defect.DieCol},r{defect.DieRow}) {defect.Zone}  "
                + $"#{defect.Defect.Id,-3} 면적 {defect.Defect.Area,-6} "
                + $"웨이퍼 ({defect.AbsX:F1},{defect.AbsY:F1})  die 안 {defect.Defect.Centroid}");
        }

        Console.WriteLine();
    }

    /// <param name="found">샘플링 <b>전</b>. 실제로 찾아낸 전부.</param>
    /// <param name="defects">샘플링 <b>후</b>. 화면에 보여준 것.</param>
    private static void PrintSummary(
        WaferRunResult run, WaferDefectList found, WaferDefectList defects, double milliseconds)
    {
        // ★ 샘플링으로 몇 개를 덜어냈는지 반드시 말한다.
        //   조용히 줄이면 "결함이 이것뿐" 으로 읽힌다 — 이 프로젝트에서 제일 위험한 모양이다.
        if (defects.Count != found.Count)
        {
            Console.WriteLine($"샘플링  {found.Count}개 중 {defects.Count}개만 남겼습니다 "
                              + $"({found.Count - defects.Count}개는 화면에서 뺀 것이지 없는 게 아닙니다)");
            Console.WriteLine("        ↑ 위 웨이퍼 맵은 샘플링 전 기준입니다");
            Console.WriteLine();
        }

        Console.WriteLine($"die {run.DieCount}개 · 결함 {defects.Count}개 "
                          + $"(E1 {defects.E1.Count()} · E0 {defects.E0.Count()})");

        int[] goldens =
        [
            .. run.Dies.Where(d => !d.Failed && d.Die.Zone == WaferZone.E1).Select(d => d.GoldenCount)
        ];

        if (goldens.Length > 0)
        {
            Console.WriteLine($"골든    {goldens.Min()}~{goldens.Max()}장"
                + (goldens.Min() == goldens.Max() ? string.Empty : "   ← 끝 열은 이웃이 모자라 적다"));
        }

        Console.WriteLine($"시간    {milliseconds:F0} ms · die 하나 평균 "
                          + $"{milliseconds / Math.Max(1, run.DieCount):F1} ms");

        DieInspection[] failures = [.. run.Failures];

        if (failures.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ 검사가 터진 die {failures.Length}개 — 결함 0개로 보이지만 안 본 것입니다");

            foreach (DieInspection failure in failures)
            {
                Console.WriteLine($"  die(c{failure.Die.Col},r{failure.Die.Row})  {failure.Failure}");
            }
        }
    }

    private static string Describe(NeighborStrategy strategy) => strategy switch
    {
        NeighborStrategy.SameRow1 => "같은 행 좌우 1칸",
        NeighborStrategy.Nearest => "가까운 순서 (좌·우·상·하)",
        _ => strategy.ToString(),
    };

    private static string Describe(SamplingOptions sampling) => sampling.Mode switch
    {
        SamplingMode.All => "안 함 — 찾은 것 전부",
        SamplingMode.BigSize => $"면적 큰 순서로 {sampling.Count}개",
        SamplingMode.Range => $"면적 {sampling.MinArea}~"
                              + (sampling.MaxArea == int.MaxValue ? "제한없음" : $"{sampling.MaxArea}")
                              + " 인 것만",
        SamplingMode.Random => $"무작위 {sampling.Count}개 · 씨 {sampling.Seed}",
        _ => sampling.Mode.ToString(),
    };

    private sealed record Args
    {
        public required string ImagePath { get; init; }

        public required string E1Path { get; init; }

        public string? E0Path { get; init; }

        public required WaferSpec Spec { get; init; }

        public NeighborStrategy Strategy { get; init; } = NeighborStrategy.SameRow1;

        public int GoldenWanted { get; init; } = 3;

        /// <summary>기본은 <see cref="SamplingMode.All"/> — 안 시키면 아무것도 안 덜어낸다.</summary>
        public SamplingOptions Sampling { get; init; } = new();

        public static Args Parse(string[] args)
        {
            string? image = null;
            string? e1 = null;
            string? e0 = null;
            int cols = 4;
            int rows = 4;
            int pitchX = 100;
            int pitchY = 100;
            int dieWidth = 0;
            int dieHeight = 0;
            int originX = 0;
            int originY = 0;
            int edgeRings = 0;
            NeighborStrategy strategy = NeighborStrategy.SameRow1;
            int goldens = 3;
            SamplingMode sampleMode = SamplingMode.All;
            int sampleCount = 200;
            int sampleSeed = 1;
            int sampleMin = 0;
            int sampleMax = int.MaxValue;

            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];

                switch (flag)
                {
                    case "-i" or "--image": image = Next(args, ref i, flag); break;
                    case "--e1": e1 = Next(args, ref i, flag); break;
                    case "--e0": e0 = Next(args, ref i, flag); break;
                    case "--goldens": goldens = Number(Next(args, ref i, flag), flag); break;
                    case "--edge-rings": edgeRings = Number(Next(args, ref i, flag), flag); break;
                    case "--neighbors": strategy = ParseStrategy(Next(args, ref i, flag)); break;
                    case "--sample": sampleMode = ParseSampling(Next(args, ref i, flag)); break;
                    case "--sample-count": sampleCount = Number(Next(args, ref i, flag), flag); break;
                    case "--sample-seed": sampleSeed = Number(Next(args, ref i, flag), flag); break;

                    case "--sample-area":
                        // ★ 여기만 값 하나를 안 받는다. "--sample-area 500" 이 "500 이상" 인지
                        //   "딱 500" 인지 읽는 사람마다 다르게 읽힌다. 둘 다 적게 한다.
                        string[] band = Next(args, ref i, flag).Split(',');

                        if (band.Length != 2)
                        {
                            throw new ArgumentException("--sample-area 는 최소,최대 형식이어야 합니다 (예: 500,5000)");
                        }

                        sampleMin = Number(band[0], flag);
                        sampleMax = Number(band[1], flag);
                        break;

                    case "--grid":
                        (cols, rows) = Pair(Next(args, ref i, flag), flag, "--grid 는 열,행 형식이어야 합니다 (예: 4,3)");
                        break;

                    case "--pitch":
                        (pitchX, pitchY) = Pair(Next(args, ref i, flag), flag, "--pitch 는 n 또는 x,y 형식이어야 합니다");
                        break;

                    case "--die":
                        (dieWidth, dieHeight) = Pair(Next(args, ref i, flag), flag, "--die 는 n 또는 w,h 형식이어야 합니다");
                        break;

                    case "--origin":
                        (originX, originY) = Pair(Next(args, ref i, flag), flag, "--origin 은 x,y 형식이어야 합니다");
                        break;

                    default:
                        throw new ArgumentException($"모르는 옵션입니다: {flag}");
                }
            }

            if (image is null)
            {
                throw new ArgumentException("-i <이미지> 는 반드시 있어야 합니다");
            }

            if (e1 is null)
            {
                throw new ArgumentException("--e1 <레시피> 는 반드시 있어야 합니다 — E1 die 를 검사할 레시피입니다");
            }

            return new Args
            {
                ImagePath = image,
                E1Path = e1,
                E0Path = e0,
                Strategy = strategy,
                GoldenWanted = goldens,
                Sampling = new SamplingOptions
                {
                    Mode = sampleMode,
                    Count = sampleCount,
                    Seed = sampleSeed,
                    MinArea = sampleMin,
                    MaxArea = sampleMax,
                },
                Spec = new WaferSpec
                {
                    Cols = cols,
                    Rows = rows,
                    PitchX = pitchX,
                    PitchY = pitchY,
                    DieWidth = dieWidth,
                    DieHeight = dieHeight,
                    OriginX = originX,
                    OriginY = originY,
                    EdgeRings = edgeRings,
                },
            };
        }

        /// <summary>한 개만 주면 가로·세로 같은 값으로 본다 — 정사각 die 가 흔하다.</summary>
        private static (int First, int Second) Pair(string text, string flag, string message)
        {
            string[] parts = text.Split(',');

            if (parts.Length is not (1 or 2))
            {
                throw new ArgumentException(message);
            }

            int first = Number(parts[0], flag);

            return (first, parts.Length == 2 ? Number(parts[1], flag) : first);
        }

        /// <summary>
        /// 숫자로 읽는다. 실패하면 <b>어느 옵션의 어떤 값</b>이었는지 말해 준다.
        ///
        /// ★ <c>int.Parse</c> 그대로 쓰면 "'100--sample' was not in a correct format" 만 나온다.
        ///   어느 옵션인지도, 왜인지도 모른다. 실제로 <b>띄어쓰기 하나가 빠져서</b> 난 오류였다 —
        ///   <c>--pitch 100--sample</c> 이 한 덩어리로 들어왔다.
        ///   그 가능성을 메시지가 먼저 말해 주면 1초 만에 잡힌다.
        /// </summary>
        private static int Number(string text, string flag)
        {
            if (int.TryParse(text, out int value))
            {
                return value;
            }

            string hint = text.Contains("--", StringComparison.Ordinal)
                ? " — 값과 다음 옵션 사이에 띄어쓰기가 빠진 것 같습니다"
                : string.Empty;

            throw new ArgumentException($"{flag} 의 값이 숫자가 아닙니다: '{text}'{hint}");
        }

        private static SamplingMode ParseSampling(string text) => text switch
        {
            "none" or "all" => SamplingMode.All,
            "bigsize" or "big" => SamplingMode.BigSize,
            "range" => SamplingMode.Range,
            "random" => SamplingMode.Random,
            _ => throw new ArgumentException(
                $"--sample 은 none | bigsize | range | random 이어야 합니다 (받은 값: {text})"),
        };

        private static NeighborStrategy ParseStrategy(string text) => text switch
        {
            "same-row1" or "samerow1" => NeighborStrategy.SameRow1,
            "nearest" => NeighborStrategy.Nearest,
            _ => throw new ArgumentException(
                $"--neighbors 는 same-row1 또는 nearest 여야 합니다 (받은 값: {text})"),
        };

        private static string Next(string[] args, ref int i, string flag)
            => ++i < args.Length ? args[i] : throw new ArgumentException($"{flag} 다음에 값이 없습니다");
    }
}