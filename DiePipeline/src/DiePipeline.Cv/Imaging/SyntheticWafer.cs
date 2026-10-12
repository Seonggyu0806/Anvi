using OpenCvSharp;

namespace DiePipeline.Cv.Imaging;

/// <summary>심은 결함 — <b>정답</b>이다. die 안 좌표(왼쪽 위).</summary>
public readonly record struct PlantedDefect(int Col, int Row, int X, int Y, int Size);

/// <summary>정렬 표식 — 설계 도면 좌표(왼쪽 위). <b>정답</b>이다.</summary>
public readonly record struct AlignMark(int X, int Y, int Size);

/// <summary>합성 원판 — <b>그림 좌표</b>(기울인 뒤)의 중심과 반지름. 그대로 검사에 넘기면 된다.</summary>
public readonly record struct SyntheticCircle(double CenterX, double CenterY, double Radius);

/// <summary>무엇을 그릴까. 기본값은 <c>demo</c> 명령과 같다.</summary>
public sealed record SyntheticWaferSpec
{
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
    /// 원판으로 그린다 — 격자 가운데를 중심으로 원 밖을 어둡게(<see cref="SyntheticWafer.OffWafer"/>) 칠한다.
    ///
    /// ★ 반지름은 <b>짧은 변 × 0.45</b>(절반의 90%). 6×6 이상이면 온전(E1) · 걸침(E0) · 원판 밖 세 가지가 다 생긴다.
    ///   처음엔 '절반 + 피치의 10분의 1' 이었는데, 그러면 8×8 미만에서 귀퉁이 die 까지 원에 걸쳐 원판 밖이 안 생겼다.
    /// ★ 결함은 원판 안에 <b>온전히</b> 들어간 die 에만 심는다 — 정답표가 E1 검사 범위와 맞아야 한다.
    /// ★ 끄면(기본) 그림이 전과 <b>한 픽셀도</b> 다르지 않다.
    /// </summary>
    public bool Round { get; init; }

    /// <summary>
    /// 격자 둘레 여백. <b>표식이 살 자리</b>다.
    /// ★ 표식을 die 무늬 위에 놓으면 검사에서 결함으로 잡힌다.
    ///   실제 웨이퍼도 정렬 마크는 die 바깥(스크라이브·가장자리)에 있다.
    /// </summary>
    public int Margin => Math.Max(40, Pitch / 2);
}

/// <summary>
/// 시험용 합성 웨이퍼 한 장 + 그 <b>정답</b>.
///
/// <para>★ 왜 <c>demo</c> 명령 밖으로 꺼냈나 — 그림 그리는 코드가 명령행 안에 <c>private</c> 으로 갇혀 있어서
/// <b>화면도 테스트도 못 썼다.</b> 꺼내면 셋(명령행·화면·테스트)이 <b>같은 그림</b>을 본다.
/// 따로 만들면 "화면에선 되는데 명령행에선 안 된다" 가 그림 차이 때문인지 코드 때문인지 구분이 안 된다.</para>
///
/// <para>★ <b>데이터가 필요 없다.</b> 원본도 회사 자료도 안 본다 — 숫자로 그리는 그림이다.</para>
///
/// <para>★ 모든 die 를 <b>픽셀 단위로 똑같이</b> 찍는다. die-to-die 는 "이웃이 나와 같다" 를
/// 전제로 하므로, 도장이 어긋나면 온 데가 결함으로 나와 시험이 성립하지 않는다.</para>
/// </summary>
public sealed class SyntheticWafer : IDisposable
{
    /// <summary>배경 밝기. 0.5 언저리 — 어두운 결함도 밝은 결함도 넣을 여지를 남긴다.</summary>
    public const int Background = 128;

    /// <summary>결함 밝기. 배경과 차이가 0.46 이라 어떤 임계값이든 넉넉히 넘는다.</summary>
    public const int DefectGray = 10;

    /// <summary>원판 밖 밝기 — E0 임계(0.15)보다 확실히 어둡다. 가리지 않으면 걸친 die 에서 결함으로 잡히는 그 배경이다.</summary>
    public const int OffWafer = 15;

