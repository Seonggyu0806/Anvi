using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 골든으로 쓸 이웃 고르기. ★ 오늘 확인할 것은 세 가지다.
///
/// <list type="number">
/// <item><b>누구를 고르나</b> — 전략마다 다르고, 가로를 먼저 본다</item>
/// <item><b>순서가 늘 같은가</b> — 흔들리면 같은 웨이퍼가 다른 답을 낸다</item>
/// <item><b>가장자리에서 ROI 크기가 안 줄어드나</b> — 줄어들면 겹칠 수가 없다</item>
/// </list>
///
/// 이미지도 파일도 안 쓴다. 격자만 보는 순수 계산이다.
/// </summary>
public sealed class GoldenSelectorTests
{
    private static DieGrid Grid(int cols, int rows, int rings = 0)
        => DieGridBuilder.Build(new WaferSpec
        { Cols = cols, Rows = rows, PitchX = 100, PitchY = 100, EdgeRings = rings });

    /// <summary>고른 이웃을 (열,행) 쌍으로 펴서 비교하기 쉽게.</summary>
    private static (int Col, int Row)[] Where(IReadOnlyList<DieOrigin> dies)
        => dies.Select(d => (d.Col, d.Row)).ToArray();

    // ─────────────────────────────── SameRow1 (교수님 방식)

