using System.Text.Json;
using DiePipeline.Editor;

namespace DiePipeline.Tests;

/// <summary>
/// 그래프 편집 규칙. ★ 화면을 안 띄우고 잰다 — 이게 5주차 완료 기준 5번이다.
/// </summary>
public sealed class GraphOpsTests
{
    private static EditorGraph Chain()
    {
        EditorGraph graph = new() { RecipeId = "t" };

        graph.Nodes.Add(new EditorNode { Id = "a", Type = "Binarize" });
        graph.Nodes.Add(new EditorNode { Id = "b", Type = "Label" });

        graph.Connections.AddRange(
        [
            new EditorConnection(null, "roi", "a", "src"),
            new EditorConnection("a", "result", "b", "mask"),
            new EditorConnection(null, "roi", "b", "intensity"),
        ]);

        return graph;
    }

    [Fact]
    public void id가_겹치지_않게_번호를_올린다()
    {
        EditorGraph graph = new();

        Assert.Equal("binarize1", GraphOps.UniqueId(graph, "Binarize"));

        graph.Nodes.Add(new EditorNode { Id = "binarize1", Type = "Binarize" });
        Assert.Equal("binarize2", GraphOps.UniqueId(graph, "Binarize"));

        graph.Nodes.Add(new EditorNode { Id = "binarize2", Type = "Binarize" });
        Assert.Equal("binarize3", GraphOps.UniqueId(graph, "Binarize"));
    }

    /// <summary>★ 선을 남기면 검증기가 영문 모를 오류를 뱉는데 화면엔 그 선이 안 보인다.</summary>
    [Fact]
    public void 블록을_지우면_닿은_선도_같이_지워진다()
    {
        EditorGraph graph = Chain();

        GraphOps.Remove(graph, "a");

        Assert.DoesNotContain(graph.Nodes, n => n.Id == "a");

        // a 로 들어가던 선과 a 에서 나가던 선이 둘 다 없어야 한다.
        Assert.DoesNotContain(graph.Connections, c => c.TargetNodeId == "a" || c.SourceNodeId == "a");

        // b 의 intensity 는 a 와 무관하니 남아야 한다.
        Assert.Contains(graph.Connections, c => c is { TargetNodeId: "b", TargetPort: "intensity" });
    }

    /// <summary>★★ 입력 포트엔 선이 하나. 화면에서 실제로 밟은 규칙이다.</summary>
    [Fact]
    public void 입력_포트에는_선이_하나만_남는다()
    {
        EditorGraph graph = Chain();

        GraphOps.Connect(graph, null, "chipRegions", "a", "src");

        EditorConnection only = Assert.Single(
            graph.Connections, c => c is { TargetNodeId: "a", TargetPort: "src" });

        Assert.Null(only.SourceNodeId);
        Assert.Equal("chipRegions", only.SourcePort);
    }

    [Fact]
    public void 다른_입력_포트의_선은_안_건드린다()
    {
        EditorGraph graph = Chain();

        GraphOps.Connect(graph, "a", "result", "b", "mask");

        Assert.Equal(3, graph.Connections.Count);
        Assert.Contains(graph.Connections, c => c is { TargetNodeId: "b", TargetPort: "intensity" });
    }

    [Fact]
    public void 컨텍스트에서_오는_선도_같은_규칙이다()
    {
        EditorGraph graph = Chain();

        // roi 로 들어오던 자리를 다른 노드 출력으로 갈아 끼운다.
        GraphOps.Connect(graph, "a", "result", "b", "intensity");

        EditorConnection only = Assert.Single(
            graph.Connections, c => c is { TargetNodeId: "b", TargetPort: "intensity" });

        Assert.Equal("a", only.SourceNodeId);
    }

    [Fact]
    public void 복제하면_파라미터가_따라오고_선은_안_따라온다()
    {
        EditorGraph graph = Chain();
        graph.Nodes[0].Params["value"] = JsonSerializer.SerializeToElement(0.2);
        graph.Nodes[0].Enabled = false;

        EditorNode copy = GraphOps.Duplicate(graph, "a");

        Assert.Equal("binarize1", copy.Id);
        Assert.Equal("Binarize", copy.Type);
        Assert.False(copy.Enabled);
        Assert.Equal(0.2, copy.Params["value"].GetDouble());

        // 선은 그대로 3개 — 복제본은 아무 데도 안 이어져 있다.
        Assert.Equal(3, graph.Connections.Count);
        Assert.DoesNotContain(graph.Connections, c => c.TargetNodeId == copy.Id || c.SourceNodeId == copy.Id);
    }

    /// <summary>★★ Clone 을 빼면 복제본을 고칠 때 원본까지 바뀐다 — 화면으로는 못 찾는 버그다.</summary>
    [Fact]
    public void 복제본을_고쳐도_원본은_그대로다()
    {
        EditorGraph graph = Chain();
        graph.Nodes[0].Params["value"] = JsonSerializer.SerializeToElement(0.2);

        EditorNode copy = GraphOps.Duplicate(graph, "a");
        copy.Params["value"] = JsonSerializer.SerializeToElement(0.9);

        Assert.Equal(0.2, graph.Nodes[0].Params["value"].GetDouble());
        Assert.Equal(0.9, copy.Params["value"].GetDouble());
    }

    [Fact]
    public void 없는_블록을_복제하면_이름을_알려준다()
    {
        ArgumentException error =
            Assert.Throws<ArgumentException>(() => GraphOps.Duplicate(Chain(), "없는것"));

        Assert.Contains("없는것", error.Message);
    }
}