using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.App;

/// <summary>
/// 시험용 합성 웨이퍼를 <b>파일로</b> 내놓는 명령.
///
/// <para>★ 그리는 일은 <see cref="SyntheticWafer"/>가 한다. 이 파일은 명령행 사정만 맡는다 —
/// 옵션 읽기, 파일 쓰기, 정답 출력. 그림이 여기 갇혀 있으면 화면도 테스트도 같은 그림을 못 쓴다.</para>
///
/// <para>★ 심은 자리를 <b>같이 출력한다.</b> 정답을 알아야 검사 결과가 맞는지 판단할 수 있다.
/// 시력검사표를 만들면서 정답표도 같이 인쇄하는 것과 같다.</para>
/// </summary>
public static class DemoCommand
{
    public static int Run(string[] argv)
    {
        Args options = Args.Parse(argv);
        SyntheticWaferSpec spec = options.Spec;

        using SyntheticWafer wafer = SyntheticWafer.Draw(spec);

        int width = wafer.Image.Cols;
        int height = wafer.Image.Rows;

        if (!Cv2.ImWrite(options.OutPath, wafer.Image))
        {
            throw new InvalidOperationException(
                $"저장하지 못했습니다: {options.OutPath} — 폴더가 있는지, 확장자가 .bmp/.png 인지 보세요");
        }

        string markPath = SaveMarkTemplate(options.OutPath, wafer);

        Console.WriteLine($"저장    {Path.GetFullPath(options.OutPath)}");
        Console.WriteLine($"        {width}×{height} · 8비트 회색");
        Console.WriteLine($"표식    {Path.GetFullPath(markPath)}  ({wafer.Marks[0].Size}×{wafer.Marks[0].Size})");
        Console.WriteLine($"격자    {spec.Cols}×{spec.Rows} · 피치 {spec.Pitch} · 씨 {spec.Seed}");
        Console.WriteLine();

        Console.WriteLine($"★ 심은 결함 {wafer.Defects.Count}개 — 이게 정답이다");

        foreach (PlantedDefect spot in wafer.Defects)
        {
            Console.WriteLine($"  die(c{spot.Col},r{spot.Row})  die 안 ({spot.X},{spot.Y})  "
                              + $"{spot.Size}×{spot.Size}  면적 {spot.Size * spot.Size}");
        }

        Console.WriteLine();
        Console.WriteLine("★ 놓인 자세 — 이것도 정답이다 (6-2 정렬이 되찾아야 할 값)");
        Console.WriteLine($"  각도 {spec.AngleDeg:F3}°  ·  배율 1.000  ·  "
                          + $"die(0,0) 자리 ({wafer.OriginX:F1},{wafer.OriginY:F1})");
        Console.WriteLine("  표식 기대 위치 — die(0,0) 을 (0,0) 으로 본 좌표다");

        foreach (AlignMark mark in wafer.Marks)
        {
            (int x, int y) = wafer.ExpectedAt(mark);
            Console.WriteLine($"    ({x},{y})  {mark.Size}×{mark.Size}");
        }

        // ★ 원판이면 원도 정답이다 — 사진 좌표 그대로 wafer 명령의 --circle 에 넘기면 된다.
        string circleArgs = string.Empty;

        if (wafer.Circle is { } circle)
        {
            Console.WriteLine();
            Console.WriteLine("★ 원판 — 이것도 정답이다 (사진 좌표)");
            Console.WriteLine($"  중심 ({circle.CenterX:F1},{circle.CenterY:F1})  ·  반지름 {circle.Radius:F1}");

            circleArgs = $" --circle {circle.CenterX:F1},{circle.CenterY:F1},{circle.Radius:F1}"
                         + " --e0 samples/recipes/die.dark.masked.json";
        }

        Console.WriteLine();
        string expectArgs = string.Join(" ",
            wafer.Marks.Select(m => wafer.ExpectedAt(m)).Select(p => $"--expect {p.X},{p.Y}"));

        Console.WriteLine("이어서 돌려 보려면 — 표식을 찾아 자세를 스스로 풀게 한다");
        Console.WriteLine($"  diepipe wafer -i {options.OutPath} --e1 samples/recipes/die.diff.json "
                          + $"--grid {spec.Cols},{spec.Rows} --pitch {spec.Pitch} "
                          + $"--mark {markPath} {expectArgs}{circleArgs}");
        Console.WriteLine();
        Console.WriteLine("정렬 없이 자리를 직접 일러 주려면 (기울기가 0에 가까울 때만 맞는다)");
        Console.WriteLine($"  diepipe wafer -i {options.OutPath} --e1 samples/recipes/die.diff.json "
                          + $"--grid {spec.Cols},{spec.Rows} --pitch {spec.Pitch} "
                          + $"--origin {wafer.OriginX:F0},{wafer.OriginY:F0}{circleArgs}");

        if (Math.Abs(spec.AngleDeg) > 0.05)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ {spec.AngleDeg:F3}° 는 기울어서 지금 wafer 명령으로는 좌표 단계에서 멈춥니다.");
            Console.WriteLine("  die 를 펴서 읽는 warp read 가 아직 없기 때문입니다(0.05° 까지만 믿습니다).");
            Console.WriteLine("  검사까지 통째로 돌려 보려면 --angle 을 0.05 이하로 주세요.");
        }

