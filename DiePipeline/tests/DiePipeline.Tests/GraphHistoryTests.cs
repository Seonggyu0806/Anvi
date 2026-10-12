using System.Text.Json;
using DiePipeline.Editor;

namespace DiePipeline.Tests;

/// <summary>
/// 되돌리기 · 다시. ★ 화면 없이 잰다 — 캔버스 버튼은 이 클래스를 부르기만 한다.
/// </summary>
public sealed class GraphHistoryTests
{
    private static EditorGraph Chain()
    {
        EditorGraph graph = new() { RecipeId = "t" };

        graph.Nodes.Add(new EditorNode
        {
            Id = "a",
            Type = "Binarize",
            X = 10,
            Y = 20,
            Params = { ["value"] = JsonDocument.Parse("0.2").RootElement },
        });
        graph.Nodes.Add(new EditorNode { Id = "b", Type = "Label" });
        graph.Connections.Add(new EditorConnection(null, "roi", "a", "src"));

        return graph;
    }

    private static string Shape(EditorGraph g)
        => string.Join(" | ", g.Nodes.Select(n => $"{n.Id}:{n.Type}:{n.Enabled}:{n.X},{n.Y}:{string.Join(",", n.Params.Select(p => p.Key + "=" + p.Value.GetRawText()))}"))
           + " || " + string.Join(" | ", g.Connections);

    [Fact]
    public void 처음엔_되돌릴_것도_다시_할_것도_없다()
    {
        GraphHistory history = new();
        EditorGraph graph = Chain();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Same(graph, history.Undo(graph));
        Assert.Same(graph, history.Redo(graph));
    }

    /// <summary>★ 바꾸기 직전에 쌓고 → 바꾸고 → 되돌리면 바꾸기 전 모양, 다시 하면 바꾼 뒤 모양.</summary>
    [Fact]
    public void 되돌리면_바꾸기_전으로_다시_하면_바꾼_뒤로()
    {
        GraphHistory history = new();
        EditorGraph graph = Chain();
        string before = Shape(graph);

        history.Push(graph);
        GraphOps.Remove(graph, "b");
        string after = Shape(graph);

        EditorGraph undone = history.Undo(graph);
        Assert.Equal(before, Shape(undone));
        Assert.True(history.CanRedo);

        EditorGraph redone = history.Redo(undone);
        Assert.Equal(after, Shape(redone));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    /// <summary>★ 쌓아 둔 과거는 뒤의 편집에 안 물든다 — 얕게 베끼면 블록 좌표 · 파라미터를 고칠 때 과거도 같이 바뀐다.</summary>
    [Fact]
    public void 쌓아_둔_과거는_뒤의_편집에_안_물든다()
    {
        GraphHistory history = new();
        EditorGraph graph = Chain();
        string before = Shape(graph);

        history.Push(graph);

        EditorNode a = graph.Nodes[0];
        a.X = 999;
        a.Enabled = false;
        a.Params["value"] = JsonDocument.Parse("0.9").RootElement;
        graph.Connections.Clear();

        Assert.Equal(before, Shape(history.Undo(graph)));
    }

    /// <summary>되돌린 뒤 새로 바꾸면 '다시' 는 사라진다 — 새 갈래가 생겼다.</summary>
    [Fact]
    public void 되돌린_뒤_새로_바꾸면_다시는_사라진다()
    {
        GraphHistory history = new();
        EditorGraph graph = Chain();

        history.Push(graph);
        GraphOps.Remove(graph, "b");
        graph = history.Undo(graph);
        Assert.True(history.CanRedo);

        history.Push(graph);
        GraphOps.Remove(graph, "a");

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void 비우면_둘_다_없다()
    {
        GraphHistory history = new();
        EditorGraph graph = Chain();

        history.Push(graph);
        graph = history.Undo(graph);
        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}