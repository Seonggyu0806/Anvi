using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 레시피 실행기. ★★ 핵심은 <b>같은 웨이퍼를 두 입구로 돌려 같은 답</b>이 나오는가다 —
/// 레시피 파일(블록 다섯)과 명령행 · 화면의 옵션(<see cref="WaferInspection"/>).
/// 같으면 가드 · 격자 · die 반복이 한 곳이라는 것이 증명된다.
/// </summary>
public sealed class WaferRecipeRunnerTests
{
    private static readonly NodeRegistry Registry = WaferBackend.CreateRegistry();

    /// <summary><c>samples/recipes/die.diff.json</c> 과 같은 내용 — <see cref="WaferInspectionTests"/> 와 같은 것.</summary>
    private const string DieDiff = """
        { "recipeId": "t", "contextInputs": [ "roi", "chipRegions" ], "pipeline": { "nodes": [
          { "id": "golden", "type": "Combine", "inputs": { "regions": "chipRegions" },
            "outputs": { "result": "golden" }, "params": { "strategy": "Mean" } },
          { "id": "diff", "type": "Difference", "inputs": { "test": "roi", "golden": "golden" },
            "outputs": { "result": "diff" },
            "params": { "windowSize": 3, "diffPolicy": "DarkOnly", "neighborMode": "MinMax" } },
          { "id": "binarize", "type": "Binarize", "inputs": { "src": "diff" },
            "outputs": { "result": "mask" }, "params": { "method": "Threshold", "value": 0.2 } },
          { "id": "label", "type": "Label", "inputs": { "mask": "mask", "intensity": "roi" },
            "outputs": { "result": "defectList" }, "params": { "connectivity": 8, "minArea": 500 } }
        ] } }
        """;

    private static Recipe E1() => RecipeLoader.Parse(DieDiff);

    private static MatImageSource Source(SyntheticWafer wafer)
    {
        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        return new MatImageSource(f32);
    }

    private static Mat Template(SyntheticWafer wafer)
    {
        using Mat raw = wafer.DrawMarkTemplate();
        Mat f32 = new();
        raw.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        return f32;
    }

    /// <summary>레시피에 적힌 경로로만 부른다 — 다른 이름이 오면 실행기가 엉뚱한 칸을 읽은 것이다.</summary>
    private static Recipe LoadRecipe(string path)
        => path == "e1.json" ? E1() : throw new FileNotFoundException(path);

    private static Func<string, Mat> LoadTemplate(SyntheticWafer wafer)
        => path => path == "mark.bmp" ? Template(wafer) : throw new FileNotFoundException(path);

    private static readonly Func<string, Recipe> NoRecipe = path => throw new InvalidOperationException($"불리면 안 된다: {path}");

    private static readonly Func<string, Mat> NoTemplate = path => throw new InvalidOperationException($"불리면 안 된다: {path}");

    private static string Expected(SyntheticWafer wafer)
        => "[" + string.Join(", ", wafer.Marks.Select(wafer.ExpectedAt).Select(p => $"[{p.X}, {p.Y}]")) + "]";

    private static string GridParams(SyntheticWafer wafer)
        => $$"""
            "e1Recipe": "e1.json", "cols": {{wafer.Spec.Cols}}, "rows": {{wafer.Spec.Rows}},
            "pitchX": {{wafer.Spec.Pitch}}, "pitchY": {{wafer.Spec.Pitch}}
            """;

    /// <summary>뒤 토막 — 웨이퍼 좌표 → 추리기(전부).</summary>
    private const string Tail = """
        ,
          { "id": "transform", "type": "TransformAbs",
            "inputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" }, "outputs": { "result": "absDefects" } },
          { "id": "sample", "type": "Sampling", "inputs": { "defects": "absDefects" }, "outputs": { "result": "finalDefects" },
            "params": { "mode": "All" } }
        """;

