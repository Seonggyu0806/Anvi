using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using DiePipeline.Editor;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 레시피의 블록 다섯. ★ 확인할 것은 "블록이 감싼 계산과 <b>같은 답</b>을 내는가" 와 "잘못 이으면 실행 전에 막는가" 다.
///
/// 블록을 이어 실제로 돌리는 것(실행기)은 3단계다. 여기서는 블록을 하나씩 돌린다.
/// </summary>
public sealed class WaferNodeTests
{
    private static readonly NodeRegistry Registry = WaferBackend.CreateRegistry();

    private static Recipe Parse(string nodes, string inputs = "[]")
        => RecipeLoader.Parse($$"""
            { "recipeId": "t", "contextInputs": {{inputs}}, "pipeline": { "nodes": [ {{nodes}} ] } }
            """);

    /// <summary>블록 하나를 만들어 돌린다 — 엔진 없이, 칠판에 재료를 올려 두고.</summary>
    private static IPipelineContext Run(string node, string inputs, params (string Key, object Value)[] given)
    {
        PipelineContext context = new();

        foreach ((string key, object value) in given)
        {
            context.SetInput(key, value);
        }

        foreach (IPipelineNode built in Registry.CreateAll(Parse(node, inputs)))
        {
            built.Execute(context);
        }

        return context;
    }

    private static Mat F32(Mat gray8)
    {
        Mat f32 = new();
        gray8.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        return f32;
    }

    private static Defect D(int id, int area) => new()
    {
        Id = id,
        Bounds = new BoundingBox(10, 10, 5, 5),
        Area = area,
        Centroid = new PointF(12, 12),
    };

    private static WaferDefectList List(params Defect[] defects)
        => new() { Items = [.. defects.Select(d => new WaferDefect { DieCol = 0, DieRow = 0, Zone = WaferZone.E1, Defect = d })] };

    private static AlignmentResult Unaligned() => new()
    {
        Alignment = WaferAlignment.Identity,
        PairCount = 0,
        ResidualPx = 0,
        ImpliedScale = 1,
    };

    // ─────────────────────────────── 표식 찾기

    /// <summary>★ 합성 웨이퍼에서 표식 조각을 찾는다 — 그린 자리(정답)에.</summary>
    [Fact]
    public void 표식_찾기는_그린_자리에서_표식을_찾는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using Mat image = F32(wafer.Image);
        using Mat raw = wafer.DrawMarkTemplate();
        using Mat template = F32(raw);

        IPipelineContext context = Run("""
            { "id": "match", "type": "TemplateMatchAll",
              "inputs": { "src": "alignSrc", "template": "alignTemplate" }, "outputs": { "matches": "m" } }
            """, """[ "alignSrc", "alignTemplate" ]""",
            (ReservedContext.AlignSrc, image), (ReservedContext.AlignTemplate, template));

        MatchList found = context.Get<MatchList>("m");

