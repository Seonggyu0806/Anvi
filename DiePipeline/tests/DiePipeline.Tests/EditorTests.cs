using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Editor;

namespace DiePipeline.Tests;

/// <summary>
/// 에디터 모델 계층 — <b>화면 없이</b> 블록 에디터의 완료 기준을 잰다.
///
/// ★ 이 파일이 존재하는 이유: 화면은 헤드리스로 검증할 수 없다.
///   그래서 <b>검증할 수 있는 것을 전부 WPF 바깥으로 끄집어냈고</b>, 여기서 못 박는다.
///   화면에 남는 건 "이 모델을 바인딩만 한다" 뿐이라, 화면이 깨져도 여기까지는 안 무너진다.
/// </summary>
public sealed class EditorTests
{
    private static NodeRegistry Registry() => CvBackend.CreateRegistry();

    /// <summary>
    /// <c>die.diff</c> 와 같은 모양을 코드로 조립한다.
    ///
    /// ★ 노드를 <b>일부러 거꾸로</b> 넣는다 — 위상정렬이 진짜로 순서를 만들어 내는지 보려고.
    /// </summary>
    private static EditorGraph Standard()
    {
        EditorGraph graph = new() { RecipeId = "editor.t" };

        graph.Nodes.Add(new EditorNode { Id = "label", Type = "Label", X = 640, Y = 80 });
        graph.Nodes.Add(new EditorNode { Id = "bin", Type = "Binarize", X = 440, Y = 80 });
        graph.Nodes.Add(new EditorNode { Id = "diff", Type = "Difference", X = 240, Y = 80 });
        graph.Nodes.Add(new EditorNode { Id = "golden", Type = "Combine", X = 40, Y = 40 });

        graph.Connections.AddRange(
        [
            new EditorConnection(null, ReservedContext.ChipRegions, "golden", "regions"),
            new EditorConnection("golden", "result", "diff", "golden"),
            new EditorConnection(null, ReservedContext.Roi, "diff", "test"),
            new EditorConnection("diff", "result", "bin", "src"),
            new EditorConnection("bin", "result", "label", "mask"),
            new EditorConnection(null, ReservedContext.Roi, "label", "intensity"),
        ]);

        return graph;
    }

    /// <summary>레시피의 뼈대만 글자로. 틀렸을 때 <b>어디가 다른지 눈으로 읽히게</b> 만든다.</summary>
    private static string Shape(Recipe recipe)
        => string.Join("\n", recipe.Pipeline.Nodes.Select(
               n => $"{n.Id}({n.Type}) in=[{Pairs(n.Inputs)}] out=[{Pairs(n.Outputs)}]"))
           + $"\nctx=[{string.Join(",", recipe.ContextInputs)}]";

