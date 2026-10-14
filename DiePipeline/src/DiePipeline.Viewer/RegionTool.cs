using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Viewer;

/// <summary>결함 표시 상자 — 원본 픽셀. 왼쪽 위 + 크기.</summary>
public readonly record struct MarkerBox(double X, double Y, double Width, double Height);

/// <summary>
/// 원본 위에 영역을 그리는 계산 — 끌기 · 도장 · 표식 설계 자리 · 결함 표시 상자 · 한 점을 가운데에
/// (교수님 <c>RegionSelection</c> · <c>AddSelectedRegion</c> · <c>ApplyFeaturePoints</c> · <c>MarkerViewModel</c> · <c>CenterOn</c>).
///
/// ★ 좌표는 전부 원본 픽셀이다 — 뷰어의 판 위 좌표가 곧 원본 픽셀이라 화면 배율을 몰라도 된다.
/// </summary>
public static class RegionTool
{
    /// <summary>결함 표시 상자의 최소 크기(원본 px) — 1~2 px 결함도 확대하면 보이게(교수님 값).</summary>
    public const double MinMarkerSize = 12;

    /// <summary>
    /// 끈 두 점 → 사각형. 거꾸로 끌어도 같은 상자이고, 그림 밖으로 그어도 그림 안만 남는다.
    /// 폭이나 높이가 1 px 이 안 되면 <c>null</c> — 끌지 않고 누르기만 한 것이다.
    /// </summary>
    public static Roi? FromDrag(double x0, double y0, double x1, double y1, int imageWidth, int imageHeight)
    {
        int left = (int)Math.Round(Math.Min(x0, x1));
        int top = (int)Math.Round(Math.Min(y0, y1));
        int width = (int)Math.Round(Math.Abs(x1 - x0));
        int height = (int)Math.Round(Math.Abs(y1 - y0));

        Roi region = ViewportPlanner.Clamp(left, top, width, height, imageWidth, imageHeight);

        return region.Width < 1 || region.Height < 1 ? null : region;
    }

    /// <summary>
    /// 정한 크기의 상자를 (x, y) 를 왼쪽 위로 찍는다.
    /// ★ 가장자리에서는 잘라 내지 않고 <b>그림 안으로 밀어 넣는다</b> — 잘라 내면 크기가 달라져 die-to-die 비교가 깨진다.
    ///   상자가 그림보다 클 때만 그림 크기로 줄어든다.
    /// </summary>
    public static Roi Stamp(double x, double y, int width, int height, int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int left = Math.Clamp((int)Math.Round(x), 0, Math.Max(0, imageWidth - width));
        int top = Math.Clamp((int)Math.Round(y), 0, Math.Max(0, imageHeight - height));

        return ViewportPlanner.Clamp(left, top, width, height, imageWidth, imageHeight);
    }

    /// <summary>
    /// 다이 미리보기에 넘겨도 되나 — 안 되면 그 까닭, 되면 <c>null</c>.
    /// ★ 숫자로 고친 값도 여기서 걸린다 — 그림 밖 영역, ROI 와 크기가 다른 정상 die(die-to-die 는 같은 크기끼리 뺀다).
    /// </summary>
    public static string? CheckDieRegions(Roi roi, IReadOnlyList<Roi> chips, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(chips);

        if (!Inside(roi, imageWidth, imageHeight))
        {
            return "ROI 가 그림 밖으로 나갑니다 — 숫자를 그림 안으로 고치세요";
        }

        if (chips.Count == 0)
        {
            return "정상 die 를 하나 이상 찍으세요 — 골든을 만들 이웃이 없습니다";
        }

        for (int i = 0; i < chips.Count; i++)
        {
            Roi chip = chips[i];

            if (!Inside(chip, imageWidth, imageHeight))
            {
                return $"정상 die {i + 1} 이 그림 밖으로 나갑니다";
            }

            if (chip.Width != roi.Width || chip.Height != roi.Height)
            {
                return $"정상 die {i + 1} 크기({chip.Width}×{chip.Height})가 ROI({roi.Width}×{roi.Height})와 다릅니다 — 같은 크기끼리 비교합니다";
            }
        }

        return null;
    }

    /// <summary>웨이퍼 레시피에 표식 자리로 써도 되나 — 안 되면 그 까닭, 되면 <c>null</c>. 정렬은 표식이 2개 이상이어야 각도를 안다.</summary>
    public static string? CheckFeatures(IReadOnlyList<Roi> features, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (features.Count < 2)
        {
            return $"정렬 특징이 {features.Count}개입니다 — 2개 이상 그리세요(하나로는 돌아간 각도를 모릅니다)";
        }

        for (int i = 0; i < features.Count; i++)
        {
            if (!Inside(features[i], imageWidth, imageHeight))
            {
                return $"정렬 특징 {i + 1} 이 그림 밖으로 나갑니다";
            }
        }

        return null;
    }

    /// <summary>
    /// 표식 설계 자리 = 특징 상자 왼쪽 위 − die(0,0) 원점. 웨이퍼 레시피의 <c>expected</c> 가 이 좌표다(교수님과 같은 규약).
    /// ★ 정렬은 표식 조각의 왼쪽 위를 찾는다 — 그래서 상자 가운데가 아니라 왼쪽 위를 쓴다.
    /// </summary>
    public static PointF Expected(Roi feature, double originX, double originY)
        => new(feature.X - originX, feature.Y - originY);

    /// <summary>
    /// 결함 표시 상자 — 가운데(웨이퍼 결함의 절대 좌표는 결함 가운데다)와 크기로. 작으면 <see cref="MinMarkerSize"/> 로 키운다.
    /// </summary>
    public static MarkerBox Marker(double centerX, double centerY, double width, double height)
    {
        double w = Math.Max(MinMarkerSize, width);
        double h = Math.Max(MinMarkerSize, height);

        return new MarkerBox(centerX - (w / 2), centerY - (h / 2), w, h);
    }

    /// <summary>
    /// 원본 (x, y) 를 화면 가운데에 두는 화면 — 화면 왼쪽 위가 가리킬 원본 좌표 + 배율.
    /// 그림 가장자리 근처라 못 가운데에 두는 것은 화면(스크롤)이 알아서 멈춘다.
    /// </summary>
    public static ViewportTransform CenteredOn(double x, double y, double scale, double screenWidth, double screenHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        return new ViewportTransform(x - (screenWidth / 2 / scale), y - (screenHeight / 2 / scale), scale);
    }

    private static bool Inside(Roi r, int imageWidth, int imageHeight)
        => r is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 } && r.X + r.Width <= imageWidth && r.Y + r.Height <= imageHeight;
}