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

    /// <summary>입력이 둘인 노드 — '끌 수 있는가'를 가르는 기준을 재는 데 쓴다.</summary>
    private sealed class DifferenceFactory : INodeFactory
    {
        public string TypeName => "Difference";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Difference", "검출",
            inputs: [new PortDescriptor("test", "검사 이미지"), new PortDescriptor("golden", "골든")],
            outputs: [new PortDescriptor("result", "차이")],
            @params: []);

        public IPipelineNode Create(NodeConfig config) => new NoopNode(config.Id);
    }

    /// <summary>입력 하나 · 출력 하나짜리 가짜 블록 — 포트 종류만 바꿔 끼워 검사를 본다.</summary>
    private sealed class FakeFactory(string type, PortDescriptor input, PortDescriptor output) : INodeFactory
    {
        public string TypeName => type;

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor(type, "웨이퍼",
            inputs: [input], outputs: [output], @params: []);

        public IPipelineNode Create(NodeConfig config) => new NoopNode(config.Id);
    }

    private static NodeRegistry Registry()
    {
        NodeRegistry registry = new();
        registry.Register(new BlurFactory());
        registry.Register(new CombineFactory());
        registry.Register(new DifferenceFactory());
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

    // ───────────────────────── 노드 끄기(Enabled) ─────────────────────────
    //
    // 끄면 그 노드의 출력이 안 생긴다. 대신 '출력 이름표 = 입력 이름표'로 별칭이 걸려서
    // 뒷 노드가 레시피를 안 고치고도 윗 것을 읽는다. 그래서 물려줄 것이 하나로 정해져야 한다.

    /// <summary>끈 노드의 출력 이름표는 그대로 읽을 수 있다 — 입력이 대신 물려진다.</summary>
    [Fact]
    public void 꺼진_노드의_이름표는_입력으로_이어진다()
        => Assert.Empty(RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "enabled": false,
                  "inputs": { "src": "roi" }, "outputs": { "result": "blurred" } },
                { "id": "c", "type": "Blur", "inputs": { "src": "blurred" }, "outputs": { "result": "x" } }
                """),
            Registry()));

    /// <summary>★ 입력이 둘이면 뒷 노드에 무엇을 물려줄지 정해지지 않는다.</summary>
    [Fact]
    public void 입력이_둘인_노드는_끌_수_없다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "d", "type": "Difference", "enabled": false,
                  "inputs": { "test": "roi", "golden": "roi" }, "outputs": { "result": "diff" } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("끌 수 없습니다"));
    }

    /// <summary>
    /// ★★ 종류가 바뀌는 노드를 끄면 <b>뒷 노드가 다른 종류를 받게 된다</b>.
    ///   Combine은 ImageList를 받아 Image를 내놓으므로, 끄면 뒷 노드에 ImageList가 흘러간다.
    ///   이걸 위해 새 검사를 만들지 않았다 — 이미 있는 <b>종류 검사</b>가 대신 잡는다.
    /// </summary>
    [Fact]
    public void 종류가_바뀌는_노드를_끄면_뒷_노드가_잡는다()
    {
        const string Nodes = """
            { "id": "g", "type": "Combine", {{끄기}}
              "inputs": { "regions": "chipRegions" }, "outputs": { "result": "golden" } },
            { "id": "b", "type": "Blur", "inputs": { "src": "golden" }, "outputs": { "result": "x" } }
            """;

        const string Both = """[ "roi", "chipRegions" ]""";

        // 켜 두면 golden은 Image다 — 아무 문제 없다.
        Assert.Empty(RecipeValidator.Validate(Parse(Nodes.Replace("{{끄기}}", ""), Both), Registry()));

        // 끄면 golden이 chipRegions(ImageList)로 이어진다 — Blur는 Image를 받는데.
        IReadOnlyList<string> errors =
            RecipeValidator.Validate(Parse(Nodes.Replace("{{끄기}}", "\"enabled\": false,"), Both), Registry());

        Assert.Contains(errors, e => e.Contains("Image를 받는데") && e.Contains("ImageList"));
    }

    /// <summary>껐다고 파라미터를 봐주면 <b>다시 켜는 순간</b> 터진다.</summary>
    [Fact]
    public void 꺼진_노드도_파라미터를_검사한다()
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "b", "type": "Blur", "enabled": false, "inputs": { "src": "roi" },
                  "outputs": { "result": "x" }, "params": { "kernel": 4 } }
                """),
            Registry());

        Assert.Contains(errors, e => e.Contains("홀수"));
    }

    // ─────────────────────────────── 웨이퍼 레시피 — 정렬 예약 입력 · 새 종류

    /// <summary>★ 정렬 예약 입력 둘은 그림이다 — 실행기가 표식 창과 표식 조각을 올린다(교수님과 같은 이름).</summary>
    [Fact]
    public void 정렬_예약_입력은_그림이다()
    {
        Assert.Equal(PortKind.Image, ReservedContext.Kinds[ReservedContext.AlignSrc]);
        Assert.Equal(PortKind.Image, ReservedContext.Kinds[ReservedContext.AlignTemplate]);
    }

    /// <summary>
    /// ★ 웨이퍼 레시피 블록이 주고받을 새 종류도 같은 검사를 받는다 —
    ///   '표식 자리 목록' 을 '놓인 자세' 칸에 꽂으면 실행 전에 거부한다.
    /// </summary>
    [Fact]
    public void 웨이퍼_종류도_짝이_안_맞으면_잡는다()
    {
        NodeRegistry registry = new();
        registry.Register(new FakeFactory("Find",
            new PortDescriptor("src", "표식 창"), new PortDescriptor("matches", "찾은 자리", PortKind.MatchList)));
        registry.Register(new FakeFactory("Place",
            new PortDescriptor("alignment", "놓인 자세", PortKind.Alignment), new PortDescriptor("grid", "격자", PortKind.DieGrid)));

        IReadOnlyList<string> errors = RecipeValidator.Validate(
            Parse("""
                { "id": "find", "type": "Find", "inputs": { "src": "alignSrc" }, "outputs": { "matches": "m" } },
                { "id": "place", "type": "Place", "inputs": { "alignment": "m" }, "outputs": { "grid": "g" } }
                """, "[ \"alignSrc\" ]"),
            registry);

        string error = Assert.Single(errors);
        Assert.Contains("Alignment", error);
        Assert.Contains("MatchList", error);
    }
}