    [Fact]
    public void 같은행1칸은_좌우_두_장을_고른다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.SameRow1);

        // ★ 왼쪽이 먼저다. 순서를 고정해야 Mean 의 덧셈 순서가 안 흔들린다.
        Assert.Equal([(1, 2), (3, 2)], Where(picked));
    }

    [Fact]
    public void 같은행1칸은_위아래를_안_본다()
    {
        // ★ 3주차 측정 — 같은 행 1칸 0.0368 < 같은 열 1칸 0.0520. 가로가 더 닮았다.
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.SameRow1);

        Assert.All(picked, d => Assert.Equal(2, d.Row));
    }

    [Fact]
    public void 왼쪽_끝에서는_한_장뿐이다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(0, 2), NeighborStrategy.SameRow1);

        Assert.Equal([(1, 2)], Where(picked));
    }

    [Fact]
    public void 오른쪽_끝에서도_한_장뿐이다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(4, 2), NeighborStrategy.SameRow1);

        Assert.Equal([(3, 2)], Where(picked));
    }

    [Fact]
    public void 한_열짜리_격자면_이웃이_없다()
    {
        // ★ 빈 목록을 돌려준다. 이걸 Combine 에 넘기면 안 되므로 러너가 걸러야 한다.
        DieGrid grid = Grid(1, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(0, 2), NeighborStrategy.SameRow1);

        Assert.Empty(picked);
    }

    // ─────────────────────────────── Nearest (Lesson16 방식)

    [Fact]
    public void 가까운순은_좌_우_상_순서로_채운다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 3);

        // ★ 거리 1 안에서 좌 → 우 → 상 순서. 세 장을 채웠으니 '하'까지 안 간다.
        Assert.Equal([(1, 2), (3, 2), (2, 1)], Where(picked));
    }

    [Fact]
    public void 네_장을_달라면_아래까지_간다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 4);

        Assert.Equal([(1, 2), (3, 2), (2, 1), (2, 3)], Where(picked));
    }

    [Fact]
    public void 모자라면_거리를_늘려서_채운다()
    {
        // ★ 이게 Nearest 의 핵심이다. 한 행짜리 격자의 맨 왼쪽 die 라 좌·상·하가 전부 없는데도
        //   오른쪽으로 거리를 늘려 3장을 채운다. 3주차에 끝 열에서 오검 0 이 나온 이유다.
        DieGrid grid = Grid(5, 1);

        var picked = GoldenSelector.Pick(grid, grid.At(0, 0), NeighborStrategy.Nearest, want: 3);

        Assert.Equal([(1, 0), (2, 0), (3, 0)], Where(picked));
    }

    [Fact]
    public void 격자보다_많이_달라면_있는_만큼만_준다()
    {
        DieGrid grid = Grid(3, 1);

        var picked = GoldenSelector.Pick(grid, grid.At(0, 0), NeighborStrategy.Nearest, want: 10);

        Assert.Equal(2, picked.Count);
    }

    [Fact]
    public void 한_장만_달라면_제일_닮은_것을_준다()
    {
        DieGrid grid = Grid(5, 5);

        var picked = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 1);

        Assert.Equal([(1, 2)], Where(picked));
    }

    // ─────────────────────────────── 결정론

    [Fact]
    public void 두_번_불러도_순서가_같다()
    {
        // ★ 골든이 이 목록에서 만들어지므로, 순서가 흔들리면 같은 웨이퍼가 다른 답을 낸다.
        DieGrid grid = Grid(5, 5);

        var first = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 4);
        var second = GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 4);

        Assert.Equal(Where(first), Where(second));
    }

    // ─────────────────────────────── 변두리는 골든이 안 된다

    [Fact]
    public void 변두리_die_는_이웃으로_안_쓴다()
    {
        // ★ E0 는 자기도 제대로 검사를 못 받은 die 다.
        //   그런 걸 골든 재료로 쓰면 '못 믿을 것으로 정상을 정의'하는 셈이다.
        DieGrid grid = Grid(5, 5, rings: 1);

        // (1,1) 은 E1 이고, 왼쪽 (0,1) 은 테두리라 E0 다.
        var picked = GoldenSelector.Pick(grid, grid.At(1, 1), NeighborStrategy.SameRow1);

        Assert.Equal([(2, 1)], Where(picked));
    }

    [Fact]
    public void 변두리_die_에_골든을_달라면_멈춘다()
    {
        // ★ 조용히 빈 목록을 주면 Combine 이 0장을 받아 이상한 답을 낸다. 소리 내며 멈춘다.
        DieGrid grid = Grid(5, 5, rings: 1);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => GoldenSelector.Pick(grid, grid.At(0, 0), NeighborStrategy.SameRow1));

        Assert.Contains("E0", ex.Message);
    }

    // ─────────────────────────────── 거부해야 하는 것

    [Fact]
    public void 격자_밖_die_는_거부한다()
    {
        DieGrid grid = Grid(5, 5);
        DieOrigin outside = new(99, 99, new PointF(0, 0), WaferZone.E1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GoldenSelector.Pick(grid, outside, NeighborStrategy.SameRow1));
    }

    [Fact]
    public void 방법을_안_정하면_거부한다()
    {
        // ★ Unknown 이 조용히 한쪽으로 쏠리면 "왜 이웃이 2장이지" 를 못 찾는다.
        DieGrid grid = Grid(5, 5);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Unknown));
    }

    [Fact]
    public void 이웃을_영_장_달라면_거부한다()
    {
        DieGrid grid = Grid(5, 5);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GoldenSelector.Pick(grid, grid.At(2, 2), NeighborStrategy.Nearest, want: 0));
    }

    [Fact]
    public void 격자가_없으면_거부한다()
    {
        DieOrigin die = new(0, 0, new PointF(0, 0), WaferZone.E1);

        Assert.Throws<ArgumentNullException>(
            () => GoldenSelector.Pick(null!, die, NeighborStrategy.SameRow1));
    }

    // ─────────────────────────────── ROI 로 바꾸기

    [Fact]
    public void 안쪽_이웃은_원점_그대로_잘린다()
    {
        DieOrigin[] neighbours = [new(1, 0, new PointF(300, 500), WaferZone.E1)];

        var rois = GoldenSelector.RoisOf(neighbours, new Roi(0, 0, 200, 200), 1000, 1000);

        Assert.Equal(new Roi(300, 500, 200, 200), rois[0]);
    }

    [Fact]
    public void 오른쪽으로_넘치면_잘라내지_않고_밀어_넣는다()
    {
        // ★★ 여기가 제일 중요하다.
        //   900 + 200 = 1100 으로 이미지(1000) 를 넘는다. 잘라서 100 폭으로 만들면
        //   검사본(200) 과 크기가 달라져 겹칠 수가 없고, Combine 이 그 die 를 통째로 건너뛴다.
        //   그래서 폭을 줄이지 말고 자리를 800 으로 옮긴다.
        DieOrigin[] neighbours = [new(4, 0, new PointF(900, 0), WaferZone.E1)];

        var rois = GoldenSelector.RoisOf(neighbours, new Roi(0, 0, 200, 200), 1000, 1000);

        Assert.Equal(new Roi(800, 0, 200, 200), rois[0]);
    }

    [Fact]
    public void 음수_자리는_영으로_밀어_넣는다()
    {
        DieOrigin[] neighbours = [new(0, 0, new PointF(-50, -30), WaferZone.E1)];

        var rois = GoldenSelector.RoisOf(neighbours, new Roi(0, 0, 200, 200), 1000, 1000);

        Assert.Equal(new Roi(0, 0, 200, 200), rois[0]);
    }

    [Fact]
    public void 골든_ROI_는_전부_검사_ROI_와_같은_크기다()
    {
        // ★ 어디에 있든 크기는 같아야 한다. 이게 깨지면 die 하나가 조용히 검사에서 빠진다.
        DieOrigin[] neighbours =
        [
            new(0, 0, new PointF(-50, 0), WaferZone.E1),
            new(1, 0, new PointF(400, 400), WaferZone.E1),
            new(2, 0, new PointF(980, 980), WaferZone.E1),
        ];
        Roi test = new(0, 0, 200, 200);

        var rois = GoldenSelector.RoisOf(neighbours, test, 1000, 1000);

        Assert.All(rois, r => Assert.Equal((test.Width, test.Height), (r.Width, r.Height)));
    }

    [Fact]
    public void 원점은_반올림해서_정수_자리로_간다()
    {
        // 정렬을 곱하면 원점이 소수가 된다. 자를 때는 정수라야 한다.
        DieOrigin[] neighbours = [new(1, 0, new PointF(299.6, 500.4), WaferZone.E1)];

        var rois = GoldenSelector.RoisOf(neighbours, new Roi(0, 0, 200, 200), 1000, 1000);

        Assert.Equal(new Roi(300, 500, 200, 200), rois[0]);
    }

    [Fact]
    public void 이미지가_검사_ROI_보다_작으면_거부한다()
    {
        DieOrigin[] neighbours = [new(0, 0, new PointF(0, 0), WaferZone.E1)];

        Assert.Throws<ArgumentException>(
            () => GoldenSelector.RoisOf(neighbours, new Roi(0, 0, 200, 200), 100, 100));
    }

    [Fact]
    public void 이웃이_없으면_ROI_도_없다()
    {
        var rois = GoldenSelector.RoisOf([], new Roi(0, 0, 200, 200), 1000, 1000);

        Assert.Empty(rois);
    }
}