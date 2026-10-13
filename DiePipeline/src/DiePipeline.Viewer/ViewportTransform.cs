using DiePipeline.Core.Domain;

namespace DiePipeline.Viewer;

/// <summary>
/// 화면 ↔ 원본 픽셀 좌표(교수님 <c>ViewportTransform</c> 과 같다).
///
/// <para><see cref="OriginX"/> · <see cref="OriginY"/> = 화면 왼쪽 위가 가리키는 <b>원본 픽셀</b> 좌표.
/// <see cref="Scale"/> = 원본 1 px 이 화면 몇 px 인가(확대 배율, &gt; 0).</para>
/// </summary>
public readonly record struct ViewportTransform(double OriginX, double OriginY, double Scale)
{
    /// <summary>화면 좌표 → 원본 픽셀 좌표.</summary>
    public PointF ToOriginal(double screenX, double screenY)
        => new(OriginX + (screenX / Scale), OriginY + (screenY / Scale));

    /// <summary>원본 픽셀 좌표 → 화면 좌표.</summary>
    public PointF ToScreen(double originalX, double originalY)
        => new((originalX - OriginX) * Scale, (originalY - OriginY) * Scale);

    /// <summary>
    /// 화면 (x, y) 아래의 원본 점을 <b>그 자리에 둔 채</b> 배율만 바꾼다 — 커서를 기준으로 확대 · 축소.
    /// ★ 화면 왼쪽 위를 기준으로 키우면 보던 곳이 화면 밖으로 밀려나 매번 다시 찾아야 한다.
    /// </summary>
    public ViewportTransform ZoomAtScreen(double screenX, double screenY, double newScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newScale);

        PointF anchor = ToOriginal(screenX, screenY);

        return new ViewportTransform(anchor.X - (screenX / newScale), anchor.Y - (screenY / newScale), newScale);
    }

    /// <summary>화면에 그림 전체가 들어가는 배율 — 가로 · 세로 중 더 빠듯한 쪽.</summary>
    public static double FitScale(int imageWidth, int imageHeight, double viewportWidth, double viewportHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);

        return Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
    }
}