namespace DiePipeline.Editor;

/// <summary>
/// 되돌리기 · 다시 — 그래프를 <b>통째로 복사해</b> 쌓아 둔다(교수님 <c>GraphHistory</c> 와 같은 방식).
///
/// ★ 무엇을 했는지(블록 추가 · 선 잇기 …)를 하나하나 기록해 거꾸로 돌리는 방식보다 단순하다 — 동작이 늘어도 여기는 안 바뀐다.
///   그래프는 블록 수십 개라 통째로 복사해도 가볍다.
/// ★ 구조가 바뀌기 <b>직전</b>(블록 추가 · 삭제 · 복제 · 선 잇기 · 끊기)에 <see cref="Push"/> 한다.
///   파라미터 한 글자마다 쌓으면 되돌리기를 수십 번 눌러야 한 단계가 돌아간다.
/// ★ 복사본은 <b>깊은 복사</b>다 — 얕게 베끼면 뒤의 편집이 쌓아 둔 과거까지 바꾼다.
/// </summary>
public sealed class GraphHistory
{
    private readonly Stack<EditorGraph> _undo = new();
    private readonly Stack<EditorGraph> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>바꾸기 <b>직전</b>에 부른다 — 지금 상태를 되돌릴 자리로 쌓고, '다시' 는 비운다(새 갈래가 생겼으니).</summary>
    public void Push(EditorGraph current)
    {
        ArgumentNullException.ThrowIfNull(current);

        _undo.Push(Copy(current));
        _redo.Clear();
    }

    /// <summary>한 단계 전으로. 되돌릴 것이 없으면 지금 그대로.</summary>
    public EditorGraph Undo(EditorGraph current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CanUndo)
        {
            return current;
        }

        _redo.Push(Copy(current));
        return _undo.Pop();
    }

    /// <summary>되돌린 것을 다시. 다시 할 것이 없으면 지금 그대로.</summary>
    public EditorGraph Redo(EditorGraph current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CanRedo)
        {
            return current;
        }

        _undo.Push(Copy(current));
        return _redo.Pop();
    }

    /// <summary>다른 그래프를 열었을 때 — 앞 그래프의 과거로 돌아가면 안 된다.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>깊은 복사. 선(<see cref="EditorConnection"/>)은 바뀌지 않는 값이라 그대로 담는다.</summary>
    public static EditorGraph Copy(EditorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return new EditorGraph
        {
            RecipeId = graph.RecipeId,
            Description = graph.Description,
            Nodes =
            [
                .. graph.Nodes.Select(n => new EditorNode
                {
                    Id = n.Id,
                    Type = n.Type,
                    Enabled = n.Enabled,
                    X = n.X,
                    Y = n.Y,
                    Params = n.Params.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal),
                }),
            ],
            Connections = [.. graph.Connections],
        };
    }
}