using System.Text.Json;

namespace DiePipeline.Editor;

/// <summary>
/// 편집기 그래프의 노드 하나.
///
/// ★ 포트를 여기 저장하지 않는다. 포트는 디스크립터에서 파생된다 — 단일 진실원.
///   여기에 또 적으면 디스크립터를 고쳤을 때 둘이 조용히 어긋난다.
/// </summary>
public sealed class EditorNode
{
    public required string Id { get; set; }

    public required string Type { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>캔버스 좌표. ★ 레시피에는 안 들어간다 — <c>*.layout.json</c>으로 따로 뺀다.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// 파라미터명 → 값. <b>NodeConfig.Params와 달리 낱개로 쪼개 들고 있다</b> —
    /// 속성폼은 칸 하나만 고치기 때문이다. 내보낼 때 다시 JSON 객체 하나로 뭉친다.
    /// </summary>
    public Dictionary<string, JsonElement> Params { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 선 하나.
///
/// ★ <see cref="SourceNodeId"/>가 <c>null</c>이면 <b>컨텍스트 입력</b>에서 들어오는 선이고,
///   이때 <see cref="SourcePort"/>는 이름표 그 자체다(예: "roi").
///   캔버스에서 roi는 '출력만 있는 블록'으로 그려진다.
/// </summary>
public sealed record EditorConnection(string? SourceNodeId, string SourcePort, string TargetNodeId, string TargetPort);

/// <summary>
/// 캔버스가 편집하는 그래프. 레시피와 양방향으로 오간다(<see cref="RecipeMapper"/>).
///
/// ★ 레시피와 무엇이 다른가:
///   레시피는 <b>줄 세운 목록</b>이고, 이어짐은 '이름표가 같음'으로만 표현된다.
///   그래프는 <b>선</b>으로 이어지고 순서가 없다.
///   그래서 내보낼 때 위상정렬로 순서를 만들어야 한다.
/// </summary>
public sealed class EditorGraph
{
    public string RecipeId { get; set; } = "untitled";

    public string Description { get; set; } = "";

    public List<EditorNode> Nodes { get; init; } = [];

    public List<EditorConnection> Connections { get; init; } = [];
}