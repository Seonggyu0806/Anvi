using System.Buffers.Binary;
using DiePipeline.Core.Imaging;
using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 영역 읽기. ★ 오늘 확인할 것은 "뜯어 읽은 것이 통째로 읽은 것과 같은가"다.
///
/// 합성 BMP를 만들어 OpenCV가 읽은 결과와 대조한다.
/// OpenCV는 BMP를 제대로 읽을 줄 아는 남의 구현이라, 우리 파서의 <b>정답표</b>가 된다.
/// </summary>
public sealed class ImageSourceTests : IDisposable
{
    private readonly List<string> _files = [];
    private readonly List<Mat> _mats = [];

    public void Dispose()
    {
        foreach (Mat mat in _mats) mat.Dispose();
        foreach (string file in _files) File.Delete(file);
    }

    private Mat Keep(Mat mat)
    {
        _mats.Add(mat);
        return mat;
    }

    /// <summary>합성 BMP 를 임시 폴더에 쓴다. 레포 밖이다.</summary>
    private string WriteBmp(int width, int height, int bits)
    {
        string path = Path.Combine(Path.GetTempPath(), $"diepipe-{Guid.NewGuid():N}.bmp");
        File.WriteAllBytes(path, MakeBmp(width, height, bits));
        _files.Add(path);
        return path;
    }

