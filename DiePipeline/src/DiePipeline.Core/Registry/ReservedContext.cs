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

    public static readonly IReadOnlyDictionary<string, PortKind> Kinds =
        new Dictionary<string, PortKind>(StringComparer.Ordinal)
        {
            [Roi] = PortKind.Image,
            [ChipRegions] = PortKind.ImageList,
        };
}