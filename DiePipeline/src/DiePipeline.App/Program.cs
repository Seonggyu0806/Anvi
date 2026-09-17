using System.Diagnostics;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.App;

/// <summary>
/// 레시피를 실데이터에 돌려 보는 <b>진단용 러너</b>.
///
/// ★ 아직 제품 CLI가 아니다. "우리 파이프라인이 진짜 웨이퍼에서 도는가"를
///   눈으로 확인하려고 만든 것이다. 격자·이웃 고르기는 웨이퍼 층에서 제대로 만든다.
///
/// ★ 원본을 <b>통째로 읽지 않는다.</b> 필요한 칸만 파일에서 뜯어 온다(Step 7).
///   실측: 수백 MB 원본에서 die 네 칸을 읽는 데 155 ms.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (RecipeValidationException error)
        {
            // 검증 오류는 이미 사람이 읽을 수 있게 정리돼 있다. 스택은 소음이다.
            Console.Error.WriteLine(error.Message);
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"오류: {error.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string[] rest = args.Skip(1).ToArray();

        return args[0] switch
        {
            "run" => Inspect(Options.Parse(rest, needRecipe: true)),
            "probe" => Probe(Options.Parse(rest, needRecipe: false)),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"모르는 명령입니다: {command}");
        PrintUsage();
        return 1;
    }

    /// <summary>
    /// 원본을 열어 생김새만 알려주고, 원하면 한 칸을 뜯어 본다.
    ///
    /// ★ 레시피 없이 <b>원본을 들여다보는</b> 도구다. 격자를 찾거나
    ///   "이 자리에 뭐가 있나"를 확인할 때 쓴다.
    /// </summary>
    private static int Probe(Options options)
    {
        using IImageSource source = ImageSource.Open(options.ImagePath);

        Console.WriteLine($"이미지  {options.ImagePath}");
        Describe(source);

        if (options.Roi is { } roi)
        {
            Stopwatch clock = Stopwatch.StartNew();
            using Mat patch = source.ReadRegion(roi);
            clock.Stop();

            Cv2.MeanStdDev(patch, out Scalar mean, out Scalar deviation);
            Cv2.MinMaxLoc(patch, out double low, out double high);

            Console.WriteLine();
            Console.WriteLine($"영역    {roi}   ({clock.Elapsed.TotalMilliseconds:F0} ms)");
            Console.WriteLine($"        밝기 평균 {mean.Val0:F4} · 표준편차 {deviation.Val0:F4} · 범위 {low:F3}~{high:F3}");
            Console.WriteLine($"        메모리 {patch.Total() * patch.ElemSize() / 1024.0 / 1024.0:F1} MB");

            Save(options.OutPath, patch);
        }

        if (options.Overview is { } longSide)
        {
            Stopwatch clock = Stopwatch.StartNew();
            using Mat overview = source.ReadOverview(longSide);
            clock.Stop();

            Console.WriteLine();
            Console.WriteLine($"개요    {overview.Cols}×{overview.Rows}   ({clock.Elapsed.TotalMilliseconds:F0} ms)");

            Save(options.OutPath, overview);
        }

        return 0;
    }

    private static int Inspect(Options options)
    {
        Recipe recipe = RecipeLoader.Parse(File.ReadAllText(options.RecipePath!));
        NodeRegistry registry = CvBackend.CreateRegistry();

        // ★ 돌리기 전에 레시피를 통째로 검사한다. 틀린 게 있으면 한 번에 다 알려준다.
        RecipeValidator.EnsureValid(recipe, registry);

        // ★ 여기가 Step 7의 결실이다. 예전엔 이미지를 통째로 메모리에 올렸다.
        //   이제는 "뜯어 올 준비"만 한다 — 파일이 몇백 MB든 상관없다.
        using IImageSource source = ImageSource.Open(options.ImagePath);

        Console.WriteLine($"이미지  {options.ImagePath}");
        Describe(source);

        Stopwatch reading = Stopwatch.StartNew();
        List<Mat> cells = CutCells(source, options);
        reading.Stop();

        try
        {
            Mat roi = cells[options.CellIndex];
            List<Mat> neighbors = cells.Where((_, i) => i != options.CellIndex).ToList();

            Console.WriteLine($"셀      피치 {options.Pitch}px · 시작 ({options.OriginX},{options.OriginY}) · " +
                              $"{cells.Count}칸 중 {options.CellIndex}번을 검사, 나머지 {neighbors.Count}칸이 이웃");
            Console.WriteLine($"        읽기 {reading.Elapsed.TotalMilliseconds:F0} ms");
            Console.WriteLine();

            PipelineContext context = new();
            context.SetInput(ReservedContext.Roi, roi);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)neighbors);

            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                new PipelineEngine().Run(registry.CreateAll(recipe), context);

                PipelineResult result = PipelineResult.From(context);
                clock.Stop();

                Report(result.Defects, clock.Elapsed.TotalMilliseconds);

                if (options.MaskPath is not null && context.TryGet(options.MaskKey, out Mat? mask) && mask is not null)
                {
                    WarnIfInsideRepository(options.MaskPath);
                    ImageFile.SaveMask(options.MaskPath, mask);
                    Console.WriteLine($"마스크 저장  {options.MaskPath}");
                }

                return result.Defects.Count == 0 ? 0 : 3;
            }
            finally
            {
                // ★ 한 번 실행 = 한 번 정리. 노드들이 칠판에 올린 이미지를 여기서 전부 놓는다.
                context.ReleaseAll();
            }
        }
        finally
        {
            foreach (Mat cell in cells)
            {
                cell.Dispose();
            }
        }
    }

    /// <summary>어떤 길로 열렸는지 보여준다 — 뜯어 읽기인지, 통째로 읽기인지.</summary>
    private static void Describe(IImageSource source)
    {
        if (source is ByteImageSource file)
        {
            ImageGeometry g = file.Geometry;

            Console.WriteLine($"        {g.Width}×{g.Height} · {g.BytesPerPixel * 8}비트 · "
                              + (g.BottomUp ? "아래→위" : "위→아래")
                              + $" · 행 {g.RowStride}바이트"
                              + (g.RowPadding > 0 ? $" (패딩 {g.RowPadding})" : string.Empty));

            Console.WriteLine($"        파일 {g.ExpectedFileSize / 1024.0 / 1024.0:F0} MB — 필요한 칸만 뜯어 읽는다");
        }
        else
        {
            Console.WriteLine($"        {source.Width}×{source.Height} · 통째로 읽음");
        }
    }

    private static void Save(string? path, Mat image)
    {
        if (path is null)
        {
            return;
        }

        WarnIfInsideRepository(path);
        ImageFile.SaveGrayF32(path, image);
        Console.WriteLine($"        저장  {path}");
    }

    /// <summary>
    /// ★ 실데이터에서 잘라낸 것을 레포 안에 저장하려 하면 알려준다.
    ///   회사 자료는 파생물도 커밋하지 않는다 — <c>.gitignore</c>는 2차 방어선일 뿐이고,
    ///   1차 방어선은 <b>애초에 레포 밖에 두는 것</b>이다.
    /// </summary>
    private static void WarnIfInsideRepository(string path)
    {
        DirectoryInfo? folder = new FileInfo(Path.GetFullPath(path)).Directory;

        while (folder is not null)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, ".git")))
            {
                Console.WriteLine();
                Console.WriteLine($"⚠ 저장 위치가 git 저장소 안입니다 — {folder.FullName}");
                Console.WriteLine("  실데이터에서 잘라낸 것은 레포 밖에 두세요.");
                Console.WriteLine();
                return;
            }

            folder = folder.Parent;
        }
    }

    private static List<Mat> CutCells(IImageSource source, Options options)
    {
        List<Mat> cells = [];

        try
        {
            for (int i = 0; i < options.Count; i++)
            {
                // ★ 파일에서 이 칸만 읽어 온다. 나머지 수백 MB는 건드리지 않는다.
                cells.Add(source.ReadRegion(new Roi(
                    options.OriginX + (i * options.Pitch),
                    options.OriginY,
                    options.Pitch,
                    options.Pitch)));
            }
        }
        catch
        {
            foreach (Mat cell in cells)
            {
                cell.Dispose();
            }

            throw;
        }

        return cells;
    }

    private static void Report(DefectList defects, double milliseconds)
    {
        Console.WriteLine($"결함 {defects.Count}개   ({milliseconds:F1} ms)");

        foreach (Defect defect in defects.Items)
        {
            string brightness = defect.MeanIntensity is { } mean
                ? $"  밝기 평균 {mean:F3} 최대 {defect.MaxIntensity:F3}"
                : string.Empty;

            Console.WriteLine($"  #{defect.Id,-3} {defect.Bounds}  면적 {defect.Area,-6} 중심 {defect.Centroid}{brightness}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            사용법
              diepipe run   -r <레시피.json> -i <이미지> [옵션]     레시피를 돌린다
              diepipe probe -i <이미지> [옵션]                      원본 생김새를 본다

            셀 자르기 (가로로 이어진 칸을 잘라 하나를 검사, 나머지를 이웃으로 쓴다)
              --pitch <n>       셀 한 변(px)          기본 100
              --origin <x,y>    첫 칸의 왼쪽 위        기본 0,0
              --count <n>       자를 칸 수            기본 6
              --cell <i>        그중 검사할 칸         기본 0

            probe 전용
              --roi <x,y,w,h>   그 영역만 뜯어 본다
              --overview <n>    긴 변이 n 이하가 되게 줄여 본다
              --out <경로>      뜯은 것을 PNG로 저장

            결과
              --save-mask <경로>  이진 마스크를 PNG로 저장
              --mask-key <이름>   저장할 칠판 이름표    기본 mask

            끝값: 0 = 결함 없음 · 3 = 결함 있음 · 1 = 오류 · 2 = 레시피 오류

            ⚠ 실데이터는 레포 밖에 두고 경로로만 넘긴다. 잘라낸 것도 레포에 넣지 않는다.
            """);
    }

    private sealed record Options
    {
        public string? RecipePath { get; init; }

        public required string ImagePath { get; init; }

        public int Pitch { get; init; } = 100;

        public int OriginX { get; init; }

        public int OriginY { get; init; }

        public int Count { get; init; } = 6;

        public int CellIndex { get; init; }

        public string? MaskPath { get; init; }

        public string MaskKey { get; init; } = "mask";

        public Roi? Roi { get; init; }

        public int? Overview { get; init; }

        public string? OutPath { get; init; }

        public static Options Parse(string[] args, bool needRecipe)
        {
            string? recipe = null;
            string? image = null;
            int pitch = 100;
            int originX = 0;
            int originY = 0;
            int count = 6;
            int cell = 0;
            string? maskPath = null;
            string maskKey = "mask";
            Roi? roi = null;
            int? overview = null;
            string? outPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];

                switch (flag)
                {
                    case "-r" or "--recipe": recipe = Next(args, ref i, flag); break;
                    case "-i" or "--image": image = Next(args, ref i, flag); break;
                    case "--pitch": pitch = int.Parse(Next(args, ref i, flag)); break;
                    case "--count": count = int.Parse(Next(args, ref i, flag)); break;
                    case "--cell": cell = int.Parse(Next(args, ref i, flag)); break;
                    case "--save-mask": maskPath = Next(args, ref i, flag); break;
                    case "--mask-key": maskKey = Next(args, ref i, flag); break;
                    case "--overview": overview = int.Parse(Next(args, ref i, flag)); break;
                    case "--out": outPath = Next(args, ref i, flag); break;

                    case "--origin":
                        int[] origin = Numbers(Next(args, ref i, flag), 2, "--origin 은 x,y 형식이어야 합니다 (예: 50,112)");
                        originX = origin[0];
                        originY = origin[1];
                        break;

                    case "--roi":
                        int[] box = Numbers(Next(args, ref i, flag), 4, "--roi 는 x,y,w,h 형식이어야 합니다 (예: 0,0,512,512)");
                        roi = new Roi(box[0], box[1], box[2], box[3]);
                        break;

                    default:
                        throw new ArgumentException($"모르는 옵션입니다: {flag}");
                }
            }

            if (image is null)
            {
                throw new ArgumentException("-i <이미지> 는 반드시 있어야 합니다");
            }

            if (needRecipe && recipe is null)
            {
                throw new ArgumentException("-r <레시피> 는 반드시 있어야 합니다");
            }

            if (cell < 0 || cell >= count)
            {
                throw new ArgumentException($"--cell 은 0 이상 {count} 미만이어야 합니다");
            }

            return new Options
            {
                RecipePath = recipe,
                ImagePath = image,
                Pitch = pitch,
                OriginX = originX,
                OriginY = originY,
                Count = count,
                CellIndex = cell,
                MaskPath = maskPath,
                MaskKey = maskKey,
                Roi = roi,
                Overview = overview,
                OutPath = outPath,
            };
        }

        private static int[] Numbers(string text, int expected, string message)
        {
            string[] parts = text.Split(',');

            if (parts.Length != expected)
            {
                throw new ArgumentException(message);
            }

            return [.. parts.Select(int.Parse)];
        }

        private static string Next(string[] args, ref int i, string flag)
            => ++i < args.Length ? args[i] : throw new ArgumentException($"{flag} 다음에 값이 없습니다");
    }
}