    /// <summary>
    /// 합성 견본과 같은 모양의 블록 다섯. ★ <c>maxMatches 4</c> — 명령행 입구가 표식 2개일 때 쓰는 값(max(4, 2×2))과 맞춘다.
    /// </summary>
    private static Recipe Aligned(
        SyntheticWafer wafer, int firstDieX = 0, int firstDieY = 0, int padding = 0, string? expected = null,
        string features = """[ { "template": "mark.bmp" } ]""", string tail = Tail)
        => RecipeLoader.Parse($$"""
            { "recipeId": "w", "contextInputs": [ "alignSrc", "alignTemplate" ], "pipeline": { "nodes": [
              { "id": "match", "type": "TemplateMatchAll", "inputs": { "src": "alignSrc", "template": "alignTemplate" },
                "outputs": { "matches": "matches" }, "params": { "maxMatches": 4 } },
              { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "matches" }, "outputs": { "alignment": "alignment" },
                "params": { "expected": {{expected ?? Expected(wafer)}}, "firstDieX": {{firstDieX}}, "firstDieY": {{firstDieY}} } },
              { "id": "inspect", "type": "InspectDies", "inputs": { "alignment": "alignment" },
                "outputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" },
                "params": { {{GridParams(wafer)}}, "searchPaddingPx": {{padding}}, "features": {{features}} } }{{tail}}
            ] } }
            """);

    /// <summary>정렬 없이 — InspectDies 에 원점을 적는다.</summary>
    private static Recipe Unaligned(SyntheticWafer wafer, int originX, int originY, string tail = Tail)
        => RecipeLoader.Parse($$"""
            { "recipeId": "w", "contextInputs": [], "pipeline": { "nodes": [
              { "id": "inspect", "type": "InspectDies",
                "outputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" },
                "params": { {{GridParams(wafer)}}, "originX": {{originX}}, "originY": {{originY}} } }{{tail}}
            ] } }
            """);

    private static MarkAlignment Marks(SyntheticWafer wafer, Mat template) => new()
    {
        Template = template,
        Expected = [.. wafer.Marks.Select(wafer.ExpectedAt).Select(p => new PointF(p.X, p.Y))],
    };

    private static WaferSpec Grid(SyntheticWafer wafer, int originX = 0, int originY = 0) => new()
    {
        Cols = wafer.Spec.Cols,
        Rows = wafer.Spec.Rows,
        PitchX = wafer.Spec.Pitch,
        PitchY = wafer.Spec.Pitch,
        OriginX = originX,
        OriginY = originY,
    };

    private static (int Col, int Row)[] Truth(SyntheticWafer wafer)
        => [.. wafer.Defects.Select(d => (d.Col, d.Row)).Order()];

    private static (int, int, double?, double?, int)[] Rows(WaferDefectList list)
        => [.. list.Items.Select(d => (d.DieCol, d.DieRow, d.AbsX, d.AbsY, d.Defect.Area))];

    /// <summary>두 입구의 결과가 같은가 — 정렬 · die 수 · 결함(자리 · 웨이퍼 좌표 · 면적)까지.</summary>
    private static void Same(WaferInspectionResult options, WaferInspectionResult recipe)
    {
        Assert.False(options.Stopped);
        Assert.False(recipe.Stopped);
        Assert.Equal(options.Alignment, recipe.Alignment);
        Assert.Equal(options.Grid!.Dies.Count, recipe.Grid!.Dies.Count);
        Assert.Equal(Rows(options.Found!), Rows(recipe.Found!));
        Assert.Equal(Rows(options.Defects!), Rows(recipe.Defects!));
    }

    // ─────────────────────────────── 두 입구 — 같은 답

