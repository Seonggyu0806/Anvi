using System.Runtime.InteropServices;
using DiePipeline.Core.Imaging;
using OpenCvSharp;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// 바이트 리더 + 자리 계산 → 이미지 소스. <b>7-1·7-2·7-3 이 여기서 합쳐진다.</b>
///
/// ★ 원본을 통째로 읽지 않는다. 요청한 영역의 <b>행 수만큼만</b> 읽는다.
///   실측: 수백 MB 원본에서 die 한 칸이 23ms(따뜻할 때)·149ms(처음).
///   웨이퍼 한 장의 die를 다 읽어도 3초 남짓이고, 메모리는 한 칸 크기만 쓴다.
/// </summary>
public sealed class ByteImageSource : IImageSource
{
    private readonly IByteReader _reader;

    public ByteImageSource(IByteReader reader, ImageGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // 기하가 파일보다 크면 읽다가 파일 밖으로 나간다. 입구에서 막는다.
        if (geometry.ExpectedFileSize > reader.Length)
        {
            throw new ArgumentException(
                $"기하가 파일보다 큽니다 — {geometry.ExpectedFileSize}바이트를 기대하는데 파일은 {reader.Length}바이트",
                nameof(geometry));
        }

        _reader = reader;
        Geometry = geometry;
    }

    public ImageGeometry Geometry { get; }

    public int Width => Geometry.Width;

    public int Height => Geometry.Height;

    public Mat ReadRegion(Roi region)
    {
        // ★ 자르지 않고 거부한다. 조용히 잘라 주면 "요청한 것과 다른 크기"가 돌아오고,
        //   골든과 검사 이미지의 크기가 안 맞아 die 하나가 통째로 건너뛰어진다.
        //   자르고 싶으면 부르는 쪽에서 Geometry.Clamp 를 쓰면 된다.
        if (!Geometry.Contains(region))
        {
            throw new ArgumentOutOfRangeException(
                nameof(region), $"{region}는 이미지 {Width}×{Height} 밖입니다");
        }

        int rowBytes = region.Width * Geometry.BytesPerPixel;

        // 버퍼 하나를 행마다 다시 쓴다. 수천 행이어도 배열은 1개다.
        byte[] row = new byte[rowBytes];

        using Mat raw = new(region.Height, region.Width, RawType());

        for (int y = 0; y < region.Height; y++)
        {
            _reader.ReadInto(Geometry.OffsetOf(region.X, region.Y + y), row, 0, rowBytes);

            // ★ 파일에서 읽은 순서대로 0행·1행·… 에 넣는다. 위아래 뒤집기는
            //   OffsetOf 가 이미 해 줬다 — 뒤집기가 두 곳에 있으면 반드시 어긋난다.
            Marshal.Copy(row, 0, raw.Ptr(y), rowBytes);
        }

        return ToGrayF32(raw, Geometry.BytesPerPixel);
    }

