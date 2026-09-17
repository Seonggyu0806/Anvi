namespace DiePipeline.Core.Domain;

/// <summary>결함 경계 사각형(픽셀).</summary>
public readonly record struct BoundingBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public override string ToString() => $"({X},{Y}) {Width}×{Height}";
}

/// <summary>부동소수 좌표 — 결함 중심은 정수로 안 떨어진다.</summary>
public readonly record struct PointF(double X, double Y)
{
    public override string ToString() => $"({X:F1},{Y:F1})";
}

/// <summary>결함 하나 = 연결된 픽셀 덩어리 하나.</summary>
public sealed record Defect
{
    public required int Id { get; init; }

    public required BoundingBox Bounds { get; init; }

    /// <summary>픽셀 수.</summary>
    public required int Area { get; init; }

    public required PointF Centroid { get; init; }

    /// <summary>
    /// 이 덩어리 안의 <b>평균 밝기</b>. 밝기 이미지를 안 넘기면 null.
    ///
    /// ★ 마스크는 "있다/없다"뿐이라 <b>얼마나 진한 결함인지</b>를 모른다.
    ///   그건 원본을 다시 봐야 알 수 있어서, 원본을 넘길 때만 계산한다.
    /// </summary>
    public double? MeanIntensity { get; init; }

    /// <summary>이 덩어리 안의 가장 밝은 값. 밝기 이미지를 안 넘기면 null.</summary>
    public double? MaxIntensity { get; init; }

    /// <summary>
    /// 원형도 — 1에 가까우면 동그랗고, 0에 가까우면 길쭉하다.
    /// 계산 비용이 있어 <c>computeShape</c>를 켤 때만 낸다.
    /// </summary>
    public double? Circularity { get; init; }

    /// <summary>
    /// 결함 유형 이름 — <c>"Scratch"</c> · <c>"Blob"</c> · <c>"Particle"</c>.
    /// <c>DefectClassify</c>를 거치지 않았으면 null.
    ///
    /// ★ enum이 아니라 문자열인 이유: 유형은 <b>공정마다 다르다.</b>
    ///   enum으로 박으면 새 유형이 생길 때마다 Core를 고쳐야 한다.
    /// </summary>
    public string? ClassLabel { get; init; }

    public override string ToString() => $"#{Id} {Bounds} area={Area} c={Centroid}";
}
