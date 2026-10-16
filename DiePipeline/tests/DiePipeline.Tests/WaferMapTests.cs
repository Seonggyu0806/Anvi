using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 맵 칸 — 칸마다 색(상태)과 결함 수. ★ 화면 없이 결과만 넣어 본다.
/// </summary>
public sealed class WaferMapTests
{
    // 3 × 2 격자. 위 줄: 원판 밖 · E0 · E1, 아래 줄: E1(결함 2) · E0(결함 1) · E1(검사 터짐)
    private static readonly WaferZone[] Zones =
        [WaferZone.Outside, WaferZone.E0, WaferZone.E1, WaferZone.E1, WaferZone.E0, WaferZone.E1];

    private static WaferInspectionResult Result(WaferStop stop = WaferStop.None)
    {
        DieOrigin[] dies = [.. Zones.Select((z, i) => new DieOrigin(i % 3, i / 3, new PointF(i % 3 * 100, i / 3 * 100), z))];

        WaferDefect At(int col, int row, int id) => new()
        {
            DieCol = col,
            DieRow = row,
            Zone = Zones[(row * 3) + col],
            Defect = new Defect { Id = id, Bounds = new BoundingBox(1, 1, 2, 2), Area = 4, Centroid = new PointF(2, 2) },
        };

        return new WaferInspectionResult
        {
            Need = new GoldenContract.Requirement(1, "시험"),
            Alignment = WaferAlignment.Identity,
            Stop = stop,
            Grid = new DieGrid
            {
                Dies = dies,
                Pitch = new PointF(100, 100),
                Cols = 3,
                Rows = 2,
                DieWidth = 100,
                DieHeight = 100,
            },
            Run = new WaferRunResult
            {
                Dies =
                [
                    .. dies.Select(d => new DieInspection
                    {
                        Die = d,
                        Roi = new Roi((int)d.Origin.X, (int)d.Origin.Y, 100, 100),
                        Defects = DefectList.Empty,
                        GoldenCount = 1,
                        Failure = (d.Col, d.Row) == (2, 1) ? "읽기 실패" : null,
                    }),
                ],
            },
            Found = new WaferDefectList { Items = [At(0, 1, 1), At(0, 1, 2), At(1, 1, 3)] },

            // ★ 샘플링 후에는 하나만 남았다고 치자 — 맵은 이것이 아니라 Found 를 센다.
            Defects = new WaferDefectList { Items = [At(0, 1, 1)] },
        };
    }

    /// <summary>칸은 열 × 행 전부, 행 우선(위 줄 왼쪽부터).</summary>
    [Fact]
    public void 칸은_격자_전부를_행_우선으로()
    {
        IReadOnlyList<WaferMapCell> cells = WaferMap.Of(Result());

        Assert.Equal([(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)], cells.Select(c => (c.Col, c.Row)));
    }

    /// <summary>★ 상태 — 원판 밖 · E0 · E1 · 결함 · 터짐. 결함 칸은 존마다 다르다(교수님 맵: E1 결함 · E0 결함 색이 다르다).</summary>
    [Fact]
    public void 칸_상태는_존_결함_터짐으로_정한다()
    {
        IReadOnlyList<WaferMapCell> cells = WaferMap.Of(Result());

        Assert.Equal(
            [WaferMapState.Outside, WaferMapState.E0, WaferMapState.E1, WaferMapState.Defective, WaferMapState.E0Defective, WaferMapState.Failed],
            cells.Select(c => c.State));
    }

    /// <summary>
    /// ★ 사람이 뺀 die(수동 off)는 '제외' 칸 — 원판 밖과 똑같이 검사하지 않았지만 맵에서 숨지 않는다. 글자 '×'.
    ///   격자 만들기부터 본다 — 수동 지정이 die 에 표시로 남아야 맵까지 온다. 사람이 E0 · E1 로 정한 die 는 보통 칸이다.
    /// </summary>
    [Fact]
    public void 수동으로_뺀_die_는_제외_칸()
    {
        WaferSpec spec = new()
        {
            Cols = 3,
            Rows = 2,
            PitchX = 100,
            PitchY = 100,
            ZoneOverrides = [new ZoneOverride(1, 1, WaferZone.Outside), new ZoneOverride(2, 1, WaferZone.E0)],
        };
        DieGrid grid = DieGridBuilder.Build(spec, WaferAlignment.Identity);

        IReadOnlyList<WaferMapCell> pending = WaferMap.Pending(grid);
        WaferMapCell excluded = pending.Single(c => (c.Col, c.Row) == (1, 1));
        WaferMapCell byHandE0 = pending.Single(c => (c.Col, c.Row) == (2, 1));

        Assert.True(grid.At(1, 1).Manual);
        Assert.Equal(WaferMapState.Excluded, excluded.State);
        Assert.Equal("×", excluded.Label);
        Assert.Contains("수동 제외", excluded.Tip, StringComparison.Ordinal);
        Assert.Equal(WaferMapState.Pending, byHandE0.State);
        Assert.Single(pending, c => c.State == WaferMapState.Excluded);
    }

    /// <summary>★ 결함 수는 샘플링 <b>전</b>을 센다 — 판정과 같은 것. 샘플링으로 덜어낸 결함도 맵에서는 안 사라진다.</summary>
    [Fact]
    public void 결함_수는_샘플링_전을_센다()
    {
        IReadOnlyList<WaferMapCell> cells = WaferMap.Of(Result());

        Assert.Equal([0, 0, 0, 2, 1, 0], cells.Select(c => c.Defects));
        Assert.Equal(["", "", "", "2", "1", "!"], cells.Select(c => c.Label));
    }

    /// <summary>★ 멈췄으면 빈 맵 — 빈 칸을 그리면 '깨끗한 웨이퍼' 로 읽힌다.</summary>
    [Fact]
    public void 멈췄으면_칸이_없다()
    {
        Assert.Empty(WaferMap.Of(Result(WaferStop.AlignmentRejected)));
    }
}