    private static string Pairs(Dictionary<string, string> map)
        => string.Join(",", map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    // ───────────────────────── 내보내기 ─────────────────────────

    /// <summary>★ 그림에는 없던 <b>순서</b>가 만들어진다.</summary>
    [Fact]
    public void 그래프를_내보내면_순서가_생기고_검증을_통과한다()
    {
        NodeRegistry registry = Registry();

        Recipe recipe = RecipeMapper.Export(Standard(), registry);

        // 넣은 순서는 label·bin·diff·golden 이었다. 의존을 따라 뒤집혔다.
        Assert.Equal(["golden", "diff", "bin", "label"], recipe.Pipeline.Nodes.Select(n => n.Id).ToArray());

        // 이름표가 규약대로 지어졌다 — 만든 쪽과 읽는 쪽이 같은 글자를 쓴다.
        Assert.Equal("golden.result", recipe.Pipeline.Nodes[0].Outputs["result"]);
        Assert.Equal("golden.result", recipe.Pipeline.Nodes[1].Inputs["golden"]);

        // ★ 아무도 안 읽는 DefectList 출력 = 파이프라인의 끝. 엔진이 꺼내는 예약 이름표가 붙어야 한다.
        //   이게 없으면 계산은 다 해 놓고 결함 0개로 나온다.
        Assert.Equal(PipelineResult.DefectListKey, recipe.Pipeline.Nodes[3].Outputs["result"]);

        // 컨텍스트 입력은 선에서 모아진다 — 손으로 안 적는다.
        Assert.Equal([ReservedContext.ChipRegions, ReservedContext.Roi], recipe.ContextInputs);

        // ★ 검증 통과 + 실제로 노드가 만들어진다 = 돌릴 수 있는 레시피다.
        Assert.Empty(RecipeValidator.Validate(recipe, registry));
        Assert.Equal(4, registry.CreateAll(recipe).Count);
    }

    /// <summary>불러왔다 다시 내보내도 같아야 한다 — 안 그러면 열 때마다 파일이 흔들린다.</summary>
    [Fact]
    public void 불러왔다_다시_내보내도_같다()
    {
        NodeRegistry registry = Registry();

        Recipe once = RecipeMapper.Export(Standard(), registry);
        Recipe twice = RecipeMapper.Export(RecipeMapper.FromRecipe(once), registry);

        Assert.Equal(Shape(once), Shape(twice));
    }

    /// <summary>
    /// ★ 끝이 아닌 DefectList 출력은 <b>이름이 그대로 남아야</b> 한다.
    ///   끝인 것만 예약 이름표를 가져간다 — 둘 다 가져가면 이름이 부딪힌다.
    /// </summary>
    [Fact]
    public void 꼬리가_붙으면_예약_이름표는_맨_끝에만_간다()
    {
        NodeRegistry registry = Registry();
        EditorGraph graph = Standard();

        graph.Nodes.Add(new EditorNode { Id = "clean", Type = "DefectFilter" });
        graph.Connections.Add(new EditorConnection("label", "result", "clean", "defects"));

        Recipe recipe = RecipeMapper.Export(graph, registry);

        Assert.Equal("label.result", recipe.Pipeline.Nodes.Single(n => n.Id == "label").Outputs["result"]);
        Assert.Equal(PipelineResult.DefectListKey,
            recipe.Pipeline.Nodes.Single(n => n.Id == "clean").Outputs["result"]);
    }

    // ───────────────────────── 막아야 하는 것 ─────────────────────────

    /// <summary>★ 내보내기는 검증을 <b>건너뛸 수 없다.</b> 파일로 나가면 누가 언제 돌릴지 모른다.</summary>
    [Fact]
    public void 선이_빠지면_내보내기가_막힌다()
    {
        EditorGraph graph = Standard();

        graph.Connections.RemoveAll(c => c.TargetNodeId == "bin");   // Binarize.src 를 끊는다

        RecipeExportException error =
            Assert.Throws<RecipeExportException>(() => RecipeMapper.Export(graph, Registry()));

        Assert.Contains(error.Errors, e => e.Contains("'src'"));
    }

    [Fact]
    public void 고리가_있으면_내보내기가_막힌다()
    {
        EditorGraph graph = Standard();

        // diff → bin → diff. 서로가 서로를 기다린다.
        graph.Connections.RemoveAll(c => c.TargetNodeId == "diff" && c.TargetPort == "test");
        graph.Connections.Add(new EditorConnection("bin", "result", "diff", "test"));

        RecipeExportException error =
            Assert.Throws<RecipeExportException>(() => RecipeMapper.Export(graph, Registry()));

        Assert.NotEmpty(error.Errors);
    }

    // ───────────────────────── 디스크립터에서 화면이 나온다 ─────────────────────────

    [Fact]
    public void 속성폼이_디스크립터에서_자동으로_나온다()
    {
        NodeRegistry registry = Registry();

        IReadOnlyList<PropertyField> fields = PropertyFormModel.For(registry.Describe("Binarize"));

        // Choices 가 있으면 타입보다 콤보박스가 먼저다 — 손으로 "Otsu" 를 타이핑하게 두지 않는다.
        PropertyField method = fields.Single(f => f.Name == "method");
        Assert.Equal(FieldControl.Choice, method.Control);
        Assert.Contains("Otsu", method.Choices!);

        PropertyField value = fields.Single(f => f.Name == "value");
        Assert.Equal(FieldControl.Number, value.Control);
        Assert.Equal(0.0, value.Min!.Value, tolerance: 1e-9);
        Assert.Equal(1.0, value.Max!.Value, tolerance: 1e-9);

        // ★ 제약이 화면까지 간다. 짝수를 고르고 나서 혼나는 것과 애초에 못 고르는 것은 다르다.
        PropertyField block = fields.Single(f => f.Name == "blockSize");
        Assert.Equal(FieldControl.Integer, block.Control);
        Assert.Equal(ParamConstraint.OddOnly, block.Constraint);

        // bool 은 체크박스
        Assert.Equal(
            FieldControl.Bool,
            PropertyFormModel.For(registry.Describe("Label")).Single(f => f.Name == "computeShape").Control);
    }

    // ───────────────────────── 슬라이더 ─────────────────────────

    private static PropertyField Field(string type, string name)
        => PropertyFormModel.For(Registry().Describe(type)).Single(f => f.Name == name);

    [Fact]
    public void 위아래_끝이_있는_실수는_100칸_슬라이더가_된다()
    {
        PropertyField value = Field("Binarize", "value");      // 0 ~ 1

        Assert.True(value.HasRange);
        Assert.Equal(0.01, value.Step, tolerance: 1e-9);
    }

    /// <summary>★ 홀수만 받는 값은 2칸씩 — 짝수에 아예 못 멈춘다.</summary>
    [Fact]
    public void 홀수만_받는_값은_두_칸씩_움직인다()
    {
        PropertyField block = Field("Binarize", "blockSize");  // 3 ~ 99, 홀수

        Assert.True(block.HasRange);
        Assert.Equal(2.0, block.Step, tolerance: 1e-9);
    }

    /// <summary>
    /// ★ 끝이 없는 값에 화면이 멋대로 끝을 정하지 않는다.
    ///   minArea 는 Min 0 만 있다 — 슬라이더가 필요하면 디스크립터에 Max 를 적어야 한다.
    /// </summary>
    [Fact]
    public void 끝이_하나뿐인_값은_슬라이더를_안_만든다()
    {
        PropertyField minArea = Field("Label", "minArea");

        Assert.False(minArea.HasRange);
        Assert.Equal(0.0, minArea.Step, tolerance: 1e-9);
    }

    /// <summary>레지스트리에만 등록하는 가짜 알고리즘 — 화면을 안 고쳐도 뜨는지 보려고.</summary>
    private sealed class NewStageFactory : INodeFactory
    {
        public string TypeName => "SuperFilter";

        public INodeDescriptor Descriptor { get; } = new NodeDescriptor("SuperFilter", "실험",
            inputs: [new PortDescriptor("src", "입력")],
            outputs: [new PortDescriptor("result", "출력")],
            @params: [new ParamDescriptor("strength", typeof(double), Default: 1.0, Min: 0.0, Max: 10.0)]);

        public IPipelineNode Create(NodeConfig config)
            => throw new NotSupportedException("테스트용 — 실행하지 않는다");
    }

    /// <summary>★★ <b>이 층 전체의 존재 이유다.</b> 이게 깨지면 블록 에디터를 만든 의미가 없다.</summary>
    [Fact]
    public void 새_알고리즘은_화면_코드_없이_팔레트와_속성폼에_뜬다()
    {
        NodeRegistry registry = Registry();

        Assert.DoesNotContain(PaletteModel.From(registry), c => c.Category == "실험");

        registry.Register(new NewStageFactory());       // ★ 고친 곳은 이 한 줄이 전부다

        PaletteCategory category = Assert.Single(PaletteModel.From(registry), c => c.Category == "실험");
        Assert.Equal("SuperFilter", Assert.Single(category.Nodes).TypeName);

        PropertyField field = Assert.Single(PropertyFormModel.For(registry.Describe("SuperFilter")));
        Assert.Equal(FieldControl.Number, field.Control);
        Assert.Equal(10.0, field.Max!.Value, tolerance: 1e-9);
    }

    // ───────────────────────── 좌표는 레시피에 안 샌다 ─────────────────────────

    [Fact]
    public void 좌표는_레시피를_타고_가지_않는다()
    {
        NodeRegistry registry = Registry();
        EditorGraph graph = Standard();                 // X/Y 가 박혀 있다

        Recipe recipe = RecipeMapper.Export(graph, registry);
        EditorGraph restored = RecipeMapper.FromRecipe(recipe);

        // ★ 레시피를 거쳐 오면 좌표가 없다 — NodeConfig 에 실을 자리가 아예 없기 때문이다.
        Assert.All(restored.Nodes, n => Assert.Equal(0.0, n.X, tolerance: 1e-9));

        // 좌표는 곁다리 파일이 따로 나른다.
        LayoutSidecar sidecar = LayoutSidecar.Parse(LayoutSidecar.From(graph).Serialize());
        sidecar.ApplyTo(restored);

        Assert.Equal(40.0, restored.Nodes.Single(n => n.Id == "golden").X, tolerance: 1e-9);
        Assert.Equal(640.0, restored.Nodes.Single(n => n.Id == "label").X, tolerance: 1e-9);
    }
}