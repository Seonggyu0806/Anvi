using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "점 하나가 아니라 <b>근방</b>과 비교한다".</summary>
public sealed class DifferenceTests : IDisposable
{
    private const int Size = 32;

    private const float Background = 0.5f;

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

    private Mat Flat(float value = Background) => Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(value)));

    /// <summary>배경 위에 네모난 얼룩 하나.</summary>
    private Mat WithSpot(float spotValue, int x0 = 10, int y0 = 10, int side = 6)
    {
        Mat mat = Flat();

        for (int y = y0; y < y0 + side; y++)
        {
            for (int x = x0; x < x0 + side; x++) mat.Set(y, x, spotValue);
        }

        return mat;
    }

    /// <summary>세로 줄무늬 — 회로 패턴을 흉내 낸다. 한 칸 밀면 값이 확 달라진다.</summary>
    private Mat Stripes(int offset = 0, int period = 6)
    {
        Mat mat = Keep(new Mat(Size, Size, MatType.CV_32FC1));

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                mat.Set(y, x, ((x + offset) / period) % 2 == 0 ? 0.8f : 0.2f);
            }
        }

        return mat;
    }

    private static Mat Run(Mat test, Mat golden, DifferenceOptions? options = null)
        => Differencers.Difference(test, golden, options ?? new DifferenceOptions());

    private static float At(Mat mat, int y, int x) => mat.At<float>(y, x);

    private static float Spot(Mat mat) => At(mat, 12, 12);

    private static float Away(Mat mat) => At(mat, 28, 28);

    // ── 기본 ───────────────────────────────────────────────────────────

    [Fact]
    public void 같은_두_장은_차이가_없다()
    {
        using Mat diff = Run(Flat(), Flat());

        Assert.Equal(0.0, Cv2.Sum(diff).Val0, tolerance: 1e-6);
    }

    [Fact]
    public void 결과도_작업_포맷이다()
    {
        using Mat diff = Run(WithSpot(0.9f), Flat());

        Assert.Equal(MatType.CV_32FC1, diff.Type());
    }

    [Fact]
    public void 빼는_동안_입력을_건드리지_않는다()
    {
        Mat test = WithSpot(0.9f);
        Mat golden = Flat();

        using Mat testBefore = test.Clone();
        using Mat goldenBefore = golden.Clone();

        using Mat diff = Run(test, golden, new DifferenceOptions { NeighborMode = NeighborMode.MinMax });

        using Mat a = new();
        Cv2.Absdiff(test, testBefore, a);
        using Mat b = new();
        Cv2.Absdiff(golden, goldenBefore, b);

        Assert.Equal(0.0, Cv2.Sum(a).Val0, tolerance: 1e-6);
        Assert.Equal(0.0, Cv2.Sum(b).Val0, tolerance: 1e-6);
    }

    // ── diffPolicy — 어느 쪽 차이를 남길까 ──────────────────────────────

    [Fact]
    public void Absolute는_밝은_결함도_어두운_결함도_잡는다()
    {
        using Mat bright = Run(WithSpot(0.9f), Flat());
        using Mat dark = Run(WithSpot(0.1f), Flat());

        Assert.Equal(0.4f, Spot(bright), tolerance: 1e-5);
        Assert.Equal(0.4f, Spot(dark), tolerance: 1e-5);
        Assert.Equal(0.0f, Away(bright), tolerance: 1e-6);
    }

    [Fact]
    public void BrightOnly는_밝은_결함만_잡는다()
    {
        DifferenceOptions options = new() { DiffPolicy = DiffPolicy.BrightOnly };

        using Mat bright = Run(WithSpot(0.9f), Flat(), options);
        using Mat dark = Run(WithSpot(0.1f), Flat(), options);

        Assert.Equal(0.4f, Spot(bright), tolerance: 1e-5);
        Assert.Equal(0.0f, Spot(dark), tolerance: 1e-6);
    }

    [Fact]
    public void DarkOnly는_어두운_결함만_잡는다()
    {
        DifferenceOptions options = new() { DiffPolicy = DiffPolicy.DarkOnly };

        using Mat bright = Run(WithSpot(0.9f), Flat(), options);
        using Mat dark = Run(WithSpot(0.1f), Flat(), options);

        Assert.Equal(0.0f, Spot(bright), tolerance: 1e-6);
        Assert.Equal(0.4f, Spot(dark), tolerance: 1e-5);
    }

    /// <summary>★ Signed만 부호를 남긴다 — 나머지 셋은 0 이상이다.</summary>
    [Fact]
    public void Signed만_음수를_남긴다()
    {
        using Mat signed = Run(WithSpot(0.1f), Flat(), new DifferenceOptions { DiffPolicy = DiffPolicy.Signed });

        Assert.True(Spot(signed) < 0, $"어두운 결함은 음수여야 한다 — {Spot(signed):F3}");

        foreach (DiffPolicy policy in new[] { DiffPolicy.Absolute, DiffPolicy.BrightOnly, DiffPolicy.DarkOnly })
        {
            using Mat diff = Run(WithSpot(0.1f), Flat(), new DifferenceOptions { DiffPolicy = policy });

            Cv2.MinMaxLoc(diff, out double min, out double _);

            Assert.True(min >= 0.0, $"{policy}: 가장 작은 값이 {min:F4}");
        }
    }

    // ── neighborMode — 골든의 어디와 비교할까 ──────────────────────────

    /// <summary>★ None이면 lo = hi = 골든이라 <b>보통 뺄셈</b>과 똑같다.</summary>
    [Fact]
    public void None은_같은_자리끼리_뺀다()
    {
        using Mat diff = Run(WithSpot(0.9f), Flat(), new DifferenceOptions { NeighborMode = NeighborMode.None });

        Assert.Equal(0.4f, Spot(diff), tolerance: 1e-5);
    }

    /// <summary>
    /// ★★ 오늘의 핵심 — 골든이 <b>한 칸 밀려</b> 있을 때.
    ///   같은 자리끼리 빼면 줄무늬 경계가 전부 차이로 남는다.
    ///   근방 범위(MinMax)와 비교하면 그 밀림이 띠 안으로 흡수된다.
    /// </summary>
    [Fact]
    public void 한_칸_밀린_골든을_MinMax가_흡수한다()
    {
        Mat test = Stripes(0);
        Mat shifted = Stripes(1);

        using Mat naive = Run(test, shifted, new DifferenceOptions { NeighborMode = NeighborMode.None });
        using Mat band = Run(test, shifted, new DifferenceOptions { NeighborMode = NeighborMode.MinMax, WindowSize = 3 });

        double naiveTotal = Cv2.Sum(naive).Val0;
        double bandTotal = Cv2.Sum(band).Val0;

        Assert.True(naiveTotal > 50, $"그냥 빼면 경계가 통째로 남는다 — {naiveTotal:F1}");
        Assert.Equal(0.0, bandTotal, tolerance: 1e-4);
    }

    /// <summary>★ 그래도 <b>진짜 결함</b>은 놓치지 않는다. 띠 밖으로 나가니까.</summary>
    [Fact]
    public void MinMax도_진짜_결함은_잡는다()
    {
        Mat test = Stripes(0);

        for (int y = 14; y < 20; y++)
        {
            for (int x = 14; x < 20; x++) test.Set(y, x, 1.0f);
        }

        using Mat diff = Run(Keep(test), Stripes(0),
            new DifferenceOptions { NeighborMode = NeighborMode.MinMax, WindowSize = 3 });

        Assert.True(At(diff, 16, 16) > 0.15f, $"결함 자리는 남아야 한다 — {At(diff, 16, 16):F3}");
    }

    /// <summary>창을 키우면 더 많이 봐준다 — 흡수력이 세지는 대신 작은 결함을 놓칠 수 있다.</summary>
    [Fact]
    public void 창이_클수록_더_많이_봐준다()
    {
        Mat test = Stripes(0);
        Mat shifted = Stripes(2);

        using Mat narrow = Run(test, shifted, new DifferenceOptions { NeighborMode = NeighborMode.MinMax, WindowSize = 3 });
        using Mat wide = Run(test, shifted, new DifferenceOptions { NeighborMode = NeighborMode.MinMax, WindowSize = 7 });

        Assert.True(Cv2.Sum(narrow).Val0 > Cv2.Sum(wide).Val0,
            $"창이 크면 차이가 줄어야 한다 — 3칸 {Cv2.Sum(narrow).Val0:F1} vs 7칸 {Cv2.Sum(wide).Val0:F1}");
        Assert.Equal(0.0, Cv2.Sum(wide).Val0, tolerance: 1e-4);
    }

    /// <summary>
    /// ★ Min은 근방 <b>최소</b>와, Max는 근방 <b>최대</b>와 비교한다 — 한쪽으로 치우친 띠다.
    ///   골든에 점 하나가 튀어 있을 때 갈린다:
    ///   Min(침식)은 그 점을 <b>지워버려</b> 없던 걸로 보고, Max(팽창)는 <b>퍼뜨려</b> 주변까지 기준을 올린다.
    /// </summary>
    [Fact]
    public void Min과_Max는_한쪽으로만_봐준다()
    {
        Mat test = Flat(0.5f);

        Mat golden = Flat(0.5f);
        golden.Set(16, 16, 0.9f);   // 골든에만 있는 점 하나

        using Mat min = Run(test, golden, new DifferenceOptions { NeighborMode = NeighborMode.Min, WindowSize = 3 });
        using Mat max = Run(test, golden, new DifferenceOptions { NeighborMode = NeighborMode.Max, WindowSize = 3 });

        // Min: 침식이 점을 지워 기준이 0.5 → 차이가 없다.
        Assert.Equal(0.0, Cv2.Sum(min).Val0, tolerance: 1e-5);

        // Max: 팽창이 점을 3×3으로 퍼뜨려 기준이 0.9 → 그 9칸이 0.4만큼 어둡다고 나온다.
        Assert.Equal(0.4f, At(max, 16, 16), tolerance: 1e-5);
        Assert.Equal(9 * 0.4, Cv2.Sum(max).Val0, tolerance: 1e-4);
    }

    // ── 입력 검사 ───────────────────────────────────────────────────────

    [Fact]
    public void 짝수_창은_거부한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Run(Flat(), Flat(), new DifferenceOptions { WindowSize = 4 }));

    [Fact]
    public void 크기가_다르면_거부한다()
    {
        Mat small = Keep(new Mat(16, 16, MatType.CV_32FC1, new Scalar(0.5)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => Run(Flat(), small));

        Assert.Contains("크기", error.Message);
    }

    [Fact]
    public void 작업_포맷이_아니면_거부한다()
    {
        Mat eightBit = Keep(new Mat(Size, Size, MatType.CV_8UC1, new Scalar(128)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => Run(Flat(), eightBit));

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

    /// <summary>★ 이웃으로 만든 골든을 빼면 검사 die에만 있는 얼룩이 혼자 남는다.</summary>
    [Fact]
    public void 이웃으로_만든_골든을_빼면_결함만_남는다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "die.diff.v1",
              "contextInputs": [ "roi", "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } },
                { "id": "diff", "type": "Difference",
                  "inputs": { "test": "roi", "golden": "golden" },
                  "outputs": { "result": "diff" },
                  "params": { "windowSize": 3, "diffPolicy": "Absolute", "neighborMode": "MinMax" } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        PipelineContext context = new();
        context.SetInput("roi", WithSpot(0.9f));
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[Flat(), Flat(), Flat()]);

        float spot = float.NaN;
        float away = float.NaN;

        List<IPipelineNode> nodes =
        [
            .. registry.CreateAll(recipe),
            new PeekNode("diff", d => { spot = Spot(d); away = Away(d); }),
        ];

        new PipelineEngine().Run(nodes, context);

        Assert.True(spot > 0.3f, $"결함은 남아야 한다 — {spot:F3}");
        Assert.Equal(0.0f, away, tolerance: 1e-6);
    }

    [Fact]
    public void 명부에_Difference가_올라가_있다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Difference");

        Assert.Equal("비교", descriptor.Category);
        Assert.Equal(2, descriptor.Inputs.Count);
        Assert.Contains(descriptor.Params, p => p.Name == "neighborMode");
    }

    /// <summary>★ 기본값은 교수님 파일 그대로다.</summary>
    [Fact]
    public void 기본값은_교수님_파일_그대로다()
    {
        INodeDescriptor descriptor = CvBackend.CreateRegistry().Describe("Difference");

        Dictionary<string, object?> defaults = descriptor.Params.ToDictionary(p => p.Name, p => p.Default);

        Assert.Equal(3, defaults["windowSize"]);
        Assert.Equal("Absolute", defaults["diffPolicy"]);
        Assert.Equal("None", defaults["neighborMode"]);
    }
}
