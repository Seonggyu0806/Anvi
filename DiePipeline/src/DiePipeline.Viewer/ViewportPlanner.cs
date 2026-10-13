using DiePipeline.Core.Imaging;

namespace DiePipeline.Viewer;

/// <summary>
/// 원본을 어떻게 보여 줄지 — 축소본 배율 · 선명 조각이 필요한가 · 지금 보이는 원본 영역(교수님 <c>LevelPolicy</c> · <c>ViewportPlanner</c>).
///
/// <para>★ 원본은 통째로 화면에 못 올린다. 그래서 두 단계로 보인다 —
/// <b>축소본</b>(긴 변 <see cref="MaxProxyLong"/> 이하) 한 장을 깔고, 그게 늘어나 흐려질 만큼 확대하면
/// <b>보이는 영역만</b> 원본에서 읽어 덮는다(선명 조각). 실제 읽기는 이미지 소스가 한다 — 여기는 계산만.</para>
/// </summary>
public static class ViewportPlanner
{
    /// <summary>이만큼 이하(픽셀 수)면 줄이지 않고 원본 그대로 올린다 — 확대해도 그대로 선명하다(교수님 값).</summary>
    public const long MaxFullDisplayPixels = 32_000_000;

    /// <summary>축소본의 긴 변 상한(교수님 값).</summary>
    public const int MaxProxyLong = 4096;

    /// <summary>선명 조각을 한 번에 읽는 상한(픽셀 수) — 넘으면 이번에는 안 읽는다(더 확대하면 읽는다). 교수님 값.</summary>
    public const long MaxSharpPixels = 6_000_000;

    /// <summary>
    /// 축소 배율(1 이상) — 축소본 1 px 이 원본 몇 px 인가. 원본이 작으면 1(줄이지 않음).
    /// ★ 이미지 소스의 '건너뛰며 솎아내기' 와 같은 셈이다 — 긴 변 ÷ 상한을 올림.
    /// </summary>
    public static int ProxyFactor(int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);

        if ((long)imageWidth * imageHeight <= MaxFullDisplayPixels)
        {
            return 1;
        }

        int longSide = Math.Max(imageWidth, imageHeight);

        return Math.Max(1, (longSide + MaxProxyLong - 1) / MaxProxyLong);
    }

    /// <summary>
    /// 이 배율에서 축소본이 흐려지나 — 축소본 1 px 이 화면에서 1 px 보다 크게 늘어나면 원본이 필요하다.
    /// </summary>
    public static bool NeedsOriginal(int proxyFactor, double scale) => scale * proxyFactor > 1.0;

    /// <summary>화면 (0,0) ~ (폭, 높이) 에 보이는 원본 영역 — 그림 안으로 잘라 맞춘다. 그림 밖만 보이면 빈 영역.</summary>
    public static Roi VisibleRegion(ViewportTransform transform, double screenWidth, double screenHeight, int imageWidth, int imageHeight)
    {
        Core.Domain.PointF topLeft = transform.ToOriginal(0, 0);
        Core.Domain.PointF bottomRight = transform.ToOriginal(screenWidth, screenHeight);

        int x0 = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
        int y0 = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
        int x1 = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X));
        int y1 = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y));

        return Clamp(x0, y0, x1 - x0, y1 - y0, imageWidth, imageHeight);
    }

    /// <summary>
    /// 사각형을 그림 안으로 잘라 맞춘다. 그림과 겹치지 않으면 빈 영역(폭 · 높이 0).
    /// ★ 다음 단계(영역 그리기)에서 마우스로 그린 사각형도 여기서 맞춘다 — 그림 밖으로 그어도 그림 안만 남는다.
    /// </summary>
    public static Roi Clamp(int x, int y, int width, int height, int imageWidth, int imageHeight)
    {
        int x0 = Math.Clamp(x, 0, imageWidth);
        int y0 = Math.Clamp(y, 0, imageHeight);
        int x1 = Math.Clamp(x + width, 0, imageWidth);
        int y1 = Math.Clamp(y + height, 0, imageHeight);

        return new Roi(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>
    /// 지금 읽어 덮을 선명 조각 — 필요 없으면(원본을 그대로 올렸거나 축소본이 아직 선명) <c>null</c>,
    /// 보이는 영역이 비었거나 너무 넓어도(<see cref="MaxSharpPixels"/> 초과) <c>null</c>.
    /// </summary>
    public static Roi? SharpRegion(
        ViewportTransform transform, double screenWidth, double screenHeight, int imageWidth, int imageHeight, int proxyFactor)
    {
        if (proxyFactor <= 1 || !NeedsOriginal(proxyFactor, transform.Scale))
        {
            return null;
        }

        Roi visible = VisibleRegion(transform, screenWidth, screenHeight, imageWidth, imageHeight);
        long pixels = (long)visible.Width * visible.Height;

        return pixels == 0 || pixels > MaxSharpPixels ? null : visible;
    }
}