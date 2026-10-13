using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Viewer;

namespace DiePipeline.Tests;

/// <summary>
/// 원본 이미지 뷰어의 계산 — 화면 ↔ 원본 좌표 · 커서 기준 확대 · 축소본 배율 · 보이는 영역. ★ 화면 없이 숫자만 본다.
/// </summary>
public sealed class ViewerGeometryTests
{
    /// <summary>화면 ↔ 원본을 오가면 제자리 — 배율 2 · 화면 왼쪽 위 = 원본 (100, 50).</summary>
    [Fact]
    public void 화면과_원본_좌표는_서로_되돌아간다()
    {
        ViewportTransform t = new(100, 50, 2);

        Assert.Equal(new PointF(105, 60), t.ToOriginal(10, 20));
        Assert.Equal(new PointF(10, 20), t.ToScreen(105, 60));
    }

    /// <summary>★ 커서 기준 확대 — 확대한 뒤에도 커서 아래 원본 점이 같은 화면 자리에 있다.</summary>
    [Fact]
    public void 커서_아래_점은_확대해도_그_자리()
    {
        ViewportTransform before = new(100, 50, 0.5);
        PointF under = before.ToOriginal(300, 200);

        ViewportTransform after = before.ZoomAtScreen(300, 200, 4);

        Assert.Equal(4, after.Scale);
        Assert.Equal(under, after.ToOriginal(300, 200));
    }

    /// <summary>화면 맞춤 배율 — 가로 · 세로 중 더 빠듯한 쪽.</summary>
    [Fact]
    public void 맞춤_배율은_빠듯한_쪽()
    {
        Assert.Equal(0.02, ViewportTransform.FitScale(50_000, 40_000, 1000, 900), precision: 9);
        Assert.Equal(2.0, ViewportTransform.FitScale(400, 300, 800, 900), precision: 9);
    }

    /// <summary>★ 축소 배율 — 3,200만 px 이하는 그대로(1), 넘으면 긴 변이 4096 이하가 되게 올림.</summary>
    [Theory]
    [InlineData(500, 400, 1)]
    [InlineData(8000, 4000, 1)]
    [InlineData(8000, 4001, 2)]
    [InlineData(50_000, 40_000, 13)]
    public void 축소_배율(int width, int height, int factor)
    {
        Assert.Equal(factor, ViewportPlanner.ProxyFactor(width, height));
    }

    /// <summary>축소본 1 px 이 화면에서 1 px 보다 커지면 선명 조각이 필요하다.</summary>
    [Fact]
    public void 흐려지면_원본이_필요하다()
    {
        Assert.False(ViewportPlanner.NeedsOriginal(13, 0.05));
        Assert.True(ViewportPlanner.NeedsOriginal(13, 0.1));
        Assert.False(ViewportPlanner.NeedsOriginal(1, 1.0));
    }

    /// <summary>보이는 영역은 그림 안으로 잘라 맞춘다 — 그림 밖까지 보이면 그림 끝까지만, 그림 밖만 보이면 빈 영역.</summary>
    [Fact]
    public void 보이는_영역은_그림_안으로()
    {
        Assert.Equal(new Roi(100, 50, 400, 300), ViewportPlanner.VisibleRegion(new ViewportTransform(100, 50, 2), 800, 600, 10_000, 10_000));
        Assert.Equal(new Roi(0, 0, 300, 200), ViewportPlanner.VisibleRegion(new ViewportTransform(-50, -50, 1), 1000, 1000, 300, 200));
        Assert.Equal(0, ViewportPlanner.VisibleRegion(new ViewportTransform(500, 500, 1), 100, 100, 300, 200).Width);
    }

    /// <summary>그린 사각형을 그림 안으로 — 음수 · 그림 밖으로 넘친 쪽을 잘라 낸다.</summary>
    [Fact]
    public void 사각형_맞추기()
    {
        Assert.Equal(new Roi(0, 10, 50, 40), ViewportPlanner.Clamp(-20, 10, 70, 40, 300, 200));
        Assert.Equal(new Roi(250, 150, 50, 50), ViewportPlanner.Clamp(250, 150, 100, 100, 300, 200));
    }

    /// <summary>★ 선명 조각 — 원본 그대로면 없음, 축소본이 아직 선명해도 없음, 너무 넓으면 없음, 아니면 보이는 영역.</summary>
    [Fact]
    public void 선명_조각은_필요할_때만()
    {
        const int W = 50_000, H = 40_000, F = 13;

        Assert.Null(ViewportPlanner.SharpRegion(new ViewportTransform(0, 0, 4), 800, 600, 500, 400, proxyFactor: 1));
        Assert.Null(ViewportPlanner.SharpRegion(new ViewportTransform(0, 0, 0.05), 800, 600, W, H, F));
        Assert.Null(ViewportPlanner.SharpRegion(new ViewportTransform(0, 0, 0.1), 1000, 1000, W, H, F));   // 10000 × 10000 — 너무 넓다
        Assert.Equal(new Roi(1000, 2000, 2000, 1500), ViewportPlanner.SharpRegion(new ViewportTransform(1000, 2000, 0.4), 800, 600, W, H, F));
    }
}