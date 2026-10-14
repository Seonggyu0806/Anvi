using DiePipeline.Core.Domain;

namespace DiePipeline.Tests;

/// <summary>px ↔ µm 환산 · 거리 글자. ★ 1.0 은 미보정 — µm 를 지어내지 않는다.</summary>
public sealed class CalibrationTests
{
    /// <summary>길이는 한 번, 넓이는 두 번 곱한다.</summary>
    [Fact]
    public void 길이는_한_번_넓이는_두_번()
    {
        Calibration c = new(0.5);

        Assert.Equal(60, c.Length(120), precision: 9);
        Assert.Equal(25, c.Area(100), precision: 9);
        Assert.True(c.IsCalibrated);
    }

    /// <summary>0 · 음수 · NaN · 무한대는 환산이 아니다 — 미보정(1.0).</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 잘못된_값은_미보정(double micronsPerPixel)
    {
        Calibration c = new(micronsPerPixel);

        Assert.Equal(1.0, c.MicronsPerPixel);
        Assert.False(c.IsCalibrated);
        Assert.Equal(Calibration.Uncalibrated, c);
    }

    /// <summary>거리 글자 — 보정이면 µm 까지, 미보정이면 px 만. Δx · Δy 는 부호까지.</summary>
    [Fact]
    public void 거리_글자()
    {
        PointF a = new(10, 50);
        PointF b = new(130, 20);

        Assert.Equal(123.69316876852982, Calibration.Distance(a, b), precision: 9);
        Assert.Equal("거리 123.7 px · 247.4 µm (Δx 120 · Δy -30)", new Calibration(2).DistanceText(a, b));
        Assert.Equal("거리 123.7 px (Δx 120 · Δy -30)", Calibration.Uncalibrated.DistanceText(a, b));
        Assert.Equal("거리 0.0 px (Δx 0 · Δy 0)", Calibration.Uncalibrated.DistanceText(a, a));
    }
}