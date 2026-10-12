using DiePipeline.Editor;

namespace DiePipeline.Tests;

/// <summary>
/// 자동 배치 — 흐름 순서대로 왼쪽에서 오른쪽으로. ★ 화면 없이 좌표만 본다.
/// </summary>
public sealed class LayoutEngineTests
{
    private const double Left = 240;

    private static EditorGraph Graph(params (string From, string To)[] lines)
    {
        EditorGraph graph = new();

        foreach (string id in lines.SelectMany(l => new[] { l.From, l.To }).Where(id => id != "roi").Distinct())
        {
            graph.Nodes.Add(new EditorNode { Id = id, Type = "Label" });
        }

        foreach ((string from, string to) in lines)
        {
            graph.Connections.Add(new EditorConnection(from == "roi" ? null : from, from == "roi" ? "roi" : "result", to, "src"));
        }

        return graph;
    }

    private static int Column(EditorGraph g, string id) => (int)((g.Nodes.First(n => n.Id == id).X - Left) / LayoutEngine.ColumnGap);

    /// <summary>★ 한 줄 흐름은 한 줄로 — 골든 → 빼기 → 이진화 → 세기가 왼쪽에서 오른쪽으로.</summary>
    [Fact]
    public void 이어진_블록은_흐름_순서대로_오른쪽으로_간다()
    {
        EditorGraph g = Graph(("roi", "golden"), ("golden", "diff"), ("roi", "diff"), ("diff", "binarize"), ("binarize", "label"));

        LayoutEngine.Layered(g, Left);

        Assert.Equal([0, 1, 2, 3], new[] { "golden", "diff", "binarize", "label" }.Select(id => Column(g, id)));
        Assert.All(g.Nodes, n => Assert.Equal(60, n.Y));
    }

    /// <summary>열 번호는 '가장 긴' 앞 경로 — 짧은 길로도 닿는 블록이 앞 열로 당겨지지 않는다.</summary>
    [Fact]
    public void 열은_가장_긴_앞_경로로_정한다()
    {
        EditorGraph g = Graph(("a", "b"), ("b", "c"), ("a", "c"));

        LayoutEngine.Layered(g, Left);

        Assert.Equal(2, Column(g, "c"));
    }

    /// <summary>같은 열이면 레시피 순서대로 위에서 아래로 — 겹치지 않는다.</summary>
    [Fact]
    public void 같은_열은_위에서_아래로_겹치지_않게()
    {
        EditorGraph g = Graph(("roi", "x"), ("roi", "y"), ("roi", "z"));

        LayoutEngine.Layered(g, Left);

        Assert.All(g.Nodes, n => Assert.Equal(Left, n.X));
        Assert.Equal([60, 60 + LayoutEngine.RowGap, 60 + (2 * LayoutEngine.RowGap)], g.Nodes.Select(n => n.Y));
    }

    /// <summary>왼쪽 열은 컨텍스트 입력 자리 — 어떤 블록도 <c>left</c> 보다 왼쪽에 안 간다. 선이 없는 블록은 첫 열.</summary>
    [Fact]
    public void 왼쪽_열은_비워_두고_외톨이는_첫_열()
    {
        EditorGraph g = Graph(("a", "b"));
        g.Nodes.Add(new EditorNode { Id = "alone", Type = "Label" });

        LayoutEngine.Layered(g, Left);

        Assert.All(g.Nodes, n => Assert.True(n.X >= Left));
        Assert.Equal(0, Column(g, "alone"));
    }

    /// <summary>★ 고리가 있어도(검증기가 막지만) 끝없이 돌지 않는다.</summary>
    [Fact]
    public void 고리가_있어도_멈춘다()
    {
        EditorGraph g = Graph(("a", "b"), ("b", "a"));

        LayoutEngine.Layered(g, Left);

        Assert.All(g.Nodes, n => Assert.True(n.X >= Left));
    }
}