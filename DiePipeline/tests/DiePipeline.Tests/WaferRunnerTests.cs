using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 러너. ★ 오늘 확인할 것은 "die 를 빠짐없이 한 번씩 올바른 검사로 보내는가"다.
///
/// <para>러너가 이미지를 안 만지므로 <b>사진 한 장 없이</b> 전체 흐름을 돌린다.
/// 검사 함수 자리에 "무엇을 받았는지 적어 두는 가짜"를 끼워 넣으면 된다.</para>
/// </summary>
public sealed class WaferRunnerTests
{
    private static DieGrid Grid(int cols, int rows, int rings = 0)
        => DieGridBuilder.Build(new WaferSpec
        { Cols = cols, Rows = rows, PitchX = 100, PitchY = 100, EdgeRings = rings });

    private static WaferRunOptions Options(
        NeighborStrategy strategy = NeighborStrategy.SameRow1, int want = 3, int w = 10_000, int h = 10_000)
        => new() { Strategy = strategy, GoldenWanted = want, ImageWidth = w, ImageHeight = h };

    private static DefectList Defects(int count)
        => new()
        {
            Items = Enumerable.Range(1, count).Select(i => new Defect
            {
                Id = i,
                Bounds = new BoundingBox(0, 0, 3, 3),
                Area = 9,
                Centroid = new PointF(1, 1),
            }).ToArray(),
        };

    /// <summary>검사 함수 자리에 끼우는 가짜 — 무엇을 받았는지 적어 둔다.</summary>
    private sealed class Recorder
    {
        public List<(Roi Roi, int Goldens)> E1 { get; } = [];

        public List<Roi> E0 { get; } = [];

        public DefectList InspectE1(Roi roi, IReadOnlyList<Roi> goldens)
        {
            E1.Add((roi, goldens.Count));
            return DefectList.Empty;
        }

        public DefectList InspectE0(Roi roi)
        {
            E0.Add(roi);
            return DefectList.Empty;
        }
    }

    // ─────────────────────────────── die 루프

    [Fact]
    public void 모든_die_를_한_번씩_돈다()
    {
        Recorder log = new();

        WaferRunResult result = WaferRunner.Run(Grid(4, 8), log.InspectE1, null, Options());

        Assert.Equal(32, result.DieCount);
        Assert.Equal(32, log.E1.Count);
    }

    [Fact]
    public void 순서는_왼쪽에서_오른쪽_위에서_아래다()
    {
        // ★ 순서가 흔들리면 같은 웨이퍼가 두 번 돌 때 결과가 달라진다.
        WaferRunResult result = WaferRunner.Run(Grid(3, 2), new Recorder().InspectE1, null, Options());

        var order = result.Dies.Select(d => (d.Die.Col, d.Die.Row)).ToArray();

        Assert.Equal([(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)], order);
    }

    [Fact]
    public void 검사_자리는_격자가_말하는_자리다()
    {
        Recorder log = new();

        WaferRunner.Run(Grid(3, 2), log.InspectE1, null, Options());

        // die(1,0) → 원점 (100,0), 크기는 피치와 같다
        Assert.Equal(new Roi(100, 0, 100, 100), log.E1[1].Roi);
    }

    // ─────────────────────────────── 골든

    [Fact]
    public void 가운데_die_는_골든_두_장을_받는다()
    {
        Recorder log = new();

        WaferRunner.Run(Grid(3, 1), log.InspectE1, null, Options());

        Assert.Equal(2, log.E1[1].Goldens);   // die(1,0) 은 좌우가 다 있다
    }

    [Fact]
    public void 끝_열은_골든_한_장뿐이다()
    {
        Recorder log = new();

        WaferRunner.Run(Grid(3, 1), log.InspectE1, null, Options());

        Assert.Equal(1, log.E1[0].Goldens);   // die(0,0) 은 왼쪽이 없다
        Assert.Equal(1, log.E1[2].Goldens);   // die(2,0) 은 오른쪽이 없다
    }

