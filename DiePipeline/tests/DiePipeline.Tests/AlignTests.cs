using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "재는 것과 믿는 것은 다르다".</summary>
public sealed class AlignTests : IDisposable
{
    private const int Size = 128;

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

    /// <summary>특징이 여기저기 흩어진 die 하나. (ox, oy)만큼 밀어 그린다.</summary>
    private Mat Die(int ox = 0, int oy = 0)
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.2)));

        (int X, int Y, int W, int H, float V)[] marks =
        [
            (20, 30, 18, 12, 0.9f), (60, 70, 10, 26, 0.7f),
            (95, 25, 14, 14, 0.5f), (40, 100, 30, 8, 0.8f),
        ];

        foreach ((int x0, int y0, int w, int h, float v) in marks)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int yy = y0 + y + oy;
                    int xx = x0 + x + ox;

                    if (yy >= 0 && yy < Size && xx >= 0 && xx < Size) mat.Set(yy, xx, v);
                }
            }
        }

        return mat;
    }

    /// <summary>바둑판 무늬 — 회로처럼 같은 모양이 계속 반복되는 die를 흉내 낸다.</summary>
    private Mat Periodic(int ox = 0, int oy = 0, int period = 8)
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                mat.Set(y, x, (((x + ox) / period) + ((y + oy) / period)) % 2 == 0 ? 0.8f : 0.2f);
            }
        }

        return mat;
    }

    private static double TotalDifference(Mat a, Mat b)
    {
        using Mat diff = new();
        Cv2.Absdiff(a, b, diff);
        return Cv2.Sum(diff).Val0;
    }

    private static float At(Mat mat, int y, int x) => mat.At<float>(y, x);

    // ── 잰다 ───────────────────────────────────────────────────────────

    [Fact]
    public void 몇_픽셀_밀렸는지_잰다()
    {
        ShiftEstimate estimate = Aligners.Measure(Die(), Die(ox: 3, oy: 2));

        Assert.True(estimate.Accepted, estimate.Reason);
        Assert.Equal(3.0, estimate.X, tolerance: 0.05);
        Assert.Equal(2.0, estimate.Y, tolerance: 0.05);
    }

    /// <summary>
    /// ★★ 함정 — Cv2.PhaseCorrelate에 '창'을 넘기면 두 입력을 그 자리에서 망가뜨린다.
    ///   골든은 칠판이 들고 있는 물건이라, 여기서 변하면 다음 단계가 <b>먹힌 골든</b>을 쓴다.
    ///   그래서 창을 안 쓴다. 이 테스트가 그 약속을 지킨다.
    /// </summary>
    [Fact]
    public void 재는_동안_입력을_건드리지_않는다()
    {
        Mat src = Die();
        Mat reference = Die(ox: 3, oy: 2);

        using Mat srcBefore = src.Clone();
        using Mat referenceBefore = reference.Clone();

        Aligners.Measure(src, reference);

        Assert.Equal(0.0, TotalDifference(src, srcBefore), tolerance: 1e-6);
        Assert.Equal(0.0, TotalDifference(reference, referenceBefore), tolerance: 1e-6);
    }

    /// <summary>같은 짝을 여러 번 재면 같은 답이 나와야 한다 — 입력이 안 변하니까.</summary>
    [Fact]
    public void 여러_번_재도_같은_답이_나온다()
    {
        Mat src = Die();
        Mat reference = Die(ox: 3, oy: 2);

        double first = Aligners.Measure(src, reference).X;

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(first, Aligners.Measure(src, reference).X, tolerance: 1e-9);
        }
    }

    // ── 되민다 ─────────────────────────────────────────────────────────

    [Fact]
    public void 되밀면_두_장이_겹친다()
    {
        Mat src = Die();
        Mat reference = Die(ox: 3, oy: 2);

        double before = TotalDifference(src, reference);

        using Mat aligned = Aligners.Align(src, reference, AlignMethod.Translation);

        double after = TotalDifference(aligned, reference);

        Assert.True(before > 100, $"원래는 많이 어긋나 있어야 한다 — {before:F1}");
        Assert.True(after < before * 0.05, $"되밀면 거의 사라져야 한다 — {before:F1} → {after:F1}");
    }

    /// <summary>
    /// ★ 옮기면 한쪽에 빈자리가 생긴다. 0(검정)으로 채우면 그 띠가 통째로 결함이 된다.
    ///   가장자리 픽셀을 늘려 채워야 검사 이미지와 값이 비슷해 차이가 안 생긴다.
    /// </summary>
    [Fact]
    public void 빈자리를_검은띠로_채우지_않는다()
    {
        using Mat aligned = Aligners.Align(Die(), Die(ox: 10), AlignMethod.Translation);

        Assert.Equal(0.2f, At(aligned, 5, 0), tolerance: 1e-3);
        Assert.Equal(0.2f, At(aligned, 5, 1), tolerance: 1e-3);
    }

    [Fact]
    public void None은_움직이지_않는다()
    {
        Mat src = Die();

        using Mat aligned = Aligners.Align(src, Die(ox: 3, oy: 2), AlignMethod.None);

        Assert.Equal(0.0, TotalDifference(aligned, src), tolerance: 1e-6);
    }

    [Fact]
    public void None도_새_이미지를_만든다()
    {
        Mat src = Die();

        using Mat aligned = Aligners.Align(src, Die(), AlignMethod.None);

        Assert.False(ReferenceEquals(src, aligned));
    }

    /// <summary>die를 통째로 <b>부드럽게</b> 민다 — 서브픽셀도 된다.</summary>
    private Mat Shifted(Mat source, double dx, double dy)
    {
        using Mat matrix = Mat.FromPixelData(2, 3, MatType.CV_64FC1, new double[] { 1, 0, dx, 0, 1, dy });

        Mat moved = Keep(new Mat());
        Cv2.WarpAffine(source, moved, matrix, source.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);

        return moved;
    }

    /// <summary>
    /// ★★ <b>정상 die를 복제해 일부러 밀고</b>, 정합이 그만큼 되찾는지 본다.
    ///   합성 결함이 아니라 "같은 그림이 옮겨진 것"이라 <b>정답을 정확히 안다.</b>
    ///
    ///   실데이터에서도 같은 방법으로 쟀다 — 1px 밀림을 1.000으로 되찾고
    ///   차이를 100% 없앴다(docs/2-real-data.md §7).
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 2.0)]
    [InlineData(3.0, -2.0)]
    [InlineData(7.0, 4.0)]
    public void 복제해_민_die를_되찾는다(double dx, double dy)
    {
        Mat original = Die();
        Mat moved = Shifted(original, dx, dy);

        ShiftEstimate estimate = Aligners.Measure(original, moved);

        Assert.True(estimate.Accepted, estimate.Reason);
        Assert.Equal(dx, estimate.X, tolerance: 0.15);
        Assert.Equal(dy, estimate.Y, tolerance: 0.15);

        double before = TotalDifference(original, moved);

        using Mat aligned = Aligners.Align(original, moved, AlignMethod.Translation);

        double after = TotalDifference(aligned, moved);

        Assert.True(after < before * 0.2, $"차이가 크게 줄어야 한다 — {before:F1} → {after:F1}");
    }

    /// <summary>안 민 복제본은 <b>0으로 잰다.</b> 없는 어긋남을 만들어내면 안 된다.</summary>
    [Fact]
    public void 안_민_복제본은_0으로_잰다()
    {
        Mat original = Die();

        ShiftEstimate estimate = Aligners.Measure(original, original.Clone());

        Assert.True(estimate.Accepted);
        Assert.Equal(0.0, estimate.X, tolerance: 1e-3);
        Assert.Equal(0.0, estimate.Y, tolerance: 1e-3);
    }

    // ── 가드 ───────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 오늘의 핵심. 바둑판 무늬는 어디에 맞춰도 똑같이 맞는다.
    ///   위상상관은 (3,2)가 아니라 (61,62)라는 <b>엉뚱한 답</b>을 자신 있게 내놓는다.
    ///   다행히 확신도(response)가 폭삭 떨어지므로, 그걸 보고 거른다.
    /// </summary>
    [Fact]
    public void 주기_패턴에서는_엉뚱한_답을_내고_가드가_막는다()
    {
        ShiftEstimate estimate = Aligners.Measure(Periodic(), Periodic(ox: 3, oy: 2));

        Assert.False(estimate.Accepted, $"거부되어야 한다 — 답 ({estimate.X:F1},{estimate.Y:F1})");
        Assert.True(estimate.Response < 0.3, $"확신도가 낮아야 한다 — {estimate.Response:F4}");
        Assert.Contains("응답", estimate.Reason);
    }

    /// <summary>거부하면 <b>안 움직인 복사본</b>이 나온다. 예외를 던지지 않는다.</summary>
    [Fact]
    public void 거부하면_예외_대신_원본을_그대로_낸다()
    {
        Mat src = Periodic();

        using Mat aligned = Aligners.Align(src, Periodic(ox: 3, oy: 2), AlignMethod.Translation);

        Assert.Equal(0.0, TotalDifference(aligned, src), tolerance: 1e-6);
    }

    /// <summary>서로 아무 상관 없는 두 장 — 확신도가 바닥이다.</summary>
    [Fact]
    public void 무관한_두_장은_거부한다()
    {
        Mat flat = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.5)));

        ShiftEstimate estimate = Aligners.Measure(flat, Die());

        Assert.False(estimate.Accepted);
    }

    /// <summary>★ 확신은 높은데 너무 멀다 — 이웃 die가 40px씩 밀릴 리는 없다.</summary>
    [Fact]
    public void 너무_먼_이동은_확신이_높아도_거부한다()
    {
        ShiftEstimate loose = Aligners.Measure(Die(), Die(ox: 40), maxShiftPx: 60);
        ShiftEstimate tight = Aligners.Measure(Die(), Die(ox: 40), maxShiftPx: 20);

        Assert.True(loose.Accepted, "한계를 늘리면 받아들인다");
        Assert.True(loose.Response > 0.3, $"확신도 자체는 높다 — {loose.Response:F3}");

        Assert.False(tight.Accepted, "한계가 20이면 거부한다");
        Assert.Contains("한계", tight.Reason);
    }

    /// <summary>결함이 하나 박혀 있어도 정합은 되어야 한다. 결함은 die 전체에 비하면 작다.</summary>
    [Fact]
    public void 결함이_있어도_정합된다()
    {
        Mat reference = Die(ox: 3, oy: 2);

        for (int y = 50; y < 60; y++)
        {
            for (int x = 50; x < 60; x++) reference.Set(y, x, 1.0f);
        }

        ShiftEstimate estimate = Aligners.Measure(Die(), reference);

        Assert.True(estimate.Accepted, estimate.Reason);
        Assert.Equal(3.0, estimate.X, tolerance: 0.1);
    }

    // ── 입력 검사 ───────────────────────────────────────────────────────

    [Fact]
    public void 크기가_다르면_거부한다()
    {
        Mat small = Keep(new Mat(64, 64, MatType.CV_32FC1, new Scalar(0.5)));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Aligners.Measure(Die(), small));

        Assert.Contains("크기", error.Message);
    }

    [Fact]
    public void 작업_포맷이_아니면_거부한다()
    {
        Mat eightBit = Keep(new Mat(Size, Size, MatType.CV_8UC1, new Scalar(128)));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Aligners.Measure(eightBit, eightBit));

        Assert.Contains("GrayF32", error.Message);
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

    /// <summary>★ 브리핑 흐름의 1~3단계가 이어진다 — 골든을 만들고, 다듬고, 되민다.</summary>
    [Fact]
    public void 골든을_만들고_다듬고_되민다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "golden.align.v1",
              "contextInputs": [ "roi", "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } },
                { "id": "norm", "type": "Normalize",
                  "inputs": { "src": "golden" },
                  "outputs": { "result": "goldenNorm" },
                  "params": { "method": "None" } },
                { "id": "align", "type": "Align",
                  "inputs": { "src": "goldenNorm", "reference": "roi" },
                  "outputs": { "result": "goldenAligned" },
                  "params": { "method": "Translation", "minResponse": 0.3, "maxShiftPx": 20 } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        // 검사할 die는 (3,2)만큼 밀려 있고, 이웃 세 장은 안 밀려 있다.
        Mat roi = Die(ox: 3, oy: 2);

        PipelineContext context = new();
        context.SetInput("roi", roi);
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[Die(), Die(), Die()]);

        double leftover = double.NaN;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("goldenAligned", g => leftover = TotalDifference(g, roi)),
        ];

        new PipelineEngine().Run(nodes, context);

        // 골든이 die 위로 되밀렸으니 남는 차이가 거의 없어야 한다.
        Assert.True(leftover < 20, $"정합 후 남은 차이 {leftover:F1}");
    }

    [Fact]
    public void 명부에_Align이_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Align");

        Assert.Equal("정합", descriptor.Category);
        Assert.Equal(2, descriptor.Inputs.Count);
        Assert.Contains(descriptor.Params, p => p.Name == "minResponse");
    }
}
