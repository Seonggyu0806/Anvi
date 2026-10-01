using System.Text.Json;

namespace DiePipeline.Editor;

/// <summary>
/// 그래프를 고치는 일들. <b>WPF를 모른다 → 테스트가 된다.</b>
///
/// ★★ 왜 화면에서 떼어 냈나:
///   <c>DiePipeline.Wpf</c>는 <c>net10.0-windows</c>라 <c>net10.0</c>인 테스트 프로젝트가
///   <b>참조조차 못 한다.</b> 거기 둔 판단은 영원히 테스트 밖이다.
///   그래서 "무엇을 어떻게 고치는가"는 여기에 두고, 화면은 <b>부르기만</b> 한다.
///
///   5주차 완료 기준 5번("화면 없이 테스트가 돈다")이 이 파일로 지켜진다.
/// </summary>
public static class GraphOps
{
    /// <summary>겹치지 않는 새 id. 타입명 소문자 + 번호(binarize1, binarize2 …).</summary>
    public static string UniqueId(EditorGraph graph, string type)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(type);

        string prefix = type.ToLowerInvariant();
        int number = 0;
        string id;

        // ★ 빈자리를 찾을 때까지 올린다. 지웠다 다시 넣으면 번호가 건너뛸 수 있는데,
        //   그게 겹치는 것보다 낫다 — 겹치면 검증기가 막아서 아무것도 못 한다.
        do
        {
            id = prefix + ++number;
        }
        while (graph.Nodes.Any(n => n.Id == id));

        return id;
    }

    /// <summary>
    /// 블록 하나와 <b>거기 닿은 선 전부</b>를 지운다.
    ///
    /// ★ 선을 같이 안 지우면 없는 블록을 가리키는 선이 남는다.
    ///   그러면 검증기가 "이름표를 아무도 안 만든다"는 영문 모를 오류를 뱉는데,
    ///   화면에는 그 선이 안 보여서 <b>원인을 못 찾는다.</b>
    /// </summary>
    public static void Remove(EditorGraph graph, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        graph.Nodes.RemoveAll(n => n.Id == nodeId);
        graph.Connections.RemoveAll(c => c.TargetNodeId == nodeId || c.SourceNodeId == nodeId);
    }

    /// <summary>
    /// 선을 잇는다. <paramref name="sourceNodeId"/>가 <c>null</c>이면 컨텍스트 입력에서 오는 선이고,
    /// 이때 <paramref name="sourcePort"/>는 이름표 그 자체다(예: "roi").
    ///
    /// ★★ <b>입력 포트 하나에는 선이 하나다.</b> 이미 있으면 갈아 끼운다.
    ///   둘을 허용하면 "어느 쪽이 들어오는지"가 정해지지 않고,
    ///   레시피로 내보낼 때 <c>Dictionary</c>가 <b>조용히 하나를 덮어쓴다</b> —
    ///   화면엔 선이 둘로 보이는데 실제로는 하나만 산다. 그 어긋남을 여기서 막는다.
    /// </summary>
    public static EditorConnection Connect(
        EditorGraph graph, string? sourceNodeId, string sourcePort, string targetNodeId, string targetPort)
    {
        ArgumentNullException.ThrowIfNull(graph);

        graph.Connections.RemoveAll(c => c.TargetNodeId == targetNodeId && c.TargetPort == targetPort);

        EditorConnection connection = new(sourceNodeId, sourcePort, targetNodeId, targetPort);
        graph.Connections.Add(connection);

        return connection;
    }

    /// <summary>
    /// 블록을 복제한다. 타입·끄기 여부·파라미터를 베끼고 <b>새 id</b>로 살짝 비껴 놓는다.
    ///
    /// ★ 선은 안 베낀다. 입력 포트엔 선이 하나라 복제본까지 이으면 원본 선이 끊긴다.
    /// </summary>
    public static EditorNode Duplicate(EditorGraph graph, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        EditorNode source = graph.Nodes.FirstOrDefault(n => n.Id == nodeId)
                            ?? throw new ArgumentException($"'{nodeId}' 블록이 없습니다", nameof(nodeId));

        EditorNode copy = new()
        {
            Id = UniqueId(graph, source.Type),
            Type = source.Type,
            Enabled = source.Enabled,
            X = source.X + 30,
            Y = source.Y + 30,
        };

        foreach (KeyValuePair<string, JsonElement> parameter in source.Params)
        {
            // ★★ Clone() 이 꼭 필요하다. 안 하면 복제본과 원본이 <b>같은 값을 들여다본다.</b>
            //   복제본 파라미터를 고치면 원본까지 바뀌는데, 화면으로는 절대 못 찾는다.
            copy.Params[parameter.Key] = parameter.Value.Clone();
        }

        graph.Nodes.Add(copy);

        return copy;
    }

    /// <summary>
    /// 좌표가 없는 그래프를 격자로 늘어놓는다(불러온 레시피에 좌표 파일이 없을 때).
    ///
    /// ★ <paramref name="left"/>부터 시작하는 이유: 캔버스 왼쪽 열은 컨텍스트 입력 블록 자리다.
    ///   0부터 깔면 둘이 겹쳐서 양쪽 다 안 읽힌다.
    /// </summary>
    public static void AutoLayout(EditorGraph graph, double left, int columns = 4)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            graph.Nodes[i].X = left + (i % columns * 180);
            graph.Nodes[i].Y = 40 + (i / columns * 170);
        }
    }
}