    [Fact]
    public void 가까운순은_거리를_늘려_원하는_만큼_채운다()
    {
        Recorder log = new();

        WaferRunner.Run(Grid(5, 1), log.InspectE1, null, Options(NeighborStrategy.Nearest, want: 3));

        // 맨 왼쪽 die 도 오른쪽으로 거리를 늘려 3장을 채운다
        Assert.Equal(3, log.E1[0].Goldens);
    }

    [Fact]
    public void 골든을_몇_장_썼는지_결과에_남는다()
    {
        // ★ 나중에 "골든 1장짜리 die 에서만 결함이 많나" 를 물을 수 있어야 한다.
        WaferRunResult result = WaferRunner.Run(Grid(3, 1), new Recorder().InspectE1, null, Options());

        Assert.Equal([1, 2, 1], result.Dies.Select(d => d.GoldenCount));
    }

    // ─────────────────────────────── 존 분기

    [Fact]
    public void 변두리_die_는_골든_없는_검사로_간다()
    {
        Recorder log = new();

        WaferRunner.Run(Grid(5, 5, rings: 1), log.InspectE1, log.InspectE0, Options());

        Assert.Equal(9, log.E1.Count);    // 안쪽 3×3
        Assert.Equal(16, log.E0.Count);   // 테두리 한 겹
    }

    [Fact]
    public void 변두리_die_는_골든이_영_장이다()
    {
        WaferRunResult result = WaferRunner.Run(
            Grid(5, 5, rings: 1), new Recorder().InspectE1, new Recorder().InspectE0, Options());

        Assert.All(result.Dies.Where(d => d.Die.Zone == WaferZone.E0),
            d => Assert.Equal(0, d.GoldenCount));
    }

    [Fact]
    public void 변두리가_있는데_그_검사법을_안_주면_멈춘다()
    {
        // ★ 조용히 건너뛰면 "결함 0개"로 보이지만 사실은 안 본 것이다. 소리 내며 멈춘다.
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => WaferRunner.Run(Grid(5, 5, rings: 1), new Recorder().InspectE1, null, Options()));

