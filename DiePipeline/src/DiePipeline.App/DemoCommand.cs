using OpenCvSharp;

namespace DiePipeline.App;

/// <summary>
/// 시험용 합성 웨이퍼를 그린다.
///
/// <para>★ <b>데이터가 필요 없다.</b> 원본도 회사 자료도 안 본다 — 숫자로 그리는 그림이다.
/// 실데이터를 못 쓰는 자리(레포·교육용·CI)에서도 <c>wafer</c> 명령을 끝까지 확인할 수 있다.</para>
///
/// <para>★ 모든 die 를 <b>픽셀 단위로 똑같이</b> 찍는다. die-to-die 는 "이웃이 나와 같다" 를
/// 전제로 하므로, 도장이 어긋나면 온 데가 결함으로 나와 시험이 성립하지 않는다.</para>
///
/// <para>★ 심은 자리를 <b>같이 출력한다.</b> 정답을 알아야 검사 결과가 맞는지 판단할 수 있다.
/// 시력검사표를 만들면서 정답표도 같이 인쇄하는 것과 같다.</para>
/// </summary>
public static class DemoCommand
{
    /// <summary>배경 밝기. 0.5 언저리 — 어두운 결함도 밝은 결함도 넣을 여지를 남긴다.</summary>
    private const int Background = 128;

    /// <summary>결함 밝기. 배경과 차이가 0.46 이라 어떤 임계값이든 넉넉히 넘는다.</summary>
    private const int DefectGray = 10;

    /// <summary>어디에 무엇을 심었나 — <b>정답</b>이다.</summary>
    public readonly record struct Planted(int Col, int Row, int X, int Y, int Size);

    /// <summary>표식 하나의 설계 좌표(왼쪽 위). <b>정답</b>이다.</summary>
    public readonly record struct Mark(int X, int Y, int Size);