    private SyntheticWafer(
        Mat image, SyntheticWaferSpec spec, IReadOnlyList<PlantedDefect> defects, IReadOnlyList<AlignMark> marks, int slack)
    {
        Image = image;
        Spec = spec;
        Defects = defects;
        Marks = marks;
        Slack = slack;

        // ★ 정렬 규약 — 기대 좌표계의 원점은 die(0,0) 이다. 그래서 정답 Origin 은
        //   "die(0,0) 이 실제로 놓인 자리" 다. die(0,0) 은 도면에서 (여백,여백) 에 있고,
        //   그 점을 기울여 보낸 자리가 정답이다.
        double radians = spec.AngleDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        double tx = spec.ShiftX + slack;
        double ty = spec.ShiftY + slack;

        OriginX = (cos * spec.Margin) - (sin * spec.Margin) + tx;
        OriginY = (sin * spec.Margin) + (cos * spec.Margin) + ty;

        // 원판도 die(0,0) 처럼 기울여 보낸 자리가 정답이다. 반지름은 기울여도 그대로다.
        if (spec.Round)
        {
            (double cx, double cy, double radius) = DesignCircle(spec);
            Circle = new SyntheticCircle((cos * cx) - (sin * cy) + tx, (sin * cx) + (cos * cy) + ty, radius);
        }
    }

    /// <summary>8비트 회색 한 장. <b>이 객체가 소유한다</b> — Dispose 하면 같이 사라진다.</summary>
    public Mat Image { get; }

    public SyntheticWaferSpec Spec { get; }

    /// <summary>심은 결함. 웨이퍼 맵과 같은 순서(위→아래, 왼→오).</summary>
    public IReadOnlyList<PlantedDefect> Defects { get; }

    /// <summary>표식 둘 — 대각선으로 멀리.</summary>
    public IReadOnlyList<AlignMark> Marks { get; }

    /// <summary>기울이며 캔버스에 준 여유(px). 잘리지 않게 사방으로 늘린 만큼이다.</summary>
    public int Slack { get; }

    /// <summary>die(0,0) 이 그림 안에 실제로 놓인 자리 — <b>정렬이 되찾아야 할 정답</b>.</summary>
    public double OriginX { get; }

    public double OriginY { get; }

    /// <summary>원판 — <b>정답</b>. 원판으로 안 그렸으면 <c>null</c>.</summary>
    public SyntheticCircle? Circle { get; }

    /// <summary>표식의 기대 위치 — die(0,0) 을 (0,0) 으로 본 좌표. 정렬에 넘길 값이다.</summary>
    public (int X, int Y) ExpectedAt(AlignMark mark) => (mark.X - Spec.Margin, mark.Y - Spec.Margin);

    public static SyntheticWafer Draw(SyntheticWaferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Cols < 1 || spec.Rows < 1)
        {
            throw new ArgumentException($"격자는 1×1 이상이어야 합니다 (현재 {spec.Cols}×{spec.Rows})", nameof(spec));
        }

        if (spec.Defects < 0)
        {
            throw new ArgumentException("결함 수는 0 이상이어야 합니다", nameof(spec));
        }

        // ★ 웨이퍼는 장비에 "살짝" 삐뚤게 놓이는 것이지 뒤집히지 않는다.
        //   30도 같은 값을 받아 주면 그림은 나오는데 정렬이 못 푸는 상황이 생기고,
        //   그때 "정렬이 고장났나" 를 의심하게 된다. 애초에 막는다.
        if (Math.Abs(spec.AngleDeg) > 5)
        {
            throw new ArgumentException(
                $"각도는 -5~5 도여야 합니다 (현재 {spec.AngleDeg}) — 실제 웨이퍼는 이보다 크게 안 돌아갑니다",
                nameof(spec));
        }

        int margin = spec.Margin;

        // 설계 도면 — 여백 + 격자 + 여백. 표식은 여백에 놓는다(die 무늬와 안 겹치게).
        Mat design = new(
            (spec.Rows * spec.Pitch) + (2 * margin),
            (spec.Cols * spec.Pitch) + (2 * margin),
            MatType.CV_8UC1,
            Scalar.All(Background));

