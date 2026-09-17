using OpenCvSharp;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// 파일과 <b>작업 포맷(GrayF32)</b> 사이의 유일한 통로.
///
/// ★ 여기가 파이프라인의 <b>입구이자 출구</b>다. 밖에서는 8비트·16비트가 들어오지만
///   안에서는 전부 [0,1] 실수로 돈다. 그 변환을 <b>한 곳에서만</b> 한다 —
///   여러 곳에서 하면 어디선가 255로 나누는 걸 빠뜨려도 조용히 지나간다.
/// </summary>
public static class ImageFile
{
    /// <summary>
    /// 이미지를 읽어 GrayF32([0,1])로 바꾼다.
    ///
    /// ★ 나누는 수가 비트깊이마다 다르다 — 8비트는 255, 16비트는 65535.
    ///   그래서 <b>파일이 몇 비트인지</b>를 보고 정해야 한다. 그냥 255로 나누면
    ///   16비트 이미지가 전부 새하얗게 포화된다.
    /// </summary>
    public static Mat LoadGrayF32(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"이미지를 못 찾았습니다: {path}", path);
        }

        // AnyDepth — 16비트 파일을 8비트로 깎지 않고 그대로 읽는다.
        using Mat raw = Cv2.ImRead(path, ImreadModes.Grayscale | ImreadModes.AnyDepth);

        if (raw.Empty())
        {
            throw new ArgumentException($"이미지를 읽을 수 없습니다(형식을 모르거나 깨졌습니다): {path}", nameof(path));
        }

        double scale = raw.Depth() switch
        {
            0 => 1.0 / 255.0,       // CV_8U
            2 => 1.0 / 65535.0,     // CV_16U
            5 => 1.0,               // CV_32F — 이미 실수, 그대로
            _ => throw new ArgumentException($"다룰 수 없는 비트깊이입니다: {raw.Type()}", nameof(path)),
        };

        Mat gray = new();

        try
        {
            raw.ConvertTo(gray, MatType.CV_32FC1, scale);
        }
        catch
        {
            gray.Dispose();
            throw;
        }

        return gray;
    }

    /// <summary>GrayF32([0,1])를 8비트 PNG로 저장한다. 눈으로 보려고 쓴다.</summary>
    public static void SaveGrayF32(string path, Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using Mat eightBit = new();
        image.ConvertTo(eightBit, MatType.CV_8UC1, 255.0);
        Cv2.ImWrite(path, eightBit);
    }

    /// <summary>이진 마스크(Gray8)를 그대로 저장한다.</summary>
    public static void SaveMask(string path, Mat mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        Cv2.ImWrite(path, mask);
    }

    /// <summary>
    /// 큰 이미지에서 <b>셀 한 칸</b>을 잘라 복사본으로 낸다.
    ///
    /// ★ 복사본인 이유: OpenCV의 잘라내기(<c>new Mat(src, rect)</c>)는 <b>원본을 들여다보는 창</b>이라
    ///   행과 행 사이에 원본의 나머지가 끼어 있다. 그대로 칠판에 올리면
    ///   원본이 살아 있는 동안만 쓸 수 있고, 통째로 읽는 연산에서 <b>엉뚱한 픽셀이 섞인다.</b>
    /// </summary>
    public static Mat Crop(Mat source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > source.Cols || y + height > source.Rows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"({x},{y}) {width}×{height} 가 이미지({source.Cols}×{source.Rows}) 밖으로 나갑니다");
        }

        using Mat view = new(source, new Rect(x, y, width, height));
        return view.Clone();
    }
}