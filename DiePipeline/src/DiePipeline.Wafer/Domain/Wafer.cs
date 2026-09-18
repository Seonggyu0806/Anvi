using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Wafer.Domain;

/// <summary>
/// die 가 웨이퍼의 어느 존인가.
///
/// <code>
///   E1 = 이웃이 사방에 있다   → die-to-die 가능
///   E0 = 변두리. 이웃이 없거나 잘려 있다 → 골든을 못 만든다 → 단일 임계 검사
/// </code>
///
/// ★ <b>교수님은 <c>E1, E0</c> 둘뿐인데 우리는 <c>Unknown = 0</c> 을 하나 더 뒀다.</b>
///   C# 에서 enum 의 기본값은 0이다. 초기화를 빠뜨린 값이 조용히 흘러들면
///   그게 <c>E1</c> 이 되어 <b>이웃 없는 die 에 die-to-die 를 시도</b>하게 된다.
///   존에는 "무해한 기본값"이 없으므로, "정하지 않았다"를 값으로 만들고 명시적으로 거부한다.
///   (Lesson16 에서 배운 것이다.)
/// </summary>
public enum WaferZone
{
    /// <summary>판정되지 않음. 받으면 거부해야 한다.</summary>
    Unknown = 0,

    /// <summary>유효 die — 이웃이 있다.</summary>
    E1 = 1,

    /// <summary>변두리 die — 골든을 만들 수 없다.</summary>
    E0 = 2,
}

/// <summary>
/// die 하나의 격자 인덱스·원점·존.
///
/// ★ <see cref="Origin"/> 은 <b>정렬을 이미 적용한</b> 실제 좌표다.
///   "설계상 여기 있어야 한다"가 아니라 "실제로 여기 있다"이다.
/// </summary>
public readonly record struct DieOrigin(int Col, int Row, PointF Origin, WaferZone Zone)
{
    public override string ToString() => $"die(c{Col},r{Row}) {Origin} {Zone}";
}

/// <summary>
/// 2D 유사변환 — 회전 + 균등 스케일 + 이동. <c>p' = s·R(θ)·p + t</c>
///
/// ★ 자유도를 일부러 4개로 묶었다(이동 2 + 회전 1 + 스케일 1).
///   어파인은 6개(기울임·축별 스케일까지)지만 <b>웨이퍼는 강체라 찌그러지지 않는다.</b>
///   표현할 수 없는 것을 표현하지 않으면 측정 노이즈에 그만큼 덜 흔들린다.
/// </summary>
public readonly record struct SimilarityTransform(double RotationDeg, double Scale, double Tx, double Ty)
{
    public static SimilarityTransform Identity { get; } = new(0, 1, 0, 0);

    /// <summary>
    /// 점 하나에 변환을 적용한다.
    ///
    /// ★ 순서가 중요하다 — <b>회전·스케일을 먼저, 이동을 나중에.</b>
    ///   반대로 하면 원점이 아닌 곳을 중심으로 도는 셈이 되어 결과가 달라진다.
    /// </summary>
    public PointF Apply(PointF p)
    {
        double rad = RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad) * Scale;
        double sin = Math.Sin(rad) * Scale;

        return new PointF((cos * p.X) - (sin * p.Y) + Tx, (sin * p.X) + (cos * p.Y) + Ty);
    }
}

/// <summary>
/// 웨이퍼가 스테이지에 어떻게 놓였는가.
///
/// ★ <see cref="Origin"/> 이 die(0,0) 의 실제 원점이다.
///   정렬 마크를 찾아 푸는 것은 Step 6에서 한다. 지금은 <see cref="Identity"/> 만 쓴다.
/// </summary>
public sealed record WaferAlignment
{
    /// <summary>die 격자 원점(웨이퍼 좌표).</summary>
    public required PointF Origin { get; init; }

    public required double RotationDeg { get; init; }

    /// <summary>균등 스케일. 보통 1 — 카메라 배율이 고정이면 풀지 않는다.</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>마크 매칭 점수. 정렬을 얼마나 믿을 수 있는가.</summary>
    public required double MeanScore { get; init; }

    /// <summary>아무것도 안 하는 정렬 — 정렬을 아직 안 붙였을 때.</summary>
    public static WaferAlignment Identity { get; } =
        new() { Origin = new PointF(0, 0), RotationDeg = 0, Scale = 1.0, MeanScore = 1.0 };

    public override string ToString()
        => $"origin={Origin} angle={RotationDeg:F3}° scale={Scale:F4} score={MeanScore:F3}";
}

