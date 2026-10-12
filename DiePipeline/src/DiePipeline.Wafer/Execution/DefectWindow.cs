using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Wafer.Execution;

/// <summary>결함 하나를 원본 해상도로 볼 창 — 읽을 영역(사진 좌표)과 그 안의 결함 네모(창 기준).</summary>
public readonly record struct DefectView(Roi Window, BoundingBox Box);

/// <summary>
/// 결함 둘레를 <b>원본 해상도로</b> 읽을 창을 정한다.
///
/// <para>★ 화면의 웨이퍼 맵은 축소본이다. 원판 원본이면 결함이 1~2 px 점이 되어 모양을 볼 수 없다.
/// 그래서 고른 결함 둘레만 <see cref="Cv.Imaging.IImageSource.ReadRegion"/> 으로 따로 읽는다 —
/// die 검사가 쓰는 것과 같은 부분 읽기라 기가픽셀 원본에서도 작은 창만 읽는다.</para>
///
/// <para>★ 결함 좌표는 <b>die 를 잘라 읽은 영역(<c>Roi</c>) 기준</b>이다. 그래서 사진 좌표 = Roi 왼쪽 위 + 결함 네모.
/// 읽기 자체가 축에 맞춰 이뤄졌으니 정렬 회전과 상관없이 정확하다.</para>
///
/// <para>★ 화면을 모른다 — 순수 계산이라 이미지 없이 시험된다.</para>
/// </summary>
public static class DefectWindow
{
    /// <summary>창 한 변 = 결함 긴 변 × 이것. 결함 둘레의 무늬가 같이 보여야 진짜인지 판단할 수 있다.</summary>
    public const int Scale = 4;

    /// <summary>창의 최소 한 변(px). 작은 결함도 둘레가 보이게.</summary>
    public const int MinSide = 128;

    /// <summary>창의 최대 한 변(px). 큰 결함이 화면 하나를 넘는 원본을 읽지 않게 — 넘는 부분은 창 밖에 남는다.</summary>
    public const int MaxSide = 1024;

    /// <param name="dieRoi">die 를 잘라 읽은 영역 — <b>사진 좌표</b>. 결함 좌표의 기준이다.</param>
    /// <param name="bounds">결함 네모 — <paramref name="dieRoi"/> 기준.</param>
    /// <param name="imageWidth">사진 폭(px).</param>
    /// <param name="imageHeight">사진 높이(px).</param>
    public static DefectView Around(Roi dieRoi, BoundingBox bounds, int imageWidth, int imageHeight)
    {
        if (imageWidth < 1 || imageHeight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageWidth), $"사진 크기는 1×1 이상이어야 합니다 (현재 {imageWidth}×{imageHeight})");
        }

        if (bounds.Width < 1 || bounds.Height < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds), $"결함 네모는 1×1 이상이어야 합니다 (현재 {bounds.Width}×{bounds.Height})");
        }

        // 결함의 사진 좌표 — die 를 읽은 자리 + die 안 자리
        int left = dieRoi.X + bounds.X;
        int top = dieRoi.Y + bounds.Y;

        int side = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * Scale, MinSide, MaxSide);
        int width = Math.Min(side, imageWidth);
        int height = Math.Min(side, imageHeight);

        // 결함 가운데에 창 가운데를 맞춘다. 사진 밖으로 나가면 창 크기는 그대로 두고 안쪽으로 민다
        // — 가장자리 결함도 같은 크기의 창으로 보인다(결함이 창 가운데가 아닐 뿐).
        int x = Math.Clamp(left + (bounds.Width / 2) - (width / 2), 0, imageWidth - width);
        int y = Math.Clamp(top + (bounds.Height / 2) - (height / 2), 0, imageHeight - height);

        return new DefectView(
            new Roi(x, y, width, height),
            new BoundingBox(left - x, top - y, bounds.Width, bounds.Height));
    }
}