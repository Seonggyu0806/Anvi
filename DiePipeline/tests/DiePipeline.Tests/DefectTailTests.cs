using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>★ 오늘의 주제는 "찾은 다음에 <b>다듬기</b>" — 거르고, 이름 붙이고, 줄인다.</summary>
public sealed class DefectTailTests : IDisposable
{
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

    /// <summary>결함 하나를 손으로 만든다 — 이미지 없이 숫자만 가지고 시험한다.</summary>
    private static Defect Make(
        int id, int x, int y, int width, int height, int? area = null, double? circularity = null)
        => new()
        {
            Id = id,
            Bounds = new BoundingBox(x, y, width, height),
            Area = area ?? (width * height),
            Centroid = new PointF(x + (width / 2.0), y + (height / 2.0)),
            Circularity = circularity,
        };

    private static DefectList List(params Defect[] defects) => new() { Items = defects };

    private static int[] Ids(DefectList list) => list.Items.Select(d => d.Id).ToArray();

    // ── DefectFilter — 모양으로 거르기 ──────────────────────────────────

    /// <summary>
    /// ★ 종횡비 = 긴변 ÷ 짧은변. 언제나 1 이상이다.
    ///   1이면 정사각(둥근 것), 크면 가늘고 길다(긁힘).
    /// </summary>
    [Fact]
    public void 종횡비로_가늘고_긴_것만_남긴다()
    {
        DefectList input = List(
            Make(1, 0, 0, 10, 10),     // 종횡비 1.0 — 둥근 것
            Make(2, 0, 0, 40, 4),      // 종횡비 10.0 — 긁힘
            Make(3, 0, 0, 12, 10));    // 종횡비 1.2

        DefectList kept = new ShapeDefectFilter(minAspect: 3.0, maxAspect: 1000, 0.0, 1.0).Filter(input);

        Assert.Equal([2], Ids(kept));
    }

    /// <summary>★ 채움비 = 면적 ÷ bbox. 대각선 긁힘은 bbox는 큰데 <b>속이 비어 있다.</b></summary>
    [Fact]
    public void 채움비로_속이_빈_것을_가려낸다()
    {
        DefectList input = List(
            Make(1, 0, 0, 20, 20, area: 400),   // 채움비 1.00 — 꽉 찬 네모
            Make(2, 0, 0, 20, 20, area: 40));   // 채움비 0.10 — 대각선 긁힘

        DefectList solid = new ShapeDefectFilter(1.0, 1000, minExtent: 0.5, maxExtent: 1.0).Filter(input);
        DefectList thin = new ShapeDefectFilter(1.0, 1000, minExtent: 0.0, maxExtent: 0.2).Filter(input);

        Assert.Equal([1], Ids(solid));
        Assert.Equal([2], Ids(thin));
    }

    /// <summary>
    /// ★★ 원형도를 <b>안 재 뒀으면 그 조건은 없는 셈</b> 친다.
    ///   "모르는 것"을 불합격으로 처리하면, computeShape를 깜빡 껐을 때
    ///   <b>결함이 전부 조용히 사라진다.</b>
    /// </summary>
    [Fact]
    public void 원형도를_안_쟀으면_그_조건은_건너뛴다()
    {
        DefectList unknown = List(Make(1, 0, 0, 10, 10));                        // Circularity = null
        DefectList known = List(Make(2, 0, 0, 10, 10, circularity: 0.2));        // 길쭉함

        ShapeDefectFilter onlyRound = new(1.0, 1000, 0.0, 1.0, minCircularity: 0.8, maxCircularity: 1.0);

        Assert.Single(onlyRound.Filter(unknown).Items);   // 모르니까 통과
        Assert.Empty(onlyRound.Filter(known).Items);      // 알고 보니 안 둥글어서 탈락
    }

    /// <summary>★ 거른 뒤에도 <b>번호를 다시 안 매긴다</b> — 원래 목록과 대조할 수 있어야 한다.</summary>
    [Fact]
    public void 거른_뒤에도_원래_번호를_지킨다()
    {
        DefectList input = List(
            Make(1, 0, 0, 10, 10),
            Make(2, 0, 0, 40, 4),
            Make(3, 0, 0, 10, 10),
            Make(4, 0, 0, 50, 5));

        DefectList kept = new ShapeDefectFilter(minAspect: 3.0, maxAspect: 1000, 0.0, 1.0).Filter(input);

        Assert.Equal([2, 4], Ids(kept));
    }

    [Fact]
    public void 기본값은_아무것도_안_거른다()
    {
        DefectList input = List(Make(1, 0, 0, 10, 10), Make(2, 0, 0, 40, 4, circularity: 0.1));

        DefectList kept = new ShapeDefectFilter(1.0, 1000.0, 0.0, 1.0).Filter(input);

        Assert.Equal([1, 2], Ids(kept));
    }

    // ── DefectClassify — 이름 붙이기 ────────────────────────────────────

