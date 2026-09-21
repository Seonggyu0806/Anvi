using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 결함 모으기 + 좌표 변환.
///
/// <para>★ 오늘 확인할 것은 두 가지다 —
/// (1) die 별 결함이 <b>하나도 안 새고</b> 꼬리표가 제대로 붙는가,
/// (2) 절대 좌표가 <b>die 원점 + 정렬 변환</b>으로 한 곳에서만 계산되는가.</para>
/// </summary>
public sealed class DefectCollectorTests
{
    private static Defect D(int id, double cx, double cy)
        => new()
        {
            Id = id,
            Bounds = new BoundingBox((int)cx - 1, (int)cy - 1, 3, 3),
            Area = 9,
            Centroid = new PointF(cx, cy),
        };

    private static DefectList List(params Defect[] defects) => new() { Items = defects };

    /// <summary>die 하나의 검사 결과를 손으로 만든다 — 러너를 안 거쳐도 된다.</summary>
    private static DieInspection Insp(
        int col, int row, Roi roi, DefectList defects,
        WaferZone zone = WaferZone.E1, int goldens = 2, string? failure = null)
        => new()
        {
            Die = new DieOrigin(col, row, new PointF(roi.X, roi.Y), zone),
            Roi = roi,
            Defects = defects,
            GoldenCount = goldens,
            Failure = failure,
        };

    private static DieGrid Grid(int cols = 3, int rows = 2, WaferAlignment? alignment = null)
        => DieGridBuilder.Build(
            new WaferSpec { Cols = cols, Rows = rows, PitchX = 100, PitchY = 100 }, alignment);

    private static WaferAlignment Align(double rotationDeg = 0, double scale = 1, double x = 0, double y = 0)
        => new() { Origin = new PointF(x, y), RotationDeg = rotationDeg, Scale = scale, MeanScore = 1.0 };

    /// <summary>모으기를 안 거치고 꼬리표 붙은 결함 하나를 바로 만든다.</summary>
    private static WaferDefectList Tagged(int col, int row, double cx, double cy, WaferZone zone = WaferZone.E1)
        => new()
        {
            Items = [new WaferDefect { DieCol = col, DieRow = row, Zone = zone, Defect = D(1, cx, cy) }],
        };

    // ─────────────────────────────── 모으기