    public static int Run(string[] argv)
    {
        Args options = Args.Parse(argv);

        Drawn drawn = Draw(options);
        int width;
        int height;

        try
        {
            width = drawn.Image.Cols;
            height = drawn.Image.Rows;

            if (!Cv2.ImWrite(options.OutPath, drawn.Image))
            {
                throw new InvalidOperationException(
                    $"저장하지 못했습니다: {options.OutPath} — 폴더가 있는지, 확장자가 .bmp/.png 인지 보세요");
            }
        }
        finally
        {
            drawn.Image.Dispose();
        }

        string markPath = SaveMarkTemplate(options, drawn.Marks[0]);

        // ★ 정렬 규약 — 기대 좌표계의 원점은 <b>die(0,0)</b> 이다.
        //   그래서 표식의 기대 위치도 die(0,0) 기준으로 적고, 풀어낸 Origin 은
        //   곧 "die(0,0) 이 실제로 놓인 자리" 가 된다. 둘이 한 규약으로 일치한다.
        double radians = options.AngleDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        double canvasTx = options.ShiftX + drawn.Slack;
        double canvasTy = options.ShiftY + drawn.Slack;

        // die(0,0) 은 도면에서 (여백,여백) 에 있다. 그 점을 기울여 보낸 자리가 정답이다.
        double originX = (cos * options.Margin) - (sin * options.Margin) + canvasTx;
        double originY = (sin * options.Margin) + (cos * options.Margin) + canvasTy;

        Console.WriteLine($"저장    {Path.GetFullPath(options.OutPath)}");
        Console.WriteLine($"        {width}×{height} · 8비트 회색");
        Console.WriteLine($"표식    {Path.GetFullPath(markPath)}  ({drawn.Marks[0].Size}×{drawn.Marks[0].Size})");
        Console.WriteLine($"격자    {options.Cols}×{options.Rows} · 피치 {options.Pitch} · 씨 {options.Seed}");
        Console.WriteLine();

        Console.WriteLine($"★ 심은 결함 {drawn.Defects.Count}개 — 이게 정답이다");

        foreach (Planted spot in drawn.Defects)
        {
            Console.WriteLine($"  die(c{spot.Col},r{spot.Row})  die 안 ({spot.X},{spot.Y})  "
                              + $"{spot.Size}×{spot.Size}  면적 {spot.Size * spot.Size}");
        }

        Console.WriteLine();
        Console.WriteLine("★ 놓인 자세 — 이것도 정답이다 (6-2 정렬이 되찾아야 할 값)");
        Console.WriteLine($"  각도 {options.AngleDeg:F3}°  ·  배율 1.000  ·  "
                          + $"die(0,0) 자리 ({originX:F1},{originY:F1})");
        Console.WriteLine("  표식 기대 위치 — die(0,0) 을 (0,0) 으로 본 좌표다");

        foreach (Mark mark in drawn.Marks)
        {
            Console.WriteLine($"    ({mark.X - options.Margin},{mark.Y - options.Margin})"
                              + $"  {mark.Size}×{mark.Size}");
        }

        Console.WriteLine();
        string expectArgs = string.Join(" ",
            drawn.Marks.Select(m => $"--expect {m.X - options.Margin},{m.Y - options.Margin}"));

        Console.WriteLine("이어서 돌려 보려면 — 표식을 찾아 자세를 스스로 풀게 한다");
        Console.WriteLine($"  diepipe wafer -i {options.OutPath} --e1 samples/recipes/die.diff.json "
                          + $"--grid {options.Cols},{options.Rows} --pitch {options.Pitch} "
                          + $"--mark {markPath} {expectArgs}");
        Console.WriteLine();
        Console.WriteLine("정렬 없이 자리를 직접 일러 주려면 (기울기가 0에 가까울 때만 맞는다)");
        Console.WriteLine($"  diepipe wafer -i {options.OutPath} --e1 samples/recipes/die.diff.json "
                          + $"--grid {options.Cols},{options.Rows} --pitch {options.Pitch} "
                          + $"--origin {originX:F0},{originY:F0}");

        if (Math.Abs(options.AngleDeg) > 0.05)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ {options.AngleDeg:F3}° 는 기울어서 지금 wafer 명령으로는 좌표 단계에서 멈춥니다.");
            Console.WriteLine("  die 를 펴서 읽는 warp read 가 아직 없기 때문입니다(0.05° 까지만 믿습니다).");
            Console.WriteLine("  검사까지 통째로 돌려 보려면 --angle 을 0.05 이하로 주세요.");
        }

