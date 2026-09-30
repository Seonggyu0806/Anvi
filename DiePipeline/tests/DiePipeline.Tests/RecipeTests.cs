using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;

namespace DiePipeline.Tests;

/// <summary>주문서(JSON) → 명부 → 노드. 아직 알고리즘은 없다.</summary>
public sealed class RecipeTests
{
    /// <summary>자기 id를 칠판에 남기는 가짜 노드.</summary>
    private sealed class EchoNode : IPipelineNode
    {
        private readonly string _outKey;
        private readonly int _times;

        public EchoNode(string id, string outKey, int times)
        {
            Id = id;
            _outKey = outKey;
            _times = times;
        }

        public string Id { get; }

        public string TypeName => "Echo";

        public void Execute(IPipelineContext context) => context.Set(_outKey, $"{Id}×{_times}");
    }

    private sealed class EchoFactory : INodeFactory
    {
        public string TypeName => "Echo";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Echo", "테스트",
            inputs: [],
            outputs: [new PortDescriptor("result", "남긴 말")],
            @params: [new ParamDescriptor("times", typeof(int), Default: 1, Min: 1)]);

        // ★ 팩토리만 자기 봉투를 뜯는다. Core는 이 안을 들여다보지 않는다.
        //   (JsonElement를 직접 만지는 게 번거로운데, Step 3에서 ParamValues가 대신해 준다)
        public IPipelineNode Create(NodeConfig config)
            => new EchoNode(
                config.Id,
                config.Outputs["result"],
                config.Params.ValueKind == JsonValueKind.Object
                && config.Params.TryGetProperty("times", out JsonElement times)
                    ? times.GetInt32()
                    : 1);
    }

    /// <summary>입력을 받아 자기 이름을 덧붙여 넘기는 가짜 노드 — 별칭(끄기)을 재는 데 쓴다.</summary>
    private sealed class RelayNode : IPipelineNode
    {
        private readonly string _inKey;
        private readonly string _outKey;

        public RelayNode(string id, string inKey, string outKey)
        {
            Id = id;
            _inKey = inKey;
            _outKey = outKey;
        }

        public string Id { get; }

        public string TypeName => "Relay";

        public void Execute(IPipelineContext context)
            => context.Set(_outKey, $"{context.Get<string>(_inKey)}>{Id}");
    }

    private sealed class RelayFactory : INodeFactory
    {
        public string TypeName => "Relay";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Relay", "테스트",
            inputs: [new PortDescriptor("src", "받을 것")],
            outputs: [new PortDescriptor("result", "넘길 것")],
            @params: []);

        public IPipelineNode Create(NodeConfig config)
            => new RelayNode(config.Id, config.Inputs["src"], config.Outputs["result"]);
    }

    private static NodeRegistry Registry()
    {
        NodeRegistry registry = new();
        registry.Register(new EchoFactory());
        registry.Register(new RelayFactory());
        return registry;
    }

    private const string Json = """
        {
          // 사람이 손으로 쓰는 파일이라 주석을 허용한다
          "recipeId": "demo.v1",
          "contextInputs": [ "roi" ],
          "pipeline": {
            "nodes": [
              {
                "id": "first",
                "type": "Echo",
                "outputs": { "result": "a" },
                "params": { "times": 3 },
              },
              {
                "id": "second",
                "type": "Echo",
                "outputs": { "result": "b" }
              }
            ]
          }
        }
        """;

    [Fact]
    public void JSON을_읽으면_레시피가_된다()
    {
        Recipe recipe = RecipeLoader.Parse(Json);

        Assert.Equal("demo.v1", recipe.RecipeId);
        Assert.Equal("1.0", recipe.SchemaVersion);
        Assert.Equal(["roi"], recipe.ContextInputs);
        Assert.Equal(2, recipe.Pipeline.Nodes.Count);
        Assert.Equal("first", recipe.Pipeline.Nodes[0].Id);
        Assert.Equal("a", recipe.Pipeline.Nodes[0].Outputs["result"]);
    }

    [Fact]
    public void 명부가_주문서를_노드로_바꾼다()
    {
        Recipe recipe = RecipeLoader.Parse(Json);

        IReadOnlyList<IPipelineNode> nodes = Registry().CreateAll(recipe);

        Assert.Equal(["first", "second"], nodes.Select(n => n.Id).ToArray());
        Assert.All(nodes, n => Assert.Equal("Echo", n.TypeName));
    }

    /// <summary>★ 끝에서 끝까지 — JSON 한 장이 실제 실행이 된다.</summary>
    [Fact]
    public void 팩토리가_파라미터를_읽는다()
    {
        Recipe recipe = RecipeLoader.Parse(Json);
        PipelineContext context = new();

        new PipelineEngine().Run(Registry().CreateAll(recipe), context);

        Assert.Equal("first×3", context.Get<string>("a"));
        Assert.Equal("second×1", context.Get<string>("b"));   // params 없으면 기본값
    }

    [Fact]
    public void 모르는_타입이면_아는_목록을_알려준다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            { "recipeId": "t", "pipeline": { "nodes": [
                { "id": "x", "type": "Ekho", "outputs": { "result": "a" } } ] } }
            """);

        UnknownNodeTypeException error =
            Assert.Throws<UnknownNodeTypeException>(() => Registry().CreateAll(recipe));

        Assert.Contains("Echo", error.Message);
    }

    [Fact]
    public void 레시피는_대소문자를_가리지_않는다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            { "RecipeId": "t", "Pipeline": { "Nodes": [
                { "Id": "x", "Type": "echo", "Outputs": { "result": "a" } } ] } }
            """);

        Assert.Equal("t", recipe.RecipeId);
        Assert.Single(Registry().CreateAll(recipe));
    }

    [Fact]
    public void 같은_타입을_두_번_등록하면_막는다()
    {
        NodeRegistry registry = Registry();

        Assert.Throws<DuplicateNodeTypeException>(() => registry.Register(new EchoFactory()));
    }

    [Fact]
    public void 깨진_JSON은_어디가_잘못됐는지_알려준다()
    {
        RecipeLoadException error = Assert.Throws<RecipeLoadException>(
            () => RecipeLoader.Parse("{ \"recipeId\": \"t\", ", origin: "mem"));

        Assert.Contains("mem", error.Message);
    }

    [Fact]
    public void 없는_파일은_전체_경로를_알려준다()
    {
        RecipeLoadException error = Assert.Throws<RecipeLoadException>(
            () => RecipeLoader.Load("없는파일.json"));

        Assert.Contains("없는파일.json", error.Message);
    }

    [Fact]
    public void 디스크립터가_설명서의_출처다()
    {
        INodeDescriptor descriptor = Registry().Describe("Echo");

        ParamDescriptor times = Assert.Single(descriptor.Params);

        Assert.Equal("times", times.Name);
        Assert.Equal(typeof(int), times.Type);
        Assert.Equal(1, times.Min);
    }

    // ───────────────────────── 노드 끄기(Enabled) ─────────────────────────

    /// <summary>a·b를 이어 붙인 레시피. <c>{{끄기}}</c> 자리에 "enabled": false 를 끼워 넣는다.</summary>
    private static Recipe TwoRelays(string aFlag = "", string bFlag = "")
        => RecipeLoader.Parse($$"""
            { "recipeId": "t", "contextInputs": [ "roi" ], "pipeline": { "nodes": [
                { "id": "a", "type": "Relay", {{aFlag}} "inputs": { "src": "roi" },  "outputs": { "result": "1차" } },
                { "id": "b", "type": "Relay", {{bFlag}} "inputs": { "src": "1차" }, "outputs": { "result": "2차" } }
            ] } }
            """);

    private const string Off = "\"enabled\": false,";

    private static PipelineContext Run(Recipe recipe)
    {
        PipelineContext context = new();
        context.Set("roi", "원본");
        new PipelineEngine().Run(Registry().CreateAll(recipe), context);
        return context;
    }

    [Fact]
    public void 꺼진_노드는_아예_만들어지지_않는다()
    {
        Assert.Equal(["a", "b"], Registry().CreateAll(TwoRelays()).Select(n => n.Id).ToArray());
        Assert.Equal(["b"], Registry().CreateAll(TwoRelays(aFlag: Off)).Select(n => n.Id).ToArray());
        Assert.Empty(Registry().CreateAll(TwoRelays(aFlag: Off, bFlag: Off)));
    }

    /// <summary>★ 끄면 뒷 노드가 <b>레시피를 안 고치고도</b> 윗 것을 읽는다.</summary>
    [Fact]
    public void 꺼진_노드의_입력이_뒷_노드로_물려진다()
    {
        Assert.Equal("원본>a>b", Run(TwoRelays()).Get<string>("2차"));

        // a를 끄면 b의 "1차"가 "roi"로 바뀌어 읽힌다 — b는 그대로인데 받는 것이 달라진다.
        Assert.Equal("원본>b", Run(TwoRelays(aFlag: Off)).Get<string>("2차"));
    }

    /// <summary>꺼진 노드가 연달아 있으면 별칭을 <b>끝까지</b> 따라가야 한다.</summary>
    [Fact]
    public void 연달아_꺼도_끝까지_따라간다()
    {
        Recipe recipe = RecipeLoader.Parse($$"""
            { "recipeId": "t", "contextInputs": [ "roi" ], "pipeline": { "nodes": [
                { "id": "a", "type": "Relay", {{Off}} "inputs": { "src": "roi" },  "outputs": { "result": "1차" } },
                { "id": "b", "type": "Relay", {{Off}} "inputs": { "src": "1차" }, "outputs": { "result": "2차" } },
                { "id": "c", "type": "Relay", "inputs": { "src": "2차" }, "outputs": { "result": "3차" } }
            ] } }
            """);

        // 2차 → 1차 → roi. 두 칸을 건너뛴다.
        Assert.Equal("원본>c", Run(recipe).Get<string>("3차"));
    }

    /// <summary>끈 노드의 출력 이름표는 <b>칠판에 아예 안 올라간다</b> — 별칭이지 복사가 아니다.</summary>
    [Fact]
    public void 꺼진_노드의_출력은_칠판에_없다()
    {
        PipelineContext context = Run(TwoRelays(aFlag: Off));

        Assert.False(context.TryGet("1차", out string? _));
        Assert.True(context.TryGet("2차", out string? _));
    }
}