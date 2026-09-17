using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 골든을 다듬는 방법.
///
/// ★ 여기 있는 것은 전부 <b>평활(smoothing)</b>이다 — 뭉갤 뿐 값의 기준을 바꾸지 않는다.
///   CLAHE·ZScore처럼 값의 기준을 옮기는 변환은 <b>일부러 넣지 않았다</b>:
///   Normalize는 골든에만 걸리므로, 값의 기준이 바뀌면 검사 이미지와 서로 다른 세계가 되어
///   다음 단계 Difference의 뺄셈이 의미를 잃는다. (docs/1-deferred.md §2)
/// </summary>
public enum NormalizeMethod
{
    /// <summary>통과. 이미 잘 맞아 있을 때.</summary>
    None,

    /// <summary>이웃과 섞어 부드럽게. <b>기본값</b>.</summary>
    GaussianBlur,

    /// <summary>이웃 중 가운데 값. 튀는 점을 지우면서 에지는 살린다.</summary>
    MedianBlur,
}

/// <summary>
/// 골든을 다듬어 <b>미세 정합오차·노이즈 민감도를 낮춘다</b>.
///
/// ★ 왜 골든에만 거나: 골든은 이웃 die 여러 장을 겹쳐 만든 것이라 경계선이 이미 살짝 번져 있다.
///   반면 검사 이미지는 한 장짜리라 경계가 날카롭다. 그대로 빼면 모든 경계에 얇은 띠가 남는다.
///   날카로운 쪽을 뭉개는 게 아니라, <b>기준 쪽을 조금 더 부드럽게</b> 해서 허용 범위를 만든다.
/// </summary>
public static class Normalizers
{
    /// <summary>OpenCV 제한 — MedianBlur는 GrayF32에서 이 커널까지만 된다.</summary>
    public const int MaxMedianKernelF32 = 5;

    public static Mat Normalize(Mat golden, NormalizeMethod method, int kernelSize = 5, double sigma = 1.2)
    {
        ArgumentNullException.ThrowIfNull(golden);

        if (golden.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException($"작업 포맷은 GrayF32여야 합니다 — 받은 것: {golden.Type()}");
        }

        // ★ None도 복사본을 낸다. 원본을 그대로 돌려주면 칠판이 같은 Mat을
        //   두 이름표로 들고 있다가 실행 끝에 두 번 놓아버린다.
        if (method == NormalizeMethod.None)
        {
            return golden.Clone();
        }

        RequireOdd(kernelSize);

        Mat dst = new();

        // ★ 실패하면 방금 만든 버퍼를 놓고 다시 던진다.
        //   여기서 안 놓으면 예외가 날 때마다 이미지 한 장씩 샌다.
        try
        {
            switch (method)
            {
                case NormalizeMethod.GaussianBlur:
                    Cv2.GaussianBlur(golden, dst, new Size(kernelSize, kernelSize), sigma);
                    break;

                case NormalizeMethod.MedianBlur:
                    RequireMedianKernel(kernelSize);
                    Cv2.MedianBlur(golden, dst, kernelSize);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, "알 수 없는 정규화 방법");
            }
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }

    /// <summary>
    /// 커널은 홀수여야 한다.
    ///
    /// ★ 짝수면 <b>중심 픽셀이 없다.</b> 4×4 창에는 한가운데가 없어서
    ///   "이 픽셀의 주변"을 대칭으로 잡을 수가 없다.
    /// </summary>
    public static void RequireOdd(int kernelSize)
    {
        if (kernelSize < 1 || (kernelSize & 1) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kernelSize), kernelSize, "커널 크기는 1 이상의 홀수여야 합니다 — 중심 픽셀이 있어야 합니다");
        }
    }

    private static void RequireMedianKernel(int kernelSize)
    {
        if (kernelSize > MaxMedianKernelF32)
        {
            // OpenCV가 던지는 예외 메시지로는 무엇을 해야 할지 알 수 없다. 대안을 알려준다.
            throw new ArgumentOutOfRangeException(
                nameof(kernelSize), kernelSize,
                $"MedianBlur는 GrayF32에서 커널 {MaxMedianKernelF32}까지만 됩니다(OpenCV 제한). " +
                "더 크게 뭉개려면 GaussianBlur를 쓰세요.");
        }
    }
}