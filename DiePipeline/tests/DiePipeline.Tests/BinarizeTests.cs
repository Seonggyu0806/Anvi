using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "어디부터 결함으로 칠 것인가" — 여섯 가지 답.</summary>
public sealed class BinarizeTests : IDisposable
{
    private const int Size = 64;

    /// <summary>결함 덩어리 6×6 = 36칸.</summary>
    private const int DefectPixels = 36;

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

    /// <summary>차이 이미지 흉내 — 배경은 약한 잡음, 한군데만 결함 덩어리.</summary>
    private Mat DiffImage(float defect = 0.4f, double noise = 0.05, int seed = 7)
    {
        Random random = new(seed);

        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++) mat.Set(y, x, (float)(random.NextDouble() * noise));
        }

        for (int y = 30; y < 36; y++)
        {
            for (int x = 30; x < 36; x++) mat.Set(y, x, defect);
        }

        return mat;
    }

    private Mat Values(params float[] values)
    {
        Mat mat = Keep(new Mat(1, values.Length, MatType.CV_32FC1));

        for (int i = 0; i < values.Length; i++) mat.Set(0, i, values[i]);

        return mat;
    }

    /// <summary>왼쪽에서 오른쪽으로 밝아지는 배경 + 작은 밝은 점 — 조명이 기운 상황.</summary>
    private Mat Tilted(float slopeTo = 0.8f)
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++) mat.Set(y, x, slopeTo * x / (Size - 1));
        }

        for (int y = 10; y < 14; y++)
        {
            for (int x = 10; x < 14; x++) mat.Set(y, x, mat.At<float>(y, x) + 0.3f);
        }

        return mat;
    }

    private static byte At(Mat mask, int x) => mask.At<byte>(0, x);

    private static int Count(Mat mask) => Cv2.CountNonZero(mask);

    // ── 출력 약속 ───────────────────────────────────────────────────────

    /// <summary>
    /// ★ 여기서 작업 포맷을 벗어난다 — 마스크는 <b>Gray8(0/255)</b>이다.
    ///   "있다/없다"뿐이라 실수가 필요 없고, 다음 단계 Label의 ConnectedComponents가 8비트만 받는다.
    /// </summary>
    [Fact]
    public void 출력은_Gray8이다()
    {
        using Mat mask = new ThresholdBinarizer(0.2).Binarize(DiffImage());

        Assert.Equal(MatType.CV_8UC1, mask.Type());
    }

    [Fact]
    public void 출력에는_0과_255만_있다()
    {
        using Mat mask = new ThresholdBinarizer(0.2).Binarize(DiffImage());

        Cv2.MinMaxLoc(mask, out double min, out double max);

        Assert.Equal(0.0, min);
        Assert.Equal(255.0, max);
    }

    [Fact]
    public void 가르는_동안_입력을_건드리지_않는다()
    {
        Mat source = DiffImage();
        using Mat before = source.Clone();

        using Mat mask = new SigmaThresholdBinarizer(3.0).Binarize(source);

        using Mat diff = new();
        Cv2.Absdiff(source, before, diff);

        Assert.Equal(0.0, Cv2.Sum(diff).Val0, tolerance: 1e-6);
    }

    // ── Threshold — 경계 ────────────────────────────────────────────────

    /// <summary>
    /// ★★ Lesson09가 [InlineData]로 못 박고 Lesson15가 주석으로 옮겨 적은 사실.
    ///   Cv2.Threshold의 Binary는 <b>'이상'이 아니라 '초과'</b>다.
    ///   임계와 <b>똑같은</b> 값은 전경이 안 된다.
    /// </summary>
    [Fact]
    public void Threshold는_이상이_아니라_초과다()
    {
        using Mat mask = new ThresholdBinarizer(0.10).Binarize(Values(0.09f, 0.10f, 0.11f));

        Assert.Equal(0, At(mask, 0));     // 미만
        Assert.Equal(0, At(mask, 1));     // ★ 딱 임계 — 전경이 아니다
        Assert.Equal(255, At(mask, 2));   // 초과
    }

    [Fact]
    public void Threshold는_결함을_정확히_잡는다()
    {
        using Mat mask = new ThresholdBinarizer(0.2).Binarize(DiffImage(defect: 0.4f, noise: 0.05));

        Assert.Equal(DefectPixels, Count(mask));
    }

    /// <summary>★ 고정 임계의 약점 — 결함이 임계보다 약하면 <b>통째로 놓친다.</b></summary>
    [Fact]
    public void Threshold는_약한_결함을_통째로_놓친다()
    {
        using Mat mask = new ThresholdBinarizer(0.2).Binarize(DiffImage(defect: 0.15f, noise: 0.05));

        Assert.Equal(0, Count(mask));
    }

    // ── Range — 구간만 ──────────────────────────────────────────────────

    /// <summary>★ InRange는 <b>양 끝을 포함</b>한다. Threshold의 '초과'와 다르다.</summary>
    [Fact]
    public void Range는_양_끝을_포함한다()
    {
        using Mat mask = new RangeBinarizer(0.10, 0.30).Binarize(Values(0.09f, 0.10f, 0.20f, 0.30f, 0.31f));

        Assert.Equal(0, At(mask, 0));
        Assert.Equal(255, At(mask, 1));   // 하한 포함
        Assert.Equal(255, At(mask, 2));
        Assert.Equal(255, At(mask, 3));   // 상한 포함
        Assert.Equal(0, At(mask, 4));
    }

    /// <summary>high &lt; low면 빈 마스크가 된다(InRange 규약). 예외가 아니다.</summary>
    [Fact]
    public void Range는_거꾸로_주면_빈_마스크를_낸다()
    {
        using Mat mask = new RangeBinarizer(0.8, 0.2).Binarize(DiffImage());

        Assert.Equal(0, Count(mask));
    }

    // ── Otsu — 스스로 가른다 ────────────────────────────────────────────

    /// <summary>
    /// 배경과 결함이 <b>비슷한 크기의 두 무리</b>일 때 Otsu가 제 몫을 한다.
    /// </summary>
    [Fact]
    public void Otsu는_두_무리로_갈릴_때_잘_가른다()
    {
        Mat twoGroups = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.2)));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size / 2; x++) twoGroups.Set(y, x, 0.8f);
        }

        using Mat mask = new OtsuBinarizer().Binarize(twoGroups);

        Assert.Equal(Size * Size / 2, Count(mask));
    }

    /// <summary>
    /// ★★ Otsu의 한계 — <b>배경이 시끄러워지면 무너진다.</b>
    ///   결함이 4096칸 중 36칸(0.9%)뿐이라 "두 무리" 가정이 성립하지 않고,
    ///   그러면 Otsu는 결함이 아니라 <b>배경 잡음을 둘로 가른다.</b>
    ///
    ///   실웨이퍼 네 구역 측정 — 결함이 없는 die인데도 860~4939칸을 잡았다.
    ///   그래서 die-to-die 레시피는 Threshold를 쓴다(교수님 case1.golden.diff.json = 0.12).
    /// </summary>
    [Fact]
    public void Otsu는_배경이_시끄러우면_무너진다()
    {
        // 잡음이 얕으면(0.05) Otsu도 36칸을 맞춘다 — 두 무리가 선명하니까.
        using Mat quiet = new OtsuBinarizer().Binarize(DiffImage(defect: 0.4f, noise: 0.05));
        Assert.Equal(DefectPixels, Count(quiet));

        // 잡음이 깊어지면(0.20) 무리가 하나로 뭉개져 배경을 가른다.
        using Mat noisy = new OtsuBinarizer().Binarize(DiffImage(defect: 0.4f, noise: 0.20));
        Assert.True(Count(noisy) > DefectPixels * 10,
            $"배경까지 잡힌다(그게 이 방법의 한계다) — {Count(noisy)}칸");

        // 같은 자리에서 고정 임계는 36칸 그대로다.
        using Mat fixedMask = new ThresholdBinarizer(0.3).Binarize(DiffImage(defect: 0.4f, noise: 0.20));
        Assert.Equal(DefectPixels, Count(fixedMask));
    }

    // ── Sigma — 분포 대비 ───────────────────────────────────────────────

    [Fact]
    public void Sigma는_배경_분포_대비로_잡는다()
    {
        using Mat mask = new SigmaThresholdBinarizer(3.0).Binarize(DiffImage(defect: 0.15f, noise: 0.05));

        Assert.Equal(DefectPixels, Count(mask));
    }

    /// <summary>k는 "얼마나 깐깐하게 볼지"다. 키우면 덜 잡는다.</summary>
    [Fact]
    public void Sigma는_k를_키우면_깐깐해진다()
    {
        Mat source = DiffImage(defect: 0.15f, noise: 0.05);

        using Mat loose = new SigmaThresholdBinarizer(1.0).Binarize(source);
        using Mat tight = new SigmaThresholdBinarizer(3.0).Binarize(source);

        Assert.True(Count(loose) > Count(tight), "k가 작으면 잡음까지 같이 잡힌다");
    }

    // ── Adaptive — 국부 평균 대비 ───────────────────────────────────────

    /// <summary>
    /// ★ 배경이 기울어 있으면 고정 임계는 못 가른다 — 오른쪽 절반이 통째로 잡힌다.
    ///   Adaptive는 <b>자기 주변과만</b> 비교하니까 기울기를 통과시킨다.
    /// </summary>
    [Fact]
    public void Adaptive는_기울어진_배경에서도_점만_잡는다()
    {
        Mat tilted = Tilted();

        using Mat fixedMask = new ThresholdBinarizer(0.5).Binarize(tilted);
        using Mat adaptive = new AdaptiveBinarizer(15, 0.05, AdaptiveBinarizer.Mode.Bright).Binarize(tilted);

        Assert.True(Count(fixedMask) > 1000, $"고정 임계는 오른쪽을 통째로 잡는다 — {Count(fixedMask)}칸");
        Assert.True(Count(adaptive) < 100, $"적응형은 점만 잡는다 — {Count(adaptive)}칸");
        Assert.Equal(255, adaptive.At<byte>(11, 11));
    }

    [Fact]
    public void Adaptive의_blockSize는_홀수여야_한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new AdaptiveBinarizer(16, 0.05, AdaptiveBinarizer.Mode.Bright));

    // ── Hysteresis — 이중 임계 ──────────────────────────────────────────

    /// <summary>
    /// ★★ 강 임계를 넘은 <b>씨앗</b>에 이어져 있으면 약한 픽셀도 살린다.
    ///   고립된 약한 잡음은 버린다. Canny가 쓰는 방식이다.
    /// </summary>
    [Fact]
    public void Hysteresis는_강한_것에_붙은_약한_것만_살린다()
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0)));

        // ① 강한 씨앗(0.8) + 거기 붙은 약한 꼬리(0.4)
        for (int x = 10; x < 14; x++) mat.Set(10, x, 0.8f);
        for (int x = 14; x < 20; x++) mat.Set(10, x, 0.4f);

        // ② 멀리 떨어진 약한 점(0.4)만 — 씨앗이 없다
        for (int x = 40; x < 46; x++) mat.Set(40, x, 0.4f);

        using Mat mask = new HysteresisBinarizer(0.3, 0.7).Binarize(mat);

        Assert.Equal(255, mask.At<byte>(10, 11));   // 씨앗
        Assert.Equal(255, mask.At<byte>(10, 17));   // 씨앗에 붙은 약한 꼬리 → 살린다
        Assert.Equal(0, mask.At<byte>(40, 42));     // 고립된 약한 점 → 버린다
        Assert.Equal(10, Count(mask));
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

    /// <summary>
    /// ★ 교수님 <c>case1.golden.diff.json</c>과 같은 모양.
    ///   Combine → Normalize → Difference → Filter(체인) → Binarize(Threshold 0.12).
    /// </summary>
    [Fact]
    public void 교수님_die_to_die_레시피_모양_그대로_돈다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "case1.golden.diff",
              "contextInputs": [ "roi", "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Mean" } },
                { "id": "normalize", "type": "Normalize",
                  "inputs": { "src": "golden" },
                  "outputs": { "result": "goldenNorm" },
                  "params": { "method": "None" } },
                { "id": "diff", "type": "Difference",
                  "inputs": { "test": "roi", "golden": "goldenNorm" },
                  "outputs": { "result": "diff" },
                  "params": { "mode": "Absolute" } },
                { "id": "blur", "type": "Filter",
                  "inputs": { "src": "diff" },
                  "outputs": { "result": "filtered" },
                  "params": { "filters": [ { "type": "Median", "kernel": 3 } ] } },
                { "id": "binarize", "type": "Binarize",
                  "inputs": { "src": "filtered" },
                  "outputs": { "result": "mask" },
                  "params": { "method": "Threshold", "value": 0.12 } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("roi", DiffImage(defect: 0.4f, noise: 0.05));
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)
        [
            DiffImage(defect: 0.0f, noise: 0.05),
            DiffImage(defect: 0.0f, noise: 0.05),
            DiffImage(defect: 0.0f, noise: 0.05),
        ]);

        int found = -1;
        MatType type = default;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("mask", m => { found = Count(m); type = m.Type(); }),
        ];

        new PipelineEngine().Run(nodes, context);

        Assert.Equal(MatType.CV_8UC1, type);

        // ★ 36칸이 아니라 32칸이다 — 체인의 Median이 6×6 덩어리의 <b>네 모서리를 깎았다.</b>
        //   모서리는 3×3 창에서 같은 편이 4칸뿐이라 다수결에서 진다.
        //   놓치는 건 아니지만 <b>면적이 실제보다 작게 잡힌다</b>는 뜻이다.
        Assert.Equal(DefectPixels - 4, found);
    }

    [Fact]
    public void 명부에_Binarize가_여섯_종류로_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Binarize");

        Assert.Equal("판정", descriptor.Category);
        Assert.Equal(
            ["Threshold", "Otsu", "Range", "Sigma", "Adaptive", "Hysteresis"],
            descriptor.Params[0].Choices!);
    }

    /// <summary>★ 기본값은 교수님 파일 그대로다. 우리가 임의로 바꾸지 않는다.</summary>
    [Fact]
    public void 기본값은_교수님_파일_그대로다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Binarize");

        Dictionary<string, object?> defaults =
            descriptor.Params.ToDictionary(p => p.Name, p => p.Default);

        Assert.Equal("Threshold", defaults["method"]);
        Assert.Equal(0.5, defaults["value"]);
        Assert.Equal(0.0, defaults["low"]);
        Assert.Equal(1.0, defaults["high"]);
        Assert.Equal(3.0, defaults["k"]);
        Assert.Equal(15, defaults["blockSize"]);
        Assert.Equal(0.05, defaults["C"]);
        Assert.Equal("Bright", defaults["direction"]);
    }
}