using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;

namespace DiePipeline.Tests;

/// <summary>실행 전에 무엇을 잡아내는가. 이미지가 없어 밀리초 단위로 돈다.</summary>
public sealed class ValidatorTests
{
    private sealed class NoopNode : IPipelineNode
    {
        public NoopNode(string id) => Id = id;

        public string Id { get; }

        public string TypeName => "Blur";

        public void Execute(IPipelineContext context)
        {
        }
    }

    private sealed class BlurFactory : INodeFactory
    {
        public string TypeName => "Blur";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Blur", "전처리",
            inputs: [new PortDescriptor("src", "입력 이미지")],
            outputs: [new PortDescriptor("result", "흐린 이미지")],
            @params:
            [
                new ParamDescriptor("kernel", typeof(int), Default: 3, Min: 1, Max: 31,
                    Constraint: ParamConstraint.OddOnly),
                new ParamDescriptor("method", typeof(string), Default: "Gaussian",
                    Choices: ["Gaussian", "Median"]),
            ]);

        public IPipelineNode Create(NodeConfig config) => new NoopNode(config.Id);
    }

    private sealed class CombineFactory : INodeFactory
    {
        public string TypeName => "Combine";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Combine", "골든 생성",
            inputs: [new PortDescriptor("regions", "정상 die들", PortKind.ImageList)],
            outputs: [new PortDescriptor("result", "골든")],
            @params: []);

        public IPipelineNode Create(NodeConfig config) => new NoopNode(config.Id);
    }

    private static NodeRegistry Registry()
    {
        NodeRegistry registry = new();
        registry.Register(new BlurFactory());
        registry.Register(new CombineFactory());
        return registry;
    }

    private static Recipe Parse(string nodes, string inputs = "[ \"roi\" ]")
        => RecipeLoader.Parse($$"""
            { "recipeId": "t", "contextInputs": {{inputs}}, "pipeline": { "nodes": [ {{nodes}} ] } }
            """);

    private const string GoodBlur = """
        { "id": "b", "type": "Blur", "inputs": { "src": "roi" },
          "outputs": { "result": "blurred" }, "params": { "kernel": 5 } }
        """;

    [Fact]
    public void 옳은_레시피는_오류가_없다()
        => Assert.Empty(RecipeValidator.Validate(Parse(GoodBlur), Registry()));

    /// <summary>★ 이 파일의 존재 이유 — 오타를 실행 전에 잡는다.</summary>
    [Fact]
    public void 아무도_만들지_않은_이름표를_읽으면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""{ "id": "b", "type": "Blur", "inputs": { "src": "없는것" }, "outputs": { "result": "x" } }"""),
            Registry());

        string error = Assert.Single(errors);

        Assert.Contains("없는것", error);
        Assert.Contains("roi", error);       // 있는 목록도 알려줘야 한다
    }

    /// <summary>적힌 순서가 실행 순서다 — 뒤에 만들 것을 먼저 읽을 수는 없다.</summary>
    [Fact]
    public void 순서가_뒤집히면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "second", "type": "Blur", "inputs": { "src": "first결과" }, "outputs": { "result": "z" } },
                { "id": "first", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "first결과" } }
                """),
            Registry());

        Assert.Single(errors);
    }

    [Fact]
    public void 같은_출력_이름을_두_노드가_쓰면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "a", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "같은이름" } },
                { "id": "b", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "같은이름" } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("이미 다른 노드가"));
    }

    [Fact]
    public void id가_중복되면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "같은id", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "a" } },
                { "id": "같은id", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "b" } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("id가 중복"));
    }

    [Fact]
    public void 배선이_빠지면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""{ "id": "b", "type": "Blur", "outputs": { "result": "x" } }"""),
            Registry());

        Assert.Contains(errors, e => e.Contains("'src'이 연결되지 않았습니다"));
    }

    /// <summary>★ 이미지 한 장을 '여러 장' 자리에 물리면 잡는다.</summary>
    [Fact]
    public void 종류가_다른_것을_물리면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""{ "id": "c", "type": "Combine", "inputs": { "regions": "roi" }, "outputs": { "result": "g" } }"""),
            Registry());

        Assert.Contains(errors, e => e.Contains("ImageList를 받는데"));
    }

    /// <summary>★ 디스크립터 선언 한 줄에서 검증이 파생된다.</summary>
    [Fact]
    public void 짝수_커널은_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "inputs": { "src": "roi" },
                  "outputs": { "result": "x" }, "params": { "kernel": 4 } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("홀수여야"));
    }

    [Fact]
    public void 범위를_벗어나면_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "inputs": { "src": "roi" },
                  "outputs": { "result": "x" }, "params": { "kernel": 99 } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("최댓값"));
    }

    [Fact]
    public void 목록에_없는_값은_잡는다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "inputs": { "src": "roi" },
                  "outputs": { "result": "x" }, "params": { "method": "Bilateral" } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("Gaussian"));
    }

    /// <summary>안 적은 건 오류가 아니다 — 기본값을 쓰겠다는 뜻.</summary>
    [Fact]
    public void 파라미터를_안_적으면_오류가_아니다()
        => Assert.Empty(RecipeValidator.Validate(
            Parse("""{ "id": "b", "type": "Blur", "inputs": { "src": "roi" }, "outputs": { "result": "x" } }"""),
            Registry()));

    /// <summary>★ 첫 오류에서 멈추지 않는다 — 하나 고치고 다시 돌리기를 반복하게 만들지 않는다.</summary>
    [Fact]
    public void 오류를_전부_모아서_돌려준다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "inputs": { "src": "없는것" },
                  "outputs": { "result": "x" }, "params": { "kernel": 4, "method": "Bilateral" } }
                """),
            Registry());

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void EnsureValid는_오류를_한_예외에_담는다()
    {
        RecipeValidationException error = Assert.Throws<RecipeValidationException>(
            () => RecipeValidator.EnsureValid(
                Parse("""{ "id": "b", "type": "Blur", "inputs": { "src": "없는것" }, "outputs": { "result": "x" } }"""),
                Registry()));

        Assert.Single(error.Errors);
        Assert.Contains("레시피 't'에 오류 1개", error.Message);
    }
}