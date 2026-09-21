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

    public static int Run(string[] argv)
    {
        Args options = Args.Parse(argv);

        List<Planted> planted;
        int width;
        int height;

        using (Mat wafer = Draw(options, out planted))
        {
            width = wafer.Cols;
            height = wafer.Rows;

            if (!Cv2.ImWrite(options.OutPath, wafer))
            {
                throw new InvalidOperationException(
                    $"저장하지 못했습니다: {options.OutPath} — 폴더가 있는지, 확장자가 .bmp/.png 인지 보세요");
            }
        }

        Console.WriteLine($"저장    {Path.GetFullPath(options.OutPath)}");
        Console.WriteLine($"        {width}×{height} · 8비트 회색");
        Console.WriteLine($"격자    {options.Cols}×{options.Rows} · 피치 {options.Pitch} · 씨 {options.Seed}");
        Console.WriteLine();

        Console.WriteLine($"★ 심은 결함 {planted.Count}개 — 이게 정답이다");

        foreach (Planted spot in planted)
        {
            Console.WriteLine($"  die(c{spot.Col},r{spot.Row})  die 안 ({spot.X},{spot.Y})  "
                              + $"{spot.Size}×{spot.Size}  면적 {spot.Size * spot.Size}");
        }

        Console.WriteLine();
        Console.WriteLine("이어서 돌려 보려면");
        Console.WriteLine($"  diepipe wafer -i {options.OutPath} --e1 samples/recipes/die.diff.json "
                          + $"--grid {options.Cols},{options.Rows} --pitch {options.Pitch}");

        return 0;
    }

    private static Mat Draw(Args options, out List<Planted> planted)
    {
        Mat wafer = new(
            options.Rows * options.Pitch,
            options.Cols * options.Pitch,
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
                        using Mat cell = new(wafer, new Rect(
                            col * options.Pitch, row * options.Pitch, options.Pitch, options.Pitch));

                        tile.CopyTo(cell);
                    }
                }
            }

            planted = Plant(wafer, options);

            return wafer;
        }
        catch
        {
            wafer.Dispose();
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
                new Rect((col * options.Pitch) + x, (row * options.Pitch) + y, size, size),
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

        public static Args Parse(string[] args)
        {
            string? outPath = null;
            int cols = 4;
            int rows = 3;
            int pitch = 100;
            int defects = 3;
            int seed = 1;

            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];

                switch (flag)
                {
                    case "-o" or "--out": outPath = Next(args, ref i, flag); break;
                    case "--pitch": pitch = Number(Next(args, ref i, flag), flag); break;
                    case "--defects": defects = Number(Next(args, ref i, flag), flag); break;
                    case "--seed": seed = Number(Next(args, ref i, flag), flag); break;

                    case "--grid":
                        string[] parts = Next(args, ref i, flag).Split(',');

                        if (parts.Length != 2)
                        {
                            throw new ArgumentException("--grid 는 열,행 형식이어야 합니다 (예: 4,3)");
                        }

                        cols = Number(parts[0], flag);
                        rows = Number(parts[1], flag);
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

            return new Args
            {
                OutPath = outPath,
                Cols = cols,
                Rows = rows,
                Pitch = pitch,
                Defects = defects,
                Seed = seed,
            };
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