using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Viewer;

namespace DiePipeline.Tests;

/// <summary>
/// 원본 위 영역 그리기의 계산 — 끌기 · 도장 · 적용 전 점검 · 표식 설계 자리 · 결함 표시 · 가운데 두기. ★ 화면 없이 숫자만 본다.
/// </summary>
public sealed class RegionToolTests
{
    /// <summary>거꾸로 끌어도(오른쪽 아래 → 왼쪽 위) 같은 상자.</summary>
    [Fact]
    public void 끌기는_방향과_상관없이_같은_상자()
    {
        Roi? forward = RegionTool.FromDrag(10, 20, 110, 70, 500, 400);
        Roi? backward = RegionTool.FromDrag(110, 70, 10, 20, 500, 400);

        Assert.Equal(new Roi(10, 20, 100, 50), forward);
        Assert.Equal(forward, backward);
    }

    /// <summary>그림 밖으로 그어도 그림 안만 남는다. 누르기만 했으면(1 px 미만) 상자가 없다.</summary>
    [Fact]
    public void 끌기는_그림_안만_남고_누르기만_하면_없다()
    {
        Assert.Equal(new Roi(0, 300, 50, 100), RegionTool.FromDrag(-30, 300, 50, 450, 500, 400));
        Assert.Null(RegionTool.FromDrag(40, 40, 40.2, 90, 500, 400));
        Assert.Null(RegionTool.FromDrag(600, 10, 700, 50, 500, 400));
    }

    /// <summary>★ 도장은 크기를 지킨다 — 가장자리에서는 잘라 내지 않고 그림 안으로 밀어 넣는다.</summary>
    [Fact]
    public void 도장은_가장자리에서_크기를_지키며_밀려_들어온다()
    {
        Assert.Equal(new Roi(30, 40, 100, 80), RegionTool.Stamp(30, 40, 100, 80, 500, 400));
        Assert.Equal(new Roi(400, 320, 100, 80), RegionTool.Stamp(470, 390, 100, 80, 500, 400));
        Assert.Equal(new Roi(0, 0, 100, 80), RegionTool.Stamp(-20, -5, 100, 80, 500, 400));
    }

    /// <summary>그림보다 큰 상자만 그림 크기로 줄어든다.</summary>
    [Fact]
    public void 그림보다_큰_도장만_줄어든다()
    {
        Assert.Equal(new Roi(0, 10, 500, 80), RegionTool.Stamp(10, 10, 600, 80, 500, 400));
    }

    /// <summary>적용 전 점검 — 정상 die 없음 · 크기 다름 · 그림 밖은 막고, 맞으면 통과.</summary>
    [Fact]
    public void 다이_영역_점검()
    {
        Roi roi = new(100, 100, 50, 50);

        Assert.Null(RegionTool.CheckDieRegions(roi, [new(0, 0, 50, 50), new(200, 100, 50, 50)], 500, 400));
        Assert.Contains("하나 이상", RegionTool.CheckDieRegions(roi, [], 500, 400));
        Assert.Contains("정상 die 2 크기(40×50)", RegionTool.CheckDieRegions(roi, [new(0, 0, 50, 50), new(200, 100, 40, 50)], 500, 400));
        Assert.Contains("정상 die 1 이 그림 밖", RegionTool.CheckDieRegions(roi, [new(460, 0, 50, 50)], 500, 400));
        Assert.Contains("ROI 가 그림 밖", RegionTool.CheckDieRegions(new Roi(-1, 0, 50, 50), [new(0, 0, 50, 50)], 500, 400));
    }

    /// <summary>정렬 특징은 2개 이상 · 그림 안.</summary>
    [Fact]
    public void 정렬_특징_점검()
    {
        Assert.Null(RegionTool.CheckFeatures([new(10, 10, 20, 20), new(300, 200, 20, 20)], 500, 400));
        Assert.Contains("1개", RegionTool.CheckFeatures([new(10, 10, 20, 20)], 500, 400));
        Assert.Contains("특징 2 이", RegionTool.CheckFeatures([new(10, 10, 20, 20), new(490, 200, 20, 20)], 500, 400));
    }

    /// <summary>★ 표식 설계 자리 = 상자 왼쪽 위 − die(0,0) 원점(교수님 규약). 원점보다 위 · 왼쪽이면 음수.</summary>
    [Fact]
    public void 설계_자리는_왼쪽_위_빼기_원점()
    {
        Assert.Equal(new PointF(-40, -40), RegionTool.Expected(new Roi(60, 80, 20, 20), 100, 120));
        Assert.Equal(new PointF(410, 310), RegionTool.Expected(new Roi(510, 430, 20, 20), 100, 120));
    }

    /// <summary>결함 표시 — 가운데 기준, 작으면 12 px 로 키운다.</summary>
    [Fact]
    public void 결함_표시는_가운데_기준_최소_12()
    {
        Assert.Equal(new MarkerBox(94, 194, 12, 12), RegionTool.Marker(100, 200, 3, 2));
        Assert.Equal(new MarkerBox(80, 190, 40, 20), RegionTool.Marker(100, 200, 40, 20));
    }

    /// <summary>가운데 두기 — 그 점이 화면 가운데 화면 좌표로 간다.</summary>
    [Fact]
    public void 가운데_두면_그_점이_화면_가운데()
    {
        ViewportTransform t = RegionTool.CenteredOn(1234, 567, 2, 800, 600);

        Assert.Equal(new PointF(400, 300), t.ToScreen(1234, 567));
    }
}