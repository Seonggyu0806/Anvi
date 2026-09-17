using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using DiePipeline.Cv.Nodes;
using DiePipeline.Cv.Stages;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "필터는 하나가 아니라 <b>줄줄이</b>다".</summary>
public sealed class FilterTests : IDisposable
{
    private const int Size = 32;

    private readonly List<Mat> _made = [];

    public void Dispose()
    {
        foreach (Mat mat in _made) mat.Dispose();
    }

    private Mat Keep(Mat mat)
    {
        _made.Add(mat);
        return mat;
    }

    private Mat Zeros() => Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0)));

    /// <summary>점 하나짜리 잡티. 커널보다 작다.</summary>
    private Mat WithSpeck(int x = 8, int y = 8, float value = 0.6f)
    {
        Mat mat = Zeros();
        mat.Set(y, x, value);
        return mat;
    }

    /// <summary>덩어리 결함. 커널보다 훨씬 크다.</summary>
    private Mat WithBlob(int x0 = 18, int y0 = 18, int side = 7, float value = 0.6f)
    {
        Mat mat = Zeros();

        for (int y = y0; y < y0 + side; y++)
        {
            for (int x = x0; x < x0 + side; x++) mat.Set(y, x, value);
        }

        return mat;
    }

    /// <summary>점 잡티 하나 + 덩어리 결함 하나. 진짜 차이 이미지를 흉내 낸다.</summary>
    private Mat WithBoth()
    {
        Mat mat = WithBlob();
        mat.Set(8, 8, 0.6f);
        return mat;
    }

    /// <summary>바탕 패턴 위에 작은 입자 하나 — TopHat이 뽑아낼 그림.</summary>
    private Mat PatternWithParticle()
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.3)));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 12; x < 20; x++) mat.Set(y, x, 0.6f);   // 세로 패턴
        }

        mat.Set(5, 5, 0.9f);                                      // 입자
        return mat;
    }

    private static float At(Mat mat, int y, int x) => mat.At<float>(y, x);

    // ── 스텝 하나하나 ───────────────────────────────────────────────────

    [Fact]
    public void 중앙값은_점_잡티를_지운다()
    {
        using Mat result = new MedianFilter(3).Apply(WithSpeck());

        Assert.Equal(0.0f, At(result, 8, 8), tolerance: 1e-6);
        Assert.Equal(0.0, Cv2.Sum(result).Val0, tolerance: 1e-6);
    }

    /// <summary>
    /// ★ 공짜는 아니다 — 중앙값은 덩어리의 <b>모서리를 깎는다.</b>
    ///   모서리 픽셀은 3×3 창에서 같은 편이 4칸뿐이라 <b>다수결에서 진다</b>(4 대 5).
    ///   7×7이 45칸으로 줄어든다. 놓치진 않지만 <b>면적이 작게 잡힌다.</b>
    /// </summary>
    [Fact]
    public void 중앙값은_덩어리의_모서리를_깎는다()
    {
        using Mat result = new MedianFilter(3).Apply(WithBlob());

        Assert.Equal(0.6f, At(result, 21, 21), tolerance: 1e-6);
        Assert.Equal(0.0f, At(result, 18, 18), tolerance: 1e-6);
        Assert.Equal(45 * 0.6, Cv2.Sum(result).Val0, tolerance: 1e-4);
    }

    /// <summary>
    /// ★ Gaussian은 점을 <b>없애지 않고 넓힌다.</b> 세기는 낮아지지만 면적이 커진다.
    ///   그래서 차이 이미지의 점 잡티에는 Median이 맞다.
    /// </summary>
    [Fact]
    public void 가우시안은_점을_지우지_않고_넓힌다()
    {
        using Mat result = new GaussianFilter(3).Apply(WithSpeck());

        Assert.True(At(result, 8, 8) < 0.6f, "가운데는 낮아진다");
        Assert.True(At(result, 8, 9) > 0.0f, "옆 픽셀은 없던 값이 생긴다");
        Assert.True(Cv2.Sum(result).Val0 > 0.5, "총량은 남는다");
    }

    /// <summary>
    /// ★★ TopHat = 원본 − Open. Open이 작은 밝은 점을 지우니,
    ///   <b>원본에서 빼면 지워진 그 점만 남는다.</b> 골든 없이 패턴 위 입자를 뽑는다.
    /// </summary>
    [Fact]
    public void TopHat은_패턴은_지우고_입자만_남긴다()
    {
        using Mat result = new MorphologyFilter(MorphologyFilter.Op.TopHat, 3).Apply(PatternWithParticle());

        Assert.True(At(result, 5, 5) > 0.5f, $"입자가 남아야 한다 — {At(result, 5, 5):F2}");
        Assert.Equal(0.0f, At(result, 5, 15), tolerance: 1e-5);   // 패턴 자리
        Assert.Equal(0.0f, At(result, 25, 2), tolerance: 1e-5);   // 배경
    }

    /// <summary>열림(Open)은 커널보다 가는 것을 지우고 덩어리는 남긴다.</summary>
    [Fact]
    public void 열림은_잡티를_지우고_덩어리는_남긴다()
    {
        using Mat result = new MorphologyFilter(MorphologyFilter.Op.Open, 3).Apply(WithBoth());

        Assert.Equal(0.0f, At(result, 8, 8), tolerance: 1e-6);
        Assert.Equal(0.6f, At(result, 21, 21), tolerance: 1e-6);
    }

    // ── 커널 규칙 ───────────────────────────────────────────────────────

    /// <summary>OpenCV 제한 — 실측으로 k=3·5만 되고 7부터 예외가 난다.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void 중앙값은_커널_3과_5를_받는다(int kernel)
        => Assert.NotNull(new MedianFilter(kernel));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4)]
    public void 중앙값은_그_밖의_커널을_거부한다(int kernel)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MedianFilter(kernel));

    /// <summary>
    /// ★ OpenCV는 짝수 커널을 <b>예외 없이 통과시킨다</b>(실측).
    ///   중심이 없어 결과가 한쪽으로 치우치는데 아무도 안 알려준다. 그래서 우리가 막는다.
    /// </summary>
    [Fact]
    public void 짝수_커널은_모든_필터가_거부한다()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaussianFilter(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MorphologyFilter(MorphologyFilter.Op.Open, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SobelFilter(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LaplacianFilter(4));
    }

    // ── 체인 ───────────────────────────────────────────────────────────

    [Fact]
    public void 빈_체인은_통과시키되_새_이미지를_낸다()
    {
        Mat source = WithBoth();

        PipelineContext context = new();
        context.SetInput("src", source);

        new FilterNode("f", [], "src", "out").Execute(context);

        Mat result = context.Get<Mat>("out");

        Assert.False(ReferenceEquals(source, result));
        Assert.Equal(Cv2.Sum(source).Val0, Cv2.Sum(result).Val0, tolerance: 1e-6);

        context.ReleaseAll();
    }

    /// <summary>★ 순서대로 걸린다 — 중앙값으로 점을 지우고, 열림으로 한 번 더 다듬는다.</summary>
    [Fact]
    public void 체인은_순서대로_걸린다()
    {
        Mat source = WithBoth();

        PipelineContext context = new();
        context.SetInput("src", source);

        new FilterNode("f",
            [new MedianFilter(3), new MorphologyFilter(MorphologyFilter.Op.Open, 3)],
            "src", "out").Execute(context);

        Mat result = context.Get<Mat>("out");

        Assert.Equal(0.0f, At(result, 8, 8), tolerance: 1e-6);
        Assert.True(At(result, 21, 21) > 0.5f, "덩어리는 남아야 한다");

        context.ReleaseAll();
    }

    /// <summary>★ 체인을 거치는 동안 <b>칠판의 원본은 안 변한다.</b></summary>
    [Fact]
    public void 체인은_원본을_건드리지_않는다()
    {
        Mat source = WithBoth();
        using Mat before = source.Clone();

        PipelineContext context = new();
        context.SetInput("src", source);

        new FilterNode("f", [new MedianFilter(3), new GaussianFilter(3)], "src", "out").Execute(context);

        using Mat diff = new();
        Cv2.Absdiff(source, before, diff);

        Assert.Equal(0.0, Cv2.Sum(diff).Val0, tolerance: 1e-6);

        context.ReleaseAll();
    }

    // ── 카탈로그와 검증 ────────────────────────────────────────────────

    /// <summary>★ 스텝 타입의 단일 진실원. 새 필터를 넣으면 여기 하나만 늘어난다.</summary>
    [Fact]
    public void 카탈로그가_여섯_종류를_들고_있다()
    {
        string[] types = FilterStepCatalog.Catalog.Select(c => c.Type).ToArray();

        Assert.Equal(["Median", "Gaussian", "Morphology", "Sobel", "Laplacian", "Bilateral"], types);
    }

    /// <summary>
    /// ★★ filters[]는 서술자로 못 적어서 팩토리가 검증한다.
    ///   그래도 <b>규칙을 새로 짜지 않는다</b> — 카탈로그의 ParamDescriptor를 ParamCheck에 넘긴다.
    /// </summary>
    [Fact]
    public void 체인_안의_잘못된_커널도_잡아낸다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "bad.filter",
              "contextInputs": [ "roi" ],
              "pipeline": { "nodes": [
                { "id": "f", "type": "Filter",
                  "inputs": { "src": "roi" }, "outputs": { "result": "out" },
                  "params": { "filters": [ { "type": "Median", "kernel": 7 } ] } }
              ] }
            }
            """);

        IReadOnlyList<string> errors = RecipeValidator.Validate(recipe, CvBackend.CreateRegistry());

        Assert.Single(errors);
        Assert.Contains("filters[0]", errors[0]);
        Assert.Contains("kernel", errors[0]);
    }

    [Fact]
    public void 모르는_필터_이름은_쓸_수_있는_것들을_알려준다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "bad.filter.type",
              "contextInputs": [ "roi" ],
              "pipeline": { "nodes": [
                { "id": "f", "type": "Filter",
                  "inputs": { "src": "roi" }, "outputs": { "result": "out" },
                  "params": { "filters": [ { "type": "Blur", "kernel": 3 } ] } }
              ] }
            }
            """);

        IReadOnlyList<string> errors = RecipeValidator.Validate(recipe, CvBackend.CreateRegistry());

        Assert.Single(errors);
        Assert.Contains("Median", errors[0]);
        Assert.Contains("Morphology", errors[0]);
    }

    /// <summary>★ 두 번째 항목의 op가 틀렸다 — 몇 번째인지 알려줘야 한다.</summary>
    [Fact]
    public void 체인의_몇_번째가_틀렸는지_알려준다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "bad.filter.op",
              "contextInputs": [ "roi" ],
              "pipeline": { "nodes": [
                { "id": "f", "type": "Filter",
                  "inputs": { "src": "roi" }, "outputs": { "result": "out" },
                  "params": { "filters": [
                    { "type": "Median", "kernel": 3 },
                    { "type": "Morphology", "op": "Explode", "kernel": 3 }
                  ] } }
              ] }
            }
            """);

        IReadOnlyList<string> errors = RecipeValidator.Validate(recipe, CvBackend.CreateRegistry());

        Assert.Single(errors);
        Assert.Contains("filters[1]", errors[0]);
    }

    // ── 파이프라인 ─────────────────────────────────────────────────────

    private sealed class PeekNode : IPipelineNode
    {
        private readonly string _key;
        private readonly Action<Mat> _peek;

        public PeekNode(string key, Action<Mat> peek)
        {
            _key = key;
            _peek = peek;
        }

        public string Id => "peek";

        public string TypeName => "Peek";

        public void Execute(IPipelineContext context) => _peek(context.Get<Mat>(_key));
    }

    /// <summary>★ 교수님 die.sigma.json과 같은 체인 — 중앙값 다음 열림.</summary>
    [Fact]
    public void 교수님_레시피_모양_그대로_돈다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "die.filter.chain",
              "contextInputs": [ "roi", "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } },
                { "id": "diff", "type": "Difference",
                  "inputs": { "test": "roi", "golden": "golden" },
                  "outputs": { "result": "diff" },
                  "params": { "mode": "Absolute" } },
                { "id": "clean", "type": "Filter",
                  "inputs": { "src": "diff" },
                  "outputs": { "result": "filtered" },
                  "params": { "filters": [
                    { "type": "Median", "kernel": 3 },
                    { "type": "Morphology", "op": "Open", "kernel": 3 }
                  ] } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("roi", WithBoth());
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[Zeros(), Zeros(), Zeros()]);

        float speck = float.NaN;
        float blob = float.NaN;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("filtered", d => { speck = At(d, 8, 8); blob = At(d, 21, 21); }),
        ];

        new PipelineEngine().Run(nodes, context);

        Assert.Equal(0.0f, speck, tolerance: 1e-6);
        Assert.True(blob > 0.5f, "덩어리는 남아야 한다");
    }

    [Fact]
    public void 명부에_Filter가_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Filter");

        Assert.Equal("전처리", descriptor.Category);

        // ★ filters[]는 중첩이라 서술자로 못 적는다 — 그래서 Params가 비어 있다.
        Assert.Empty(descriptor.Params);
    }
}