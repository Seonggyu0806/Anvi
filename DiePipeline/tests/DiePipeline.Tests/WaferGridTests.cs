using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// die 격자. ★ 오늘 확인할 것은 "설계 격자에 정렬을 곱해 실제 자리가 나오는가"다.
///
/// 이미지도 파일도 안 쓴다. 순수 계산이라 밀리초 단위로 돈다.
/// </summary>
public sealed class WaferGridTests
{
    private static WaferSpec Spec() => new() { Cols = 4, Rows = 3, PitchX = 100, PitchY = 80 };

    private static WaferAlignment Placed(double x, double y, double angle = 0, double scale = 1)
        => new() { Origin = new PointF(x, y), RotationDeg = angle, Scale = scale, MeanScore = 1.0 };

    [Fact]
    public void 격자를_세면_열곱하기행이다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec());

        Assert.Equal(12, grid.Count);
        Assert.Equal(4, grid.Cols);
        Assert.Equal(3, grid.Rows);
    }

    [Fact]
    public void 순서는_왼쪽에서_오른쪽_위에서_아래다()
    {
        // ★ row-major 고정. 이 순서가 결과의 순서가 되므로 흔들리면 재현이 깨진다.
        IReadOnlyList<DieOrigin> dies = DieGridBuilder.Build(Spec()).Dies;

        Assert.Equal((0, 0), (dies[0].Col, dies[0].Row));
        Assert.Equal((3, 0), (dies[3].Col, dies[3].Row));
        Assert.Equal((0, 1), (dies[4].Col, dies[4].Row));   // 한 행 넘어감
    }

    [Fact]
    public void 원점은_피치_간격으로_놓인다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec());

        Assert.Equal(new PointF(0, 0), grid.At(0, 0).Origin);
        Assert.Equal(new PointF(200, 80), grid.At(2, 1).Origin);
    }

    [Fact]
    public void 설계_원점을_더한다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec() with { OriginX = 7, OriginY = 5 });

        Assert.Equal(new PointF(107, 85), grid.At(1, 1).Origin);
    }

    [Fact]
    public void die_크기를_안_주면_피치와_같다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec());

        Assert.Equal(100, grid.DieWidth);
        Assert.Equal(80, grid.DieHeight);
    }

    [Fact]
    public void die_가_피치보다_작으면_그_차이가_스크라이브다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec() with { DieWidth = 90, DieHeight = 70 });

        Assert.Equal(90, grid.DieWidth);
        Assert.Equal(new Roi(200, 80, 90, 70), grid.RoiOf(grid.At(2, 1)));
    }

    [Fact]
    public void 정렬이_없으면_설계_그대로다()
    {
        // null 과 Identity 가 같은 결과여야 한다
        Assert.Equal(
            DieGridBuilder.Build(Spec()).Dies,
            DieGridBuilder.Build(Spec(), WaferAlignment.Identity).Dies);
    }

    [Fact]
    public void 웨이퍼가_밀려_있으면_원점도_밀린다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec(), Placed(50, 20));

        Assert.Equal(new PointF(50, 20), grid.At(0, 0).Origin);
        Assert.Equal(new PointF(150, 20), grid.At(1, 0).Origin);
    }

    [Fact]
    public void 웨이퍼가_돌아_있으면_원점도_돈다()
    {
        // 90도 돌리면 오른쪽 이웃이 아래로 간다
        DieGrid grid = DieGridBuilder.Build(Spec(), Placed(0, 0, angle: 90));
        PointF p = grid.At(1, 0).Origin;

        Assert.Equal(0, Math.Round(p.X));
        Assert.Equal(100, Math.Round(p.Y));
    }

    [Fact]
    public void 조금만_기울어도_멀리서는_크게_벌어진다()
    {
        // ★ 1도는 작아 보이지만 300px 떨어진 die 는 5px 내려간다.
        //   정렬을 안 하면 그만큼 어긋난 자리를 자른다.
        DieGrid grid = DieGridBuilder.Build(Spec(), Placed(0, 0, angle: 1));
        PointF p = grid.At(3, 0).Origin;

        Assert.Equal(300, Math.Round(p.X));
        Assert.Equal(5, Math.Round(p.Y));
    }

    [Theory]
    [InlineData(0, 25)]    // 전부 E1
    [InlineData(1, 9)]     // 테두리 한 겹이 E0 → 3×3 만 남는다
    [InlineData(2, 1)]     // 두 겹 → 가운데 하나만
    public void edgeRings_만큼_바깥이_변두리가_된다(int rings, int expectedValid)
    {
        DieGrid grid = DieGridBuilder.Build(
            new WaferSpec { Cols = 5, Rows = 5, PitchX = 10, PitchY = 10, EdgeRings = rings });

        Assert.Equal(expectedValid, grid.Dies.Count(d => d.Zone == WaferZone.E1));
    }

    [Fact]
    public void 존은_절대_Unknown_으로_안_나온다()
    {
        // Unknown 은 "초기화를 빠뜨렸다"는 표시지, 격자가 만들어 낼 값이 아니다.
        DieGrid grid = DieGridBuilder.Build(Spec() with { EdgeRings = 1 });

        Assert.DoesNotContain(grid.Dies, d => d.Zone == WaferZone.Unknown);
    }

    [Fact]
    public void 초기화를_빠뜨린_존은_Unknown_이다()
    {
        // ★ 이게 Unknown 을 둔 이유다. 교수님처럼 E1 이 0이면 이 실수가 안 보인다.
        Assert.Equal(WaferZone.Unknown, default(WaferZone));
    }

    [Fact]
    public void 격자_밖은_거부한다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec());

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.At(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.At(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.At(-1, 0));
    }

    [Fact]
    public void die_가_피치보다_크면_거부한다()
    {
        // ROI 가 겹치면 같은 결함이 두 번 잡힌다
        Assert.Throws<ArgumentException>(() => DieGridBuilder.Build(Spec() with { DieWidth = 120 }));
    }

    [Fact]
    public void 이상한_형상은_거부한다()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DieGridBuilder.Build(Spec() with { Cols = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => DieGridBuilder.Build(Spec() with { PitchX = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => DieGridBuilder.Build(Spec() with { EdgeRings = -1 }));
    }

    [Fact]
    public void 스케일_0인_정렬은_거부한다()
    {
        // 모든 좌표가 원점으로 무너진다. 증상이 "전부 (0,0)"이라 원인을 못 찾는다.
        Assert.Throws<ArgumentException>(() => DieGridBuilder.Build(Spec(), Placed(0, 0, scale: 0)));
    }

    [Fact]
    public void 결함은_변환_전에는_절대좌표가_없다()
    {
        // ★ double? 이라 "아직 변환 안 됨"이 타입에 드러난다. 0이면 원점의 결함과 구분이 안 된다.
        WaferDefect defect = new()
        {
            DieCol = 0,
            DieRow = 0,
            Zone = WaferZone.E1,
            Defect = new Defect { Id = 1, Bounds = new BoundingBox(1, 2, 3, 4), Area = 12, Centroid = new PointF(2, 3) },
        };

        Assert.Null(defect.AbsX);
        Assert.Null(defect.AbsY);
    }

    [Fact]
    public void 결함_목록은_존으로_갈라_볼_수_있다()
    {
        Defect one = new() { Id = 1, Bounds = new BoundingBox(1, 2, 3, 4), Area = 12, Centroid = new PointF(2, 3) };

        WaferDefectList list = new()
        {
            Items =
            [
                new WaferDefect { DieCol = 1, DieRow = 1, Zone = WaferZone.E1, Defect = one },
                new WaferDefect { DieCol = 0, DieRow = 0, Zone = WaferZone.E0, Defect = one },
            ],
        };

        Assert.Equal(2, list.Count);
        Assert.Single(list.E1);
        Assert.Single(list.E0);
        Assert.Empty(WaferDefectList.Empty.Items);
    }

    // ─────────────────────────────── 원판 — 원 기준 존

    /// <summary>5×5 · 피치 100 · 원 중심 (250,250) 반지름 200.</summary>
    private static WaferSpec Round() => new()
    {
        Cols = 5,
        Rows = 5,
        PitchX = 100,
        PitchY = 100,
        Circle = new WaferCircle(250, 250, 200),
    };

    /// <summary>격자를 글자 지도로 — <c>#</c> 온전(E1) · <c>e</c> 걸침(E0) · <c>.</c> 원판 밖.</summary>
    private static string Map(DieGrid grid)
        => string.Join("\n", Enumerable.Range(0, grid.Rows).Select(row => string.Join(" ",
            Enumerable.Range(0, grid.Cols).Select(col => grid.At(col, row).Zone switch
            {
                WaferZone.E1 => "#",
                WaferZone.E0 => "e",
                WaferZone.Outside => ".",
                _ => "?",
            }))));

    /// <summary>
    /// ★ 교수님 정의 — "E0 = 원판 밖과 겹치는 die만". 온전히 들어가면 E1, 걸치면 E0, 안 닿으면 die 가 아니다.
    ///   사각형 테두리(edgeRings)로는 이 계단 모양이 안 나온다.
    /// </summary>
    [Fact]
    public void 원을_주면_온전_걸침_밖으로_나눈다()
    {
        string expected = string.Join("\n",
            ". e e e .",
            "e e # e e",
            "e # # # e",
            "e e # e e",
            ". e e e .");

        Assert.Equal(expected, Map(DieGridBuilder.Build(Round())));
    }

    /// <summary>
    /// ★ 귀퉁이가 하나도 안 들어가도 걸칠 수 있다 — 원의 끝이 die 변 가운데로 파고드는 경우.
    ///   귀퉁이만 보면 '원판 밖' 으로 잘못 버려진다.
    /// </summary>
    [Fact]
    public void 원이_변_가운데만_파고들어도_걸침이다()
    {
        DieGrid grid = DieGridBuilder.Build(new WaferSpec
        {
            Cols = 3,
            Rows = 1,
            PitchX = 100,
            PitchY = 100,
            Circle = new WaferCircle(150, -40, 60),     // die(1,0) 윗변 가운데로 20px 파고든다 · 귀퉁이는 64px
        });

        Assert.Equal(WaferZone.Outside, grid.At(0, 0).Zone);
        Assert.Equal(WaferZone.E0, grid.At(1, 0).Zone);
        Assert.Equal(WaferZone.Outside, grid.At(2, 0).Zone);
    }

    /// <summary>
    /// ★ 원은 <b>사진 좌표</b>다. 정렬로 격자가 밀리면 판정도 밀린 자리로 한다 —
    ///   설계 좌표로 판정하면 웨이퍼가 밀린 만큼 엉뚱한 칸이 E0 가 된다.
    /// </summary>
    [Fact]
    public void 원판_판정은_정렬된_실제_자리로_한다()
    {
        WaferSpec spec = new()
        {
            Cols = 1, Rows = 1, PitchX = 100, PitchY = 100, Circle = new WaferCircle(1050, 1050, 80),
        };

        Assert.Equal(WaferZone.E1, DieGridBuilder.Build(spec, Placed(1000, 1000)).At(0, 0).Zone);
        Assert.Equal(WaferZone.Outside, DieGridBuilder.Build(spec).At(0, 0).Zone);
    }

    /// <summary>돌아간 격자에서도 '변만 걸친' 칸을 찾는다 — 원 중심을 die 자기 좌표로 되돌려 잰다.</summary>
    [Fact]
    public void 돌아간_격자에서도_변만_걸친_칸을_찾는다()
    {
        WaferSpec spec = new()
        {
            Cols = 1, Rows = 1, PitchX = 100, PitchY = 100, Circle = new WaferCircle(-50, -40, 60),
        };

        // 90° — die 가 x -100~0 · y 0~100 자리로 돈다. 원은 그 윗변 가운데로 파고든다(귀퉁이는 64px 밖)
        Assert.Equal(WaferZone.E0, DieGridBuilder.Build(spec, Placed(0, 0, angle: 90)).At(0, 0).Zone);
    }

    [Fact]
    public void 원과_edgeRings_는_같이_못_쓴다()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => DieGridBuilder.Build(Round() with { EdgeRings = 1 }));

        Assert.Contains("같이 못", error.Message);
    }

    /// <summary>
    /// ★ 가장자리 여유는 '검사에 쓰는 원' 만 줄인다 — 잰 원은 그대로다.
    ///   die(2,1) 은 반지름 200 이면 온전(E1)인데, 여유 50(검사 반지름 150)이면 걸침(E0)이 된다.
    /// </summary>
    [Fact]
    public void 가장자리_여유만큼_줄인_원으로_칸을_나눈다()
    {
        WaferSpec spec = Round() with { EdgeExclusion = 50 };

        Assert.Equal(WaferZone.E1, DieGridBuilder.Build(Round()).At(2, 1).Zone);
        Assert.Equal(WaferZone.E0, DieGridBuilder.Build(spec).At(2, 1).Zone);
        Assert.Equal(200, spec.Circle!.Radius);              // 잰 원은 그대로
        Assert.Equal(150, spec.InspectedCircle!.Radius);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(200)]
    [InlineData(double.NaN)]
    public void 가장자리_여유는_0_이상_반지름_미만이다(double exclusion)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => DieGridBuilder.Build(Round() with { EdgeExclusion = exclusion }));

    /// <summary>원판 없이 여유만 주면 거부한다 — 조용히 무시하면 "여유를 줬는데 왜 그대로지" 가 된다.</summary>
    [Fact]
    public void 원판_없이_가장자리_여유만_주면_거부한다()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => DieGridBuilder.Build(Spec() with { EdgeExclusion = 10 }));

        Assert.Contains("원판", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 반지름이_0_이하면_거부한다(double radius)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => DieGridBuilder.Build(Round() with { Circle = new WaferCircle(250, 250, radius) }));

    // ─────────────────────────────── 수동 오버라이드 목록

    /// <summary>
    /// ★ 자동 판정 뒤에, 적힌 die 만 덮어쓴다 — 교수님 Phase8 §10 "'자동' 판정만 믿지 말 것(노치·플랫 존재)".
    ///   가운데 온전한 칸을 제외, 걸친 칸을 E1, 원판 밖 칸을 E0 로. 나머지는 그대로.
    /// </summary>
    [Fact]
    public void 수동_지정은_자동_판정을_덮어쓴다()
    {
        WaferSpec spec = Round() with
        {
            ZoneOverrides =
            [
                new ZoneOverride(2, 2, WaferZone.Outside),
                new ZoneOverride(1, 0, WaferZone.E1),
                new ZoneOverride(0, 0, WaferZone.E0),
            ],
        };

        string expected = string.Join("\n",
            "e # e e .",
            "e e # e e",
            "e # . # e",
            "e e # e e",
            ". e e e .");

        Assert.Equal(expected, Map(DieGridBuilder.Build(spec)));
    }

    /// <summary>원판 없이도 쓴다 — 사각형 격자에서 한 칸만 E0 로.</summary>
    [Fact]
    public void 원판이_없어도_수동_지정이_된다()
    {
        DieGrid grid = DieGridBuilder.Build(Spec() with { ZoneOverrides = [new ZoneOverride(1, 1, WaferZone.E0)] });

        Assert.Equal(WaferZone.E0, grid.At(1, 1).Zone);
        Assert.Equal(grid.Count - 1, grid.Dies.Count(d => d.Zone == WaferZone.E1));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, -1)]
    public void 격자_밖_die_를_지정하면_거부한다(int col, int row)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DieGridBuilder.Build(
            Round() with { ZoneOverrides = [new ZoneOverride(col, row, WaferZone.E0)] }));

    [Fact]
    public void 정하지_않은_존으로_지정하면_거부한다()
        => Assert.Throws<ArgumentException>(() => DieGridBuilder.Build(
            Round() with { ZoneOverrides = [new ZoneOverride(1, 1, WaferZone.Unknown)] }));

    /// <summary>같은 die 를 두 번 적으면 어느 쪽이 이겼는지 모른다.</summary>
    [Fact]
    public void 같은_die_를_두_번_지정하면_거부한다()
        => Assert.Throws<ArgumentException>(() => DieGridBuilder.Build(Round() with
        {
            ZoneOverrides = [new ZoneOverride(1, 1, WaferZone.E0), new ZoneOverride(1, 1, WaferZone.Outside)],
        }));
}