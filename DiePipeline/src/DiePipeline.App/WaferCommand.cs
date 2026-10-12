using System.Text;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using OpenCvSharp;

namespace DiePipeline.App;

/// <summary>
/// 웨이퍼 한 장을 통째로 돌리는 <b>명령</b> — 옵션을 읽고, 결과를 글자로 보여 주고, 끝값을 정한다.
///
/// <para>★ 검사 흐름(정렬 → 격자 → die 마다 레시피 → 모으기)은 <see cref="WaferInspection"/>이 한다.
/// 5주차에 여기서 꺼냈다 — 화면도 <b>같은 흐름</b>을 써야 해서다. 정렬을 못 믿을 때 멈추는 가드가
/// 명령행에만 남으면, 화면에서 "안 봤는데 깨끗해 보이는" 웨이퍼가 생긴다.</para>
///
/// <para>★ 원본은 <b>통째로 읽지 않는다.</b> die 하나를 볼 때 그 칸과 이웃 칸만 뜯어 온다.</para>
/// </summary>
public static class WaferCommand
{
    public static int Run(string[] argv)
    {
        Args options = Args.Parse(argv);

        using IImageSource source = ImageSource.Open(options.ImagePath);

        // ★ 입구는 둘(옵션 · 레시피 파일), 속은 하나 — 정렬 가드 · 격자 · die 반복은 WaferInspection 한 곳이다.
        return options.RecipePath is null ? ByOptions(options, source) : ByRecipe(options.ImagePath, options.RecipePath, source);
    }