    /// <summary>
    /// ★ 거르는 게 아니라 <b>이름표를 단다.</b> 개수는 그대로다.
    /// <code>
    ///   원형도 &lt; 0.6            → Scratch
    ///   그 외 면적 ≥ 100         → Blob
    ///   그 외                    → Particle
    /// </code>
    /// </summary>
    [Fact]
    public void 모양과_크기로_유형을_붙인다()
    {
        DefectList input = List(
            Make(1, 0, 0, 40, 4, circularity: 0.15),      // 길쭉 → Scratch
            Make(2, 0, 0, 20, 20, circularity: 0.9),      // 둥글고 면적 400 → Blob
            Make(3, 0, 0, 5, 5, circularity: 0.9));       // 둥글고 면적 25 → Particle

        DefectList result = new RuleBasedDefectClassifier(0.6, 100.0).Classify(input);

        Assert.Equal(3, result.Count);
        Assert.Equal(["Scratch", "Blob", "Particle"], result.Items.Select(d => d.ClassLabel));
    }

    /// <summary>
    /// ★★ 원형도를 안 재 뒀으면 <b>둥근 것(1.0)</b>으로 본다.
    ///   "모른다"를 Scratch로 몰면 computeShape를 깜빡했을 때 <b>전부 긁힘</b>이 된다.
    /// </summary>
    [Fact]
    public void 원형도를_안_쟀으면_둥근_것으로_본다()
    {
        DefectList input = List(Make(1, 0, 0, 40, 4));   // 길쭉한데 Circularity = null

        DefectList result = new RuleBasedDefectClassifier(0.6, 100.0).Classify(input);

        Assert.Equal("Blob", result.Items[0].ClassLabel);
    }

    [Fact]
    public void 유형을_붙여도_다른_값은_안_바뀐다()
    {
        Defect original = Make(7, 3, 4, 10, 10, circularity: 0.9);

        Defect labeled = new RuleBasedDefectClassifier(0.6, 100.0).Classify(List(original)).Items[0];

        Assert.Equal(original.Id, labeled.Id);
        Assert.Equal(original.Bounds, labeled.Bounds);
        Assert.Equal(original.Area, labeled.Area);
        Assert.Null(original.ClassLabel);            // 원래 것은 안 건드린다
        Assert.Equal("Blob", labeled.ClassLabel);
    }

    // ── DefectLimit — 줄이기 ────────────────────────────────────────────

    /// <summary>★ 면적 <b>큰 것부터</b> N개. 심각한 것을 먼저 보게 한다.</summary>
    [Fact]
    public void 면적이_큰_것부터_N개만_남긴다()
    {
        DefectList input = List(
            Make(1, 0, 0, 5, 5),        // 25
            Make(2, 0, 0, 20, 20),      // 400
            Make(3, 0, 0, 10, 10),      // 100
            Make(4, 0, 0, 30, 30));     // 900

        DefectList kept = new TopLimitDefectFilter(0, 100000000, maxCount: 2).Filter(input);

        Assert.Equal([4, 2], Ids(kept));
    }

    /// <summary>
    /// ★★ 면적이 같으면 <b>Id 오름차순</b>으로 한 번 더 정렬한다.
    ///   안 그러면 같은 입력인데 결과 순서가 들쭉날쭉해진다.
    /// </summary>
    [Fact]
    public void 면적이_같으면_번호_순으로_정한다()
    {
        DefectList input = List(
            Make(5, 0, 0, 10, 10),
            Make(2, 0, 0, 10, 10),
            Make(9, 0, 0, 10, 10),
            Make(1, 0, 0, 10, 10));

        for (int i = 0; i < 5; i++)
        {
            DefectList kept = new TopLimitDefectFilter(0, 100000000, maxCount: 2).Filter(input);

            Assert.Equal([1, 2], Ids(kept));
        }
    }

    [Fact]
    public void 면적_범위_밖은_버린다()
    {
        DefectList input = List(
            Make(1, 0, 0, 2, 2),         // 4
            Make(2, 0, 0, 10, 10),       // 100
            Make(3, 0, 0, 100, 100));    // 10000

        DefectList kept = new TopLimitDefectFilter(minArea: 10, maxArea: 1000, maxCount: 100).Filter(input);

        Assert.Equal([2], Ids(kept));
    }

    [Fact]
    public void 개수가_한계보다_적으면_다_남는다()
    {
        DefectList input = List(Make(1, 0, 0, 10, 10), Make(2, 0, 0, 20, 20));

        DefectList kept = new TopLimitDefectFilter(0, 100000000, maxCount: 100).Filter(input);

        Assert.Equal(2, kept.Count);
    }