    [Fact]
    public void 모든_die_의_결함이_하나도_안_샌다()
    {
        List<DieInspection> perDie =
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), List(D(1, 10, 10))),
            Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 20, 20), D(2, 30, 30))),
            Insp(2, 0, new Roi(200, 0, 100, 100), DefectList.Empty),
        ];

        Assert.Equal(3, DefectCollector.Collect(perDie).Count);
    }

    [Fact]
    public void 어느_die_의_것인지_꼬리표가_붙는다()
    {
        List<DieInspection> perDie =
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), List(D(1, 10, 10))),
            Insp(3, 2, new Roi(300, 200, 100, 100), List(D(1, 20, 20))),
        ];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Equal((0, 0), (result.Items[0].DieCol, result.Items[0].DieRow));
        Assert.Equal((3, 2), (result.Items[1].DieCol, result.Items[1].DieRow));
    }

    [Fact]
    public void 존도_같이_붙는다()
    {
        List<DieInspection> perDie =
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), List(D(1, 10, 10)), WaferZone.E0, goldens: 0),
            Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 20, 20))),
        ];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Single(result.E0);
        Assert.Single(result.E1);
    }

    /// <summary>★ 원본 결함은 손대지 않는다 — ROI 상대 좌표 그대로여야 한다.</summary>
    [Fact]
    public void 원본_결함은_그대로_보존된다()
    {
        Defect original = D(7, 42.5, 13.25);
        List<DieInspection> perDie = [Insp(1, 1, new Roi(100, 100, 100, 100), List(original))];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Same(original, result.Items[0].Defect);
        Assert.Equal(42.5, result.Items[0].Defect.Centroid.X);
    }

    /// <summary>★ 모으기 단계에서는 절대 좌표를 안 채운다 — 변환은 한 곳에서만 한다.</summary>
    [Fact]
    public void 모으기만_해서는_절대좌표가_비어_있다()
    {
        List<DieInspection> perDie = [Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 20, 20)))];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Null(result.Items[0].AbsX);
        Assert.Null(result.Items[0].AbsY);
    }

    [Fact]
    public void 러너가_준_순서를_지킨다()
    {
        List<DieInspection> perDie =
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), List(D(1, 1, 1))),
            Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 2, 2))),
            Insp(0, 1, new Roi(0, 100, 100, 100), List(D(1, 3, 3))),
        ];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Equal([(0, 0), (1, 0), (0, 1)], result.Items.Select(d => (d.DieCol, d.DieRow)));
    }

    /// <summary>★ 터진 die 는 결함 0개로 들어온다 — 여기서는 깨끗한 die 와 구분이 안 된다.</summary>
    [Fact]
    public void 터진_die_는_결함_0개로_들어온다()
    {
        List<DieInspection> perDie =
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), DefectList.Empty, failure: "터졌다"),
            Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 20, 20))),
        ];

        WaferDefectList result = DefectCollector.Collect(perDie);

        Assert.Single(result.Items);
        Assert.Equal(1, result.Items[0].DieCol);
    }

    [Fact]
    public void die_가_없으면_빈_목록()
        => Assert.Equal(0, DefectCollector.Collect([]).Count);

    [Fact]
    public void 결함이_하나도_없으면_빈_목록()
    {
        List<DieInspection> perDie = [Insp(0, 0, new Roi(0, 0, 100, 100), DefectList.Empty)];

        Assert.Equal(0, DefectCollector.Collect(perDie).Count);
    }

    [Fact]
    public void 모으기는_null_을_거부한다()
        => Assert.Throws<ArgumentNullException>(() => DefectCollector.Collect(null!));

    // ─────────────────────────────── 좌표 변환

    [Fact]
    public void die_원점을_더해서_절대좌표가_된다()
    {
        WaferDefectList result = AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align());

        Assert.Equal(120.0, result.Items[0].AbsX);   // die(1,0) 원점 100 + 20
        Assert.Equal(30.0, result.Items[0].AbsY);
    }

    [Fact]
    public void 세로_자리도_더해진다()
    {
        WaferDefectList result = AbsoluteTransform.Transform(Tagged(2, 1, 5, 7), Grid(), Align());

        Assert.Equal((205.0, 107.0), (result.Items[0].AbsX, result.Items[0].AbsY));
    }

    [Fact]
    public void 소수점_중심도_그대로_더해진다()
    {
        WaferDefectList result = AbsoluteTransform.Transform(Tagged(1, 0, 20.25, 30.5), Grid(), Align());

        Assert.Equal(120.25, result.Items[0].AbsX);
        Assert.Equal(30.5, result.Items[0].AbsY);
    }

    /// <summary>★ 정렬 이동분은 die 원점에 이미 들어 있다 — 여기서 또 더하면 두 번 밀린다.</summary>
    [Fact]
    public void 정렬_이동은_die_원점에_한_번만_들어간다()
    {
        DieGrid grid = Grid(alignment: Align(x: 8, y: 5));

        WaferDefectList result = AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), grid, Align(x: 8, y: 5));

        Assert.Equal(128.0, result.Items[0].AbsX);   // 100 + 8 + 20.  8 이 두 번 들어가면 136 이다
        Assert.Equal(35.0, result.Items[0].AbsY);
    }

    /// <summary>
    /// ★ 확대율은 <b>die 원점에도 결함 오프셋에도</b> 걸린다.
    /// 한쪽만 걸면 die 위치는 늘어나는데 die 안쪽은 안 늘어나는 불일치가 생긴다.
    /// </summary>
    [Fact]
    public void 확대율이_결함_오프셋에도_걸린다()
    {
        DieGrid grid = Grid(alignment: Align(scale: 2));

        WaferDefectList result = AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), grid, Align(scale: 2));

        Assert.Equal(240.0, result.Items[0].AbsX);   // 원점 200 + 20×2
        Assert.Equal(60.0, result.Items[0].AbsY);    //        0 + 30×2
    }

    /// <summary>★ warp read 가 붙기 전까지의 가드 — 조용히 두 배로 도는 것을 막는다.</summary>
    [Fact]
    public void 기울기가_크면_거부한다()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align(rotationDeg: 0.5)));

        Assert.Contains("warp", ex.Message);
    }

    [Fact]
    public void 반대쪽으로_기울어도_거부한다()
        => Assert.Throws<InvalidOperationException>(
            () => AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align(rotationDeg: -0.5)));

    [Fact]
    public void 임계_안쪽_기울기는_통과한다()
    {
        WaferDefectList result =
            AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align(rotationDeg: 0.04));

        Assert.NotNull(result.Items[0].AbsX);
    }

    /// <summary>★ 원본은 그대로 남는다 — 절대 좌표는 덮어쓰는 게 아니라 덧붙이는 것이다.</summary>
    [Fact]
    public void 원본_상대좌표는_안_지워진다()
    {
        WaferDefectList result = AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align());

        Assert.Equal(20.0, result.Items[0].Defect.Centroid.X);
        Assert.Equal(120.0, result.Items[0].AbsX);
    }

    /// <summary>★ 두 번 걸어도 같은 답 — 원본에서 매번 새로 계산하기 때문이다.</summary>
    [Fact]
    public void 두_번_변환해도_값이_안_밀린다()
    {
        WaferDefectList once = AbsoluteTransform.Transform(Tagged(1, 0, 20, 30), Grid(), Align());

        WaferDefectList twice = AbsoluteTransform.Transform(once, Grid(), Align());

        Assert.Equal(120.0, twice.Items[0].AbsX);
    }

    [Fact]
    public void 꼬리표와_존은_변환해도_그대로()
    {
        WaferDefectList result =
            AbsoluteTransform.Transform(Tagged(2, 1, 5, 5, WaferZone.E0), Grid(), Align());

        Assert.Equal((2, 1), (result.Items[0].DieCol, result.Items[0].DieRow));
        Assert.Equal(WaferZone.E0, result.Items[0].Zone);
    }

    [Fact]
    public void 여러_die_가_각자_제_자리를_받는다()
    {
        WaferDefectList list = DefectCollector.Collect(
        [
            Insp(0, 0, new Roi(0, 0, 100, 100), List(D(1, 10, 10))),
            Insp(1, 0, new Roi(100, 0, 100, 100), List(D(1, 10, 10))),
            Insp(0, 1, new Roi(0, 100, 100, 100), List(D(1, 10, 10))),
        ]);

        WaferDefectList result = AbsoluteTransform.Transform(list, Grid(), Align());

        Assert.Equal([(10.0, 10.0), (110.0, 10.0), (10.0, 110.0)],
            result.Items.Select(d => (d.AbsX!.Value, d.AbsY!.Value)));
    }

    /// <summary>★ 검사할 때와 다른 격자를 넘기면 멈춘다 — 그럴듯하지만 틀린 좌표를 내느니.</summary>
    [Fact]
    public void 격자에_없는_die_는_거부한다()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => AbsoluteTransform.Transform(Tagged(9, 9, 10, 10), Grid(), Align()));

        Assert.Contains("c9", ex.Message);
    }

    [Fact]
    public void 빈_목록은_빈_채로_나온다()
        => Assert.Equal(0, AbsoluteTransform.Transform(WaferDefectList.Empty, Grid(), Align()).Count);

    [Fact]
    public void 변환도_null_을_거부한다()
    {
        Assert.Throws<ArgumentNullException>(
            () => AbsoluteTransform.Transform(null!, Grid(), Align()));
        Assert.Throws<ArgumentNullException>(
            () => AbsoluteTransform.Transform(WaferDefectList.Empty, null!, Align()));
        Assert.Throws<ArgumentNullException>(
            () => AbsoluteTransform.Transform(WaferDefectList.Empty, Grid(), null!));
    }
}