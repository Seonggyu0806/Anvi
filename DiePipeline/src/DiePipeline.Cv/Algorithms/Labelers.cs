using DiePipeline.Core.Domain;
using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>라벨링 옵션.</summary>
public sealed record LabelOptions
{
    /// <summary>4 또는 8. 대각선으로 닿은 픽셀을 한 덩어리로 볼지.</summary>
    public int Connectivity { get; init; } = 8;

    /// <summary>이보다 작은 덩어리는 버린다(잡음 제거).</summary>
    public int MinArea { get; init; } = 1;

    /// <summary>이보다 큰 덩어리는 버린다.</summary>
    public int MaxArea { get; init; } = int.MaxValue;

    /// <summary>밝기 이미지가 있으면 결함별 평균·최대 밝기를 낸다.</summary>
    public bool ComputeIntensityStats { get; init; } = true;

    /// <summary>원형도 등 윤곽 기반 값. 비용이 있어 기본은 끔.</summary>
    public bool ComputeShapeFeatures { get; init; }
}

/// <summary>
/// 이진 마스크의 <b>연결요소</b>를 뽑아 결함 목록을 만든다. 파이프라인의 마지막 칸.
///
/// ★ 여기서 처음으로 <b>이미지가 아니라 값</b>이 나온다.
///   앞 단계들은 전부 Mat을 내놨지만, 이 노드는 좌표·면적·밝기라는 <b>숫자</b>를 낸다.
///   그래서 칠판에 올라가는 것도 Mat이 아니라 DefectList다.
/// </summary>
public static class Labelers
{
    public static DefectList Label(Mat mask, Mat? intensity, LabelOptions options)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);

        if (mask.Type() != MatType.CV_8UC1)
        {
            throw new ArgumentException($"마스크는 Gray8이어야 합니다 — 받은 것: {mask.Type()}");
        }

        if (options.Connectivity is not (4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.Connectivity, "연결성은 4 또는 8이어야 합니다");
        }

        if (intensity is not null && (intensity.Rows != mask.Rows || intensity.Cols != mask.Cols))
        {
            throw new ArgumentException(
                $"밝기 이미지 크기가 마스크와 달라요 — 마스크 {mask.Cols}x{mask.Rows}, " +
                $"밝기 {intensity.Cols}x{intensity.Rows}");
        }

        // ★ 여기서는 using을 쓴다 — 앞 노드들과 반대다.
        //   labels/stats/centroids는 아무도 물려받지 않는다. 숫자만 뽑고 버린다.
        //   규칙: '다음 노드가 쓸 것'만 칠판에 올리고, 나머지는 그 자리에서 놓는다.
        using Mat labels = new();
        using Mat stats = new();
        using Mat centroids = new();

        int count = Cv2.ConnectedComponentsWithStats(
            mask, labels, stats, centroids,
            options.Connectivity == 4 ? PixelConnectivity.Connectivity4 : PixelConnectivity.Connectivity8,
            MatType.CV_32S);

        List<Defect> defects = [];
        int nextId = 1;

        // ★ label 0은 <b>배경</b>이다. 1부터 센다.
        for (int label = 1; label < count; label++)
        {
            int area = stats.Get<int>(label, (int)ConnectedComponentsTypes.Area);

            if (area < options.MinArea || area > options.MaxArea)
            {
                continue;
            }

            double? mean = null;
            double? max = null;

            if (options.ComputeIntensityStats && intensity is not null)
            {
                (mean, max) = Brightness(labels, intensity, label);
            }

            defects.Add(new Defect
            {
                // ★ 걸러낸 뒤 1부터 다시 매긴다 — 결과에 1, 4, 7 같은 구멍이 안 생기게.
                Id = nextId++,
                Bounds = new BoundingBox(
                    stats.Get<int>(label, (int)ConnectedComponentsTypes.Left),
                    stats.Get<int>(label, (int)ConnectedComponentsTypes.Top),
                    stats.Get<int>(label, (int)ConnectedComponentsTypes.Width),
                    stats.Get<int>(label, (int)ConnectedComponentsTypes.Height)),
                Area = area,
                Centroid = new PointF(
                    centroids.Get<double>(label, 0),
                    centroids.Get<double>(label, 1)),
                MeanIntensity = mean,
                MaxIntensity = max,
                Circularity = options.ComputeShapeFeatures ? Circularity(labels, label, area) : null,
            });
        }

        return new DefectList { Items = defects };
    }

    private static (double Mean, double Max) Brightness(Mat labels, Mat intensity, int label)
    {
        // 이 덩어리만 255인 마스크를 만들어 거기서만 통계를 낸다.
        using Mat only = new();
        Cv2.Compare(labels, Scalar.All(label), only, CmpTypes.EQ);

        double mean = Cv2.Mean(intensity, only).Val0;

        using Mat masked = new();
        intensity.CopyTo(masked, only);
        Cv2.MinMaxLoc(masked, out _, out double max);

        return (mean, max);
    }

    /// <summary>
    /// 원형도 = 4π × 면적 ÷ 둘레².
    ///
    /// ★ 완전한 원이면 정확히 1이 나온다. 길쭉하거나 울퉁불퉁하면 0 쪽으로 간다.
    ///   같은 면적이면 <b>원이 둘레가 제일 짧다</b>는 성질을 쓴 것이다.
    /// </summary>
    private static double Circularity(Mat labels, int label, int area)
    {
        using Mat only = new();
        Cv2.Compare(labels, Scalar.All(label), only, CmpTypes.EQ);

        Cv2.FindContours(only, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return 0.0;
        }

        double perimeter = Cv2.ArcLength(contours[0], closed: true);

        // 픽셀 격자에서는 계단 때문에 둘레가 실제보다 짧게 나와 1을 넘길 수 있다. 1에서 자른다.
        return perimeter > 1e-6
            ? Math.Min(1.0, 4.0 * Math.PI * area / (perimeter * perimeter))
            : 0.0;
    }
}