/// <summary>
/// die 원점 격자 — <b>미리 계산해 둔 목록</b>이다.
///
/// ★ 왜 "물어보면 계산"이 아니라 목록인가:
///   정렬이 붙으면 원점이 회전·이동을 거친다. 그 계산을 부를 때마다 하면
///   <b>같은 die 의 원점이 호출마다 미세하게 달라질</b> 여지가 생긴다(부동소수).
///   한 번 계산해 굳혀 두면 격자가 "사실"이 되고, 그 뒤로는 아무도 못 바꾼다.
///
/// ★ 순서는 <b>row-major 고정</b>이다(왼→오, 위→아래).
///   die 인덱스가 결과에 들어가므로, 순서가 흔들리면 같은 웨이퍼를 두 번 검사했을 때
///   결과 비교가 매번 깨진다.
/// </summary>
public sealed record DieGrid
{
    public required IReadOnlyList<DieOrigin> Dies { get; init; }

    /// <summary>die 원점 사이의 간격. <b>die 크기가 아니다.</b></summary>
    public required PointF Pitch { get; init; }

    public required int Cols { get; init; }

    public required int Rows { get; init; }

    /// <summary>die 하나의 실제 폭. 피치보다 작으면 그 차이가 스크라이브 라인이다.</summary>
    public required int DieWidth { get; init; }

    public required int DieHeight { get; init; }

    public int Count => Dies.Count;

    public bool Contains(int col, int row) => col >= 0 && col < Cols && row >= 0 && row < Rows;

    /// <summary>
    /// 인덱스로 찾기. row-major 라서 <c>row × Cols + col</c> 이다.
    ///
    /// ★ 격자 밖이면 <b>던진다.</b> 조용히 가장자리 die 를 돌려주면
    ///   "검사했다고 믿는데 엉뚱한 자리를 본" 상태가 된다.
    /// </summary>
    public DieOrigin At(int col, int row)
    {
        if (!Contains(col, row))
        {
            throw new ArgumentOutOfRangeException(
                nameof(col), $"die(c{col},r{row})는 격자 {Cols}×{Rows} 밖입니다");
        }

        return Dies[(row * Cols) + col];
    }

    /// <summary>
    /// 이 die 를 잘라낼 사각형.
    ///
    /// ★ 자리 계산이 여기 <b>한 곳에만</b> 있어야 한다.
    ///   러너·뷰어·에디터에 흩뿌리면 반드시 어긋난다 — 그때 증상은
    ///   "결함이 왜 이렇게 많지?"로만 나타나서 원인을 못 찾는다.
    /// </summary>
    public Roi RoiOf(DieOrigin die)
        => new((int)Math.Round(die.Origin.X), (int)Math.Round(die.Origin.Y), DieWidth, DieHeight);

    /// <summary>순회 순서. 이미 row-major 로 담겨 있다.</summary>
    public IEnumerable<DieOrigin> InRowMajorOrder() => Dies;
}

/// <summary>
/// 웨이퍼 전체에서 결함 하나 — die 안의 결함에 <b>어느 die였는지</b>를 붙인 것.
/// </summary>
public sealed record WaferDefect
{
    public required int DieCol { get; init; }

    public required int DieRow { get; init; }

    public required WaferZone Zone { get; init; }

    /// <summary>
    /// die 레시피가 낸 원본. <b>ROI 상대 좌표 그대로</b> 보존한다.
    ///
    /// ★ 절대좌표로 덮어쓰지 않는 이유: die 레시피의 테스트가 이 값을 기준으로 굳어 있다.
    ///   여기서 바꾸면 아래층이 전부 깨지고, "원래 무엇이었나"를 잃는다.
    /// </summary>
    public required Defect Defect { get; init; }

    /// <summary>
    /// 웨이퍼 절대 X. <b>변환 전에는 null 이다.</b>
    ///
    /// ★ <c>double?</c> 인 것이 설계다 — "아직 변환 안 됐다"가 <b>타입에 드러난다.</b>
    ///   0으로 두면 "원점에 있는 결함"과 구분이 안 된다.
    /// </summary>
    public double? AbsX { get; init; }

    public double? AbsY { get; init; }
}

/// <summary>웨이퍼 전체 결함 목록.</summary>
public sealed record WaferDefectList
{
    public required IReadOnlyList<WaferDefect> Items { get; init; }

    public int Count => Items.Count;

    public static WaferDefectList Empty { get; } = new() { Items = [] };

    public IEnumerable<WaferDefect> E1 => Items.Where(d => d.Zone == WaferZone.E1);

    public IEnumerable<WaferDefect> E0 => Items.Where(d => d.Zone == WaferZone.E0);
}

/// <summary>
/// die 하나의 검사 요약.
///
/// ★ 결함이 0개인 die 도 반드시 기록한다.
///   <b>"검사했는데 깨끗했다"와 "검사를 안 했다"는 다르다.</b>
/// </summary>
public sealed record DieResult
{
    public required int Col { get; init; }

    public required int Row { get; init; }

    public required WaferZone Zone { get; init; }

    public required int DefectCount { get; init; }
}