    private static byte[] MakeBmp(int width, int height, int bits)
    {
        int rows = Math.Abs(height);
        int paletteBytes = bits <= 8 ? (1 << bits) * 4 : 0;
        int dataOffset = 54 + paletteBytes;
        int stride = ((width * bits) + 31) / 32 * 4;
        byte[] file = new byte[dataOffset + (stride * rows)];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2, 4), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10, 4), (uint)dataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28, 2), (ushort)bits);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30, 4), 0);

        for (int i = 0; i < paletteBytes / 4; i++)
        {
            file[54 + (i * 4)] = (byte)i;
            file[54 + (i * 4) + 1] = (byte)i;
            file[54 + (i * 4) + 2] = (byte)i;
        }

        // 무늬가 있어야 어긋남이 보인다. 시드 고정이라 늘 같은 그림이다.
        Random rng = new(7);

        for (int i = 0; i < stride * rows; i++)
        {
            file[dataOffset + i] = (byte)rng.Next(256);
        }

        return file;
    }

    private static double MaxDiff(Mat a, Mat b)
    {
        if (a.Rows != b.Rows || a.Cols != b.Cols) return double.MaxValue;

        using Mat d = new();
        Cv2.Absdiff(a, b, d);
        Cv2.MinMaxLoc(d, out _, out double max);
        return max;
    }

    [Theory]
    [InlineData(7, 5, 8)]
    [InlineData(7, 5, 24)]
    [InlineData(16, 9, 8)]
    [InlineData(5, -4, 24)]     // 높이 음수 = 위→아래
    [InlineData(1, 1, 8)]
    public void 뜯어_읽은_것이_통째로_읽은_것과_같다(int width, int height, int bits)
    {
        // ★ 오늘의 핵심. 아래→위 뒤집기와 행 패딩이 조금이라도 틀리면 여기서 어긋난다.
        string path = WriteBmp(width, height, bits);

        Mat whole = Keep(ImageFile.LoadGrayF32(path));
        using IImageSource source = ImageSource.Open(path);
        Mat part = Keep(source.ReadRegion(new Roi(0, 0, source.Width, source.Height)));

        Assert.Equal(whole.Cols, part.Cols);
        Assert.Equal(whole.Rows, part.Rows);
        Assert.True(MaxDiff(whole, part) < 1e-6);
    }

    [Theory]
    [InlineData(0, 0, 4, 3)]
    [InlineData(12, 6, 4, 3)]
    [InlineData(5, 2, 1, 1)]
    [InlineData(0, 0, 16, 9)]
    public void 일부만_읽어도_같은_자리가_나온다(int x, int y, int w, int h)
    {
        string path = WriteBmp(16, 9, 8);

        Mat whole = Keep(ImageFile.LoadGrayF32(path));
        using IImageSource source = ImageSource.Open(path);

        Mat part = Keep(source.ReadRegion(new Roi(x, y, w, h)));
        Mat expected = Keep(new Mat(whole, new Rect(x, y, w, h)).Clone());

        Assert.True(MaxDiff(expected, part) < 1e-6);
    }

    [Fact]
    public void 결과는_늘_GrayF32다()
    {
        // 8비트든 24비트든 위층은 한 가지만 상대하면 된다.
        using IImageSource eight = ImageSource.Open(WriteBmp(8, 8, 8));
        using IImageSource twentyFour = ImageSource.Open(WriteBmp(8, 8, 24));

        Assert.Equal(MatType.CV_32FC1, Keep(eight.ReadRegion(new Roi(0, 0, 8, 8))).Type());
        Assert.Equal(MatType.CV_32FC1, Keep(twentyFour.ReadRegion(new Roi(0, 0, 8, 8))).Type());
    }

    [Fact]
    public void 이미지_밖_영역은_거부한다()
    {
        using IImageSource source = ImageSource.Open(WriteBmp(16, 9, 8));

        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRegion(new Roi(14, 0, 4, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRegion(new Roi(-1, 0, 4, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRegion(new Roi(0, 0, 0, 3)));
    }

    [Fact]
    public void 개요는_솎아낸_크기로_나온다()
    {
        using IImageSource source = ImageSource.Open(WriteBmp(16, 9, 8));

        // 긴 변 16 을 8 이하로 → 2칸마다 하나 → 8 × 5
        Mat overview = Keep(source.ReadOverview(8));

        Assert.Equal(8, overview.Cols);
        Assert.Equal(5, overview.Rows);
    }

    [Fact]
    public void 메모리_소스도_같은_자리를_준다()
    {
        Mat image = Keep(new Mat(9, 16, MatType.CV_32FC1));

        for (int y = 0; y < 9; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                image.Set(y, x, ((y * 16f) + x) / 255f);
            }
        }

        using IImageSource source = new MatImageSource(image, owns: false);
        Mat part = Keep(source.ReadRegion(new Roi(2, 1, 3, 2)));

        Assert.Equal(3, part.Cols);
        Assert.Equal(2, part.Rows);
        Assert.Equal(18f / 255f, part.At<float>(0, 0), 5);   // y=1,x=2 → 1*16+2 = 18
    }

    [Fact]
    public void 메모리_소스는_창이_아니라_복사본을_준다()
    {
        // ★ 창문을 그대로 주면 필터가 이웃 die 픽셀을 읽어 테두리에 가짜 결함이 생긴다.
        Mat image = Keep(new Mat(8, 8, MatType.CV_32FC1, new Scalar(0.5)));
        using IImageSource source = new MatImageSource(image, owns: false);

        Mat part = Keep(source.ReadRegion(new Roi(2, 2, 4, 4)));
        part.Set(0, 0, 0.9f);

        Assert.Equal(0.5f, image.At<float>(2, 2), 5);   // 원본은 그대로여야 한다
    }

    [Fact]
    public void GrayF32가_아닌_Mat은_거부한다()
    {
        Mat wrong = Keep(new Mat(4, 4, MatType.CV_8UC1));

        Assert.Throws<ArgumentException>(() => new MatImageSource(wrong, owns: false));
    }

    [Fact]
    public void 없는_파일은_거부한다()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"diepipe-none-{Guid.NewGuid():N}.bmp");

        Assert.Throws<FileNotFoundException>(() => ImageSource.Open(missing));
    }

    [Fact]
    public void 깨진_BMP는_거부하고_파일을_놓아준다()
    {
        // ★ 파일을 안 놓으면 다음에 열 때 "다른 프로세스가 쓰는 중"으로 막힌다.
        string path = Path.Combine(Path.GetTempPath(), $"diepipe-bad-{Guid.NewGuid():N}.bmp");
        File.WriteAllBytes(path, new byte[100]);   // BM 도 아니다
        _files.Add(path);

        Assert.Throws<InvalidDataException>(() => ImageSource.Open(path));

        // 잠겨 있지 않아야 지워진다
        File.Delete(path);
        _files.Remove(path);
        Assert.False(File.Exists(path));
    }
}