        Assert.Contains("E0", ex.Message);
    }

    [Fact]
    public void 변두리가_없으면_그_검사법이_없어도_된다()
    {
        // rings 0 = 전부 E1. 지금 우리 데이터가 이 상태다.
        WaferRunResult result = WaferRunner.Run(Grid(4, 8), new Recorder().InspectE1, null, Options());

        Assert.Equal(32, result.DieCount);
        Assert.Empty(result.Failures);
    }

    // ─────────────────────────────── 실패해도 계속 돈다

    [Fact]
    public void die_하나가_터져도_나머지는_돈다()
    {
        int seen = 0;
        DefectList Boom(Roi roi, IReadOnlyList<Roi> goldens)
        {
            seen++;
            return seen == 2 ? throw new InvalidDataException("읽기 실패") : DefectList.Empty;
        }

        WaferRunResult result = WaferRunner.Run(Grid(4, 1), Boom, null, Options());

        Assert.Equal(4, result.DieCount);       // 네 개 다 결과가 있다
        Assert.Single(result.Failures);         // 그중 하나만 실패
    }

    [Fact]
    public void 터진_die_는_까닭이_남는다()
    {
        DefectList Boom(Roi roi, IReadOnlyList<Roi> goldens) => throw new InvalidDataException("읽기 실패");

        WaferRunResult result = WaferRunner.Run(Grid(2, 1), Boom, null, Options());

        Assert.All(result.Dies, d => Assert.Contains("읽기 실패", d.Failure));
        Assert.All(result.Dies, d => Assert.Empty(d.Defects.Items));
    }

    [Fact]
    public void 취소는_삼키지_않는다()
    {
        // ★ catch(Exception) 이 취소를 먹으면 "취소를 눌렀는데 계속 도는" 상태가 된다.
        DefectList Cancel(Roi roi, IReadOnlyList<Roi> goldens) => throw new OperationCanceledException();

        Assert.Throws<OperationCanceledException>(
            () => WaferRunner.Run(Grid(4, 1), Cancel, null, Options()));
    }

    [Fact]
    public void 취소_토큰이_걸리면_멈춘다()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => WaferRunner.Run(Grid(4, 1), new Recorder().InspectE1, null, Options(), cts.Token));
    }

    // ─────────────────────────────── 이미지 경계

    [Fact]
    public void 이미지_밖으로_나간_만큼_검사_자리가_잘린다()
    {
        Recorder log = new();

        // 3칸 × 피치 100 = 300 인데 이미지는 250 까지만 있다
        WaferRunner.Run(Grid(3, 1), log.InspectE1, null, Options(w: 250, h: 100));

        Assert.Equal(new Roi(200, 0, 50, 100), log.E1[2].Roi);   // 마지막 die 가 50 폭으로 잘린다
    }

    [Fact]
    public void 골든은_잘린_검사_자리와_같은_크기다()
    {
        // ★★ 크기가 한 픽셀이라도 다르면 겹칠 수가 없어 그 die 가 통째로 건너뛰어진다.
        Recorder log = new();
        List<Roi> goldens = [];

        DefectList Capture(Roi roi, IReadOnlyList<Roi> g)
        {
            goldens.AddRange(g);
            return DefectList.Empty;
        }

        WaferRunner.Run(Grid(3, 1), Capture, null, Options(w: 250, h: 100));

        Assert.All(goldens, g => Assert.True(g.Width <= 100 && g.Height == 100));
        Assert.All(goldens, g => Assert.True(g.X >= 0 && g.X + g.Width <= 250));
    }

    [Fact]
    public void 이미지_완전히_밖이면_그_die_만_실패로_남는다()
    {
        // 격자나 정렬이 크게 틀어진 경우다. 던지지 않고 기록만 한다.
        Recorder log = new();

        WaferRunResult result = WaferRunner.Run(Grid(3, 1), log.InspectE1, null, Options(w: 150, h: 100));

        Assert.Single(result.Failures);
        Assert.Contains("이미지 밖", result.Failures.Single().Failure);
        Assert.Equal(2, log.E1.Count);   // 나머지 둘은 검사에 갔다
    }

    // ─────────────────────────────── 집계

    [Fact]
    public void 결함을_전부_더해_센다()
    {
        DefectList Two(Roi roi, IReadOnlyList<Roi> goldens) => Defects(2);

        WaferRunResult result = WaferRunner.Run(Grid(4, 1), Two, null, Options());

        Assert.Equal(8, result.DefectCount);
    }

    [Fact]
    public void 요약은_웨이퍼_맵에_찍을_모양이다()
    {
        DefectList One(Roi roi, IReadOnlyList<Roi> goldens) => Defects(1);

        WaferRunResult result = WaferRunner.Run(Grid(2, 1), One, null, Options());
        DieResult summary = result.Dies[0].Summary;

        Assert.Equal(0, summary.Col);
        Assert.Equal(WaferZone.E1, summary.Zone);
        Assert.Equal(1, summary.DefectCount);
    }

    // ─────────────────────────────── 거부해야 하는 것

    [Fact]
    public void 이웃_고르는_법을_안_정하면_거부한다()
    {
        Assert.Throws<ArgumentException>(
            () => WaferRunner.Run(Grid(3, 1), new Recorder().InspectE1, null,
                Options(NeighborStrategy.Unknown)));
    }

    [Fact]
    public void 이미지_크기가_이상하면_거부한다()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WaferRunner.Run(Grid(3, 1), new Recorder().InspectE1, null, Options(w: 0)));
    }

    [Fact]
    public void 격자가_없으면_거부한다()
    {
        Assert.Throws<ArgumentNullException>(
            () => WaferRunner.Run(null!, new Recorder().InspectE1, null, Options()));
    }

    [Fact]
    public void 검사_함수가_없으면_거부한다()
    {
        Assert.Throws<ArgumentNullException>(
            () => WaferRunner.Run(Grid(3, 1), null!, null, Options()));
    }
}