    // ── 파이프라인 ─────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 브리핑 흐름 전부 — 7단계 + 꼬리 3종.
    ///   결함을 찾고 → 모양으로 거르고 → 이름 붙이고 → 심각한 것부터 줄인다.
    /// </summary>
    [Fact]
    public void 찾은_뒤_거르고_이름_붙이고_줄인다()
    {
        Recipe recipe = RecipeLoader.Parse("""
            {
              "recipeId": "die.full.v1",
              "contextInputs": [ "roi", "chipRegions" ],
              "pipeline": { "nodes": [
                { "id": "golden", "type": "Combine",
                  "inputs": { "regions": "chipRegions" },
                  "outputs": { "result": "golden" },
                  "params": { "strategy": "Median" } },
                { "id": "diff", "type": "Difference",
                  "inputs": { "test": "roi", "golden": "golden" },
                  "outputs": { "result": "diff" },
                  "params": { "windowSize": 3, "diffPolicy": "Absolute", "neighborMode": "None" } },
                { "id": "binarize", "type": "Binarize",
                  "inputs": { "src": "diff" },
                  "outputs": { "result": "mask" },
                  "params": { "method": "Threshold", "value": 0.12 } },
                { "id": "label", "type": "Label",
                  "inputs": { "mask": "mask", "intensity": "roi" },
                  "outputs": { "result": "raw" },
                  "params": { "minArea": 1, "computeShape": true } },
                { "id": "shape", "type": "DefectFilter",
                  "inputs": { "defects": "raw" },
                  "outputs": { "result": "shaped" },
                  "params": { "minAspect": 1.0, "maxAspect": 1000.0 } },
                { "id": "classify", "type": "DefectClassify",
                  "inputs": { "defects": "shaped" },
                  "outputs": { "result": "classified" },
                  "params": { "circularityThreshold": 0.6, "areaThreshold": 100.0 } },
                { "id": "limit", "type": "DefectLimit",
                  "inputs": { "defects": "classified" },
                  "outputs": { "result": "defectList" },
                  "params": { "maxCount": 2 } }
              ] }
            }
            """);

        NodeRegistry registry = CvBackend.CreateRegistry();

        Assert.Empty(RecipeValidator.Validate(recipe, registry));

        const int Size = 64;

        Mat clean = Keep(new Mat(Size, Size, MatType.CV_32FC1, new Scalar(0.2)));

        Mat roi = Keep(clean.Clone());

        // 큰 덩어리 · 중간 덩어리 · 작은 점 — 셋을 심는다.
        Cv2.Rectangle(roi, new Rect(5, 5, 14, 14), Scalar.All(0.9), -1);
        Cv2.Rectangle(roi, new Rect(30, 30, 8, 8), Scalar.All(0.9), -1);
        Cv2.Rectangle(roi, new Rect(50, 50, 3, 3), Scalar.All(0.9), -1);

        PipelineContext context = new();
        context.SetInput("roi", roi);
        context.SetInput("chipRegions", (IReadOnlyList<Mat>)[clean, Keep(clean.Clone()), Keep(clean.Clone())]);

        new PipelineEngine().Run(registry.CreateAll(recipe), context);

        DefectList all = context.Get<DefectList>("raw");
        DefectList final = context.Get<DefectList>(PipelineResult.DefectListKey);

        Assert.Equal(3, all.Count);                                  // 셋 다 찾았고
        Assert.Equal(2, final.Count);                                // 큰 것 둘만 남겼다
        Assert.True(final.Items[0].Area > final.Items[1].Area, "큰 것부터 나온다");
        Assert.All(final.Items, d => Assert.NotNull(d.ClassLabel));  // 이름표가 붙었다

        context.ReleaseAll();
    }

    [Fact]
    public void 명부에_꼬리_3종이_올라가_있다()
    {
        NodeRegistry registry = CvBackend.CreateRegistry();

        foreach (string type in new[] { "DefectFilter", "DefectClassify", "DefectLimit" })
        {
            INodeDescriptor descriptor = registry.Describe(type);

            Assert.Equal("출력", descriptor.Category);
            Assert.Equal(PortKind.DefectList, descriptor.Inputs[0].Kind);
            Assert.Equal(PortKind.DefectList, descriptor.Outputs[0].Kind);
        }
    }

    /// <summary>★ 기본값은 교수님 파일 그대로다.</summary>
    [Fact]
    public void 기본값은_교수님_파일_그대로다()
    {
        NodeRegistry registry = CvBackend.CreateRegistry();

        Dictionary<string, object?> shape =
            registry.Describe("DefectFilter").Params.ToDictionary(p => p.Name, p => p.Default);

        Assert.Equal(1.0, shape["minAspect"]);
        Assert.Equal(1000.0, shape["maxAspect"]);
        Assert.Equal(0.0, shape["minExtent"]);
        Assert.Equal(1.0, shape["maxExtent"]);

        Dictionary<string, object?> classify =
            registry.Describe("DefectClassify").Params.ToDictionary(p => p.Name, p => p.Default);

        Assert.Equal(0.6, classify["circularityThreshold"]);
        Assert.Equal(100.0, classify["areaThreshold"]);

        Dictionary<string, object?> limit =
            registry.Describe("DefectLimit").Params.ToDictionary(p => p.Name, p => p.Default);

        Assert.Equal(0, limit["minArea"]);
        Assert.Equal(100000000, limit["maxArea"]);
        Assert.Equal(100, limit["maxCount"]);
    }
}