    public Mat ReadOverview(int maxLongSide)
    {
        if (maxLongSide < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongSide), "긴 변은 1 이상이어야 합니다");
        }

        int step = Math.Max(1, ((Math.Max(Width, Height) + maxLongSide) - 1) / maxLongSide);
        int outW = ((Width + step) - 1) / step;
        int outH = ((Height + step) - 1) / step;
        int bpp = Geometry.BytesPerPixel;

        byte[] source = new byte[Geometry.RowBytes];
        byte[] thinned = new byte[outW * bpp];

        using Mat raw = new(outH, outW, RawType());

        for (int y = 0; y < outH; y++)
        {
            // step 행마다 한 줄만 읽는다 — 파일의 1/step 만 건드린다는 뜻이다.
            _reader.ReadInto(Geometry.OffsetOf(0, Math.Min(y * step, Height - 1)), source, 0, Geometry.RowBytes);

            for (int x = 0; x < outW; x++)
            {
                Buffer.BlockCopy(source, Math.Min(x * step, Width - 1) * bpp, thinned, x * bpp, bpp);
            }

            Marshal.Copy(thinned, 0, raw.Ptr(y), thinned.Length);
        }

        return ToGrayF32(raw, bpp);
    }

    public void Dispose() => _reader.Dispose();

    private MatType RawType() => Geometry.BytesPerPixel switch
    {
        1 => MatType.CV_8UC1,
        2 => MatType.CV_16UC1,
        _ => MatType.CV_8UC3,
    };

    /// <summary>
    /// 읽어 온 바이트 → 작업 포맷(GrayF32, [0,1]).
    ///
    /// ★ 나누는 수가 비트깊이마다 다르다. 이걸 통일해 두면 <b>레시피의 임계값이
    ///   비트깊이에 안 흔들린다</b> — 8비트 원본에서 잡은 0.12가 16비트에서도 그대로다.
    ///   <c>ImageFile.LoadGrayF32</c>와 같은 규칙이라 두 경로가 같은 값을 낸다.
    /// </summary>
    private static Mat ToGrayF32(Mat raw, int bytesPerPixel)
    {
        Mat result = new();

        try
        {
            if (bytesPerPixel == 3)
            {
                // 우리 원본은 회색을 24비트로 저장해 세 채널이 같지만,
                // 진짜 컬러가 와도 맞게 돌도록 정식으로 회색 변환을 한다.
                using Mat gray = new();
                Cv2.CvtColor(raw, gray, ColorConversionCodes.BGR2GRAY);
                gray.ConvertTo(result, MatType.CV_32FC1, 1.0 / 255.0);
            }
            else
            {
                raw.ConvertTo(result, MatType.CV_32FC1, bytesPerPixel == 2 ? 1.0 / 65535.0 : 1.0 / 255.0);
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }
}

/// <summary>
/// 이미 메모리에 있는 GrayF32 한 장을 소스인 척한다.
///
/// ★ 이게 있어야 웨이퍼 층을 <b>파일 없이</b> 테스트할 수 있다.
///   합성 이미지 한 장 만들어 넣으면 격자·이웃·러너가 전부 돈다.
///   7-2의 <c>ArrayByteReader</c>와 같은 역할이다.
/// </summary>
public sealed class MatImageSource : IImageSource
{
    private readonly Mat _image;
    private readonly bool _owns;

    /// <param name="owns">false면 Dispose 해도 원본을 놓지 않는다 — 남의 Mat을 빌려 쓸 때.</param>
    public MatImageSource(Mat image, bool owns = true)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException($"GrayF32 여야 합니다. 현재 {image.Type()}", nameof(image));
        }

        _image = image;
        _owns = owns;
        Width = image.Cols;
        Height = image.Rows;
    }

    public int Width { get; }

    public int Height { get; }

    public Mat ReadRegion(Roi region)
    {
        if (region.X < 0 || region.Y < 0 || region.Width < 1 || region.Height < 1
            || region.Right > Width || region.Bottom > Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region), $"{region}는 이미지 {Width}×{Height} 밖입니다");
        }

        // ★ Clone 이 필요하다. OpenCV의 부분행렬은 '창문'이라 원본과 픽셀을 공유한다 —
        //   그대로 넘기면 필터가 창 밖(이웃 die)을 읽어 테두리에 가짜 결함이 생긴다.
        using Mat window = new(_image, new Rect(region.X, region.Y, region.Width, region.Height));
        return window.Clone();
    }

    public Mat ReadOverview(int maxLongSide)
    {
        if (maxLongSide < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongSide), "긴 변은 1 이상이어야 합니다");
        }

        int step = Math.Max(1, ((Math.Max(Width, Height) + maxLongSide) - 1) / maxLongSide);

        if (step == 1)
        {
            return _image.Clone();
        }

        // 파일 쪽과 같게 '솎아내기'로 맞춘다. 두 소스가 다르게 굴면 테스트가 거짓말을 한다.
        Mat result = new(((Height + step) - 1) / step, ((Width + step) - 1) / step, MatType.CV_32FC1);
        int rows = result.Rows;
        int cols = result.Cols;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                result.Set(y, x, _image.At<float>(Math.Min(y * step, Height - 1), Math.Min(x * step, Width - 1)));
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_owns)
        {
            _image.Dispose();
        }
    }
}