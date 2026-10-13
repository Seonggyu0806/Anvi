using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 웨이퍼 오버레이 — 개요 그림 위의 die 테두리 · 결함 네모. ★ 화면 없이 좌표만 본다.
/// </summary>
public sealed class WaferOverlayTests
{
    // 2 × 1 격자, 피치 100 · die 80. 왼쪽 die 에 결함 하나(웨이퍼 좌표 (150.5, 40) · 크기 20 × 10).
    private static WaferInspectionResult Result(WaferStop stop = WaferStop.None, double? absX = 150.5)
    {
        DieOrigin[] dies =
        [
            new(0, 0, new PointF(100, 0), WaferZone.E1),
            new(1, 0, new PointF(200, 0), WaferZone.Outside),
        ];

        WaferDefect defect = new()
        {
            DieCol = 0,
            DieRow = 0,
            Zone = WaferZone.E1,
            Defect = new Defect { Id = 7, Bounds = new BoundingBox(40, 30, 20, 10), Area = 200, Centroid = new PointF(50, 35) },
            AbsX = absX,
            AbsY = 40,
        };

        return new WaferInspectionResult
        {
            Need = new GoldenContract.Requirement(1, "시험"),
            Alignment = WaferAlignment.Identity,
            Stop = stop,
            Grid = new DieGrid { Dies = dies, Pitch = new PointF(100, 100), Cols = 2, Rows = 1, DieWidth = 80, DieHeight = 80 },
            Found = new WaferDefectList { Items = [defect] },
            Defects = new WaferDefectList { Items = [defect] },
        };
    }

    /// <summary>die 테두리 = 실제로 잘라 읽는 자리(<c>RoiOf</c>) · 색은 맵과 같은 상태.</summary>
    [Fact]
    public void die_테두리는_읽는_자리와_맵_상태()
    {
        WaferOverlayLayout layout = WaferOverlay.Of(Result(), overviewPixel: 1);

        Assert.Equal([new Roi(100, 0, 80, 80), new Roi(200, 0, 80, 80)], layout.Dies.Select(d => d.Area));
        Assert.Equal([WaferMapState.Defective, WaferMapState.Outside], layout.Dies.Select(d => d.State));
    }

    /// <summary>결함 네모의 중심 = 결함의 웨이퍼 좌표. 크기는 결함 크기.</summary>
    [Fact]
    public void 결함_네모는_웨이퍼_좌표가_중심()
    {
        OverlayDefect box = WaferOverlay.Of(Result(), overviewPixel: 1).Defects.Single();

        Assert.Equal((140.5, 35.0, 20.0, 10.0), (box.X, box.Y, box.Width, box.Height));
        Assert.Equal(7, box.Defect.Defect.Id);
    }

    /// <summary>★ 원본이 개요보다 크면 네모를 개요 기준 8 px 이상으로 — 개요 1 px = 원본 10 px 이면 80 px.</summary>
    [Fact]
    public void 작은_결함은_개요에서_8px_이상()
    {
        OverlayDefect box = WaferOverlay.Of(Result(), overviewPixel: 10).Defects.Single();

        Assert.Equal((80.0, 80.0), (box.Width, box.Height));
        Assert.Equal((150.5, 40.0), (box.X + (box.Width / 2), box.Y + (box.Height / 2)));
    }

    /// <summary>웨이퍼 좌표가 없는 결함(변환 전)은 그리지 않는다 — 0 으로 그리면 원점에 있는 결함처럼 보인다.</summary>
    [Fact]
    public void 웨이퍼_좌표가_없으면_안_그린다()
    {
        Assert.Empty(WaferOverlay.Of(Result(absX: null), overviewPixel: 1).Defects);
    }

    /// <summary>★ 멈췄으면 아무것도 안 그린다 — 빈 그림은 '깨끗한 웨이퍼' 로 읽힌다.</summary>
    [Fact]
    public void 멈췄으면_비었다()
    {
        WaferOverlayLayout layout = WaferOverlay.Of(Result(WaferStop.AlignmentRejected), overviewPixel: 1);

        Assert.Empty(layout.Dies);
        Assert.Empty(layout.Defects);
    }
}