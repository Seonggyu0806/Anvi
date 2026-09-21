namespace DiePipeline.Core.Domain;

/// <summary>템플릿을 찾아낸 자리 하나.</summary>
public sealed record Match
{
    /// <summary>
    /// 찾은 자리 — <b>템플릿의 왼쪽 위 모서리</b>다. <b>가운데가 아니다.</b>
    ///
    /// ★ <c>matchTemplate</c> 의 규약이 그렇다. 여기서 중심으로 바꿔 두면
    ///   "어느 쪽이었더라" 를 쓰는 쪽마다 다시 헷갈린다 — <b>규약 그대로 들고 다니고 한 번만 적어 둔다.</b>
    ///   <c>demo</c> 명령이 찍는 "표식 설계 자리" 도 왼쪽 위라 그대로 비교된다.
    /// </summary>
    public required PointF Location { get; init; }

    /// <summary>
    /// 얼마나 닮았나. <b>언제나 "클수록 좋다"</b>이다.
    ///
    /// ★ 방식마다 방향이 다르다(<c>SqDiffNormed</c> 는 낮을수록 좋음).
    ///   그걸 그대로 들고 다니면 "이번엔 어느 쪽이 좋은 거지?" 를 매번 따져야 하고,
    ///   <b>가드가 반대로 걸리는</b> 사고가 난다. 입구에서 한 방향으로 맞춰 둔다.
    /// </summary>
    public required double Score { get; init; }

    public override string ToString() => $"{Location} score={Score:F3}";
}

/// <summary>찾아낸 자리들. <b>점수 높은 순</b>이다.</summary>
public sealed record MatchList
{
    public required IReadOnlyList<Match> Items { get; init; }

    public int Count => Items.Count;

    public static MatchList Empty { get; } = new() { Items = [] };
}