        return 0;
    }

    /// <summary>
    /// 표식 조각을 따로 저장한다 — 정렬이 <b>찾을 대상</b>이다.
    ///
    /// ★ <b>기울이기 전</b> 모양으로 저장한다. 실제로도 그렇다 —
    ///   찾는 쪽은 설계 도면의 표식을 들고 있고, 사진 속 웨이퍼만 삐뚤어져 있다.
    /// </summary>
    private static string SaveMarkTemplate(Args options, Mark mark)
    {
        string path = Path.ChangeExtension(options.OutPath, null) + ".mark"
                      + (Path.GetExtension(options.OutPath) is { Length: > 0 } ext ? ext : ".bmp");

        using Mat template = new(mark.Size, mark.Size, MatType.CV_8UC1, Scalar.All(Background));

        DrawMark(template, new Mark(0, 0, mark.Size));

        if (!Cv2.ImWrite(path, template))
        {
            throw new InvalidOperationException($"표식을 저장하지 못했습니다: {path}");
        }

        return path;
    }

    /// <summary>
    /// 설계대로 반듯하게 그린 뒤, 마지막에 <b>통째로 기울인다.</b>
    ///
    /// <para>★ 왜 그리면서 기울이지 않고 다 그린 뒤에 기울이나 —
    /// 실제 웨이퍼가 그렇기 때문이다. <b>웨이퍼는 설계대로 만들어지고, 장비에 삐뚤게 놓인다.</b>
    /// 순서를 그대로 흉내 내야 시험이 진짜와 같은 모양이 된다.</para>
    ///
    /// <para>★ 회전 중심은 <b>(0,0)</b> 이다. 가운데가 아니다 —
    /// 그래야 우리 <c>SimilarityTransform</c>(<c>p' = s·R(θ)·p + t</c>)과 식이 정확히 같아져서,
    /// 정렬이 되찾아야 할 정답이 <b>준 각도·이동 그대로</b>가 된다.</para>
    /// </summary>
    /// <summary>그린 결과 한 묶음 — 그림 + 정답(결함·표식) + 기울이며 준 여유.</summary>
    private sealed record Drawn(Mat Image, List<Planted> Defects, List<Mark> Marks, int Slack);

    private static Drawn Draw(Args options)
    {
        int margin = options.Margin;

        // 설계 도면 — 여백 + 격자 + 여백. 표식은 여백에 놓는다(die 무늬와 안 겹치게).
        Mat design = new(
            (options.Rows * options.Pitch) + (2 * margin),
            (options.Cols * options.Pitch) + (2 * margin),
            MatType.CV_8UC1,
            Scalar.All(Background));

        try
        {
            using (Mat tile = Tile(options.Pitch))
            {
                // ★ 같은 무늬를 도장처럼 찍는다. 한 장을 만들어 N번 복사하는 것이
                //   매번 다시 그리는 것보다 안전하다 — 그리는 계산이 한 곳이라 어긋날 수가 없다.
                for (int row = 0; row < options.Rows; row++)
                {
                    for (int col = 0; col < options.Cols; col++)
                    {
                        // Mat(원본, 사각형) 은 복사가 아니라 그 자리를 가리키는 창이다.
                        // 여기에 쓰면 원본에 써진다.
                        using Mat cell = new(design, new Rect(
                            margin + (col * options.Pitch), margin + (row * options.Pitch),
                            options.Pitch, options.Pitch));

                        tile.CopyTo(cell);
                    }
                }
            }

            List<Planted> planted = Plant(design, options);
            List<Mark> marks = DrawMarks(design, options);

            Mat tilted = Tilt(design, options, out int slack);

            return new Drawn(tilted, planted, marks, slack);
        }
        finally
        {
            design.Dispose();
        }
    }

    /// <summary>
    /// 정렬 표식 두 개 — <b>대각선으로 멀리</b> 놓는다.
    ///
    /// <para>★ 왜 두 개인가: 하나로는 <b>얼마나 밀렸는지</b>만 알고 <b>얼마나 돌았는지</b>를 모른다.
    /// 못 자국 하나로는 액자가 비뚤었는지 알 수 없는 것과 같다.</para>
    ///
    /// <para>★ 왜 멀리인가: 가까우면 같은 각도라도 두 점의 어긋남 차이가 작아서,
    /// 재는 오차 1px 이 각도 오차로 크게 뻥튀기된다. 멀수록 각도가 정확해진다.
    /// (교수님 문서도 "대각선으로 멀리 2개 이상, 가까우면 각도 민감도 나쁨" 이라고 적는다.)</para>
    ///
    /// <para>★ 모양이 <b>비대칭</b>이다. die 무늬처럼 반복되지도 않는다 —
    /// 2주차에 "규칙적인 무늬에서 정합이 엉뚱한 자리를 자신 있게 짚는" 사고를 겪었다.
    /// 표식만은 웨이퍼에 딱 두 개여야 그 함정에 안 빠진다.</para>
    /// </summary>
    private static List<Mark> DrawMarks(Mat design, Args options)
    {
        int size = Math.Max(16, options.Margin * 3 / 5);
        int inset = (options.Margin - size) / 2;

        List<Mark> marks =
        [
            new Mark(inset, inset, size),
            new Mark(design.Cols - size - inset, design.Rows - size - inset, size),
        ];

        foreach (Mark mark in marks)
        {
            DrawMark(design, mark);
        }

        return marks;
    }

    private static void DrawMark(Mat target, Mark mark)
    {
        // 밝은 네모 + 왼쪽 위에 어두운 네모. 비대칭이라 "어느 쪽이 위인지" 가 드러난다.
        Cv2.Rectangle(target, new Rect(mark.X, mark.Y, mark.Size, mark.Size), Scalar.All(255), -1);

        Cv2.Rectangle(target,
            new Rect(mark.X + (mark.Size / 6), mark.Y + (mark.Size / 6), mark.Size / 3, mark.Size / 3),
            Scalar.All(0), -1);
    }

    /// <summary>설계 도면을 기울여 "장비에 놓인 모습" 으로 만든다.</summary>
    private static Mat Tilt(Mat design, Args options, out int slack)
    {
        double radians = options.AngleDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // ★ 기울이면 그림이 캔버스 밖으로 밀려난다. 잘리면 표식을 못 찾으니 여유를 준다.
        //   대각선 길이 × sin(각도) 가 최대로 밀리는 양이다.
        double diagonal = Math.Sqrt((design.Cols * (double)design.Cols) + (design.Rows * (double)design.Rows));

        slack = (int)Math.Ceiling(diagonal * Math.Abs(sin))
                + Math.Max(Math.Abs(options.ShiftX), Math.Abs(options.ShiftY)) + 4;

        using Mat transform = new(2, 3, MatType.CV_64FC1);

        transform.Set(0, 0, cos);
        transform.Set(0, 1, -sin);
        transform.Set(0, 2, options.ShiftX + (double)slack);
        transform.Set(1, 0, sin);
        transform.Set(1, 1, cos);
        transform.Set(1, 2, options.ShiftY + (double)slack);

        Mat tilted = new();

        try
        {
            Cv2.WarpAffine(design, tilted, transform,
                new Size(design.Cols + (2 * slack), design.Rows + (2 * slack)),
                InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(Background));

            return tilted;
        }
        catch
        {
            tilted.Dispose();
            throw;
        }
    }

    /// <summary>
    /// die 한 장의 무늬.
    ///
    /// ★ 무늬를 <b>오른쪽에 몰아 둔다.</b> 왼쪽을 비워야 결함 심을 자리가 생긴다.
    ///   무늬 위에 심으면 <b>골든에도 같은 무늬가 있어서 차이가 안 나</b> 안 잡힌다.
    /// </summary>
    private static Mat Tile(int pitch)
    {
        Mat tile = new(pitch, pitch, MatType.CV_8UC1, Scalar.All(Background));

        try
        {
            int margin = pitch / 10;
            int thickness = Math.Max(1, pitch / 40);

            // 테두리 — die 경계를 눈으로 찾기 쉽게
            Cv2.Rectangle(tile,
                new Rect(margin, margin, pitch - (2 * margin), pitch - (2 * margin)),
                Scalar.All(230), thickness);

            // 세로선 · 검은 네모 · 동그라미 — 전부 오른쪽
            Cv2.Rectangle(tile,
                new Rect(pitch * 72 / 100, margin, thickness, pitch - (2 * margin)),
                Scalar.All(230), -1);

            Cv2.Rectangle(tile,
                new Rect(pitch * 80 / 100, pitch * 15 / 100, pitch * 12 / 100, pitch * 12 / 100),
                Scalar.All(40), -1);

            Cv2.Circle(tile,
                new Point(pitch * 82 / 100, pitch * 78 / 100),
                Math.Max(1, pitch * 7 / 100),
                Scalar.All(200), -1);

            return tile;
        }
        catch
        {
            tile.Dispose();
            throw;
        }
    }

    private static List<Planted> Plant(Mat wafer, Args options)
    {
        // ★ 레시피의 minArea 500 을 넉넉히 넘겨야 한다. 작게 심으면 "안 잡혔다" 가
        //   파이프라인 잘못인지 너무 작아서인지 구분이 안 된다.
        int size = Math.Max(24, options.Pitch * 3 / 10);

        int least = (options.Pitch / 10) + 3;                  // 테두리 안쪽
        int mostX = (options.Pitch * 68 / 100) - size;         // 세로선 왼쪽
        int mostY = (options.Pitch * 88 / 100) - size;

        if (mostX < least || mostY < least)
        {
            throw new ArgumentException(
                $"피치 {options.Pitch} 는 결함({size}×{size})을 심기에 좁습니다 — --pitch 를 100 이상으로 주세요");
        }

        int total = options.Cols * options.Rows;

        if (options.Defects > total)
        {
            throw new ArgumentException(
                $"결함 {options.Defects}개는 die {total}개보다 많습니다 — die 하나에 하나씩만 심습니다");
        }

        // ★ 씨를 받는다. 같은 씨면 같은 그림이 나온다 —
        //   "어제 결과와 다른데?" 가 프로그램 탓인지 그림 탓인지 구분할 수 있어야 한다.
        Random random = new(options.Seed);

        int[] order = [.. Enumerable.Range(0, total).OrderBy(_ => random.Next())];

        List<Planted> planted = [];

        foreach (int index in order.Take(options.Defects))
        {
            int col = index % options.Cols;
            int row = index / options.Cols;

            int x = random.Next(least, mostX + 1);
            int y = random.Next(least, mostY + 1);

            Cv2.Rectangle(wafer,
                new Rect(options.Margin + (col * options.Pitch) + x,
                         options.Margin + (row * options.Pitch) + y,
                         size, size),
                Scalar.All(DefectGray), -1);

            planted.Add(new Planted(col, row, x, y, size));
        }

        // 웨이퍼 맵과 같은 순서(위→아래, 왼→오)로 찍어야 눈으로 대조하기 쉽다.
        return [.. planted.OrderBy(spot => (spot.Row, spot.Col))];
    }

    private sealed record Args
    {
        public required string OutPath { get; init; }

        public int Cols { get; init; } = 4;

        public int Rows { get; init; } = 3;

        public int Pitch { get; init; } = 100;

        public int Defects { get; init; } = 3;

        /// <summary>기본값을 <b>고정</b>해 둔다 — 씨가 없으면 매번 다른 그림이 나와 비교가 안 된다.</summary>
        public int Seed { get; init; } = 1;

        /// <summary>얼마나 기울여 놓을까(도). 0 이면 반듯하다.</summary>
        public double AngleDeg { get; init; }

        public int ShiftX { get; init; }

        public int ShiftY { get; init; }

        /// <summary>
        /// 격자 둘레 여백. <b>표식이 살 자리</b>다.
        ///
        /// ★ 표식을 die 무늬 위에 놓으면 검사에서 결함으로 잡힌다.
        ///   실제 웨이퍼도 정렬 마크는 die 바깥(스크라이브·가장자리)에 있다.
        /// </summary>
        public int Margin => Math.Max(40, Pitch / 2);

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

            if (cols < 1 || rows < 1)
            {
                throw new ArgumentException($"격자는 1×1 이상이어야 합니다 (현재 {cols}×{rows})");
            }

            if (defects < 0)
            {
                throw new ArgumentException("--defects 는 0 이상이어야 합니다");
            }

            // ★ 웨이퍼는 장비에 "살짝" 삐뚤게 놓이는 것이지 뒤집히지 않는다.
            //   30도 같은 값을 받아 주면 그림은 나오는데 정렬이 못 푸는 상황이 생기고,
            //   그때 "정렬이 고장났나" 를 의심하게 된다. 애초에 막는다.
            if (Math.Abs(angle) > 5)
            {
                throw new ArgumentException(
                    $"--angle 은 -5~5 도여야 합니다 (현재 {angle}) — 실제 웨이퍼는 이보다 크게 안 돌아갑니다");
            }

            return new Args
            {
                OutPath = outPath,
                Cols = cols,
                Rows = rows,
                Pitch = pitch,
                Defects = defects,
                Seed = seed,
                AngleDeg = angle,
                ShiftX = shiftX,
                ShiftY = shiftY,
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