        foreach (AlignMark mark in wafer.Marks)
        {
            (int x, int y) = wafer.ExpectedAt(mark);
            Assert.Contains(found.Items, m => Math.Abs(m.Location.X - (wafer.OriginX + x)) < 0.5
                                              && Math.Abs(m.Location.Y - (wafer.OriginY + y)) < 0.5);
        }
    }

    // ─────────────────────────────── 자세 계산

    private const string Solve = """
        { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "m" }, "outputs": { "alignment": "a" },
          "params": { "expected": [[-40, -40], [410, 310]], "firstDieX": FX, "firstDieY": FY } }
        """;

    private static MatchList Marks(params (double X, double Y)[] at)
        => new() { Items = [.. at.Select(p => new Match { Location = new PointF(p.X, p.Y), Score = 0.99 })] };

    /// <summary>★ 표식 자리로 die(0,0) 의 실제 자리를 되찾는다 — 대략 원점이 조금 틀려도 같은 답.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, -5)]
    public void 자세_계산은_die_원점을_되찾는다(int firstDieX, int firstDieY)
    {
        // die(0,0) 이 (54,54) 에 놓였다면 표식은 (54-40, 54-40) · (54+410, 54+310) 에 찍힌다
        MatchList matches = Marks((14, 14), (464, 364));

        IPipelineContext context = Run(
            Solve.Replace("FX", firstDieX.ToString()).Replace("FY", firstDieY.ToString()), "[]", ("m", matches));

        AlignmentResult result = context.Get<AlignmentResult>("a");

        Assert.True(result.Accepted);
        Assert.Equal(54, result.Alignment.Origin.X, tolerance: 1e-6);
        Assert.Equal(54, result.Alignment.Origin.Y, tolerance: 1e-6);
    }

    /// <summary>블록은 명령행 · 화면이 쓰는 함수(<see cref="AlignmentSolver.SolveAround"/>)와 같은 답을 낸다.</summary>
    [Fact]
    public void 자세_계산_블록은_명령행과_같은_함수를_쓴다()
    {
        MatchList matches = Marks((15.5, 13.25), (465.75, 363.5));

        AlignmentResult block = Run(Solve.Replace("FX", "3").Replace("FY", "2"), "[]", ("m", matches))
            .Get<AlignmentResult>("a");
        AlignmentResult direct = AlignmentSolver.SolveAround(
            matches, [new PointF(-40, -40), new PointF(410, 310)], new PointF(3, 2), new AlignmentOptions());

        Assert.Equal(direct, block);
    }

    /// <summary>
    /// ★ 거부돼도 던지지 않고 <b>판정을 담아</b> 낸다 — 멈추는 건 실행기의 일.
    ///   두 번째 표식이 같은 방향으로 10% 먼 자리 → 각도는 그대로인데 표식 사이 거리가 변했다 → 배율 가드(4주차)가 잡는다.
    /// </summary>
    [Fact]
    public void 자세_계산은_못_믿으면_까닭을_담아_낸다()
    {
        // 설계상 두 표식 사이는 (450, 350). 찾은 자리는 (495, 385) — 1.1 배
        MatchList matches = Marks((14, 14), (509, 399));

        AlignmentResult result = Run(Solve.Replace("FX", "0").Replace("FY", "0"), "[]", ("m", matches))
            .Get<AlignmentResult>("a");

        Assert.False(result.Accepted);
        Assert.Contains("거리", result.Rejection);
    }

    // ─────────────────────────────── die 전부 검사 — 경계 블록

    private const string Inspect = """
        { "id": "inspect", "type": "InspectDies", "inputs": INPUTS,
          "outputs": { "defects": "wd", "grid": "g", "alignment": "ao" },
          "params": PARAMS }
        """;

    private static string InspectNode(string parameters, string inputs = "{}")
        => Inspect.Replace("INPUTS", inputs).Replace("PARAMS", parameters);

    /// <summary>★ 엔진이 돌리면 조용히 넘어가지 않고 멈춘다 — 빈 결과가 "결함 0개" 로 읽히면 안 된다.</summary>
    [Fact]
    public void die_전부_검사는_엔진이_돌리면_멈춘다()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run(InspectNode("""{ "e1Recipe": "die.diff.json" }"""), "[]"));

        Assert.Contains("경계 블록", error.Message);
    }

    /// <summary>블록에 적힌 설정을 실행기가 읽는 모양 — 격자 · 원판 · 수동 지정 · 표식 조각 · 찾는 범위.</summary>
    [Fact]
    public void die_전부_검사의_설정을_읽는다()
    {
        NodeConfig config = Parse(InspectNode("""
            { "e1Recipe": "../recipes/die.diff.json", "e0Recipe": "../recipes/die.dark.masked.json",
              "cols": 6, "rows": 5, "pitchX": 100, "pitchY": 90,
              "circle": { "centerX": 300, "centerY": 250, "radius": 240 }, "edgeExclusionPx": 4.5,
              "zones": [ { "col": 2, "row": 4, "zone": "off" }, { "col": 0, "row": 2, "zone": "E0" } ],
              "neighbors": "nearest", "features": [ { "template": "features/demo_mark.bmp" } ], "searchPaddingPx": 20 }
            """)).Pipeline.Nodes[0];

        InspectDiesSettings settings = InspectDiesNodeFactory.Read(config);

        Assert.Equal("../recipes/die.diff.json", settings.E1Recipe);
        Assert.Equal("../recipes/die.dark.masked.json", settings.E0Recipe);
        Assert.Equal((6, 5, 100, 90), (settings.Spec.Cols, settings.Spec.Rows, settings.Spec.PitchX, settings.Spec.PitchY));
        Assert.Equal(new WaferCircle(300, 250, 240), settings.Spec.Circle);
        Assert.Equal(4.5, settings.Spec.EdgeExclusion);
        Assert.Equal(
            [new ZoneOverride(2, 4, WaferZone.Outside), new ZoneOverride(0, 2, WaferZone.E0)],
            settings.Spec.ZoneOverrides);
        Assert.Equal(NeighborStrategy.Nearest, settings.Neighbors);
        Assert.Equal(["features/demo_mark.bmp"], settings.Features);
        Assert.Equal(20, settings.SearchPadding);
    }

    /// <summary>안 적으면 — E0 없음 · 원판 없음 · 같은 행 좌우 · 찾는 범위 없음(사진 전체).</summary>
    [Fact]
    public void die_전부_검사의_기본값()
    {
        InspectDiesSettings settings = InspectDiesNodeFactory.Read(
            Parse(InspectNode("""{ "e1Recipe": "a.json" }""")).Pipeline.Nodes[0]);

        Assert.Null(settings.E0Recipe);
        Assert.Null(settings.Spec.Circle);
        Assert.Empty(settings.Spec.ZoneOverrides);
        Assert.Equal(NeighborStrategy.SameRow1, settings.Neighbors);
        Assert.Null(settings.SearchPadding);
    }

    // ─────────────────────────────── 웨이퍼 좌표 · 결과 추리기

    /// <summary>블록은 감싼 계산(<see cref="AbsoluteTransform.Transform"/>)과 같은 답을 낸다.</summary>
    [Fact]
    public void 웨이퍼_좌표_블록은_변환과_같은_답을_낸다()
    {
        DieGrid grid = DieGridBuilder.Build(new WaferSpec { Cols = 2, Rows = 1, PitchX = 100, PitchY = 100, OriginX = 30, OriginY = 20 });
        WaferDefectList defects = List(D(1, 25));

        WaferDefectList block = Run("""
            { "id": "t", "type": "TransformAbs", "inputs": { "defects": "wd", "grid": "g", "alignment": "a" },
              "outputs": { "result": "abs" } }
            """, "[]", ("wd", defects), ("g", grid), ("a", Unaligned())).Get<WaferDefectList>("abs");

        WaferDefect expected = Assert.Single(AbsoluteTransform.Transform(defects, grid, WaferAlignment.Identity).Items);
        WaferDefect actual = Assert.Single(block.Items);

        Assert.Equal((expected.AbsX, expected.AbsY), (actual.AbsX, actual.AbsY));
    }

    /// <summary>기본값은 교수님 값(큰 순 · 100개) — 여기서는 1개만 남겨 큰 것이 남는지 본다.</summary>
    [Fact]
    public void 결과_추리기_블록은_큰_순으로_남긴다()
    {
        WaferDefectList result = Run("""
            { "id": "s", "type": "Sampling", "inputs": { "defects": "wd" }, "outputs": { "result": "r" },
              "params": { "mode": "BigSize", "count": 1 } }
            """, "[]", ("wd", List(D(1, 30), D(2, 900), D(3, 60)))).Get<WaferDefectList>("r");

        Assert.Equal(900, Assert.Single(result.Items).Defect.Area);
    }

    // ─────────────────────────────── 레시피 검증

    /// <summary>★★ 합성 견본과 같은 모양의 블록 다섯 — 검증을 통과한다.</summary>
    private const string Demo = """
        { "id": "match", "type": "TemplateMatchAll",
          "inputs": { "src": "alignSrc", "template": "alignTemplate" }, "outputs": { "matches": "matches" },
          "params": { "maxMatches": 4 } },
        { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "matches" }, "outputs": { "alignment": "alignment" },
          "params": { "expected": [[-40, -40], [410, 310]] } },
        { "id": "inspect", "type": "InspectDies", "inputs": { "alignment": "alignment" },
          "outputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" },
          "params": { "e1Recipe": "../recipes/die.diff.json", "cols": 4, "rows": 3, "pitchX": 100, "pitchY": 100,
                      "features": [ { "template": "features/demo_mark.bmp" } ] } },
        { "id": "transform", "type": "TransformAbs",
          "inputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" }, "outputs": { "result": "absDefects" } },
        { "id": "sample", "type": "Sampling", "inputs": { "defects": "absDefects" }, "outputs": { "result": "finalDefects" },
          "params": { "mode": "All" } }
        """;

    [Fact]
    public void 블록_다섯을_이은_웨이퍼_레시피는_검증을_통과한다()
    {
        Assert.Empty(RecipeValidator.Validate(Parse(Demo, """[ "alignSrc", "alignTemplate" ]"""), Registry));
    }

    /// <summary>★ '찾은 표식 자리' 를 '놓인 자세' 칸에 바로 꽂으면 실행 전에 거부 — 자세 계산을 건너뛴 배선.</summary>
    [Fact]
    public void 자세_계산을_건너뛰고_이으면_거부한다()
    {
        string skipped = Demo.Replace("""{ "alignment": "alignment" },""", """{ "alignment": "matches" },""");

        IReadOnlyList<string> errors = RecipeValidator.Validate(Parse(skipped, """[ "alignSrc", "alignTemplate" ]"""), Registry);

        Assert.Contains(errors, e => e.Contains("Alignment") && e.Contains("MatchList"));
    }

    [Theory]
    [InlineData("""{ "expected": [[-40, -40]] }""", "2개 이상")]
    [InlineData("""{ "expected": [[-40, -40], [410]] }""", "expected[1]")]
    public void 자세_계산의_표식_자리가_틀리면_거부한다(string parameters, string message)
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(Parse($$"""
            { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "x" }, "outputs": { "alignment": "a" },
              "params": {{parameters}} }
            """, "[ \"x\" ]"), Registry);

        Assert.Contains(errors, e => e.Contains(message));
    }

    [Theory]
    [InlineData("""{ "e1Recipe": "" }""", "e1Recipe")]
    [InlineData("""{ "e1Recipe": "a.json", "circle": { "centerX": 1, "centerY": 1, "radius": -5 } }""", "radius")]
    [InlineData("""{ "e1Recipe": "a.json", "circle": { "centerX": 1 } }""", "circle")]
    [InlineData("""{ "e1Recipe": "a.json", "zones": [ { "col": 1, "row": 1, "zone": "skip" } ] }""", "zones[0]")]
    [InlineData("""{ "e1Recipe": "a.json", "zones": [ { "col": "1", "row": 1, "zone": "off" } ] }""", "zones[0]")]
    [InlineData("""{ "e1Recipe": "a.json", "features": [ { "template": "" } ] }""", "features[0]")]
    public void die_전부_검사의_묶음_설정이_틀리면_거부한다(string parameters, string message)
    {
        IReadOnlyList<string> errors = RecipeValidator.Validate(Parse(InspectNode(parameters)), Registry);

        Assert.Contains(errors, e => e.Contains(message));
    }

    /// <summary>★ 정렬을 이었는데 찾을 조각이 없으면 — 실행기가 아무것도 못 찾는다. 실행 전에 막는다.</summary>
    [Fact]
    public void 정렬을_이었는데_표식_조각이_없으면_거부한다()
    {
        string noFeatures = Demo.Replace("""[ { "template": "features/demo_mark.bmp" } ]""", "[]");

        IReadOnlyList<string> errors = RecipeValidator.Validate(Parse(noFeatures, """[ "alignSrc", "alignTemplate" ]"""), Registry);

        Assert.Contains(errors, e => e.Contains("features") && e.Contains("표식 조각"));
    }

    /// <summary>★ 정렬하면 대략 원점은 자세 계산 블록 한 곳 — InspectDies 에도 원점을 적으면 두 곳이 된다.</summary>
    [Fact]
    public void 정렬하면서_원점을_두_곳에_적으면_거부한다()
    {
        string twice = Demo.Replace("\"cols\": 4,", "\"cols\": 4, \"originX\": 54,");

        IReadOnlyList<string> errors = RecipeValidator.Validate(Parse(twice, """[ "alignSrc", "alignTemplate" ]"""), Registry);

        Assert.Contains(errors, e => e.Contains("firstDieX"));
    }

    // ─────────────────────────────── 명부 · die 탭 팔레트

    /// <summary>die 명부(Cv)에는 웨이퍼 블록 넷이 없다. 표식 찾기는 교수님처럼 공통 명부에 있다.</summary>
    [Fact]
    public void die_명부에는_웨이퍼_블록이_없다()
    {
        NodeRegistry die = CvBackend.CreateRegistry();

        Assert.True(die.Contains("TemplateMatchAll"));
        Assert.All(["SolveAlignment", "InspectDies", "TransformAbs", "Sampling"], t => Assert.False(die.Contains(t)));
        Assert.All(["TemplateMatchAll", "SolveAlignment", "InspectDies", "TransformAbs", "Sampling", "Binarize"],
            t => Assert.True(Registry.Contains(t)));
    }

    /// <summary>★ die 탭 팔레트는 웨이퍼 종류를 주고받는 블록을 숨긴다 — 이름이 아니라 포트 종류로(교수님 8.5a).</summary>
    [Fact]
    public void die_탭_팔레트는_웨이퍼_블록을_숨긴다()
    {
        IReadOnlyList<PaletteCategory> palette = PaletteModel.Filtered(
            CvBackend.CreateRegistry(), d => !PaletteModel.TouchesPortKind(d, PaletteModel.WaferKinds));

        string[] shown = [.. palette.SelectMany(c => c.Nodes).Select(d => d.TypeName)];

        Assert.DoesNotContain("TemplateMatchAll", shown);
        Assert.Contains("Binarize", shown);
        Assert.Contains("MaskApply", shown);
    }
}