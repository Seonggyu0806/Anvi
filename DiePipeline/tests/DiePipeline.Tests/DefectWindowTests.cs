using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 결함을 원본 해상도로 볼 창. ★ 확인할 것은 "창이 결함을 품는가" 와 "창 안의 결함 네모가 맞는 자리인가" 다.
///
/// 숫자는 전부 지어낸 것이다 — 이미지 없이 계산만 본다.
/// </summary>
public sealed class DefectWindowTests
{
    /// <summary>창 안의 결함 네모를 사진 좌표로 되돌린다.</summary>
    private static (int X, int Y) Back(DefectView view) => (view.Window.X + view.Box.X, view.Window.Y + view.Box.Y);

    /// <summary>★ 작은 결함 — 최소 크기 창의 가운데에 온다. 사진 좌표 = die 를 읽은 자리 + die 안 자리.</summary>
    [Fact]
    public void 결함이_창_가운데에_온다()
    {
        DefectView view = DefectWindow.Around(new Roi(1000, 2000, 500, 500), new BoundingBox(100, 100, 20, 10), 5000, 5000);

        Assert.Equal(new Roi(1046, 2041, 128, 128), view.Window);
        Assert.Equal(new BoundingBox(54, 59, 20, 10), view.Box);
        Assert.Equal((1100, 2100), Back(view));
    }

    [Theory]
    [InlineData(10, 10, 128)]      // 4배 = 40 → 최소 128
    [InlineData(100, 50, 400)]     // 긴 변 100 × 4
    [InlineData(400, 300, 1024)]   // 1600 → 최대 1024
    public void 창은_결함_긴_변의_4배이고_최소_최대가_있다(int width, int height, int side)
    {
        DefectView view = DefectWindow.Around(new Roi(0, 0, 3000, 3000), new BoundingBox(1000, 1000, width, height), 5000, 5000);

        Assert.Equal((side, side), (view.Window.Width, view.Window.Height));
    }

    /// <summary>★ 사진 가장자리 결함 — 창 크기는 그대로, 안쪽으로 민다. 결함은 창 가운데가 아닐 뿐 자리는 맞다.</summary>
    [Fact]
    public void 사진_왼쪽_위_끝에서는_안쪽으로_민다()
    {
        DefectView view = DefectWindow.Around(new Roi(0, 0, 500, 500), new BoundingBox(2, 3, 10, 10), 5000, 5000);

        Assert.Equal(new Roi(0, 0, 128, 128), view.Window);
        Assert.Equal(new BoundingBox(2, 3, 10, 10), view.Box);
    }

    [Fact]
    public void 사진_오른쪽_아래_끝에서도_안쪽으로_민다()
    {
        DefectView view = DefectWindow.Around(new Roi(4500, 4500, 500, 500), new BoundingBox(490, 490, 10, 10), 5000, 5000);

        Assert.Equal(new Roi(5000 - 128, 5000 - 128, 128, 128), view.Window);
        Assert.Equal((4990, 4990), Back(view));
    }

    /// <summary>사진이 창보다 작으면 사진 전체가 창이다.</summary>
    [Fact]
    public void 사진이_창보다_작으면_사진_전체를_본다()
    {
        DefectView view = DefectWindow.Around(new Roi(0, 0, 50, 40), new BoundingBox(20, 10, 5, 5), 50, 40);

        Assert.Equal(new Roi(0, 0, 50, 40), view.Window);
        Assert.Equal((20, 10), Back(view));
    }

    /// <summary>
    /// ★ 왼쪽·위로 잘린 die — 잘라 읽은 영역이 사진 0 에서 시작한다. 결함 좌표도 그 영역 기준이라
    ///   die 원점(사진 밖, 음수)이 아니라 읽은 자리에 더한다. 5주차 잘린 die 좌표 버그와 같은 규칙.
    /// </summary>
    [Fact]
    public void 잘린_die_도_잘라_읽은_자리_기준이다()
    {
        DefectView view = DefectWindow.Around(new Roi(0, 0, 70, 80), new BoundingBox(10, 10, 4, 4), 500, 500);

        Assert.Equal((10, 10), Back(view));
    }

    [Theory]
    [InlineData(0, 5, 100, 100)]
    [InlineData(5, 5, 0, 100)]
    public void 크기가_0_이면_거부한다(int boxWidth, int boxHeight, int imageWidth, int imageHeight)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DefectWindow.Around(
            new Roi(0, 0, 50, 50), new BoundingBox(1, 1, boxWidth, boxHeight), imageWidth, imageHeight));
}