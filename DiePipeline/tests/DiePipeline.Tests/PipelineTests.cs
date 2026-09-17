using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;

namespace DiePipeline.Tests;

/// <summary>칠판과 엔진 — 순서·소유권·오류 메시지.</summary>
public sealed class PipelineTests
{
    /// <summary>시킨 일을 칠판에 적는 가짜 노드. 테스트에서 진짜 알고리즘은 필요 없다.</summary>
    private sealed class FakeNode : IPipelineNode
    {
        private readonly Action<IPipelineContext> _work;

        public FakeNode(string id, Action<IPipelineContext> work)
        {
            Id = id;
            _work = work;
        }

        public string Id { get; }

        public string TypeName => "Fake";

        public void Execute(IPipelineContext context) => _work(context);
    }

    /// <summary>놓였는지 확인할 수 있는 가짜 자원.</summary>
    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void 노드는_적힌_순서대로_돈다()
    {
        List<string> order = [];
        PipelineContext context = new();

        new PipelineEngine().Run(
            [
                new FakeNode("a", _ => order.Add("a")),
                new FakeNode("b", _ => order.Add("b")),
                new FakeNode("c", _ => order.Add("c")),
            ],
            context);

        Assert.Equal(["a", "b", "c"], order);
    }

    [Fact]
    public void 앞_노드가_올린_것을_뒤_노드가_읽는다()
    {
        PipelineContext context = new();
        int seen = 0;

        new PipelineEngine().Run(
            [
                new FakeNode("write", c => c.Set("n", 42)),
                new FakeNode("read", c => seen = c.Get<int>("n")),
            ],
            context);

        Assert.Equal(42, seen);
    }

    /// <summary>★ 조용히 덮어쓰면 "결함이 왜 이렇게 적지?"로만 나타나 추적이 안 된다.</summary>
    [Fact]
    public void 같은_이름표에_두_번_쓰면_막는다()
    {
        PipelineContext context = new();
        context.Set("n", 1);

        Assert.Throws<InvalidOperationException>(() => context.Set("n", 2));
    }

    /// <summary>문자열 키의 대가가 오타다 — 있는 목록을 같이 알려줘야 1초 만에 잡는다.</summary>
    [Fact]
    public void 없는_이름표를_읽으면_있는_것을_알려준다()
    {
        PipelineContext context = new();
        context.Set("blurred", 1);

        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => context.Get<int>("blured"));

        Assert.Contains("blurred", error.Message);
    }

    /// <summary>★ 노드는 자기 산출물의 수명을 못 정한다 — 실행이 끝나야 놓는다.</summary>
    [Fact]
    public void 실행이_끝나면_칠판이_소유한_것을_놓는다()
    {
        Probe probe = new();
        PipelineContext context = new();

        new PipelineEngine().Run([new FakeNode("make", c => c.Set("p", probe))], context);

        Assert.True(probe.Disposed);
    }

    /// <summary>손님이 가져온 재료는 안 치운다.</summary>
    [Fact]
    public void 입력으로_넣은_것은_놓지_않는다()
    {
        Probe probe = new();
        PipelineContext context = new();
        context.SetInput("roi", probe);

        new PipelineEngine().Run([], context);

        Assert.False(probe.Disposed);
    }

    /// <summary>예외로 빠져나가도 반납은 반드시 된다. 누수 0.</summary>
    [Fact]
    public void 노드가_터져도_반납은_된다()
    {
        Probe probe = new();
        PipelineContext context = new();

        Assert.Throws<InvalidOperationException>(() => new PipelineEngine().Run(
            [
                new FakeNode("make", c => c.Set("p", probe)),
                new FakeNode("boom", _ => throw new InvalidOperationException("터짐")),
            ],
            context));

        Assert.True(probe.Disposed);
    }

    [Fact]
    public void 결함_노드가_없으면_빈_목록이_나온다()
    {
        PipelineResult result = new PipelineEngine().Run([], new PipelineContext());

        Assert.Equal(0, result.Defects.Count);
    }
}