    /// <summary>★★ 레시피 파일로 돌려도 옵션으로 돌린 것과 같다 — 정렬해서 심은 결함을 전부 찾는다.</summary>
    [Fact]
    public void 레시피로_돌려도_옵션으로_돌린_것과_같다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { ShiftX = 3, ShiftY = 2 });
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult options = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer), Alignment = Marks(wafer, template) }, source, Registry);
        WaferInspectionResult recipe = WaferRecipeRunner.Run(Aligned(wafer), source, Registry, LoadRecipe, LoadTemplate(wafer));

        Same(options, recipe);
        Assert.Equal(wafer.OriginX, recipe.Alignment.Origin.X, tolerance: 0.5);

        (int Col, int Row)[] found = [.. recipe.Found!.Items.Select(d => (d.DieCol, d.DieRow)).Order()];
        Assert.Equal(Truth(wafer), found);
    }

    /// <summary>읽은 영역을 적어 두는 소스 — "사진 전체를 읽었나" 를 본다.</summary>
    private sealed class RecordingSource(IImageSource inner) : IImageSource
    {
        public List<Roi> Reads { get; } = [];

        public int Width => inner.Width;

        public int Height => inner.Height;

        public Mat ReadRegion(Roi region)
        {
            Reads.Add(region);
            return inner.ReadRegion(region);
        }

        public Mat ReadOverview(int maxLongSide) => inner.ReadOverview(maxLongSide);

        public void Dispose() => inner.Dispose();
    }

    /// <summary>
    /// ★ 창으로 찾아도 같다 — 대략 원점은 자세 계산 블록(firstDieX/Y) 한 곳, 창의 여유는 InspectDies 에.
    ///   사진 전체는 한 번도 안 읽는다.
    /// </summary>
    [Fact]
    public void 창으로_찾아도_옵션과_같고_사진_전체를_안_읽는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { ShiftX = 3, ShiftY = 2 });
        using Mat template = Template(wafer);
        int x = (int)Math.Round(wafer.OriginX) + 4;
        int y = (int)Math.Round(wafer.OriginY) - 3;

        using RecordingSource byOptions = new(Source(wafer));
        using RecordingSource byRecipe = new(Source(wafer));

        WaferInspectionResult options = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer, x, y),
                Alignment = Marks(wafer, template) with { SearchPadding = 10 },
            },
            byOptions,
            Registry);
        WaferInspectionResult recipe = WaferRecipeRunner.Run(
            Aligned(wafer, firstDieX: x, firstDieY: y, padding: 10), byRecipe, Registry, LoadRecipe, LoadTemplate(wafer));

        Same(options, recipe);
        Assert.Equal(byOptions.Reads, byRecipe.Reads);
        Assert.DoesNotContain(new Roi(0, 0, wafer.Image.Cols, wafer.Image.Rows), byRecipe.Reads);
    }

    /// <summary>정렬을 안 이으면 InspectDies 의 원점으로 돈다 — 뒤 토막의 웨이퍼 좌표 블록은 '반듯하게 놓임' 을 받는다.</summary>
    [Fact]
    public void 정렬을_안_이으면_원점으로_돈다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        int x = (int)Math.Round(wafer.OriginX);
        int y = (int)Math.Round(wafer.OriginY);

        WaferInspectionResult options = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer, x, y) }, source, Registry);
        WaferInspectionResult recipe = WaferRecipeRunner.Run(Unaligned(wafer, x, y), source, Registry, LoadRecipe, NoTemplate);

        Same(options, recipe);
        Assert.Null(recipe.Solved);
    }

    /// <summary>뒤 토막이 없으면 좌표 변환만 한다(교수님 하위호환과 같다) — 추리지 않는다.</summary>
    [Fact]
    public void 뒤_토막이_없으면_좌표_변환만_한다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult options = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer), Alignment = Marks(wafer, template) }, source, Registry);
        WaferInspectionResult recipe = WaferRecipeRunner.Run(
            Aligned(wafer, tail: ""), source, Registry, LoadRecipe, LoadTemplate(wafer));

        Same(options, recipe);
        Assert.Equal(Rows(recipe.Found!), Rows(recipe.Defects!));
    }

    /// <summary>뒤 토막의 추리기가 보여 줄 것을 추린다. 판정용(추리기 전)은 그대로 남는다.</summary>
    [Fact]
    public void 뒤_토막의_추리기가_보여_줄_것만_남긴다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferRecipeRunner.Run(
            Aligned(wafer, tail: Tail.Replace("\"mode\": \"All\"", "\"mode\": \"BigSize\", \"count\": 1")),
            source, Registry, LoadRecipe, LoadTemplate(wafer));

        Assert.Equal(Truth(wafer).Length, result.Found!.Items.Count);
        Assert.True(result.Found.Items.Count > 1);

        WaferDefect kept = Assert.Single(result.Defects!.Items);
        Assert.Equal(result.Found.Items.Max(d => d.Defect.Area), kept.Defect.Area);
    }

    /// <summary>★★ 정렬을 못 믿으면 검사를 시작하지 않는다 — 가드가 레시피 입구에도 그대로 있다.</summary>
    [Fact]
    public void 정렬을_못_믿으면_멈추고_까닭을_담는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferRecipeRunner.Run(
            Aligned(wafer, expected: "[[-40, -40], [300, 310]]"), source, Registry, LoadRecipe, LoadTemplate(wafer));

        Assert.Equal(WaferStop.AlignmentRejected, result.Stop);
        Assert.Null(result.Run);
        Assert.Null(result.Defects);
        Assert.StartsWith("정렬을 믿을 수 없어", result.StopReason);
    }

    // ─────────────────────────────── 토막 낼 수 없는 레시피 — 검사 전에 거부

    /// <summary>★ die 를 하나도 돌기 전에, 파일도 하나도 읽기 전에 막는다(로더가 불리면 실패).</summary>
    [Theory]
    [InlineData("none", "정확히 1개")]
    [InlineData("two", "정확히 1개")]
    [InlineData("stray", "이어지지 않은")]
    [InlineData("twoEnds", "끝")]
    public void 토막_낼_수_없으면_검사_전에_거부한다(string shape, string message)
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        string inspect = $$"""
            { "id": "inspect", "type": "InspectDies",
              "outputs": { "defects": "waferDefects", "grid": "grid", "alignment": "alignmentOut" },
              "params": { {{GridParams(wafer)}} } }
            """;

        string nodes = shape switch
        {
            // InspectDies 가 없다
            "none" => """
                { "id": "match", "type": "TemplateMatchAll", "inputs": { "src": "alignSrc", "template": "alignTemplate" },
                  "outputs": { "matches": "matches" } },
                { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "matches" }, "outputs": { "alignment": "alignment" },
                  "params": { "expected": [[0, 0], [100, 100]] } }
                """,

            // 둘이다
            "two" => inspect + "," + inspect.Replace("\"inspect\"", "\"inspect2\"").Replace("\"waferDefects\"", "\"d2\"")
                         .Replace("\"grid\": \"grid\"", "\"grid\": \"g2\"").Replace("\"alignmentOut\"", "\"a2\""),

            // 표식 찾기가 어디에도 안 이어졌다
            "stray" => inspect + """
                , { "id": "match", "type": "TemplateMatchAll", "inputs": { "src": "alignSrc", "template": "alignTemplate" },
                    "outputs": { "matches": "matches" } }
                """,

            // 뒤 토막의 끝이 둘 — 어느 것이 결과인지 못 고른다
            "twoEnds" => inspect + """
                , { "id": "a", "type": "Sampling", "inputs": { "defects": "waferDefects" }, "outputs": { "result": "r1" } },
                  { "id": "b", "type": "Sampling", "inputs": { "defects": "waferDefects" }, "outputs": { "result": "r2" } }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Recipe recipe = RecipeLoader.Parse($$"""
            { "recipeId": "w", "contextInputs": [ "alignSrc", "alignTemplate" ], "pipeline": { "nodes": [ {{nodes}} ] } }
            """);

        WaferRecipeException error = Assert.Throws<WaferRecipeException>(
            () => WaferRecipeRunner.Run(recipe, source, Registry, NoRecipe, NoTemplate));

        Assert.Contains(message, error.Message);
    }

    /// <summary>조각이 둘이면 거부 — 조용히 첫 번째만 쓰면 둘 다 쓰인다고 믿게 된다.</summary>
    [Fact]
    public void 표식_조각이_둘이면_거부한다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferRecipeException error = Assert.Throws<WaferRecipeException>(() => WaferRecipeRunner.Run(
            Aligned(wafer, features: """[ { "template": "mark.bmp" }, { "template": "other.bmp" } ]"""),
            source, Registry, NoRecipe, NoTemplate));

        Assert.Contains("1개만", error.Message);
    }

    /// <summary>배선이 틀린 레시피는 토막 내기 전에 검증기가 막는다 — 실행기도 같은 검증기를 거친다.</summary>
    [Fact]
    public void 배선이_틀리면_검증기가_먼저_막는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        Recipe skipped = Aligned(wafer);
        skipped.Pipeline.Nodes[2].Inputs["alignment"] = "matches";     // 자세 계산을 건너뛰었다

        Assert.Throws<RecipeValidationException>(() => WaferRecipeRunner.Run(skipped, source, Registry, NoRecipe, NoTemplate));
    }

    // ─────────────────────────────── 견본 — samples/wafer

    /// <summary>
    /// 레포의 <c>samples/</c> 아래 파일 — 시험이 도는 폴더에서 위로 올라가며 <c>DiePipeline.slnx</c> 가 있는 곳을 찾는다.
    /// </summary>
    private static string Sample(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiePipeline.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", relative);
            }
        }

        throw new DirectoryNotFoundException("DiePipeline.slnx 가 있는 폴더를 못 찾았습니다");
    }

    /// <summary>
    /// ★ 견본 조각 그림은 코드가 그리는 조각과 <b>픽셀까지 같다</b> — 그림 코드가 바뀌면 견본이 낡았다고 여기서 알린다.
    ///   (<c>diepipe demo -o x.bmp</c> 가 저장하는 <c>x.mark.bmp</c> 를 옮겨 둔 것이다.)
    /// </summary>
    [Fact]
    public void 견본_조각은_코드가_그린_조각과_같다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using Mat drawn = wafer.DrawMarkTemplate();
        using Mat saved = Cv2.ImRead(Sample("wafer/features/demo_mark.bmp"), ImreadModes.Grayscale);
        using Mat diff = new();

        Assert.Equal((drawn.Cols, drawn.Rows), (saved.Cols, saved.Rows));

        Cv2.Absdiff(drawn, saved, diff);
        Assert.Equal(0, Cv2.CountNonZero(diff));
    }

    /// <summary>
    /// ★★ 견본 레시피 파일을 그대로 — <c>diepipe demo</c> 기본 그림에서 심은 결함을 전부, 그 die 에서 찾는다.
    ///   안의 경로(<c>../recipes/die.diff.json</c> · <c>features/demo_mark.bmp</c>)가 파일 기준으로 풀리는지도 여기서 본다.
    /// </summary>
    [Fact]
    public void 견본_레시피는_합성_웨이퍼에서_심은_결함을_전부_찾는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferRecipeRunner.RunFile(Sample("wafer/demo.wafer.json"), source, Registry);

        Assert.False(result.Stopped);
        Assert.Equal(wafer.OriginX, result.Alignment.Origin.X, tolerance: 0.5);
        Assert.Equal(wafer.OriginY, result.Alignment.Origin.Y, tolerance: 0.5);
        Assert.Equal(12, result.Grid!.Dies.Count);

        (int Col, int Row)[] found = [.. result.Found!.Items.Select(d => (d.DieCol, d.DieRow)).Order()];
        Assert.Equal(Truth(wafer), found);
    }

    /// <summary>레시피 안의 경로 — 상대면 레시피 파일이 있는 폴더 기준, 절대면 그대로.</summary>
    [Fact]
    public void 레시피_안의_경로는_레시피_파일_기준이다()
    {
        string recipe = Path.Combine(Path.GetTempPath(), "a", "b", "w.wafer.json");
        string absolute = Path.Combine(Path.GetTempPath(), "x", "die.json");

        Assert.Equal(Path.Combine(Path.GetTempPath(), "a", "recipes", "die.json"),
            WaferRecipeRunner.PathFrom(recipe, "../recipes/die.json"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "a", "b", "features", "m.bmp"),
            WaferRecipeRunner.PathFrom(recipe, "features/m.bmp"));
        Assert.Equal(absolute, WaferRecipeRunner.PathFrom(recipe, absolute));
    }
}