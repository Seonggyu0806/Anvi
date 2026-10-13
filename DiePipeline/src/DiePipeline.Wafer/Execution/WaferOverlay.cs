using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>오버레이의 die 테두리 하나 — 원본 픽셀 좌표. 색은 웨이퍼 맵 칸과 같은 상태로 고른다.</summary>
public sealed record OverlayDie(int Col, int Row, Roi Area, WaferMapState State);

/// <summary>오버레이의 결함 네모 하나 — 원본 픽셀 좌표. 중심이 결함의 웨이퍼 좌표다.</summary>
public sealed record OverlayDefect(WaferDefect Defect, double X, double Y, double Width, double Height);

/// <summary>오버레이에 그릴 것 전부. 멈췄으면 둘 다 비었다.</summary>
public sealed record WaferOverlayLayout(IReadOnlyList<OverlayDie> Dies, IReadOnlyList<OverlayDefect> Defects)
{
    public static WaferOverlayLayout Empty { get; } = new([], []);
}

/// <summary>
/// 검사 결과 → 웨이퍼 개요 그림 위에 겹쳐 그릴 die 테두리 · 결함 네모(교수님 웨이퍼 오버레이와 같은 자리).
///
/// ★ 좌표는 전부 <b>원본 픽셀</b>이다. 화면은 개요 그림을 원본 크기 판에 늘여 깔고 그 위에 그대로 얹는다 — 배율 곱셈이 화면에 없다.
/// ★ 원판 원본에서는 수십 px 결함이 개요 그림에서 1 px 도 안 된다. 그래서 네모를 <b>개요 그림 기준 최소 8 px</b> 로 키운다(교수님과 같다).
/// ★ 결함은 추린 뒤 목록(<see cref="WaferInspectionResult.Defects"/>) — 결함 표와 같은 것. die 색은 맵과 같이 추리기 전 수로 정한다.
/// </summary>
public static class WaferOverlay
{
    /// <summary>결함 네모의 최소 크기 — 개요 그림 픽셀.</summary>
    public const double MinBoxOverviewPx = 8;

    /// <param name="overviewPixel">개요 그림 1 px 이 원본 몇 px 인가(원본 폭 ÷ 개요 폭). 원본을 그대로 보이면 1.</param>
    public static WaferOverlayLayout Of(WaferInspectionResult result, double overviewPixel)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(overviewPixel);

        // ★ 멈췄으면 아무것도 안 그린다 — 빈 그림은 "깨끗한 웨이퍼" 로 읽힌다(맵과 같은 규칙).
        if (result.Stopped || result.Grid is not { } grid)
        {
            return WaferOverlayLayout.Empty;
        }

        Dictionary<(int Col, int Row), WaferMapState> states =
            WaferMap.Of(result).ToDictionary(c => (c.Col, c.Row), c => c.State);

        OverlayDie[] dies =
        [
            .. grid.InRowMajorOrder().Select(d => new OverlayDie(d.Col, d.Row, grid.RoiOf(d), states[(d.Col, d.Row)])),
        ];

        double min = MinBoxOverviewPx * overviewPixel;

        OverlayDefect[] defects =
        [
            .. (result.Defects?.Items ?? [])
                .Where(d => d.AbsX is not null && d.AbsY is not null)
                .Select(d =>
                {
                    double w = Math.Max(min, d.Defect.Bounds.Width);
                    double h = Math.Max(min, d.Defect.Bounds.Height);

                    return new OverlayDefect(d, d.AbsX!.Value - (w / 2), d.AbsY!.Value - (h / 2), w, h);
                }),
        ];

        return new WaferOverlayLayout(dies, defects);
    }
}