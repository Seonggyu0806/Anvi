using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "가우시안과 중앙값의 성격이 정반대"라는 것.</summary>
public sealed class NormalizeTests : IDisposable
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

    private Mat Flat(float value) => Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(value)));

    /// <summary>한가운데에 점 하나만 튀는 이미지 — 점 노이즈를 흉내 낸다.</summary>
    private Mat WithSpike(float background, float spike)
    {
        Mat mat = Flat(background);
        mat.Set(16, 16, spike);
        return mat;
    }

    /// <summary>왼쪽 절반은 어둡고 오른쪽 절반은 밝은 이미지 — 가운데에 에지가 하나 있다.</summary>
    private Mat Edge(float left, float right)
    {
        Mat mat = Flat(left);

        for (int y = 0; y < Size; y++)
        {
            for (int x = Size / 2; x < Size; x++)
            {
                mat.Set(y, x, right);
            }
        }

        return mat;
    }

    private static float At(Mat mat, int y, int x) => mat.At<float>(y, x);

    [Fact]
    public void None은_값을_그대로_둔다()
    {
        Mat source = WithSpike(0.5f, 1.0f);

        using Mat result = Normalizers.Normalize(source, NormalizeMethod.None);

        Assert.Equal(1.0f, At(result, 16, 16), tolerance: 1e-6);
        Assert.Equal(0.5f, At(result, 0, 0), tolerance: 1e-6);
    }

    /// <summary>★ None도 복사본을 낸다 — 안 그러면 같은 Mat을 칠판이 두 번 놓아버린다.</summary>
    [Fact]
    public void None도_새_이미지를_만든다()
    {
        Mat source = Flat(0.5f);

        using Mat result = Normalizers.Normalize(source, NormalizeMethod.None);

        Assert.False(ReferenceEquals(source, result));
    }

    [Fact]
    public void 가우시안은_튀는_점을_눌러준다()
    {
        Mat source = WithSpike(0.5f, 1.0f);

        using Mat result = Normalizers.Normalize(source, NormalizeMethod.GaussianBlur, kernelSize: 5);

        Assert.True(At(result, 16, 16) < 1.0f, "튀는 점이 눌려야 한다");
        Assert.True(At(result, 16, 16) > 0.5f, "완전히 사라지면 안 된다 — 이웃으로 퍼질 뿐이다");
    }

    /// <summary>★ 가우시안은 이웃에 '퍼뜨린다' — 옆 픽셀이 같이 올라간다.</summary>
    [Fact]
    public void 가우시안은_튀는_점을_이웃으로_퍼뜨린다()
    {
        Mat source = WithSpike(0.5f, 1.0f);

        using Mat result = Normalizers.Normalize(source, NormalizeMethod.GaussianBlur, kernelSize: 5);

        Assert.True(At(result, 16, 17) > 0.5f, "옆 픽셀도 올라가 있어야 한다");
    }

    /// <summary>★ 중앙값은 튀는 점을 '지운다' — 퍼뜨리지 않는다. 가우시안과 성격이 정반대다.</summary>
    [Fact]
    public void 중앙값은_튀는_점을_아예_지운다()
    {
        Mat source = WithSpike(0.5f, 1.0f);

        using Mat result = Normalizers.Normalize(source, NormalizeMethod.MedianBlur, kernelSize: 3);

        Assert.Equal(0.5f, At(result, 16, 16), tolerance: 1e-6);
        Assert.Equal(0.5f, At(result, 16, 17), tolerance: 1e-6);
    }

    /// <summary>★ 에지에서 갈린다 — 가우시안은 경계를 번지게 하고, 중앙값은 살린다.</summary>
    [Fact]
    public void 중앙값은_에지를_살리고_가우시안은_번지게_한다()
    {
        Mat source = Edge(left: 0.2f, right: 0.8f);

        using Mat gaussian = Normalizers.Normalize(source, NormalizeMethod.GaussianBlur, kernelSize: 5);
        using Mat median = Normalizers.Normalize(source, NormalizeMethod.MedianBlur, kernelSize: 5);

        // 에지 바로 왼쪽 픽셀(x=15)은 원래 0.2다.
        Assert.True(At(gaussian, 16, 15) > 0.25f, "가우시안은 오른쪽 밝기를 끌어와 번진다");
        Assert.Equal(0.2f, At(median, 16, 15), tolerance: 1e-6);
    }

    [Fact]
    public void 짝수_커널은_거부한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Normalizers.Normalize(Flat(0.5f), NormalizeMethod.GaussianBlur, kernelSize: 4));

    /// <summary>
    /// ★ OpenCV 제한 — MedianBlur는 GrayF32에서 커널 5까지만 된다.
    ///   그대로 두면 OpenCVException이 나는데, 그 메시지로는 무엇을 해야 할지 알 수 없다.
    /// </summary>
    [Fact]
    public void 중앙값_커널이_5를_넘으면_무엇을_하라고_알려준다()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Normalizers.Normalize(Flat(0.5f), NormalizeMethod.MedianBlur, kernelSize: 7));

        Assert.Contains("GaussianBlur", error.Message);
    }

    [Fact]
    public void 작업_포맷이_아니면_거부한다()
    {
        Mat eightBit = Keep(new Mat(Size, Size, MatType.CV_8UC1, new Scalar(128)));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Normalizers.Normalize(eightBit, NormalizeMethod.GaussianBlur));

        Assert.Contains("GrayF32", error.Message);
    }

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

    /// <summary>★ 브리핑 흐름의 1~2단계가 이어진다 — Combine 다음에 Normalize.</summary>
    [Fact]
    public void 골든을_만들고_바로_정규화한다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "golden.norm.v1",
              "contextInputs": [ "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } },
                { "id": "norm", "type": "Normalize",
                  "inputs": { "src": "golden" },
                  "outputs": { "result": "goldenNorm" },
                  "params": { "method": "GaussianBlur", "kernelSize": 5, "sigma": 1.2 } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[Flat(0.5f), Flat(0.5f), WithSpike(0.5f, 1.0f)]);

        float spot = float.NaN;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("goldenNorm", g => spot = At(g, 16, 16)),
        ];

        new PipelineEngine().Run(nodes, context);

        // 중앙값이 오염을 지웠고, 블러는 평평한 이미지를 바꾸지 않는다.
        Assert.Equal(0.5f, spot, tolerance: 1e-5);
    }

    [Fact]
    public void 명부에_Normalize가_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Normalize");

        Assert.Equal("전처리", descriptor.Category);
        Assert.Contains("MedianBlur", descriptor.Params[0].Choices!);
    }
}