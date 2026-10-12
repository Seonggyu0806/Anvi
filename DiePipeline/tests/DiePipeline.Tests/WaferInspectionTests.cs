using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 한 장 처음부터 끝까지 — <b>합성 웨이퍼의 정답</b>과 대조한다.
///
/// ★ 명령행과 화면이 이 흐름 하나를 같이 쓴다. 여기가 맞으면 둘 다 맞고, 틀리면 둘 다 틀린다.
/// </summary>
public sealed class WaferInspectionTests
{
    /// <summary><c>samples/recipes/die.diff.json</c> 과 같은 내용(실데이터로 확정한 값).</summary>
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

    /// <summary>화면 시드 그래프와 같은 짝 — Median · 부호만 바꿔 끼운다 · minArea 4.</summary>
    private static Recipe MedianRecipe(string diffPolicy) => RecipeLoader.Parse($$"""
        { "recipeId": "t", "contextInputs": [ "roi", "chipRegions" ], "pipeline": { "nodes": [
          { "id": "golden", "type": "Combine", "inputs": { "regions": "chipRegions" },
            "outputs": { "result": "golden" }, "params": { "strategy": "Median" } },
          { "id": "diff", "type": "Difference", "inputs": { "test": "roi", "golden": "golden" },
            "outputs": { "result": "diff" },
            "params": { "windowSize": 3, "diffPolicy": "{{diffPolicy}}", "neighborMode": "None" } },
          { "id": "binarize", "type": "Binarize", "inputs": { "src": "diff" },
            "outputs": { "result": "mask" }, "params": { "method": "Threshold", "value": 0.2 } },
          { "id": "label", "type": "Label", "inputs": { "mask": "mask", "intensity": "roi" },
            "outputs": { "result": "defectList" }, "params": { "connectivity": 8, "minArea": 4 } }
        ] } }
        """);

    /// <summary>합성 웨이퍼(8비트)를 작업 포맷(GrayF32)으로 — 파일에서 읽을 때와 같은 배율(1/255).</summary>
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

