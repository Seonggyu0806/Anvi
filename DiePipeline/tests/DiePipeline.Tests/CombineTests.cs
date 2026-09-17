using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>골든 만들기. ★ 오늘 확인할 것은 "중앙값이 오염된 이웃을 무시하는가"다.</summary>
public sealed class CombineTests : IDisposable
{
    private const int Size = 8;

    // 테스트가 만든 Mat들. 여기선 파이프라인이 아니라 우리가 치운다.
    private readonly List<Mat> _made = [];

    public void Dispose()
    {
        foreach (Mat mat in _made) mat.Dispose();
    }

    private Mat Flat(float value)
    {
        Mat mat = new(Size, Size, MatType.CV_32FC1, new Scalar(value));
        _made.Add(mat);
        return mat;
    }

    /// <summary>한가운데에 점 하나만 다른 이미지 — '오염된 이웃'을 흉내 낸다.</summary>
    private Mat WithSpot(float background, float spot)
    {
        Mat mat = Flat(background);
        mat.Set(4, 4, spot);
        return mat;
    }

    private static float At(Mat mat, int y, int x) => mat.At<float>(y, x);

    [Fact]
    public void 한_장만_주면_그대로_나온다()
    {
        using Mat golden = CombineStrategies.Combine([Flat(0.5f)], CombineStrategy.Median);

        Assert.Equal(0.5f, At(golden, 0, 0), tolerance: 1e-6);
    }

    [Fact]
    public void 평균은_말_그대로_평균이다()
    {
        using Mat golden = CombineStrategies.Combine(
            [Flat(0.2f), Flat(0.4f), Flat(0.6f)], CombineStrategy.Mean);

        Assert.Equal(0.4f, At(golden, 0, 0), tolerance: 1e-6);
    }

    [Fact]
    public void 중앙값은_가운데_값을_고른다()
    {
        using Mat golden = CombineStrategies.Combine(
            [Flat(0.1f), Flat(0.5f), Flat(0.9f)], CombineStrategy.Median);

        Assert.Equal(0.5f, At(golden, 0, 0), tolerance: 1e-6);
    }

    [Fact]
    public void 짝수_장이면_가운데_둘을_평균한다()
    {
        using Mat golden = CombineStrategies.Combine(
            [Flat(0.1f), Flat(0.3f), Flat(0.5f), Flat(0.9f)], CombineStrategy.Median);

        Assert.Equal(0.4f, At(golden, 0, 0), tolerance: 1e-6);
    }

    /// <summary>★ Median을 기본값으로 고른 이유 전부가 이 테스트다.</summary>
    [Fact]
    public void 중앙값은_오염된_이웃_한_장을_무시한다()
    {
        Mat dirty = WithSpot(background: 0.5f, spot: 0.0f);

        using Mat median = CombineStrategies.Combine(
            [Flat(0.5f), Flat(0.5f), dirty], CombineStrategy.Median);

        // 오염된 자리인데도 골든은 깨끗하다.
        Assert.Equal(0.5f, At(median, 4, 4), tolerance: 1e-6);
    }

    /// <summary>같은 오염을 평균은 1/3만큼 끌고 들어온다 — 그게 유령 결함이 된다.</summary>
    [Fact]
    public void 평균은_같은_오염에_끌려간다()
    {
        Mat dirty = WithSpot(background: 0.5f, spot: 0.0f);

        using Mat mean = CombineStrategies.Combine(
            [Flat(0.5f), Flat(0.5f), dirty], CombineStrategy.Mean);

        Assert.Equal(1.0f / 3.0f, At(mean, 4, 4), tolerance: 1e-5);
        Assert.True(At(mean, 4, 4) < 0.5f, "평균 골든은 오염 쪽으로 끌려가야 한다");
    }

    [Fact]
    public void Min과_Max는_양_끝을_고른다()
    {
        Mat[] three = [Flat(0.1f), Flat(0.5f), Flat(0.9f)];

        using Mat min = CombineStrategies.Combine(three, CombineStrategy.Min);
        using Mat max = CombineStrategies.Combine(three, CombineStrategy.Max);

        Assert.Equal(0.1f, At(min, 0, 0), tolerance: 1e-6);
        Assert.Equal(0.9f, At(max, 0, 0), tolerance: 1e-6);
    }

    [Fact]
    public void 빈_목록은_거부한다()
        => Assert.Throws<ArgumentException>(() => CombineStrategies.Combine([], CombineStrategy.Median));

    /// <summary>크기가 다르면 엉뚱한 픽셀끼리 비교되는데 예외는 안 난다 — 입구에서 막는다.</summary>
    [Fact]
    public void 크기가_다르면_거부한다()
    {
        Mat small = new(4, 4, MatType.CV_32FC1, new Scalar(0.5));
        _made.Add(small);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CombineStrategies.Combine([Flat(0.5f), small], CombineStrategy.Median));

        Assert.Contains("크기가 다릅니다", error.Message);
    }

    [Fact]
    public void 작업_포맷이_아니면_거부한다()
    {
        Mat eightBit = new(Size, Size, MatType.CV_8UC1, new Scalar(128));
        _made.Add(eightBit);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CombineStrategies.Combine([eightBit], CombineStrategy.Median));

        Assert.Contains("GrayF32", error.Message);
    }

    /// <summary>
    /// ★ 값을 '실행 중에' 읽는 이유: 실행이 끝나면 칠판이 골든을 놓아버린다(ReleaseAll).
    ///   끝난 뒤에 Get으로 꺼내면 이미 해제된 Mat을 잡게 된다.
    /// </summary>
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

    [Fact]
    public void 레시피로도_돌아간다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "golden.v1",
              "contextInputs": [ "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[Flat(0.5f), Flat(0.5f), WithSpot(0.5f, 0.0f)]);

        float middle = float.NaN;
        float corner = float.NaN;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("golden", g => { middle = At(g, 4, 4); corner = At(g, 0, 0); }),
        ];

        new PipelineEngine().Run(nodes, context);

        // 오염된 이웃 한 장이 있었지만 중앙값이라 골든은 깨끗하다.
        Assert.Equal(0.5f, middle, tolerance: 1e-6);
        Assert.Equal(0.5f, corner, tolerance: 1e-6);
    }

    [Fact]
    public void 명부에_Combine이_올라가_있다()
    {
        NodeRegistry registry = CvBackend.CreateRegistry();

        INodeDescriptor descriptor = registry.Describe("Combine");

        Assert.Equal(PortKind.ImageList, descriptor.Inputs[0].Kind);
        Assert.Contains("Median", descriptor.Params[0].Choices!);
    }
}