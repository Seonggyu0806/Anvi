using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "픽셀을 <b>덩어리</b>로 묶어 숫자로 바꾸기".</summary>
public sealed class LabelTests : IDisposable
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

    /// <summary>빈 마스크(전부 0).</summary>
    private Mat Mask() => Keep(new Mat(Size, Size, MatType.CV_8UC1, new Scalar(0)));

    private static void Fill(Mat mask, int x0, int y0, int w, int h, byte value = 255)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++) mask.Set(y, x, value);
        }
    }

    private static DefectList Run(Mat mask, LabelOptions? options = null, Mat? intensity = null)
        => Labelers.Label(mask, intensity, options ?? new LabelOptions());

    // ── 덩어리 세기 ─────────────────────────────────────────────────────

    [Fact]
    public void 빈_마스크는_결함이_없다()
    {
        DefectList result = Run(Mask());

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void 떨어진_덩어리는_따로_센다()
    {
        Mat mask = Mask();
        Fill(mask, 2, 2, 4, 4);
        Fill(mask, 20, 20, 5, 5);

        DefectList result = Run(mask);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void 붙어_있는_픽셀은_하나로_묶는다()
    {
        Mat mask = Mask();
        Fill(mask, 2, 2, 10, 1);   // 가로 막대
        Fill(mask, 2, 3, 1, 10);   // 세로 막대 — 위에 닿아 있다

        DefectList result = Run(mask);

        Assert.Single(result.Items);
        Assert.Equal(20, result.Items[0].Area);
    }

    /// <summary>
    /// ★★ 4연결과 8연결의 차이 — <b>대각선으로만 닿은</b> 두 점.
    ///   8연결은 한 덩어리로 보고, 4연결은 남남으로 본다.
    /// </summary>
    [Fact]
    public void 대각선으로만_닿으면_4연결과_8연결이_갈린다()
    {
        Mat mask = Mask();
        mask.Set(5, 5, (byte)255);
        mask.Set(6, 6, (byte)255);   // 대각선으로만 닿음

        DefectList eight = Run(mask, new LabelOptions { Connectivity = 8, MinArea = 0 });
        DefectList four = Run(mask, new LabelOptions { Connectivity = 4, MinArea = 0 });

        Assert.Single(eight.Items);
        Assert.Equal(2, four.Count);
    }

    // ── 재는 값 ─────────────────────────────────────────────────────────

    [Fact]
    public void 덩어리의_위치와_크기를_잰다()
    {
        Mat mask = Mask();
        Fill(mask, 7, 11, 4, 6);

        Defect defect = Run(mask).Items[0];

        Assert.Equal(new BoundingBox(7, 11, 4, 6), defect.Bounds);
        Assert.Equal(24, defect.Area);
    }

    /// <summary>무게중심은 정수로 안 떨어진다 — 4칸짜리의 중심은 칸과 칸 사이다.</summary>
    [Fact]
    public void 무게중심은_소수점으로_나온다()
    {
        Mat mask = Mask();
        Fill(mask, 10, 10, 2, 2);

        Defect defect = Run(mask).Items[0];

        Assert.Equal(10.5, defect.Centroid.X, tolerance: 1e-6);
        Assert.Equal(10.5, defect.Centroid.Y, tolerance: 1e-6);
    }

    // ── 거르기 ──────────────────────────────────────────────────────────

    [Fact]
    public void minArea보다_작은_것은_버린다()
    {
        Mat mask = Mask();
        mask.Set(3, 3, (byte)255);   // 1칸짜리 잡티
        Fill(mask, 20, 20, 5, 5);    // 25칸짜리 결함

        DefectList result = Run(mask, new LabelOptions { MinArea = 5 });

        Assert.Single(result.Items);
        Assert.Equal(25, result.Items[0].Area);
    }

    [Fact]
    public void maxArea보다_큰_것도_버린다()
    {
        Mat mask = Mask();
        Fill(mask, 1, 1, 20, 20);   // 400칸 — 결함이라기엔 너무 크다
        Fill(mask, 25, 25, 3, 3);   // 9칸

        DefectList result = Run(mask, new LabelOptions { MaxArea = 100 });

        Assert.Single(result.Items);
        Assert.Equal(9, result.Items[0].Area);
    }

    /// <summary>
    /// ★ 거른 뒤 번호를 <b>1부터 다시</b> 매긴다.
    ///   안 그러면 결과에 1, 4, 7 처럼 구멍이 생겨 "2번, 3번은 어디 갔나" 가 된다.
    /// </summary>
    [Fact]
    public void 거른_뒤_번호를_1부터_다시_매긴다()
    {
        Mat mask = Mask();
        mask.Set(1, 1, (byte)255);     // 버려질 것
        Fill(mask, 10, 10, 4, 4);      // 남을 것
        mask.Set(1, 20, (byte)255);    // 버려질 것
        Fill(mask, 20, 20, 4, 4);      // 남을 것

        DefectList result = Run(mask, new LabelOptions { MinArea = 5 });

        Assert.Equal([1, 2], result.Items.Select(d => d.Id));
    }

    // ── 밝기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 마스크는 "있다/없다"뿐이라 <b>얼마나 진한 결함인지</b>를 모른다.
    ///   원본을 같이 넘겨야 평균·최대 밝기가 나온다.
    /// </summary>
    [Fact]
    public void 원본을_같이_주면_밝기를_잰다()
    {
        Mat mask = Mask();
        Fill(mask, 10, 10, 2, 2);

        Mat intensity = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0)));
        intensity.Set(10, 10, 0.2f);
        intensity.Set(10, 11, 0.4f);
        intensity.Set(11, 10, 0.6f);
        intensity.Set(11, 11, 0.8f);

        Defect defect = Run(mask, intensity: intensity).Items[0];

        Assert.Equal(0.5, defect.MeanIntensity!.Value, tolerance: 1e-5);
        Assert.Equal(0.8, defect.MaxIntensity!.Value, tolerance: 1e-5);
    }

    [Fact]
    public void 원본을_안_주면_밝기는_비어_있다()
    {
        Mat mask = Mask();
        Fill(mask, 10, 10, 3, 3);

        Defect defect = Run(mask).Items[0];

        Assert.Null(defect.MeanIntensity);
        Assert.Null(defect.MaxIntensity);
    }

    /// <summary>★ 밝기는 <b>그 덩어리 안에서만</b> 잰다. 옆 덩어리 값이 섞이면 안 된다.</summary>
    [Fact]
    public void 밝기는_그_덩어리_안에서만_잰다()
    {
        Mat mask = Mask();
        Fill(mask, 2, 2, 2, 2);
        Fill(mask, 20, 20, 2, 2);

        Mat intensity = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0)));
        Fill32(intensity, 2, 2, 2, 2, 0.2f);
        Fill32(intensity, 20, 20, 2, 2, 0.9f);

        DefectList result = Run(mask, intensity: intensity);

        Assert.Equal(0.2, result.Items[0].MeanIntensity!.Value, tolerance: 1e-5);
        Assert.Equal(0.9, result.Items[1].MeanIntensity!.Value, tolerance: 1e-5);
    }

    private static void Fill32(Mat mat, int x0, int y0, int w, int h, float value)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++) mat.Set(y, x, value);
        }
    }

    // ── 원형도 ──────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 원형도 = 4π × 면적 ÷ 둘레². 같은 면적이면 <b>원이 둘레가 제일 짧다</b>는 성질을 쓴다.
    ///   동그란 것은 1에 가깝고, 길쭉한 것은 0 쪽으로 간다.
    /// </summary>
    [Fact]
    public void 원형도는_동그란_것과_길쭉한_것을_가른다()
    {
        Mat blob = Mask();
        Cv2.Circle(blob, new Point(16, 16), 8, Scalar.All(255), -1);

        Mat streak = Mask();
        Fill(streak, 2, 15, 28, 2);   // 가늘고 긴 줄무늬

        LabelOptions options = new() { ComputeShapeFeatures = true };

        double round = Run(blob, options).Items[0].Circularity!.Value;
        double thin = Run(streak, options).Items[0].Circularity!.Value;

        Assert.True(round > 0.7, $"동그란 것은 1에 가까워야 한다 — {round:F3}");
        Assert.True(thin < 0.3, $"길쭉한 것은 0에 가까워야 한다 — {thin:F3}");
    }

    [Fact]
    public void 원형도는_켜야_나온다()
    {
        Mat mask = Mask();
        Fill(mask, 10, 10, 4, 4);

        Assert.Null(Run(mask).Items[0].Circularity);
        Assert.NotNull(Run(mask, new LabelOptions { ComputeShapeFeatures = true }).Items[0].Circularity);
    }

    // ── 입력 검사 ───────────────────────────────────────────────────────

    /// <summary>★ 마스크는 Gray8이어야 한다 — Binarize가 그렇게 내주기로 한 약속이다.</summary>
    [Fact]
    public void 마스크가_Gray8이_아니면_거부한다()
    {
        Mat float32 = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(1)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => Run(float32));

        Assert.Contains("Gray8", error.Message);
    }

    [Fact]
    public void 연결성이_4도_8도_아니면_거부한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Run(Mask(), new LabelOptions { Connectivity = 6 }));

    [Fact]
    public void 밝기_이미지_크기가_다르면_거부한다()
    {
        Mat small = Keep(new Mat(16, 16, MatType.CV_32FC1, new Scalar(0)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => Run(Mask(), intensity: small));

        Assert.Contains("크기", error.Message);
    }

    // ── 파이프라인 ─────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 브리핑 흐름 1~7단계 전부. 교수님 <c>case1.golden.diff.json</c>과 같은 모양이고,
    ///   드디어 <b>DefectList</b>가 나온다.
    /// </summary>
    [Fact]
    public void 이웃과_비교해서_결함_목록을_만든다()
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
                  "params": { "method": "Threshold", "value": 0.12 } },
                { "id": "label", "type": "Label",
                  "inputs": { "mask": "mask", "intensity": "roi" },
                  "outputs": { "result": "defectList" },
                  "params": { "connectivity": 8, "minArea": 5, "maxArea": 100000 } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        // 깨끗한 die 세 장이 이웃, 검사 die에는 결함 두 개.
        Mat clean = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.3)));

        Mat roi = Keep(clean.Clone());
        Fill32(roi, 5, 5, 4, 4, 0.9f);       // 밝은 결함
        Fill32(roi, 22, 22, 3, 3, 0.9f);     // 또 하나

        PipelineContext context = new();
        context.SetInput("roi", roi);
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[clean, Keep(clean.Clone()), Keep(clean.Clone())]);

        new PipelineEngine().Run(registry.CreateAll(recipe), context);

        DefectList defects = context.Get<DefectList>(PipelineResult.DefectListKey);

        Assert.Equal(2, defects.Count);
        Assert.Equal([1, 2], defects.Items.Select(d => d.Id));

        // 중앙값 필터가 모서리를 깎으므로 면적은 원래보다 작다.
        Assert.All(defects.Items, d => Assert.True(d.Area >= 5, $"면적 {d.Area}"));

        // 원본을 같이 넘겼으니 밝기가 나온다.
        Assert.All(defects.Items, d => Assert.True(d.MaxIntensity!.Value > 0.8, $"최대 밝기 {d.MaxIntensity}"));

        // ★ 칠판에서 결과 봉투를 꺼낸다 — 파이프라인의 진짜 끝.
        //   봉투는 이름표가 <b>"defectList"</b>인 칸만 집어간다. 레시피가 다른 이름을 쓰면 빈 결과가 나온다.
        //   교수님 레시피도 같은 이름을 쓴다(case1.golden.diff.json).
        PipelineResult result = PipelineResult.From(context);

        Assert.Equal(2, result.Defects.Count);

        context.ReleaseAll();
    }

    [Fact]
    public void 밝기를_안_이어도_레시피가_통과한다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "label.no.intensity",
              "contextInputs": [ "roi" ],
              "pipeline": { "nodes": [
                { "id": "binarize", "type": "Binarize",
                  "inputs": { "src": "roi" },
                  "outputs": { "result": "mask" },
                  "params": { "method": "Threshold", "value": 0.5 } },
                { "id": "label", "type": "Label",
                  "inputs": { "mask": "mask" },
                  "outputs": { "result": "defects" },
                  "params": { "minArea": 1 } }
              ] }
            }
            """);

        // ★ intensity는 Optional이라 안 이어도 검증을 통과해야 한다.
        Assert.Empty(RecipeValidator.Validate(recipe, CvBackend.CreateRegistry()));
    }

    /// <summary>
    /// ★★ 결과 봉투는 이름표가 <b>"defectList"</b>인 칸만 집어간다.
    ///   다른 이름으로 지으면 노드는 잘 돌았는데 <b>결과가 비어서 나온다</b> — 조용히 틀리는 종류다.
    ///   레시피를 쓸 때 반드시 이 이름으로 짓는다(교수님 레시피도 같다).
    /// </summary>
    [Fact]
    public void 봉투는_defectList라는_이름표만_집어간다()
    {
        Mat mask = Mask();
        Fill(mask, 10, 10, 4, 4);

        DefectList defects = Run(mask);

        PipelineContext wrongName = new();
        wrongName.Set("myDefects", defects);
        Assert.Equal(0, PipelineResult.From(wrongName).Defects.Count);

        PipelineContext rightName = new();
        rightName.Set(PipelineResult.DefectListKey, defects);
        Assert.Equal(1, PipelineResult.From(rightName).Defects.Count);
    }

    [Fact]
    public void 명부에_Label이_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Label");

        Assert.Equal("출력", descriptor.Category);
        Assert.Equal(PortKind.DefectList, descriptor.Outputs[0].Kind);
        Assert.True(descriptor.Inputs.Single(p => p.Name == "intensity").Optional);
    }
}