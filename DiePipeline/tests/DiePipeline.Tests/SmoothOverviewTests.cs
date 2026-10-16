using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 평균 축소본 — 솎아낸 축소본과 같은 크기 · 칸 평균 · 가는 선이 끊기지 않음 · 멈춤. ★ 합성 그림만.
/// </summary>
public sealed class SmoothOverviewTests
{
    private static MatImageSource Filled(int width, int height, Func<int, int, float> value)
    {
        Mat image = new(height, width, MatType.CV_32FC1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.Set(y, x, value(x, y));
            }
        }

        return new MatImageSource(image);
    }

    /// <summary>★ 솎아낸 축소본과 같은 크기 — 뷰어가 하나를 다른 하나로 바꿔 끼운다. 나누어떨어지지 않는 크기도.</summary>
    [Theory]
    [InlineData(100, 70, 6)]
    [InlineData(97, 41, 4)]
    [InlineData(64, 64, 8)]
    public void 솎아낸_축소본과_크기가_같다(int width, int height, int step)
    {
        using MatImageSource source = Filled(width, height, (_, _) => 0.5f);
        using Mat thinned = source.ReadOverview((Math.Max(width, height) + step - 1) / step);
        using Mat smooth = SmoothOverview.Read(source, step);

        Assert.Equal((thinned.Cols, thinned.Rows), (smooth.Cols, smooth.Rows));
        Assert.Equal(MatType.CV_32FC1, smooth.Type());
    }

    /// <summary>칸 안을 전부 평균 낸다 — 고른 그림은 그대로, 칸마다 다른 값이면 그 칸의 평균.</summary>
    [Fact]
    public void 칸_안을_평균_낸다()
    {
        // 4 × 4 칸 하나에 0 과 1 이 반씩(왼쪽 2열 0, 오른쪽 2열 1) → 0.5
        using MatImageSource source = Filled(8, 4, (x, _) => x % 4 < 2 ? 0f : 1f);
        using Mat smooth = SmoothOverview.Read(source, 4);

        Assert.Equal((2, 1), (smooth.Cols, smooth.Rows));
        Assert.Equal(0.5f, smooth.At<float>(0, 0), 4);
        Assert.Equal(0.5f, smooth.At<float>(0, 1), 4);
    }

    /// <summary>
    /// ★ 칸보다 가는 선 — 솎아내기는 남는 픽셀에 안 걸리면 선이 통째로 사라진다. 평균은 옅어질 뿐(1/step) 모든 칸에 남는다.
    /// </summary>
    [Fact]
    public void 가는_선이_사라지지_않는다()
    {
        const int step = 6;

        // 6 px 마다 세로선 1 px — 칸의 4번째 열에 있어 솎아내기(칸의 0번째 열)에는 안 걸린다
        using MatImageSource source = Filled(60, 60, (x, _) => x % step == 3 ? 1f : 0f);
        using Mat thinned = source.ReadOverview(60 / step);
        using Mat smooth = SmoothOverview.Read(source, step);

        Cv2.MinMaxLoc(thinned, out double thinMin, out double thinMax);
        Cv2.MinMaxLoc(smooth, out double smoothMin, out double smoothMax);

        Assert.Equal(0, thinMax, 6);
        Assert.Equal(1.0 / step, smoothMin, 4);
        Assert.Equal(1.0 / step, smoothMax, 4);
    }

    /// <summary>칸이 1 이면 원본 그대로.</summary>
    [Fact]
    public void 칸이_1이면_원본_그대로()
    {
        using MatImageSource source = Filled(9, 5, (x, y) => ((y * 9) + x) / 45f);
        using Mat whole = source.ReadRegion(new Core.Imaging.Roi(0, 0, 9, 5));
        using Mat smooth = SmoothOverview.Read(source, 1);
        using Mat gap = new();

        Cv2.Absdiff(whole, smooth, gap);
        Cv2.MinMaxLoc(gap, out _, out double worst);

        Assert.Equal(0, worst, 6);
    }

    /// <summary>멈추라고 하면 멈춘다 — 다른 사진을 열면 뷰어가 이전 것을 멈춘다.</summary>
    [Fact]
    public void 멈추라고_하면_멈춘다()
    {
        using MatImageSource source = Filled(40, 40, (_, _) => 0.5f);
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SmoothOverview.Read(source, 4, cancel.Token));
    }
}