        try
        {
            using (Mat tile = Tile(spec.Pitch))
            {
                // ★ 같은 무늬를 도장처럼 찍는다. 한 장을 만들어 N번 복사하는 것이
                //   매번 다시 그리는 것보다 안전하다 — 그리는 계산이 한 곳이라 어긋날 수가 없다.
                for (int row = 0; row < spec.Rows; row++)
                {
                    for (int col = 0; col < spec.Cols; col++)
                    {
                        // Mat(원본, 사각형) 은 복사가 아니라 그 자리를 가리키는 창이다.
                        using Mat cell = new(design, new Rect(
                            margin + (col * spec.Pitch), margin + (row * spec.Pitch), spec.Pitch, spec.Pitch));

                        tile.CopyTo(cell);
                    }
                }
            }

            // ★ 결함보다 먼저 칠한다 — 결함은 온전한 die 에만 심으니 원판 밖과 안 겹친다.
            if (spec.Round)
            {
                PaintOffWafer(design, spec);
            }

            List<PlantedDefect> planted = Plant(design, spec);
            List<AlignMark> marks = DrawMarks(design, spec);

            Mat tilted = Tilt(design, spec, out int slack);

            return new SyntheticWafer(tilted, spec, planted, marks, slack);
        }
        finally
        {
            design.Dispose();
        }
    }

    /// <summary>
    /// 표식 조각 한 장 — 정렬이 <b>찾을 대상</b>이다. <b>호출자가 Dispose 한다.</b>
    ///
    /// ★ <b>기울이기 전</b> 모양이다. 실제로도 그렇다 —
    ///   찾는 쪽은 설계 도면의 표식을 들고 있고, 사진 속 웨이퍼만 삐뚤어져 있다.
    /// </summary>
    public Mat DrawMarkTemplate()
    {
        AlignMark first = Marks[0];

        Mat template = new(first.Size, first.Size, MatType.CV_8UC1, Scalar.All(Background));

        try
        {
            DrawMark(template, new AlignMark(0, 0, first.Size));
            return template;
        }
        catch
        {
            template.Dispose();
            throw;
        }
    }

    public void Dispose() => Image.Dispose();

    /// <summary>
    /// 정렬 표식 두 개 — <b>대각선으로 멀리</b> 놓는다.
    ///
    /// <para>★ 왜 두 개인가: 하나로는 <b>얼마나 밀렸는지</b>만 알고 <b>얼마나 돌았는지</b>를 모른다.
    /// 못 자국 하나로는 액자가 비뚤었는지 알 수 없는 것과 같다.</para>
    ///
    /// <para>★ 왜 멀리인가: 가까우면 재는 오차 1px 이 각도 오차로 크게 뻥튀기된다.</para>
    ///
    /// <para>★ 모양이 <b>비대칭</b>이다. die 무늬처럼 반복되지도 않는다 —
    /// 규칙적인 무늬에서 정합이 엉뚱한 자리를 자신 있게 짚는 함정에 안 빠지려고.</para>
    /// </summary>
    private static List<AlignMark> DrawMarks(Mat design, SyntheticWaferSpec spec)
    {
        int size = Math.Max(16, spec.Margin * 3 / 5);
        int inset = (spec.Margin - size) / 2;

        List<AlignMark> marks =
        [
            new AlignMark(inset, inset, size),
            new AlignMark(design.Cols - size - inset, design.Rows - size - inset, size),
        ];

        foreach (AlignMark mark in marks)
        {
            DrawMark(design, mark);
        }

        return marks;
    }

    private static void DrawMark(Mat target, AlignMark mark)
    {
        // 밝은 네모 + 왼쪽 위에 어두운 네모. 비대칭이라 "어느 쪽이 위인지" 가 드러난다.
        Cv2.Rectangle(target, new Rect(mark.X, mark.Y, mark.Size, mark.Size), Scalar.All(255), -1);

        Cv2.Rectangle(target,
            new Rect(mark.X + (mark.Size / 6), mark.Y + (mark.Size / 6), mark.Size / 3, mark.Size / 3),
            Scalar.All(0), -1);
    }

    /// <summary>
    /// 설계대로 반듯하게 그린 뒤, 마지막에 <b>통째로 기울인다.</b>
    ///
    /// <para>★ 실제 웨이퍼가 그렇다 — <b>설계대로 만들어지고, 장비에 삐뚤게 놓인다.</b></para>
    ///
    /// <para>★ 회전 중심은 <b>(0,0)</b> 이다. 그래야 <c>SimilarityTransform</c>(<c>p' = s·R(θ)·p + t</c>)과
    /// 식이 정확히 같아져서, 정렬이 되찾아야 할 정답이 <b>준 각도·이동 그대로</b>가 된다.</para>
    /// </summary>
    private static Mat Tilt(Mat design, SyntheticWaferSpec spec, out int slack)
    {
        double radians = spec.AngleDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // ★ 기울이면 그림이 캔버스 밖으로 밀려난다. 잘리면 표식을 못 찾으니 여유를 준다.
        double diagonal = Math.Sqrt((design.Cols * (double)design.Cols) + (design.Rows * (double)design.Rows));

        slack = (int)Math.Ceiling(diagonal * Math.Abs(sin))
                + Math.Max(Math.Abs(spec.ShiftX), Math.Abs(spec.ShiftY)) + 4;

        using Mat transform = new(2, 3, MatType.CV_64FC1);

        transform.Set(0, 0, cos);
        transform.Set(0, 1, -sin);
        transform.Set(0, 2, spec.ShiftX + (double)slack);
        transform.Set(1, 0, sin);
        transform.Set(1, 1, cos);
        transform.Set(1, 2, spec.ShiftY + (double)slack);

        Mat tilted = new();

        try
        {
            Cv2.WarpAffine(design, tilted, transform,
                new Size(design.Cols + (2 * slack), design.Rows + (2 * slack)),
                InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(spec.Round ? OffWafer : Background));

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

            Cv2.Rectangle(tile,
                new Rect(margin, margin, pitch - (2 * margin), pitch - (2 * margin)),
                Scalar.All(230), thickness);

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

    private static List<PlantedDefect> Plant(Mat wafer, SyntheticWaferSpec spec)
    {
        // ★ 레시피의 minArea 500 을 넉넉히 넘겨야 한다. 작게 심으면 "안 잡혔다" 가
        //   파이프라인 잘못인지 너무 작아서인지 구분이 안 된다.
        int size = Math.Max(24, spec.Pitch * 3 / 10);

        int least = (spec.Pitch / 10) + 3;                  // 테두리 안쪽
        int mostX = (spec.Pitch * 68 / 100) - size;         // 세로선 왼쪽
        int mostY = (spec.Pitch * 88 / 100) - size;

        if (mostX < least || mostY < least)
        {
            throw new ArgumentException(
                $"피치 {spec.Pitch} 는 결함({size}×{size})을 심기에 좁습니다 — 100 이상으로 주세요", nameof(spec));
        }

        int total = spec.Cols * spec.Rows;

        // ★ 원판이면 온전한 die 중에서만 고른다. 원판이 아니면 0..total 그대로라 난수 호출 순서가 전과 같다.
        int[] candidates = spec.Round
            ? [.. Enumerable.Range(0, total).Where(i => Whole(spec, i % spec.Cols, i / spec.Cols))]
            : [.. Enumerable.Range(0, total)];

        if (spec.Defects > candidates.Length)
        {
            throw new ArgumentException(
                spec.Round
                    ? $"결함 {spec.Defects}개는 원판 안에 온전히 들어간 die {candidates.Length}개보다 많습니다 — "
                      + "die 하나에 하나씩만 심습니다"
                    : $"결함 {spec.Defects}개는 die {total}개보다 많습니다 — die 하나에 하나씩만 심습니다",
                nameof(spec));
        }

        // ★ 씨를 받는다. 같은 씨면 같은 그림이 나온다 —
        //   "어제 결과와 다른데?" 가 프로그램 탓인지 그림 탓인지 구분할 수 있어야 한다.
        //   ★★ 아래 난수 호출 순서를 바꾸면 같은 씨라도 다른 그림이 된다. 건드리지 말 것.
        Random random = new(spec.Seed);

        int[] order = [.. candidates.OrderBy(_ => random.Next())];

        List<PlantedDefect> planted = [];

        foreach (int index in order.Take(spec.Defects))
        {
            int col = index % spec.Cols;
            int row = index / spec.Cols;

            int x = random.Next(least, mostX + 1);
            int y = random.Next(least, mostY + 1);

            Cv2.Rectangle(wafer,
                new Rect(spec.Margin + (col * spec.Pitch) + x, spec.Margin + (row * spec.Pitch) + y, size, size),
                Scalar.All(DefectGray), -1);

            planted.Add(new PlantedDefect(col, row, x, y, size));
        }

        // 웨이퍼 맵과 같은 순서(위→아래, 왼→오)로 — 눈으로 대조하기 쉽게.
        return [.. planted.OrderBy(spot => (spot.Row, spot.Col))];
    }

    /// <summary>설계 도면 좌표의 원판 — 격자 가운데 · 짧은 변 × 0.45.</summary>
    private static (double CenterX, double CenterY, double Radius) DesignCircle(SyntheticWaferSpec spec)
        => (spec.Margin + (spec.Cols * spec.Pitch / 2.0),
            spec.Margin + (spec.Rows * spec.Pitch / 2.0),
            Math.Min(spec.Cols, spec.Rows) * spec.Pitch * 0.45);

    /// <summary>
    /// die 네 귀퉁이가 전부 원판 안인가 — 웨이퍼 층이 E1 이라고 부르는 그 칸이다(같은 규칙).
    /// </summary>
    private static bool Whole(SyntheticWaferSpec spec, int col, int row)
    {
        (double cx, double cy, double radius) = DesignCircle(spec);

        double x0 = spec.Margin + (col * spec.Pitch);
        double y0 = spec.Margin + (row * spec.Pitch);

        (double X, double Y)[] corners =
            [(x0, y0), (x0 + spec.Pitch, y0), (x0, y0 + spec.Pitch), (x0 + spec.Pitch, y0 + spec.Pitch)];

        return corners.All(p => Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy))) <= radius);
    }

    /// <summary>
    /// 원판 밖을 <see cref="OffWafer"/> 로 칠한다.
    ///
    /// ★ <b>픽셀 중심</b>이 원 안이면 원판 안 — 웨이퍼 층이 die 마다 만드는 원판 마스크와 같은 규칙이다.
    ///   규칙이 다르면 경계에서 몇 픽셀이 '마스크는 안인데 그림은 어두운' 상태가 되어, 가려도 결함이 남는다.
    /// </summary>
    private static void PaintOffWafer(Mat design, SyntheticWaferSpec spec)
    {
        (double cx, double cy, double radius) = DesignCircle(spec);

        // 크기는 한 번만 읽는다 — Mat 의 Rows/Cols 는 부를 때마다 OpenCV 까지 다녀온다.
        int rows = design.Rows;
        int cols = design.Cols;

        for (int y = 0; y < rows; y++)
        {
            double dy = y + 0.5 - cy;
            double reach2 = (radius * radius) - (dy * dy);

            int from = int.MaxValue;
            int to = int.MinValue;

            if (reach2 >= 0)
            {
                double reach = Math.Sqrt(reach2);
                from = Math.Max(0, (int)Math.Ceiling(cx - reach - 0.5));
                to = Math.Min(cols - 1, (int)Math.Floor(cx + reach - 0.5));
            }

            if (from > to)
            {
                // 이 행은 통째로 원판 밖
                Cv2.Rectangle(design, new Rect(0, y, cols, 1), Scalar.All(OffWafer), -1);
                continue;
            }

            // 원판 안 = [from, to]. 그 왼쪽과 오른쪽만 칠한다.
            if (from > 0)
            {
                Cv2.Rectangle(design, new Rect(0, y, from, 1), Scalar.All(OffWafer), -1);
            }

            if (to < cols - 1)
            {
                Cv2.Rectangle(design, new Rect(to + 1, y, cols - 1 - to, 1), Scalar.All(OffWafer), -1);
            }
        }
    }
}
