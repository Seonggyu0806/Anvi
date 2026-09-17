using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 회색 이미지를 <b>결함이냐 아니냐</b>로 가른다. Binarize 단계의 한 방법.
///
/// ★ 출력은 <b>Gray8(0 또는 255)</b>이다. 여기서 작업 포맷을 벗어난다 —
///   마스크는 "있다/없다"뿐이라 실수가 필요 없고, 다음 단계 Label의
///   <c>ConnectedComponents</c>가 8비트만 받기 때문이다.
/// </summary>
public interface IBinarizer
{
    /// <summary>입력은 건드리지 않고 8비트 마스크(0/255)를 낸다.</summary>
    Mat Binarize(Mat src);
}

/// <summary>고정 임계 이진화. 임계값은 작업 포맷([0,1]) 스케일.</summary>
public sealed class ThresholdBinarizer : IBinarizer
{
    private readonly double _value;

    public ThresholdBinarizer(double value) => _value = value;

    public Mat Binarize(Mat src)
    {
        Mat dst = new();

        try
        {
            // ★ 경계 — Binary는 '이상'이 아니라 <b>초과</b>(src > thresh)다.
            //   실측: 임계 0.10일 때 0.09→0, 0.10→0, 0.11→255.
            using Mat binF32 = new();
            Cv2.Threshold(src, binF32, _value, 255, ThresholdTypes.Binary);
            binF32.ConvertTo(dst, MatType.CV_8U);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return dst;
    }
}

/// <summary>Otsu 자동 임계. F32[0,1] → 8U 변환 후 Otsu.</summary>
public sealed class OtsuBinarizer : IBinarizer
{
    public Mat Binarize(Mat src)
    {
        Mat dst = new();

        try
        {
            using Mat src8 = new();
            src.ConvertTo(src8, MatType.CV_8U, 255.0);
            Cv2.Threshold(src8, dst, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
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
/// 양방향(Range) 이진화 — low ≤ x ≤ high 구간만 전경(255).
/// 임계는 작업 포맷([0,1]) 스케일. high &lt; low면 빈 마스크가 된다(InRange 규약).
/// </summary>
public sealed class RangeBinarizer : IBinarizer
{
    private readonly double _low;
    private readonly double _high;

    public RangeBinarizer(double low, double high)
    {
        _low = low;
        _high = high;
    }

    public Mat Binarize(Mat src)
    {
        Mat dst = new();

        try
        {
            // InRange는 구간 내 255, 밖 0의 8U 마스크를 바로 만든다(추가 변환 불필요).
            Cv2.InRange(src, new Scalar(_low), new Scalar(_high), dst);
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
/// 통계 임계 — 전경 = (값 &gt; 평균 + k·표준편차). 희소한 밝은 결함을 배경 분포 대비로 잡는다.
/// 임계가 이미지 통계에서 자동 산출돼 절대값에 둔감(단, 결함이 많아 분포를 끌어올리면 둔해짐).
/// </summary>
public sealed class SigmaThresholdBinarizer : IBinarizer
{
    private readonly double _k;

    public SigmaThresholdBinarizer(double k = 3.0) => _k = k;

    public Mat Binarize(Mat src)
    {
        Cv2.MeanStdDev(src, out Scalar mean, out Scalar stdDev);
        double threshold = mean.Val0 + (_k * stdDev.Val0);

        Mat dst = new();

        try
        {
            using Mat binF32 = new();
            Cv2.Threshold(src, binF32, threshold, 255, ThresholdTypes.Binary);
            binF32.ConvertTo(dst, MatType.CV_8U);
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
/// 적응형(국부 평균) 이진화 — 픽셀이 자기 <b>국부 평균</b>(가우시안 가중)에서 C 이상 벗어나면 전경.
/// 조명 불균일에 강건(고정 임계가 못 가르는 기울어진 배경에서도 국부 편차만 잡는다).
/// 국부 창 <c>blockSize</c>(홀수), 편차 임계 <c>C</c>(작업 포맷 [0,1] 오프셋). 방향: Bright/Dark/Both.
/// </summary>
public sealed class AdaptiveBinarizer : IBinarizer
{
    public enum Mode
    {
        /// <summary>src − 국부평균 &gt; C</summary>
        Bright,

        /// <summary>국부평균 − src &gt; C</summary>
        Dark,

        /// <summary>|src − 국부평균| &gt; C</summary>
        Both,
    }

    private readonly int _blockSize;
    private readonly double _c;
    private readonly Mode _mode;

    public AdaptiveBinarizer(int blockSize, double c, Mode mode)
    {
        if (blockSize < 3 || (blockSize & 1) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "blockSize는 3 이상의 홀수여야 합니다");
        }

        _blockSize = blockSize;
        _c = c;
        _mode = mode;
    }

    public Mat Binarize(Mat src)
    {
        Mat dst = new();

        try
        {
            using Mat localMean = new();
            Cv2.GaussianBlur(src, localMean, new Size(_blockSize, _blockSize), 0);   // 국부 평균(가우시안 가중)

            using Mat binF32 = new();

            if (_mode == Mode.Both)
            {
                using Mat absDev = new();
                Cv2.Absdiff(src, localMean, absDev);                                  // |src − 국부평균|
                Cv2.Threshold(absDev, binF32, _c, 255, ThresholdTypes.Binary);
            }
            else
            {
                using Mat dev = new();
                Cv2.Subtract(src, localMean, dev);                                    // src − 국부평균 (양수=밝은 편차)

                // Bright: dev > C, Dark: dev ≤ −C(어두운 편차).
                Cv2.Threshold(dev, binF32, _mode == Mode.Bright ? _c : -_c, 255,
                    _mode == Mode.Bright ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv);
            }

            binF32.ConvertTo(dst, MatType.CV_8U);
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
/// 히스테리시스(이중 임계) 이진화 — 강 임계(high) 초과 '씨앗'에 8-연결된, 약 임계(low) 초과 후보만 전경.
/// 약하지만 강한 결함에 이어진 픽셀은 살리고 고립된 약한 노이즈는 버린다(Canny식). low ≤ high 권장.
/// </summary>
public sealed class HysteresisBinarizer : IBinarizer
{
    private readonly double _low;
    private readonly double _high;

    public HysteresisBinarizer(double low, double high)
    {
        _low = low;
        _high = high;
    }

    public Mat Binarize(Mat src)
    {
        int rows = src.Rows;
        int cols = src.Cols;

        using Mat lowF = new();
        using Mat highF = new();
        Cv2.Threshold(src, lowF, _low, 255, ThresholdTypes.Binary);      // 약 후보
        Cv2.Threshold(src, highF, _high, 255, ThresholdTypes.Binary);    // 강 씨앗

        using Mat lowMask = new();
        using Mat highMask = new();
        lowF.ConvertTo(lowMask, MatType.CV_8U);
        highF.ConvertTo(highMask, MatType.CV_8U);

        using Mat labels = new();
        int count = Cv2.ConnectedComponents(lowMask, labels, PixelConnectivity.Connectivity8, MatType.CV_32S);

        // ★ 픽셀을 하나씩 Mat에 물어보면 매번 네이티브 호출(P/Invoke)이 일어난다.
        //   1024×1024면 백만 번이다. 통째로 배열에 받아 <b>순수 C#으로</b> 훑는다.
        //   Combine에서 쓴 것과 같은 수법이다.
        labels.GetArray(out int[] labelData);
        highMask.GetArray(out byte[] seedData);

        // 씨앗(강 임계)이 닿는 연결요소만 활성화.
        bool[] active = new bool[count];

        for (int i = 0; i < seedData.Length; i++)
        {
            if (seedData[i] == 0)
            {
                continue;
            }

            int label = labelData[i];

            // 씨앗은 항상 약 후보(high ≥ low)라 label > 0이지만,
            // 오설정(low > high) 시 배경을 물들이지 않도록 막는다.
            if (label > 0)
            {
                active[label] = true;
            }
        }

        // 활성 라벨 픽셀만 전경.
        byte[] output = new byte[rows * cols];

        for (int i = 0; i < output.Length; i++)
        {
            int label = labelData[i];
            output[i] = label > 0 && active[label] ? (byte)255 : (byte)0;
        }

        return Mat.FromPixelData(rows, cols, MatType.CV_8UC1, output);
    }
}