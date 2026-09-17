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

    private static NodeRegistry Registry()
    {
        NodeRegistry registry = new();
        registry.Register(new EchoFactory());
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
}