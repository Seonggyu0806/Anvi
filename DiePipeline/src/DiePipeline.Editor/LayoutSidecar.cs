using System.Text.Json;
using DiePipeline.Core.Configuration;

namespace DiePipeline.Editor;

/// <summary>
/// 캔버스 좌표를 레시피와 <b>따로</b> 보관하는 곁다리 파일(<c>*.layout.json</c>).
///
/// ★ 왜 레시피에 안 넣나:
///   레시피는 <b>검사가 무엇을 하는지</b> 적는 파일이다. 거기에 "이 블록은 왼쪽에서 320픽셀"이
///   섞이면, 블록을 화면에서 끌어 옮기기만 해도 <b>레시피가 바뀐 것으로 보인다</b> —
///   검사 내용은 한 글자도 안 바뀌었는데.
///
///   그러면 git 이력이 못 쓰게 된다. "3주차에 임계값을 왜 0.2로 바꿨지"를 찾으려고
///   이력을 뒤지는데, 블록 옮긴 기록 수십 개에 묻힌다.
/// </summary>
public sealed class LayoutSidecar
{
    public sealed record NodeLayout(double X, double Y);

    public Dictionary<string, NodeLayout> Nodes { get; init; } = new(StringComparer.Ordinal);

    public static LayoutSidecar From(EditorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return new LayoutSidecar
        {
            Nodes = graph.Nodes.ToDictionary(n => n.Id, n => new NodeLayout(n.X, n.Y), StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// 좌표를 그래프에 되먹인다(불러올 때).
    ///
    /// ★ 사이드카에 없는 노드는 <b>건드리지 않는다</b>. 레시피만 받고 좌표 파일이 없을 수도 있고,
    ///   누가 레시피에만 노드를 손으로 더 적어 넣었을 수도 있다.
    ///   그때 0,0으로 밀어 버리면 블록들이 왼쪽 위 구석에 겹쳐 쌓인다.
    /// </summary>
    public void ApplyTo(EditorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        foreach (EditorNode node in graph.Nodes)
        {
            if (Nodes.TryGetValue(node.Id, out NodeLayout? layout))
            {
                node.X = layout.X;
                node.Y = layout.Y;
            }
        }
    }

    // ★ 레시피와 <b>같은</b> JSON 설정을 쓴다(그래서 RecipeLoader.Options를 public으로 열었다).
    //   따로 만들면 한쪽만 고쳤을 때 조용히 어긋난다 — 주석 허용 여부 같은 게 달라진다.
    public string Serialize() => JsonSerializer.Serialize(this, RecipeLoader.Options);

    public static LayoutSidecar Parse(string json)
        => JsonSerializer.Deserialize<LayoutSidecar>(json, RecipeLoader.Options) ?? new LayoutSidecar();
}