        return 0;
    }

    /// <summary>
    /// 표식 조각을 따로 저장한다 — 정렬이 <b>찾을 대상</b>이다.
    /// 기울이기 전 모양이다(<see cref="SyntheticWafer.DrawMarkTemplate"/>).
    /// </summary>
    private static string SaveMarkTemplate(string outPath, SyntheticWafer wafer)
    {
        string path = Path.ChangeExtension(outPath, null) + ".mark"
                      + (Path.GetExtension(outPath) is { Length: > 0 } ext ? ext : ".bmp");

        using Mat template = wafer.DrawMarkTemplate();

        if (!Cv2.ImWrite(path, template))
        {
            throw new InvalidOperationException($"표식을 저장하지 못했습니다: {path}");
        }

        return path;
    }

    /// <summary>
    /// 명령행 옵션. ★ 값이 말이 되는지(격자·각도·피치)는 <see cref="SyntheticWafer.Draw"/>가 본다 —
    /// 여기서 또 보면 규칙이 두 군데 생긴다. 여기는 <b>글자를 숫자로 읽는 것</b>까지만.
    /// </summary>
    private sealed record Args
    {
        public required string OutPath { get; init; }

        public required SyntheticWaferSpec Spec { get; init; }

        public static Args Parse(string[] args)
        {
            string? outPath = null;
            int cols = 4;
            int rows = 3;
            int pitch = 100;
            int defects = 3;
            int seed = 1;
            double angle = 0;
            int shiftX = 0;
            int shiftY = 0;
            bool round = false;

            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];

                switch (flag)
                {
                    case "-o" or "--out": outPath = Next(args, ref i, flag); break;
                    case "--pitch": pitch = Number(Next(args, ref i, flag), flag); break;
                    case "--defects": defects = Number(Next(args, ref i, flag), flag); break;
                    case "--seed": seed = Number(Next(args, ref i, flag), flag); break;
                    case "--angle": angle = Decimal(Next(args, ref i, flag), flag); break;
                    case "--round": round = true; break;      // 값이 없는 스위치다

                    case "--grid":
                        string[] parts = Next(args, ref i, flag).Split(',');

                        if (parts.Length != 2)
                        {
                            throw new ArgumentException("--grid 는 열,행 형식이어야 합니다 (예: 4,3)");
                        }

                        cols = Number(parts[0], flag);
                        rows = Number(parts[1], flag);
                        break;

                    case "--shift":
                        string[] move = Next(args, ref i, flag).Split(',');

                        if (move.Length != 2)
                        {
                            throw new ArgumentException("--shift 는 x,y 형식이어야 합니다 (예: 8,5)");
                        }

                        shiftX = Number(move[0], flag);
                        shiftY = Number(move[1], flag);
                        break;

                    default:
                        throw new ArgumentException($"모르는 옵션입니다: {flag}");
                }
            }

            if (outPath is null)
            {
                throw new ArgumentException("-o <경로.bmp> 는 반드시 있어야 합니다");
            }

            return new Args
            {
                OutPath = outPath,
                Spec = new SyntheticWaferSpec
                {
                    Cols = cols,
                    Rows = rows,
                    Pitch = pitch,
                    Defects = defects,
                    Seed = seed,
                    AngleDeg = angle,
                    ShiftX = shiftX,
                    ShiftY = shiftY,
                    Round = round,
                },
            };
        }

        /// <summary>소수점 있는 값. 각도는 0.7 처럼 준다.</summary>
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
        /// 숫자로 읽는다. 실패하면 <b>어느 옵션의 어떤 값</b>이었는지 말해 준다.
        ///
        /// ★ <c>int.Parse</c> 그대로 쓰면 "'100--sample' was not in a correct format" 만 나온다.
        ///   어느 옵션인지도, 왜인지도 모른다. 실제로 <b>띄어쓰기 하나가 빠져서</b> 난 오류였다.
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

        private static string Next(string[] args, ref int i, string flag)
            => ++i < args.Length ? args[i] : throw new ArgumentException($"{flag} 다음에 값이 없습니다");
    }
}
