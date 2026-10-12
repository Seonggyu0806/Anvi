namespace DiePipeline.Core.Registry;

/// <summary>
/// 실행기가 칠판에 미리 올려 주는 <b>예약 이름표</b>.
///
/// ★ 검증기의 출발점이다. "이 이름표를 앞의 어떤 노드도 만들지 않았다"를 판단하려면
///   "노드가 만들지 않았어도 이미 있는 것"의 목록이 필요하다.
/// </summary>
public static class ReservedContext
{
    /// <summary>검사 대상 die 한 장.</summary>
    public const string Roi = "roi";

    /// <summary>골든을 만들 정상 die들.</summary>
    public const string ChipRegions = "chipRegions";

    /// <summary>
    /// 이 die 에서 <b>원판 안</b>인 곳 — Gray8, 안 255 · 밖 0. 레시피가 <c>contextInputs</c> 에 적었을 때만 올린다.
    ///
    /// ★ 교수님이 정렬을 붙이며 <c>alignSrc</c>·<c>alignTemplate</c> 을 예약 입력으로 더한 것과 같은 방식이다.
    ///   가리는 일은 레시피의 <c>MaskApply</c> 블록이 한다 — 실행기는 재료만 올린다.
    /// </summary>
    public const string WaferMask = "waferMask";

    /// <summary>
    /// 표식을 찾을 그림 — 웨이퍼 레시피의 표식 찾기 블록이 받는다. 이름은 교수님과 같다.
    ///
    /// ★ 실행기가 표식마다 '있을 법한 자리 ± 찾는 범위' <b>창을 잘라</b> 올린다. 찾는 범위가 0 이면 사진 전체 한 번.
    ///   원판 원본은 통째로 못 읽는다 — 창 읽기는 블록이 아니라 실행기의 일이다(교수님 Phase5 #5.10).
    /// </summary>
    public const string AlignSrc = "alignSrc";

    /// <summary>표식 조각 — 표식 찾기 블록이 <see cref="AlignSrc"/> 에서 찾을 대상. 이름은 교수님과 같다.</summary>
    public const string AlignTemplate = "alignTemplate";

    public static readonly IReadOnlyDictionary<string, PortKind> Kinds =
        new Dictionary<string, PortKind>(StringComparer.Ordinal)
        {
            [Roi] = PortKind.Image,
            [ChipRegions] = PortKind.ImageList,
            [WaferMask] = PortKind.Image,
            [AlignSrc] = PortKind.Image,
            [AlignTemplate] = PortKind.Image,
        };
}