    /// <summary>옵션으로 — 격자 · 표식 · 원판을 명령줄에 적는다.</summary>
    private static int ByOptions(Args options, IImageSource source)
    {
        NodeRegistry registry = CvBackend.CreateRegistry();
        WaferSpec spec = options.Spec!;

        Recipe e1Recipe = Load(options.E1Path!, registry);
        Recipe? e0Recipe = options.E0Path is null ? null : Load(options.E0Path, registry);

        // 표식 조각은 우리가 읽고 우리가 놓는다 — WaferInspection 은 빌려 쓰기만 한다.
        using Mat? template = options.MarkPath is null ? null : ImageFile.LoadGrayF32(options.MarkPath);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = e1Recipe,
                E0 = e0Recipe,
                Spec = spec,
                Alignment = template is null
                    ? null
                    : new MarkAlignment
                    {
                        Template = template,
                        Expected = options.Expected,
                        Options = options.Align,
                        SearchPadding = options.MarkPadding,
                    },
                Strategy = options.Strategy,
                GoldenWanted = options.GoldenWanted,
                Sampling = options.Sampling,
            },
            source,
            registry);

        Header header = new()
        {
            ImagePath = options.ImagePath,
            Spec = spec,
            E1Path = options.E1Path!,
            E0Path = options.E0Path,
            Strategy = options.Strategy,
            GoldenWanted = options.GoldenWanted,
            Sampling = Describe(options.Sampling),
        };

        return Report(result, source, header, template is null ? null : $"{options.MarkPath}  ({template.Cols}×{template.Rows})");
    }

    /// <summary>
    /// 웨이퍼 레시피 파일로 — 격자 · 표식 · 원판 · 수동 지정이 파일 하나에 있다.
    /// ★ 안에 적힌 경로는 레시피 파일이 있는 폴더 기준이다(<see cref="WaferRecipeRunner.PathFrom"/>).
    /// </summary>
    private static int ByRecipe(string imagePath, string recipePath, IImageSource source)
    {
        WaferInspectionResult result = WaferRecipeRunner.RunFile(recipePath, source, WaferBackend.CreateRegistry());

        // 머리말에 찍을 값 — 실행기가 읽은 것과 같은 함수로 같은 블록을 읽는다. 돌았으면 InspectDies 는 정확히 1개다.
        Recipe recipe = RecipeLoader.Load(recipePath);
        NodeConfig inspect = recipe.Pipeline.Nodes.Single(n => n.Enabled && n.Type == "InspectDies");
        InspectDiesSettings settings = InspectDiesNodeFactory.Read(inspect);
        NodeConfig[] tail = [.. recipe.Pipeline.Nodes.SkipWhile(n => n != inspect).Skip(1).Where(n => n.Enabled)];

        Header header = new()
        {
            RecipePath = recipePath,
            Blocks = string.Join(" → ", recipe.Pipeline.Nodes.Where(n => n.Enabled).Select(n => $"{n.Id}({n.Type})")),
            ImagePath = imagePath,
            Spec = settings.Spec,
            E1Path = WaferRecipeRunner.PathFrom(recipePath, settings.E1Recipe),
            E0Path = settings.E0Recipe is null ? null : WaferRecipeRunner.PathFrom(recipePath, settings.E0Recipe),
            Strategy = settings.Neighbors,
            GoldenWanted = settings.GoldenWanted,
            Sampling = tail.Length == 0
                ? "안 함 — 뒤 토막이 없어 좌표 변환만 했다"
                : $"레시피 뒤 토막이 한다 — {string.Join(" → ", tail.Select(n => n.Id))}",
        };

        string? mark = settings.Features.Count == 0 || !inspect.Inputs.ContainsKey("alignment")
            ? null
            : WaferRecipeRunner.PathFrom(recipePath, settings.Features[0]);

        return Report(result, source, header, mark);
    }

    /// <summary>
    /// 결과를 글자로 보여 주고 끝값을 정한다 — 두 입구가 같이 쓴다.
    /// </summary>
    /// <param name="mark">찾은 표식 조각(사람이 읽는 한 줄). 정렬을 안 했으면 null.</param>
    private static int Report(WaferInspectionResult result, IImageSource source, Header header, string? mark)
    {
        if (result.Solved is { } solved)
        {
            PrintMarks(mark ?? "(조각)", result.Marks!);
            PrintAlignment(solved);
        }

        // ★★ 못 믿겠으면 검사를 안 했다 — 까닭은 WaferInspection 이 사람 말로 적어 준다.
        //   화면도 같은 문장을 띄운다.
        if (result.Stopped)
        {
            Console.Error.WriteLine(result.StopReason);
            return 5;
        }

        DieGrid grid = result.Grid!;
        WaferRunResult run = result.Run!;
        WaferDefectList found = result.Found!;
        WaferDefectList defects = result.Defects!;

        PrintHeader(header, source, grid, result.Need);

        // ★ 격자가 사진 일부만 덮으면 맵보다 먼저 말한다 — 귀퉁이만 보고 '깨끗함' 이 나올 수 있다.
        if (result.CoverageWarning is not null)
        {
            Console.WriteLine($"⚠ {result.CoverageWarning}");
            Console.WriteLine();
        }

        // ★ 원판인데 E0 레시피가 원판 밖을 안 가린다 — 걸친 die 의 배경이 결함으로 잡힐 수 있다.
        if (result.EdgeWarning is not null)
        {
            Console.WriteLine($"⚠ {result.EdgeWarning}");
            Console.WriteLine();
        }

        PrintMap(grid, run);
        PrintDefects(defects);
        PrintSummary(run, found, defects, result.InspectMs);

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

    private static void PrintMarks(string label, MatchList marks)
    {
        Console.WriteLine($"표식    {label} · 사진에서 {marks.Count}군데 찾음");

        foreach (Match mark in marks.Items)
        {
            Console.WriteLine($"        ({mark.Location.X:F2},{mark.Location.Y:F2})  점수 {mark.Score:F4}");
        }
    }

    private static void PrintAlignment(AlignmentResult solved)
    {
        WaferAlignment placed = solved.Alignment;

        Console.WriteLine($"정렬    각도 {placed.RotationDeg:F4}° · 배율 {placed.Scale:F4} · "
                          + $"die(0,0) 자리 ({placed.Origin.X:F1},{placed.Origin.Y:F1})");
        Console.WriteLine($"        짝 {solved.PairCount}개 · 어긋남 {solved.ResidualPx:F2}px · "
                          + $"배율추정 {solved.ImpliedScale:F4} · 점수 {placed.MeanScore:F3}");

        if (!solved.Accepted)
        {
            Console.WriteLine($"        ✗ {solved.Rejection}");
        }

        Console.WriteLine();
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
        Header header, IImageSource source, DieGrid grid, GoldenContract.Requirement need)
    {
        int e0 = grid.Dies.Count(d => d.Zone == WaferZone.E0);
        int off = grid.Dies.Count(d => d.Zone == WaferZone.Outside);
        int excluded = header.Spec.ZoneOverrides.Count(z => z.Zone == WaferZone.Outside);

        // ★ 레시피로 돌렸으면 무엇으로 돌았는지가 맨 위 — 같은 사진이라도 레시피가 다르면 다른 검사다.
        if (header.RecipePath is not null)
        {
            Console.WriteLine($"파일    {header.RecipePath}  (웨이퍼 레시피)");
            Console.WriteLine($"블록    {header.Blocks}");
        }

        Console.WriteLine($"이미지  {header.ImagePath}");
        Console.WriteLine($"        {source.Width}×{source.Height}");
        Console.WriteLine($"격자    {grid.Cols}×{grid.Rows} = "
                          + (off > 0 ? $"{grid.Count}칸 (die {grid.Count - off}) · " : $"{grid.Count} die · ")
                          + $"피치 {grid.Pitch.X:F0}×{grid.Pitch.Y:F0} · die {grid.DieWidth}×{grid.DieHeight}");
        Console.WriteLine($"존      E1 {grid.Count - e0 - off}개 · E0 {e0}개"
                          + (off - excluded > 0 ? $" · 원판 밖 {off - excluded}칸(die 아님 — 검사 안 함)" : string.Empty)
                          + (excluded > 0 ? $" · 수동 제외 {excluded}칸(검사 안 함)" : string.Empty));

        // ★ 사람이 덮어쓴 die 는 결과보다 먼저 보인다 — 특히 제외는 die 전체가 빠진다.
        if (header.Spec.ZoneOverrides.Count > 0)
        {
            Console.WriteLine("수동    " + string.Join(" · ", header.Spec.ZoneOverrides.Select(WaferInputText.Describe))
                + "   ← 자동 판정을 사람이 덮어씀");
        }

        if (header.Spec.Circle is { } circle)
        {
            Console.WriteLine($"원판    중심 ({circle.CenterX:F1},{circle.CenterY:F1}) · 반지름 {circle.Radius:F1}   ← 사진 좌표");

            if (header.Spec.EdgeExclusion > 0)
            {
                Console.WriteLine($"        가장자리 여유 {header.Spec.EdgeExclusion:F1}px — "
                                  + $"반지름 {header.Spec.InspectedCircle!.Radius:F1} 안쪽만 검사");
            }
        }
        Console.WriteLine($"레시피  E1  {header.E1Path}");
        Console.WriteLine($"        E0  {header.E0Path ?? "(없음 — E0 die 가 있으면 멈춘다)"}");
        Console.WriteLine($"이웃    {Describe(header.Strategy)}"
                          + (header.Strategy == NeighborStrategy.Nearest
                              ? $" · 최대 {header.GoldenWanted}장"
                              : string.Empty));
        Console.WriteLine($"골든    최소 {need.Minimum}장 — {need.Reason}");
        Console.WriteLine($"샘플링  {header.Sampling}");

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
        Console.WriteLine("웨이퍼 맵   숫자=결함 수 · `.`=깨끗함 · `-`=E0 · `×`=검사 실패 · 빈칸=원판 밖·수동 제외");
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
                    : die.OffWafer ? " "
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

    /// <summary>
    /// 결과 머리말에 찍을 것 — 옵션으로 돌렸든 레시피로 돌렸든 같은 모양으로 모은다.
    /// ★ 레시피로 돌렸으면 경로는 <b>레시피 기준으로 푼 실제 경로</b>다 — 무엇을 읽었는지가 보여야 한다.
    /// </summary>
    private sealed record Header
    {
        /// <summary>레시피로 돌렸을 때만.</summary>
        public string? RecipePath { get; init; }

        /// <summary>돈 블록 — <c>id(종류) → …</c>. 레시피로 돌렸을 때만.</summary>
        public string? Blocks { get; init; }

        public required string ImagePath { get; init; }

        public required WaferSpec Spec { get; init; }

        public required string E1Path { get; init; }

        public string? E0Path { get; init; }

        public NeighborStrategy Strategy { get; init; }

        public int GoldenWanted { get; init; }

        /// <summary>결과를 어떻게 추렸나 — 사람이 읽는 한 줄.</summary>
        public required string Sampling { get; init; }
    }

    private sealed record Args
    {
        public required string ImagePath { get; init; }

        /// <summary>웨이퍼 레시피 파일. 주면 다른 웨이퍼 옵션은 못 쓴다 — 값은 전부 파일에서 온다.</summary>
        public string? RecipePath { get; init; }

        /// <summary>옵션으로 돌릴 때 필수. 레시피로 돌리면 null.</summary>
        public string? E1Path { get; init; }

        public string? E0Path { get; init; }

        /// <summary>옵션으로 돌릴 때만. 레시피로 돌리면 null — 격자는 레시피의 InspectDies 에 있다.</summary>
        public WaferSpec? Spec { get; init; }

        public NeighborStrategy Strategy { get; init; } = NeighborStrategy.SameRow1;

        public int GoldenWanted { get; init; } = 3;

        /// <summary>기본은 <see cref="SamplingMode.All"/> — 안 시키면 아무것도 안 덜어낸다.</summary>
        public SamplingOptions Sampling { get; init; } = new();

        /// <summary>정렬 표식 조각. <c>null</c> 이면 정렬을 안 한다(반듯하게 놓였다고 본다).</summary>
        public string? MarkPath { get; init; }

        /// <summary>
        /// 표식들이 <b>설계상</b> 어디에 있어야 하는가.
        ///
        /// ★ <b>die(0,0) 을 (0,0) 으로 본 좌표</b>다. 사진 좌표가 아니다 —
        ///   사진 좌표는 웨이퍼가 어떻게 놓였느냐에 따라 매번 달라지니 설계값이 될 수 없다.
        ///   이 규약 덕에 풀어낸 원점이 곧 "die(0,0) 이 실제로 놓인 자리" 가 된다.
        /// </summary>
        public IReadOnlyList<PointF> Expected { get; init; } = [];

        /// <summary>표식을 찾을 창의 여유(px). <c>null</c> 이면 사진 전체를 뒤진다.</summary>
        public int? MarkPadding { get; init; }

        public AlignmentOptions Align { get; init; } = new();

        public static Args Parse(string[] args)
        {
            string? image = null;
            string? recipe = null;
            List<string> used = [];
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
            WaferCircle? circle = null;
            List<ZoneOverride> zones = [];
            double edgeExclusion = 0;
            NeighborStrategy strategy = NeighborStrategy.SameRow1;
            int goldens = 3;
            string? markPath = null;
            List<PointF> expected = [];
            int? markPadding = null;
            double alignMinScore = new AlignmentOptions().MinScore;
            double alignMaxAngle = new AlignmentOptions().MaxAngleDeg;
            SamplingMode sampleMode = SamplingMode.All;
            int sampleCount = 200;
            int sampleSeed = 1;
            int sampleMin = 0;
            int sampleMax = int.MaxValue;

            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];

                // 사진 · 레시피 말고 무엇을 줬나 — 레시피와 섞였는지 보려고 적어 둔다.
                if (flag is not ("-i" or "--image" or "-r" or "--recipe"))
                {
                    used.Add(flag);
                }

                switch (flag)
                {
                    case "-i" or "--image": image = Next(args, ref i, flag); break;
                    case "-r" or "--recipe": recipe = Next(args, ref i, flag); break;
                    case "--e1": e1 = Next(args, ref i, flag); break;
                    case "--e0": e0 = Next(args, ref i, flag); break;
                    case "--goldens": goldens = Number(Next(args, ref i, flag), flag); break;
                    case "--edge-rings": edgeRings = Number(Next(args, ref i, flag), flag); break;
                    case "--circle": circle = Circle(Next(args, ref i, flag), flag); break;
                    case "--zone": zones.Add(Text(flag, Next(args, ref i, flag), WaferInputText.Zone)); break;
                    case "--edge-exclusion": edgeExclusion = Decimal(Next(args, ref i, flag), flag); break;
                    case "--neighbors": strategy = ParseStrategy(Next(args, ref i, flag)); break;
                    case "--mark": markPath = Next(args, ref i, flag); break;
                    case "--mark-padding": markPadding = Number(Next(args, ref i, flag), flag); break;
                    case "--align-min-score": alignMinScore = Decimal(Next(args, ref i, flag), flag); break;
                    case "--align-max-angle": alignMaxAngle = Decimal(Next(args, ref i, flag), flag); break;

                    case "--expect": expected.Add(Text(flag, Next(args, ref i, flag), WaferInputText.Point)); break;

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

            // ★ 레시피 파일과 옵션을 섞으면 어느 값이 쓰였는지 결과에서 안 보인다 — 값은 한 곳에서만 온다.
            if (recipe is not null)
            {
                if (used.Count > 0)
                {
                    throw new ArgumentException(
                        $"--recipe 와 {string.Join(", ", used.Distinct())} 를 같이 쓸 수 없습니다 — "
                        + "값은 레시피 파일에서만 옵니다. 바꾸려면 레시피 파일을 고치세요");
                }

                return new Args { ImagePath = image, RecipePath = recipe };
            }

            if (e1 is null)
            {
                throw new ArgumentException(
                    "--e1 <레시피> 는 반드시 있어야 합니다 — E1 die 를 검사할 레시피입니다 (또는 --recipe <웨이퍼레시피>)");
            }

            if (markPath is not null && expected.Count < 2)
            {
                throw new ArgumentException(
                    $"--mark 을 줬으면 --expect 를 2개 이상 줘야 합니다 (현재 {expected.Count}개) — "
                    + "표식 하나로는 얼마나 밀렸는지만 알고 얼마나 돌았는지를 모릅니다");
            }

            if (markPath is null && expected.Count > 0)
            {
                throw new ArgumentException("--expect 만 있고 --mark 이 없습니다 — 찾을 조각을 같이 주세요");
            }

            // ★ --mark 와 같이 준 --origin 은 '정렬 전 대략 자리'(교수님 firstDieX/Y)다 — 표식을 거기서 찾고,
            //   정렬이 정확한 자리로 바꾼다. 이동이 두 번 들어가지 않게 WaferInspection 이 격자에선 뺀다.
            if (markPadding is not null && markPath is null)
            {
                throw new ArgumentException("--mark-padding 만 있고 --mark 이 없습니다 — 찾을 조각을 같이 주세요");
            }

            if (markPadding is < 0)
            {
                throw new ArgumentException($"--mark-padding 은 0 이상이어야 합니다 (현재 {markPadding})");
            }

            return new Args
            {
                ImagePath = image,
                E1Path = e1,
                E0Path = e0,
                Strategy = strategy,
                GoldenWanted = goldens,
                MarkPath = markPath,
                Expected = expected,
                MarkPadding = markPadding,
                Align = new AlignmentOptions { MinScore = alignMinScore, MaxAngleDeg = alignMaxAngle },
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
                    Circle = circle,
                    EdgeExclusion = edgeExclusion,
                    ZoneOverrides = zones,
                },
            };
        }

        /// <summary>소수점 있는 값.</summary>
        private static double Decimal(string text, string flag)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            string hint = text.Contains("--", StringComparison.Ordinal)
                ? " — 값과 다음 옵션 사이에 띄어쓰기가 빠진 것 같습니다"
                : string.Empty;

            throw new ArgumentException($"{flag} 의 값이 숫자가 아닙니다: '{text}'{hint}");
        }

        /// <summary>
        /// 원판 — <b>사진 좌표</b>의 중심x,중심y,반지름. 소수점을 받는다.
        /// ★ --expect(die(0,0) 기준 설계 좌표)와 다르다. 원판은 웨이퍼가 어떻게 놓였든 사진 속 그 자리다.
        /// </summary>
        private static WaferCircle Circle(string text, string flag)
        {
            string[] parts = text.Split(',');

            if (parts.Length != 3)
            {
                throw new ArgumentException(
                    "--circle 은 중심x,중심y,반지름 형식이어야 합니다 (예: 354,354,270) — 사진 좌표입니다");
            }

            return new WaferCircle(Decimal(parts[0], flag), Decimal(parts[1], flag), Decimal(parts[2], flag));
        }

        /// <summary>
        /// 글자 해석은 <see cref="WaferInputText"/> 한 곳 — 화면과 같은 규칙이다.
        /// 틀리면 어느 옵션이었는지를 앞에 붙여 명령행 오류로 바꾼다.
        /// </summary>
        private static T Text<T>(string flag, string text, Func<string, T> read)
        {
            try
            {
                return read(text);
            }
            catch (FormatException error)
            {
                throw new ArgumentException($"{flag} {error.Message}");
            }
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