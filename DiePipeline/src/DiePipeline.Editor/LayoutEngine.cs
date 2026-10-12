namespace DiePipeline.Editor;

/// <summary>
/// 자동 배치 — 흐름 순서대로 <b>왼쪽에서 오른쪽으로</b> 놓는다(교수님 <c>LayoutEngine.Layered</c> 와 같은 방식).
///
/// ★ 줄(열)은 '이 블록 앞에 블록이 몇 단 있나'(가장 긴 의존 경로)로 정한다. 골든 → 빼기 → 이진화 → 덩어리 세기가
///   한 줄로 읽힌다. 전에는 목록 순서대로 4칸씩 꺾어 놓아, 흐름과 상관없이 위아래로 흩어졌다.
/// ★ 같은 열 안에서는 레시피에 적힌 순서대로 위에서 아래로.
/// ★ 컨텍스트 입력 블록(roi …)은 대상이 아니다 — 화면이 캔버스 왼쪽 열에 따로 놓는다. 그래서 <paramref name="left"/> 부터 깐다.
/// ★ 화면을 모른다 — 숫자만 계산하니 시험된다.
/// </summary>
public static class LayoutEngine
{
    /// <summary>
    /// 열 사이 간격(px) — Nodify 블록(포트 이름 · 체크박스)이 겹치지 않을 만큼.
    /// ★ 가장 넓은 블록(웨이퍼 InspectDies — 출력 셋 + 체크박스)이 260 이면 옆 열에 닿아서 300 으로 늘렸다.
    /// </summary>
    public const double ColumnGap = 300;

    /// <summary>같은 열 안 줄 사이 간격(px).</summary>
    public const double RowGap = 160;

    /// <param name="left">첫 열의 x — 왼쪽은 컨텍스트 입력 자리라 비운다.</param>
    /// <param name="top">첫 줄의 y.</param>
    public static void Layered(EditorGraph graph, double left, double top = 60)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // 블록끼리의 의존 — 받는 블록 ← 주는 블록들(컨텍스트에서 오는 선은 빼고)
        HashSet<string> ids = new(graph.Nodes.Select(n => n.Id), StringComparer.Ordinal);
        Dictionary<string, List<string>> dependsOn = graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (EditorConnection c in graph.Connections)
        {
            if (c.SourceNodeId is { } from && ids.Contains(from) && ids.Contains(c.TargetNodeId) && from != c.TargetNodeId)
            {
                dependsOn[c.TargetNodeId].Add(from);
            }
        }

        Dictionary<string, int> level = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);

        // 가장 긴 의존 경로 = 열 번호. ★ 고리가 있으면(검증기가 막지만) 되돌아오는 선은 0 으로 쳐서 무한히 돌지 않는다.
        int Level(string id)
        {
            if (level.TryGetValue(id, out int known))
            {
                return known;
            }

            if (!visiting.Add(id))
            {
                return 0;
            }

            int deepest = dependsOn[id].Select(dep => Level(dep) + 1).DefaultIfEmpty(0).Max();

            visiting.Remove(id);
            level[id] = deepest;
            return deepest;
        }

        Dictionary<int, int> rows = [];

        foreach (EditorNode node in graph.Nodes)
        {
            int column = Level(node.Id);
            int row = rows.GetValueOrDefault(column);
            rows[column] = row + 1;

            node.X = left + (column * ColumnGap);
            node.Y = top + (row * RowGap);
        }
    }
}