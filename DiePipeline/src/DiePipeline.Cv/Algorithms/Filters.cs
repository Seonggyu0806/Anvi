using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 이미지 한 장을 다듬어 <b>새 이미지</b>를 낸다. Filter 체인의 한 칸.
///
/// ★ 교수님 구조 그대로다 — Filter는 방법 하나를 고르는 게 아니라
///   <b>순서 있는 목록</b>이고, 이 인터페이스가 그 목록의 한 칸이다.
/// </summary>
public interface IImageFilter
{
    /// <summary>입력은 건드리지 않고 새 Mat을 낸다.</summary>
    Mat Apply(Mat src);
}

/// <summary>중앙값 필터(점잡음 제거). GrayF32는 kernel 3/5만 지원.</summary>
public sealed class MedianFilter : IImageFilter
{
    private readonly int _kernel;

    public MedianFilter(int kernel = 3)
    {
        // ★ OpenCV 제한. 실측 — k=3 OK · k=5 OK · k=7 예외 · k=9 예외.
        if (kernel is not (3 or 5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kernel), kernel, "중앙값 커널은 32F 이미지에서 3 또는 5여야 합니다");
        }

        _kernel = kernel;
    }

    public Mat Apply(Mat src)
    {
        Mat dst = new();

        try
        {
            Cv2.MedianBlur(src, dst, _kernel);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>가우시안 필터.</summary>
public sealed class GaussianFilter : IImageFilter
{
    private readonly int _kernel;
    private readonly double _sigma;

    public GaussianFilter(int kernel = 3, double sigma = 0)
    {
        Kernels.RequireOdd(kernel);
        _kernel = kernel;
        _sigma = sigma;
    }

    public Mat Apply(Mat src)
    {
        Mat dst = new();

        try
        {
            Cv2.GaussianBlur(src, dst, new Size(_kernel, _kernel), _sigma);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>
/// 모폴로지 연산으로 형태를 정리/추출한다. Open/Close/Erode/Dilate에 더해
/// TopHat(밝은 미세 결함 = src−Open)·BlackHat(어두운 미세 결함 = Close−src)로 패턴 위 입자를 뽑는다.
/// </summary>
public sealed class MorphologyFilter : IImageFilter
{
    public enum Op
    {
        Open,
        Close,
        Erode,
        Dilate,
        TopHat,
        BlackHat,
    }

    private readonly Op _op;
    private readonly int _kernel;

    public MorphologyFilter(Op op, int kernel = 3)
    {
        // ★ OpenCV는 짝수 커널을 <b>예외 없이 통과시킨다</b>(실측). 중심이 없어 결과가 한쪽으로 치우치는데
        //   아무도 안 알려준다. 그래서 우리가 막는다.
        Kernels.RequireOdd(kernel);
        _op = op;
        _kernel = kernel;
    }

    public Mat Apply(Mat src)
    {
        using Mat element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(_kernel, _kernel));

        MorphTypes type = _op switch
        {
            Op.Open => MorphTypes.Open,
            Op.Close => MorphTypes.Close,
            Op.Erode => MorphTypes.Erode,
            Op.Dilate => MorphTypes.Dilate,
            Op.TopHat => MorphTypes.TopHat,
            Op.BlackHat => MorphTypes.BlackHat,
            _ => throw new ArgumentOutOfRangeException(nameof(_op), _op, "알 수 없는 모폴로지 연산"),
        };

        Mat dst = new();

        try
        {
            Cv2.MorphologyEx(src, dst, type, element);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>
/// Sobel 에지 강도(gradient magnitude) — 결함 경계/스크래치 강조용. 커널 홀수.
/// ⚠ 출력은 gradient 크기라 <b>[0,1]을 초과</b>할 수 있다(작업 포맷의 절대 스케일이 아니다).
/// 뒤에 Binarize를 둘 때 [0,1] 임계(Threshold/Range)는 부적합 — 통계 기반(Otsu/Sigma)을 권장.
/// </summary>
public sealed class SobelFilter : IImageFilter
{
    private readonly int _kernel;

    public SobelFilter(int kernel = 3)
    {
        Kernels.RequireOdd(kernel);
        _kernel = kernel;
    }

    public Mat Apply(Mat src)
    {
        Mat dst = new();

        try
        {
            using Mat gx = new();
            using Mat gy = new();
            Cv2.Sobel(src, gx, MatType.CV_32F, 1, 0, _kernel);
            Cv2.Sobel(src, gy, MatType.CV_32F, 0, 1, _kernel);
            Cv2.Magnitude(gx, gy, dst);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>
/// 라플라시안 에지(2차 미분)의 절댓값 — 결함 경계/스폿을 강조한다. 커널 홀수.
/// ⚠ 출력은 2차 미분 강도라 <b>[0,1]을 초과</b>할 수 있다(Sobel처럼 뒤 Binarize는 Otsu/Sigma 권장).
/// </summary>
public sealed class LaplacianFilter : IImageFilter
{
    private readonly int _kernel;

    public LaplacianFilter(int kernel = 3)
    {
        Kernels.RequireOdd(kernel);
        _kernel = kernel;
    }

    public Mat Apply(Mat src)
    {
        Mat dst = new();

        try
        {
            using Mat lap = new();
            Cv2.Laplacian(src, lap, MatType.CV_32F, _kernel);

            // |2차 미분| = 에지/스폿 강도(부호 제거)
            using Mat abs = Cv2.Abs(lap);
            abs.CopyTo(dst);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>
/// 양방향 필터(에지 보존 평활) — 공간+밝기 가중으로 경계를 살리며 노이즈를 줄인다.
/// sigmaColor는 작업 포맷([0,1]) 밝기 스케일, sigmaSpace는 픽셀 거리. 입력≠출력(제자리 연산 불가).
/// </summary>
public sealed class BilateralFilter : IImageFilter
{
    private readonly int _diameter;
    private readonly double _sigmaColor;
    private readonly double _sigmaSpace;

    public BilateralFilter(int diameter = 5, double sigmaColor = 0.1, double sigmaSpace = 5.0)
    {
        _diameter = diameter;
        _sigmaColor = sigmaColor;
        _sigmaSpace = sigmaSpace;
    }

    public Mat Apply(Mat src)
    {
        Mat dst = new();

        try
        {
            Cv2.BilateralFilter(src, dst, _diameter, _sigmaColor, _sigmaSpace);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>커널 규칙 — 여러 필터가 같이 쓴다.</summary>
public static class Kernels
{
    /// <summary>
    /// 커널은 1 이상 홀수여야 한다.
    ///
    /// ★ 짝수면 <b>중심 픽셀이 없다.</b> 4×4 창에는 한가운데가 없어서
    ///   "이 픽셀의 주변"을 대칭으로 잡을 수가 없다.
    /// </summary>
    public static void RequireOdd(int kernel)
    {
        if (kernel < 1 || (kernel & 1) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kernel), kernel, "커널 크기는 1 이상의 홀수여야 합니다 — 중심 픽셀이 있어야 합니다");
        }
    }
}