    private static MarkAlignment Marks(SyntheticWafer wafer, Mat template, params (int X, int Y)[] expected)
        => new()
        {
            Template = template,
            Expected = expected.Length > 0
                ? [.. expected.Select(p => new PointF(p.X, p.Y))]
                : [.. wafer.Marks.Select(wafer.ExpectedAt).Select(p => new PointF(p.X, p.Y))],
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

    private static (int Col, int Row)[] Found(WaferInspectionResult result)
        => [.. result.Found!.Items.Select(d => (d.DieCol, d.DieRow)).Order()];

    /// <summary>★★ 표식을 찾아 자세를 풀고, 심은 결함을 <b>정확히 그 die 들에서</b> 찾는다.</summary>
    [Fact]
    public void 표식으로_정렬하고_심은_결함을_전부_찾는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer), Alignment = Marks(wafer, template) },
            source,
            CvBackend.CreateRegistry());

        Assert.False(result.Stopped);

        // 되찾은 die(0,0) 자리 = 그릴 때 정답
        Assert.Equal(wafer.OriginX, result.Alignment.Origin.X, tolerance: 0.5);
        Assert.Equal(wafer.OriginY, result.Alignment.Origin.Y, tolerance: 0.5);

        Assert.Equal(Truth(wafer), Found(result));
    }

    [Fact]
    public void 반듯하면_원점만_줘도_돈다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) },
            source,
            CvBackend.CreateRegistry());

        Assert.Null(result.Solved);
        Assert.Equal(Truth(wafer), Found(result));
    }

    /// <summary>★ warp read 가 없으니 0.05° 를 넘으면 <b>검사를 시작하지 않는다</b> — 화면에서도 똑같이.</summary>
    [Fact]
    public void 기울면_검사를_시작하지_않는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { AngleDeg = 0.7 });
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer), Alignment = Marks(wafer, template) },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(WaferStop.RotationNeedsWarp, result.Stop);
        Assert.Null(result.Run);
        Assert.Contains("warp read", result.StopReason);

        // 정렬 자체는 풀렸다 — 각도를 화면에 보여 줄 수 있어야 한다.
        Assert.Equal(0.7, result.Alignment.RotationDeg, tolerance: 0.1);
    }

    /// <summary>★★ 정렬을 못 믿으면 검사 결과가 <b>아예 없다</b> — "결함 0개" 로 보이면 안 된다.</summary>
    [Fact]
    public void 기대_위치가_틀리면_정렬을_거부한다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer),
                Alignment = Marks(wafer, template, (-40, -40), (300, 310)),     // 두 번째가 110px 틀렸다
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(WaferStop.AlignmentRejected, result.Stop);
        Assert.Null(result.Run);
        Assert.Null(result.Found);
        Assert.StartsWith("정렬을 믿을 수 없어", result.StopReason);
    }

    // ─────────────────────────────── 정렬 창 — 표식 근처만 읽는다(교수님 Phase5 #5.10)

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

    private static (int X, int Y) TrueOrigin(SyntheticWafer wafer)
        => ((int)Math.Round(wafer.OriginX), (int)Math.Round(wafer.OriginY));

    /// <summary>
    /// ★ 대략 원점(교수님 firstDieX/Y)을 같이 줘도 <b>이동이 두 번 들어가지 않는다</b> —
    ///   전에는 그게 무서워 명령행에서 둘을 같이 못 쓰게 막았다.
    /// </summary>
    [Fact]
    public void 대략_원점을_같이_줘도_정렬_결과는_같다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { ShiftX = 3, ShiftY = 2 });
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer, 6, -4), Alignment = Marks(wafer, template) },
            source,
            CvBackend.CreateRegistry());

        Assert.False(result.Stopped);
        Assert.Equal(wafer.OriginX, result.Alignment.Origin.X, tolerance: 0.5);
        Assert.Equal(wafer.OriginY, result.Alignment.Origin.Y, tolerance: 0.5);
        Assert.Equal(wafer.OriginX, result.Grid!.Dies[0].Origin.X, tolerance: 0.5);     // 격자도 한 번만 밀렸다
        Assert.Equal(Truth(wafer), Found(result));
    }

    /// <summary>
    /// ★★ 창으로 찾아도 사진 전체를 뒤진 것과 <b>같은 자리</b>가 나오고, 사진 전체는 <b>한 번도</b> 안 읽는다.
    ///   원판 원본은 통째로 읽으면 수 GB 다 — 5주차에 정렬을 못 쓴 까닭.
    /// </summary>
    [Fact]
    public void 표식_근처_창만_읽어도_같은_자리를_찾는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { ShiftX = 3, ShiftY = 2 });
        using Mat template = Template(wafer);
        (int x, int y) = TrueOrigin(wafer);
        Roi everything = new(0, 0, wafer.Image.Cols, wafer.Image.Rows);

        using RecordingSource whole = new(Source(wafer));
        using RecordingSource windowed = new(Source(wafer));

        WaferInspectionResult full = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer), Alignment = Marks(wafer, template) },
            whole,
            CvBackend.CreateRegistry());

        WaferInspectionResult near = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer, x + 4, y - 3),                                  // 대략 원점 — 몇 px 틀려도 된다
                Alignment = Marks(wafer, template) with { SearchPadding = 10 },
            },
            windowed,
            CvBackend.CreateRegistry());

        Assert.Contains(everything, whole.Reads);
        Assert.DoesNotContain(everything, windowed.Reads);

        Assert.False(near.Stopped);
        Assert.Equal(full.Alignment.Origin.X, near.Alignment.Origin.X, tolerance: 0.01);
        Assert.Equal(full.Alignment.Origin.Y, near.Alignment.Origin.Y, tolerance: 0.01);
        Assert.Equal(full.Alignment.RotationDeg, near.Alignment.RotationDeg, tolerance: 1e-6);
        Assert.Equal(Truth(wafer), Found(near));
    }

    /// <summary>★ 창 안에 표식이 없으면 <b>정렬을 거부</b>하고, 창에서만 찾았다는 걸 같이 말한다.</summary>
    [Fact]
    public void 창_밖에_표식이_있으면_정렬을_거부하고_창을_말한다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);
        (int x, int y) = TrueOrigin(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer, x + 60, y + 60),                                // 대략 원점이 창보다 크게 틀렸다
                Alignment = Marks(wafer, template) with { SearchPadding = 5 },
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(WaferStop.AlignmentRejected, result.Stop);
        Assert.Null(result.Run);
        Assert.Contains("±5px", result.StopReason);
    }

    /// <summary>판정은 샘플링 <b>전</b>으로 한다 — 둘 다 돌려줘야 화면이 "몇 개 뺐는지" 를 말할 수 있다.</summary>
    [Fact]
    public void 샘플링_전과_후를_둘_다_준다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer),
                Alignment = Marks(wafer, template),
                Sampling = new SamplingOptions { Mode = SamplingMode.BigSize, Count = 1 },
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(3, result.Found!.Count);
        Assert.Equal(1, result.Defects!.Count);
    }

    [Fact]
    public void 레시피가_틀리면_돌기_전에_막는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        Recipe broken = RecipeLoader.Parse(DieDiff.Replace("\"minArea\": 500", "\"minArea\": -1"));

        Assert.Throws<RecipeValidationException>(() => WaferInspection.Run(
            new WaferInspectionRequest { E1 = broken, Spec = Grid(wafer, 54, 54) },
            source,
            CvBackend.CreateRegistry()));
    }

    /// <summary>
    /// ★★ 5주차 화면에서 "3개 심었는데 4개" 로 나온 것 그대로.
    ///   c0r2 와 c2r2 에 심은 결함이 die 안에서 26×28 만큼 겹친다. 가운데 c1r2 는 그 둘을 골든으로 쓰고,
    ///   Median 3장 중 둘이 어두우니 중앙값이 어두워진다 → 부호 없이 보면 c1r2 에 <b>겹친 넓이 728</b> 짜리 결함.
    ///   DarkOnly 로 보면 그건 '검사 die 가 더 밝음' 이라 버려진다. <b>장수가 아니라 부호가 막는다.</b>
    /// </summary>
    /// <summary>
    /// ★ 5주차 — 실데이터를 열고 격자를 합성용 값 그대로 둔 채 검사했다. 귀퉁이만 보고 결과가 나왔다.
    ///   격자 밖에 die 가 한 칸 이상 더 들어갈 자리가 있으면 알린다. 검사는 멈추지 않는다.
    /// </summary>
    [Fact]
    public void 격자가_사진_일부만_덮으면_경고한다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        // 4열 웨이퍼에 2열만 — 오른쪽에 두 열이 남는다
        WaferSpec half = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) with { Cols = 2 };

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = half },
            source,
            CvBackend.CreateRegistry());

        Assert.False(result.Stopped);
        Assert.NotNull(result.CoverageWarning);
        Assert.Contains("오른쪽에 2열", result.CoverageWarning);
        Assert.DoesNotContain("아래", result.CoverageWarning);
    }

    /// <summary>합성 웨이퍼의 테두리 여백은 한 칸이 안 된다 — 격자가 맞으면 경고가 없어야 한다.</summary>
    [Fact]
    public void 격자가_사진을_다_덮으면_경고가_없다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest { E1 = E1(), Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) },
            source,
            CvBackend.CreateRegistry());

        Assert.Null(result.CoverageWarning);
    }

    // ─────────────────────────────── 수동 제외

    /// <summary>
    /// ★ 사람이 제외한 die 는 검사하지 않고 die 수에서도 빠진다 — 그 die 에 심은 결함도 안 나온다.
    ///   대신 그 die 는 '원판 밖' 상태로 남아 깨끗한 die 와 구별된다.
    /// </summary>
    [Fact]
    public void 사람이_제외한_die_는_검사하지_않고_개수에서_빠진다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        (int col, int row) = Truth(wafer)[0];

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) with
                {
                    ZoneOverrides = [new ZoneOverride(col, row, WaferZone.Outside)],
                },
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(Truth(wafer).Skip(1), Found(result));
        Assert.Equal(wafer.Spec.Cols * wafer.Spec.Rows - 1, result.Run!.DieCount);
        Assert.True(result.Run.Dies.Single(d => d.Die.Col == col && d.Die.Row == row).OffWafer);
    }

    // ─────────────────────────────── 원판 — waferMask

    /// <summary>원이 없으면 전부 원판 안(255) — 사각형 웨이퍼에서도 같은 레시피가 돈다.</summary>
    [Fact]
    public void 원이_없으면_마스크는_전부_켜짐이다()
    {
        using Mat mask = WaferInspection.WaferMaskOf(new Roi(0, 0, 10, 8), null);

        Assert.Equal(10 * 8, Cv2.CountNonZero(mask));
    }

    /// <summary>
    /// ★ 픽셀 <b>중심</b>이 원 안이면 켜짐. die 자리는 사진 좌표라 원도 사진 좌표 그대로 댄다.
    ///   die 왼쪽 위 귀퉁이에 중심을 둔 반지름 5 — 4분의 1 원 안에 픽셀 중심이 20개 들어간다.
    /// </summary>
    [Fact]
    public void 마스크는_픽셀_중심이_원_안인_곳만_켜진다()
    {
        using Mat mask = WaferInspection.WaferMaskOf(new Roi(100, 100, 10, 10), new WaferCircle(100, 100, 5));

        Assert.Equal(20, Cv2.CountNonZero(mask));
        Assert.Equal(255, mask.At<byte>(2, 2));   // 중심 (102.5,102.5) — 3.5 떨어짐
        Assert.Equal(0, mask.At<byte>(4, 4));     // 중심 (104.5,104.5) — 6.4 떨어짐
    }

    /// <summary>E0 를 가려서 보는 레시피 — die.dark 앞에 MaskApply 한 블록.</summary>
    private const string DarkMasked = """
        { "recipeId": "t.e0.masked", "contextInputs": [ "roi", "waferMask" ], "pipeline": { "nodes": [
          { "id": "mask", "type": "MaskApply", "inputs": { "src": "roi", "mask": "waferMask" },
            "outputs": { "result": "masked" }, "params": { "fill": 1.0 } },
          { "id": "binarize", "type": "Binarize", "inputs": { "src": "masked" },
            "outputs": { "result": "dark" }, "params": { "method": "Range", "low": 0.0, "high": 0.15 } },
          { "id": "label", "type": "Label", "inputs": { "mask": "dark", "intensity": "roi" },
            "outputs": { "result": "defectList" }, "params": { "connectivity": 8, "minArea": 200 } }
        ] } }
        """;

    /// <summary>같은 레시피에서 가리기만 뺀 것 — 교수님 die.dark 그대로.</summary>
    private const string Dark = """
        { "recipeId": "t.e0", "contextInputs": [ "roi" ], "pipeline": { "nodes": [
          { "id": "binarize", "type": "Binarize", "inputs": { "src": "roi" },
            "outputs": { "result": "dark" }, "params": { "method": "Range", "low": 0.0, "high": 0.15 } },
          { "id": "label", "type": "Label", "inputs": { "mask": "dark", "intensity": "roi" },
            "outputs": { "result": "defectList" }, "params": { "connectivity": 8, "minArea": 200 } }
        ] } }
        """;

    /// <summary>
    /// 6×6 합성 웨이퍼의 원판 밖을 어둡게 칠한다 — 원형 원본처럼. 원판 밖은 E0 임계(0.15)보다 어둡다.
    /// 칠하는 기준도 <see cref="WaferInspection.WaferMaskOf"/> — 그림과 마스크가 같은 원을 본다.
    /// </summary>
    private static WaferInspectionResult RunRound(string e0Json)
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { Cols = 6, Rows = 6, Defects = 0 });

        WaferCircle circle = new(wafer.OriginX + 300, wafer.OriginY + 300, 250);

        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);

        using (Mat inside = WaferInspection.WaferMaskOf(new Roi(0, 0, f32.Cols, f32.Rows), circle))
        using (Mat outside = new())
        {
            Cv2.Compare(inside, Scalar.All(0), outside, CmpTypes.EQ);
            f32.SetTo(Scalar.All(0.05), outside);
        }

        using MatImageSource source = new(f32);

        return WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                E0 = RecipeLoader.Parse(e0Json),
                Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) with { Circle = circle },
            },
            source,
            CvBackend.CreateRegistry());
    }

    /// <summary>
    /// ★★ 로드맵 3번의 끝 — 원판 밖은 안 보고, 걸친 칸은 E0 레시피로 보되 원판 밖 배경은 가린다.
    ///   결함을 하나도 안 심었으니 0개여야 맞다.
    /// </summary>
    [Fact]
    public void 원판에서_걸친_칸을_가려서_보면_배경이_결함이_안_된다()
    {
        WaferInspectionResult result = RunRound(DarkMasked);

        Assert.False(result.Stopped);
        Assert.Empty(result.Run!.Failures);
        Assert.Contains(result.Run.Dies, d => d.OffWafer);
        Assert.Equal(result.Run.Dies.Count(d => !d.OffWafer), result.Run.DieCount);
        Assert.Contains(result.Grid!.Dies, d => d.Zone == WaferZone.E0);

        Assert.Equal(0, result.Found!.Count);
        Assert.Null(result.EdgeWarning);
    }

    /// <summary>
    /// ★ 같은 그림을 가리지 않는 E0 레시피(교수님 die.dark 그대로)로 보면 — 배경이 걸친 칸에서 결함이 된다.
    ///   그래서 레시피를 보고 미리 경고한다.
    /// </summary>
    [Fact]
    public void 원판에서_가리지_않으면_배경이_결함이_되고_경고가_뜬다()
    {
        WaferInspectionResult result = RunRound(Dark);

        Assert.NotEmpty(result.Found!.E0);
        Assert.Empty(result.Found.E1);
        Assert.NotNull(result.EdgeWarning);
    }

    /// <summary>
    /// ★★ 로드맵 완료 기준 1·2 — 합성 <b>원형</b> 웨이퍼를 표식으로 정렬해 검사하면 심은 결함 = 찾은 결함.
    ///   원판 밖 칸은 결과에 없고, 걸친 칸은 die.dark.masked 로 검사돼도 배경이 결함으로 안 잡힌다.
    /// </summary>
    [Fact]
    public void 원형_웨이퍼에서_심은_결함을_정확히_찾는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(
            new SyntheticWaferSpec { Cols = 6, Rows = 6, Round = true, Defects = 5 });
        using MatImageSource source = Source(wafer);
        using Mat template = Template(wafer);
        SyntheticCircle c = wafer.Circle!.Value;

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                E0 = RecipeLoader.Parse(DarkMasked),
                Spec = Grid(wafer) with { Circle = new WaferCircle(c.CenterX, c.CenterY, c.Radius) },
                Alignment = Marks(wafer, template),
            },
            source,
            CvBackend.CreateRegistry());

        Assert.False(result.Stopped);
        Assert.Empty(result.Run!.Failures);
        Assert.Contains(result.Run.Dies, d => d.OffWafer);
        Assert.Contains(result.Grid!.Dies, d => d.Zone == WaferZone.E0);

        Assert.Equal(Truth(wafer), Found(result));
        Assert.Null(result.EdgeWarning);
    }

    /// <summary>★ 완료 기준 3 — 걸친 칸이라도 <b>원판 안쪽</b>의 어두운 결함은 잡힌다. 가린 건 원판 밖뿐이다.</summary>
    [Fact]
    public void 걸친_칸의_원판_안쪽_결함은_잡힌다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(
            new SyntheticWaferSpec { Cols = 6, Rows = 6, Round = true, Defects = 0 });
        SyntheticCircle c = wafer.Circle!.Value;

        // die(1,1) — 왼쪽 위 귀퉁이가 원판 밖이라 걸친 칸이지만, 오른쪽 아래는 원판 안쪽 깊숙이 있다.
        //   결함(24×24)을 그 오른쪽 아래쪽, 무늬를 피한 자리에 둔다 — 원 경계에서 50px 넘게 안쪽이다
        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        Cv2.Rectangle(
            f32,
            new Rect((int)wafer.OriginX + wafer.Spec.Pitch + 40, (int)wafer.OriginY + wafer.Spec.Pitch + 50, 24, 24),
            Scalar.All(0.04),
            -1);

        using MatImageSource source = new(f32);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                E0 = RecipeLoader.Parse(DarkMasked),
                Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY)
                       with { Circle = new WaferCircle(c.CenterX, c.CenterY, c.Radius) },
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Equal(WaferZone.E0, result.Grid!.At(1, 1).Zone);

        WaferDefect found = Assert.Single(result.Found!.Items);
        Assert.Equal((1, 1, WaferZone.E0), (found.DieCol, found.DieRow, found.Zone));
    }

    /// <summary>
    /// ★ 가장자리 여유 띠 안의 어두운 것은 안 본다 — 원을 맞춘 오차의 틈이나 노치가 여기 들어간다.
    ///   같은 그림에서 여유 0 이면 잡히고, 여유 40 이면 가려진다. 마스크도 칸 판정과 같은 원을 쓴다.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(40, 0)]
    public void 가장자리_여유_띠_안은_검사하지_않는다(double exclusion, int expected)
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(
            new SyntheticWaferSpec { Cols = 6, Rows = 6, Round = true, Defects = 0 });
        SyntheticCircle c = wafer.Circle!.Value;

        // die(0,2) 안, 원판 가장자리에서 4~34px 안쪽 — 원판 안이지만 여유 40 의 띠 안이다
        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        Cv2.Rectangle(
            f32,
            new Rect((int)wafer.OriginX + 40, (int)wafer.OriginY + (2 * wafer.Spec.Pitch) + 46, 24, 24),
            Scalar.All(0.04),
            -1);

        using MatImageSource source = new(f32);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = E1(),
                E0 = RecipeLoader.Parse(DarkMasked),
                Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY) with
                {
                    Circle = new WaferCircle(c.CenterX, c.CenterY, c.Radius),
                    EdgeExclusion = exclusion,
                },
            },
            source,
            CvBackend.CreateRegistry());

        Assert.Empty(result.Run!.Failures);
        Assert.Equal(WaferZone.E0, result.Grid!.At(0, 2).Zone);
        Assert.Equal(expected, result.Found!.Count);
    }

    [Theory]
    [InlineData("Absolute", true)]
    [InlineData("DarkOnly", false)]
    public void Median_도_이웃_둘이_겹치면_미러가_난다(string diffPolicy, bool mirrored)
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec { Cols = 4, Rows = 8, Seed = 1 });
        using MatImageSource source = Source(wafer);

        WaferInspectionResult result = WaferInspection.Run(
            new WaferInspectionRequest
            {
                E1 = MedianRecipe(diffPolicy),
                Spec = Grid(wafer, (int)wafer.OriginX, (int)wafer.OriginY),
                Strategy = NeighborStrategy.Nearest,
                GoldenWanted = 3,
            },
            source,
            CvBackend.CreateRegistry());

        // 심은 자리가 이 모양이어야 이 시험이 뜻이 있다 — 그림이 바뀌면 여기서 먼저 깨진다
        (int, int)[] planted = [(0, 2), (1, 5), (2, 2)];
        Assert.Equal(planted, Truth(wafer));

        if (mirrored)
        {
            WaferDefect ghost = Assert.Single(result.Found!.Items, d => (d.DieCol, d.DieRow) == (1, 2));
            Assert.Equal(728, ghost.Defect.Area);
            Assert.NotNull(result.Need.Warning);
        }
        else
        {
            Assert.Equal(Truth(wafer), Found(result));
            Assert.Null(result.Need.Warning);
        }
    }
}