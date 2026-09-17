using System.Diagnostics;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
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

        if (args[0] != "run")
        {
            Console.Error.WriteLine($"모르는 명령입니다: {args[0]}");
            PrintUsage();
            return 1;
        }

        Options options = Options.Parse(args.Skip(1).ToArray());

        Recipe recipe = RecipeLoader.Parse(File.ReadAllText(options.RecipePath));
        NodeRegistry registry = CvBackend.CreateRegistry();

        // ★ 돌리기 전에 레시피를 통째로 검사한다. 틀린 게 있으면 <b>한 번에 다</b> 알려준다.
        RecipeValidator.EnsureValid(recipe, registry);

        using Mat tile = ImageFile.LoadGrayF32(options.ImagePath);
        Console.WriteLine($"이미지  {options.ImagePath}");
        Console.WriteLine($"        {tile.Cols}×{tile.Rows}  {tile.Type()}");

        List<Mat> cells = CutCells(tile, options);

        try
        {
            Mat roi = cells[options.CellIndex];
            List<Mat> neighbors = cells.Where((_, i) => i != options.CellIndex).ToList();

            Console.WriteLine($"셀      피치 {options.Pitch}px · 시작 ({options.OriginX},{options.OriginY}) · " +
                              $"{cells.Count}칸 중 {options.CellIndex}번을 검사, 나머지 {neighbors.Count}칸이 이웃");
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

    private static List<Mat> CutCells(Mat tile, Options options)
    {
        List<Mat> cells = [];

        try
        {
            for (int i = 0; i < options.Count; i++)
            {
                cells.Add(ImageFile.Crop(
                    tile,
                    options.OriginX + (i * options.Pitch),
                    options.OriginY,
                    options.Pitch,
                    options.Pitch));
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
              diepipe run -r <레시피.json> -i <이미지> [옵션]

            셀 자르기 (가로로 이어진 칸을 잘라 0번을 검사, 나머지를 이웃으로 쓴다)
              --pitch <n>       셀 한 변(px)          기본 100
              --origin <x,y>    첫 칸의 왼쪽 위        기본 0,0
              --count <n>       자를 칸 수            기본 6
              --cell <i>        그중 검사할 칸         기본 0

            결과
              --save-mask <경로>  이진 마스크를 PNG로 저장
              --mask-key <이름>   저장할 칠판 이름표    기본 mask

            끝값: 0 = 결함 없음 · 3 = 결함 있음 · 1 = 오류 · 2 = 레시피 오류
            """);
    }

    private sealed record Options
    {
        public required string RecipePath { get; init; }

        public required string ImagePath { get; init; }

        public int Pitch { get; init; } = 100;

        public int OriginX { get; init; }

        public int OriginY { get; init; }

        public int Count { get; init; } = 6;

        public int CellIndex { get; init; }

        public string? MaskPath { get; init; }

        public string MaskKey { get; init; } = "mask";

        public static Options Parse(string[] args)
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

                    case "--origin":
                        string[] parts = Next(args, ref i, flag).Split(',');

                        if (parts.Length != 2)
                        {
                            throw new ArgumentException("--origin 은 x,y 형식이어야 합니다 (예: 50,112)");
                        }

                        originX = int.Parse(parts[0]);
                        originY = int.Parse(parts[1]);
                        break;

                    default:
                        throw new ArgumentException($"모르는 옵션입니다: {flag}");
                }
            }

            if (recipe is null || image is null)
            {
                throw new ArgumentException("-r <레시피> 와 -i <이미지> 는 반드시 있어야 합니다");
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
            };
        }

        private static string Next(string[] args, ref int i, string flag)
            => ++i < args.Length ? args[i] : throw new ArgumentException($"{flag} 다음에 값이 없습니다");
    }
}
