using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>골든을 검사 이미지에 맞춰 되미는 방법.</summary>
public enum AlignMethod
{
    /// <summary>안 움직인다. 이미 맞아 있거나, 믿을 수 없는 경우.</summary>
    None,

    /// <summary>평행이동만. 위상상관으로 몇 픽셀 밀렸는지 재서 그만큼 되민다.</summary>
    Translation,
}

/// <summary>얼마나 밀렸는지 재 본 결과. 받아들였는지와 그 이유까지 같이 들고 온다.</summary>
public readonly record struct ShiftEstimate(double X, double Y, double Response, bool Accepted, string Reason)
{
    /// <summary>안 움직이기로 한 결과.</summary>
    public static ShiftEstimate Rejected(double x, double y, double response, string reason)
        => new(x, y, response, false, reason);
}

/// <summary>
/// 골든을 검사 이미지에 <b>겹쳐 놓는다</b>.
///
/// ★ 왜 필요한가: 스테이지가 die를 옮길 때 몇 픽셀씩 어긋난다. 어긋난 채로 빼면
///   <b>모든 경계선이 통째로 결함으로 나온다</b> — 실제로 v1에서 오검 11개가 이렇게 나왔다.
///
/// ★ 무엇을 안 하는가: 회전·확대는 다루지 않는다. 같은 웨이퍼 위 이웃 die끼리는
///   돌아가지도 커지지도 않는다. 겪지 않은 문제를 먼저 풀지 않는다. (docs/1-deferred.md §3)
/// </summary>
public static class Aligners
{
    /// <summary>
    /// 이보다 확신이 약하면 안 움직인다.
    /// 실측 — 멀쩡한 짝 0.76~0.98 / 주기 패턴 0.005~0.064. 사이가 넓어서 0.3이면 충분히 갈린다.
    /// </summary>
    public const double DefaultMinResponse = 0.3;

    /// <summary>이보다 많이 밀렸다는 답이 나오면 믿지 않는다.</summary>
    public const double DefaultMaxShiftPx = 20.0;

    /// <summary>
    /// <paramref name="src"/>를 <paramref name="reference"/>에 맞춰 되민 <b>새 이미지</b>를 낸다.
    /// 잴 수 없거나 믿을 수 없으면 <b>안 움직인 복사본</b>을 낸다 — 예외를 던지지 않는다.
    /// </summary>
    public static Mat Align(
        Mat src,
        Mat reference,
        AlignMethod method,
        double minResponse = DefaultMinResponse,
        double maxShiftPx = DefaultMaxShiftPx)
    {
        RequirePair(src, reference);

        if (method == AlignMethod.None)
        {
            return src.Clone();
        }

        ShiftEstimate estimate = Measure(src, reference, minResponse, maxShiftPx);

        // ★ 거부는 실패가 아니다. 안 움직인 골든으로 검사를 이어간다.
        //   잘못 움직이면 없던 결함이 생기지만, 안 움직이면 있는 결함이 조금 덜 선명해질 뿐이다.
        //   둘 중에서는 후자가 훨씬 낫다.
        if (!estimate.Accepted)
        {
            return src.Clone();
        }

        return Translate(src, estimate.X, estimate.Y);
    }

    /// <summary>
    /// 몇 픽셀 밀렸는지 잰다. <b>이미지를 바꾸지 않는다.</b>
    /// </summary>
    public static ShiftEstimate Measure(
        Mat src,
        Mat reference,
        double minResponse = DefaultMinResponse,
        double maxShiftPx = DefaultMaxShiftPx)
    {
        RequirePair(src, reference);

        // ★★ 함정 — Cv2.PhaseCorrelate에 '창(window)'을 넘기면 <b>두 입력을 그 자리에서 망가뜨린다.</b>
        //    실측: 창을 주고 한 번 부르면 src1·src2 둘 다 픽셀 합이 2166만큼 달라졌다.
        //    같은 짝을 반복해서 부르면 답이 2.985 → 2.970 → 2.954로 계속 흘러내린다.
        //    (입력이 매번 조금씩 더 먹히기 때문이다.)
        //    빈 Mat을 넘기면 창을 안 쓰고, 그때는 입력이 한 톨도 안 변한다.
        using Mat noWindow = new();

        Point2d shift = Cv2.PhaseCorrelate(src, reference, noWindow, out double response);

        if (double.IsNaN(shift.X) || double.IsNaN(shift.Y) || double.IsNaN(response))
        {
            return ShiftEstimate.Rejected(0, 0, 0, "위상상관이 숫자를 못 냈습니다");
        }

        // 가드 ① 확신이 약하면 믿지 않는다.
        if (response < minResponse)
        {
            return ShiftEstimate.Rejected(
                shift.X, shift.Y, response,
                $"응답 {response:F3} < 기준 {minResponse:F3} — 주기 패턴이거나 서로 무관한 이미지일 수 있습니다");
        }

        // 가드 ② 말이 안 되게 멀면 믿지 않는다.
        double distance = Math.Sqrt((shift.X * shift.X) + (shift.Y * shift.Y));

        if (distance > maxShiftPx)
        {
            return ShiftEstimate.Rejected(
                shift.X, shift.Y, response,
                $"이동 {distance:F1}px > 한계 {maxShiftPx:F1}px — 엉뚱한 곳을 짝지었을 수 있습니다");
        }

        return new ShiftEstimate(shift.X, shift.Y, response, true, "정상");
    }

    private static Mat Translate(Mat src, double dx, double dy)
    {
        // 2×3 아핀 행렬. 회전·확대 없이 옮기기만 하므로 왼쪽 2×2는 단위행렬이다.
        //   [ 1  0  dx ]
        //   [ 0  1  dy ]
        using Mat matrix = Mat.FromPixelData(2, 3, MatType.CV_64FC1, new double[] { 1, 0, dx, 0, 1, dy });

        Mat dst = new();

        try
        {
            // ★ BorderTypes.Replicate — 옮기면 한쪽에 빈자리가 생긴다.
            //   0(검정)으로 채우면 그 띠가 다음 단계에서 <b>통째로 결함</b>이 된다.
            //   가장자리 픽셀을 늘려 채우면 검사 이미지와 비슷한 값이라 차이가 안 생긴다.
            Cv2.WarpAffine(src, dst, matrix, src.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }

    private static void RequirePair(Mat src, Mat reference)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(reference);

        if (src.Type() != MatType.CV_32FC1 || reference.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException(
                $"작업 포맷은 GrayF32여야 합니다 — src: {src.Type()}, reference: {reference.Type()}");
        }

        if (src.Rows != reference.Rows || src.Cols != reference.Cols)
        {
            throw new ArgumentException(
                $"두 이미지의 크기가 같아야 합니다 — src: {src.Cols}x{src.Rows}, reference: {reference.Cols}x{reference.Rows}");
        }
    }
}