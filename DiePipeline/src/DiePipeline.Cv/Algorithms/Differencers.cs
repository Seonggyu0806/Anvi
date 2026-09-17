using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>어느 쪽 차이를 남길지.</summary>
public enum DiffPolicy
{
    /// <summary>둘 다. 밝아진 곳도 어두워진 곳도 남긴다. <b>기본값</b>.</summary>
    Absolute,

    /// <summary>기준보다 <b>어두운</b> 결함만. 패턴이 떨어져 나간 자리.</summary>
    DarkOnly,

    /// <summary>기준보다 <b>밝은</b> 결함만. 이물질처럼 덧붙은 것.</summary>
    BrightOnly,

    /// <summary>밝음 − 어두움. 부호를 남긴다.</summary>
    Signed,
}

/// <summary>
/// 골든의 <b>어디와</b> 비교할지 — 정합오차에 견디게 하는 장치.
///
/// ★ test 픽셀을 골든의 <b>같은 자리 한 점</b>이 아니라 <b>근방의 통계</b>와 비교한다.
///   그러면 골든이 살짝 흐려졌거나 1픽셀 밀려도 차이로 안 잡힌다.
/// </summary>
public enum NeighborMode
{
    /// <summary>같은 자리끼리 직접 비교.</summary>
    None,

    /// <summary>근방 <b>최소값</b>과 비교.</summary>
    Min,

    /// <summary>근방 <b>최대값</b>과 비교.</summary>
    Max,

    /// <summary>[근방 최소, 근방 최대] <b>띠 밖</b>일 때만 결함.</summary>
    MinMax,

    /// <summary>근방 <b>평균</b>과 비교.</summary>
    Mean,
}

/// <summary>Difference 설정.</summary>
public sealed record DifferenceOptions
{
    /// <summary>근방을 몇 칸으로 볼지(홀수).</summary>
    public int WindowSize { get; init; } = 3;

    public DiffPolicy DiffPolicy { get; init; } = DiffPolicy.Absolute;

    public NeighborMode NeighborMode { get; init; } = NeighborMode.None;
}

/// <summary>
/// 검사 이미지에서 골든을 <b>뺀다</b>. 결함이 처음으로 모습을 드러내는 곳.
///
/// ★ 통일 모델 — 골든에서 <b>허용 띠</b> [lo, hi]를 만들고 그 밖으로 나간 만큼만 센다.
/// <code>
///   bright = max(test − hi, 0)     띠 위로 넘친 만큼
///   dark   = max(lo − test, 0)     띠 아래로 모자란 만큼
/// </code>
///   NeighborMode가 <c>None</c>이면 lo = hi = 골든이라 <b>보통 뺄셈</b>과 똑같아진다.
///   그래서 방법이 다섯 개인데 코드는 한 갈래다.
///
/// ★★ 실데이터에서 배운 것 — <b>Normalize와 NeighborMode는 짝이다.</b>
///   골든만 흐리게(Normalize) 해 놓고 같은 자리끼리(None) 빼면
///   <b>모든 패턴 경계가 차이로 남는다</b>(실웨이퍼 타일에서 오검 13개).
///   근방과 비교하면 그 흐림이 띠 안으로 흡수된다.
/// </summary>
public static class Differencers
{
    public static Mat Difference(Mat test, Mat golden, DifferenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequirePair(test, golden);

        if (options.WindowSize < 1 || (options.WindowSize & 1) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.WindowSize, "윈도우 크기는 1 이상의 홀수여야 합니다");
        }

        (Mat low, Mat high, Mat[] temporary) = BuildBand(golden, options.NeighborMode, options.WindowSize);

        try
        {
            using Mat bright = new();
            using Mat dark = new();

            // ★ GrayF32에서는 뺄셈이 음수를 그대로 남긴다(실측: 0.5 − 0.9 = −0.4).
            //   8비트였다면 0에서 잘렸겠지만 실수는 안 잘린다. 직접 잘라줘야 한다.
            Cv2.Subtract(test, high, bright);
            Cv2.Max(bright, Scalar.All(0), bright);

            Cv2.Subtract(low, test, dark);
            Cv2.Max(dark, Scalar.All(0), dark);

            Mat result = new();

            try
            {
                switch (options.DiffPolicy)
                {
                    case DiffPolicy.Absolute:
                        // 한쪽이 0이라 더하기가 곧 |차이|다.
                        Cv2.Add(bright, dark, result);
                        break;

                    case DiffPolicy.BrightOnly:
                        bright.CopyTo(result);
                        break;

                    case DiffPolicy.DarkOnly:
                        dark.CopyTo(result);
                        break;

                    case DiffPolicy.Signed:
                        Cv2.Subtract(bright, dark, result);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(options), options.DiffPolicy, "알 수 없는 차이 정책");
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }

            return result;
        }
        finally
        {
            foreach (Mat mat in temporary)
            {
                mat.Dispose();
            }
        }
    }

    /// <summary>
    /// 골든에서 허용 띠 [lo, hi]를 만든다.
    ///
    /// ★ 근방 최소 = <b>침식(Erode)</b>, 근방 최대 = <b>팽창(Dilate)</b>이다.
    ///   이웃을 하나씩 훑는 게 아니라 OpenCV의 모폴로지 한 번이라 <b>빠르다.</b>
    /// </summary>
    private static (Mat Low, Mat High, Mat[] Temporary) BuildBand(Mat golden, NeighborMode mode, int window)
    {
        switch (mode)
        {
            case NeighborMode.None:
                // 띠가 아니라 점 하나 — 보통 뺄셈이 된다. 새로 만든 게 없으니 놓을 것도 없다.
                return (golden, golden, []);

            case NeighborMode.Min:
                {
                    Mat min = Erode(golden, window);
                    return (min, min, [min]);
                }

            case NeighborMode.Max:
                {
                    Mat max = Dilate(golden, window);
                    return (max, max, [max]);
                }

            case NeighborMode.Mean:
                {
                    Mat mean = new();
                    Cv2.BoxFilter(golden, mean, -1, new Size(window, window));
                    return (mean, mean, [mean]);
                }

            case NeighborMode.MinMax:
                {
                    Mat min = Erode(golden, window);
                    Mat max = Dilate(golden, window);
                    return (min, max, [min, max]);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "알 수 없는 이웃 비교 방법");
        }
    }

    private static Mat Erode(Mat src, int window)
    {
        using Mat element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(window, window));
        Mat dst = new();
        Cv2.Erode(src, dst, element, borderType: BorderTypes.Replicate);
        return dst;
    }

    private static Mat Dilate(Mat src, int window)
    {
        using Mat element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(window, window));
        Mat dst = new();
        Cv2.Dilate(src, dst, element, borderType: BorderTypes.Replicate);
        return dst;
    }

    private static void RequirePair(Mat test, Mat golden)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(golden);

        if (test.Type() != MatType.CV_32FC1 || golden.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException(
                $"작업 포맷은 GrayF32여야 합니다 — test: {test.Type()}, golden: {golden.Type()}");
        }

        if (test.Rows != golden.Rows || test.Cols != golden.Cols)
        {
            throw new ArgumentException(
                $"두 이미지의 크기가 같아야 합니다 — test: {test.Cols}x{test.Rows}, golden: {golden.Cols}x{golden.Rows}");
        }
    }
}