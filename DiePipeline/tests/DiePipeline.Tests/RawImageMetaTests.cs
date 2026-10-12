using System.Runtime.InteropServices;
using System.Text.Json;
using DiePipeline.Core.Imaging;
using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// raw + 사이드카 읽기. ★ 확인할 것은 "같은 그림을 raw 로 읽든 BMP 로 읽든 값이 같은가" 다.
///
/// 그림은 시험이 직접 만들어 임시 폴더에 쓴다 — 회사 자료를 끌어들이지 않는다.
/// BMP 는 이미 시험된 길이라, raw 쪽의 <b>정답표</b>가 된다.
/// </summary>
public sealed class RawImageMetaTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (string file in _files)
        {
            File.Delete(file);
        }
    }

    private string Temp(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"diepipe-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    /// <summary>시드를 고정한 무늬 그림 — 한 칸이라도 어긋나면 값이 다르다.</summary>
    private static Mat Pattern(int width, int height, MatType type)
    {
        Mat image = new(height, width, type);
        byte[] data = new byte[image.Total() * image.ElemSize()];
        new Random(7).NextBytes(data);
        Marshal.Copy(data, 0, image.Data, data.Length);
        return image;
    }

    /// <summary>Mat 의 픽셀 바이트를 헤더 없이 그대로 — raw 파일의 내용이다.</summary>
    private static byte[] Bytes(Mat image)
    {
        byte[] data = new byte[image.Total() * image.ElemSize()];
        Marshal.Copy(image.Data, data, 0, data.Length);
        return data;
    }

    private static string Sidecar(int width, int height, int bytesPerPixel, string rowOrder = "TopDown")
        => $$"""{ "width": {{width}}, "height": {{height}}, "bytesPerPixel": {{bytesPerPixel}}, "rowOrder": "{{rowOrder}}", "micronsPerPixel": 0.5 }""";

    private static double MaxDiff(Mat a, Mat b)
    {
        using Mat d = new();
        Cv2.Absdiff(a, b, d);
        Cv2.MinMaxLoc(d, out _, out double max);
        return max;
    }

    // ─────────────────────────────── 사이드카

    [Fact]
    public void 사이드카_JSON_을_읽는다()
    {
        RawImageMeta meta = RawImageMeta.Parse(Sidecar(40, 30, 3));

        Assert.Equal(40, meta.Width);
        Assert.Equal(30, meta.Height);
        Assert.Equal(3, meta.BytesPerPixel);
        Assert.Equal("TopDown", meta.RowOrder);
        Assert.Equal(0.5, meta.MicronsPerPixel);
        Assert.Equal(40L * 30 * 3, meta.ExpectedFileSize);
    }

    /// <summary>필수가 아닌 값은 교수님과 같은 기본값 — 회색 8비트 · 위→아래 · 물리 크기 모름.</summary>
    [Fact]
    public void 폭과_높이만_있어도_읽는다()
    {
        RawImageMeta meta = RawImageMeta.Parse("""{ "Width": 8, "HEIGHT": 4, }  // 대소문자 · 뒤 쉼표 · 주석""");

        Assert.Equal((8, 4, 1, "TopDown"), (meta.Width, meta.Height, meta.BytesPerPixel, meta.RowOrder));
        Assert.Null(meta.MicronsPerPixel);
    }

    [Fact]
    public void 폭이_없으면_거부한다()
    {
        Assert.Throws<JsonException>(() => RawImageMeta.Parse("""{ "height": 4 }"""));
    }

    [Fact]
    public void 사이드카는_뒤에_붙인_이름을_먼저_찾는다()
    {
        string raw = Temp(".raw");
        string appended = raw + ".json";
        string swapped = Path.ChangeExtension(raw, ".json");
        _files.Add(appended);
        _files.Add(swapped);

        Assert.Null(RawImageMeta.FindSidecar(raw));

        File.WriteAllText(swapped, "{}");
        Assert.Equal(swapped, RawImageMeta.FindSidecar(raw));

        File.WriteAllText(appended, "{}");
        Assert.Equal(appended, RawImageMeta.FindSidecar(raw));
    }

    /// <summary>★ 다른 행 순서를 조용히 받으면 그림이 뒤집혀 die 번호가 전부 어긋난다.</summary>
    [Fact]
    public void 위에서_아래가_아닌_행_순서는_거부한다()
    {
        RawImageMeta meta = RawImageMeta.Parse(Sidecar(8, 4, 1, "BottomUp"));

        Assert.Throws<NotSupportedException>(() => meta.ToGeometry());
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(8, 4, 4)]
    public void 기하가_틀리면_거부한다(int width, int height, int bytesPerPixel)
    {
        RawImageMeta meta = RawImageMeta.Parse(Sidecar(width, height, bytesPerPixel));

        Assert.Throws<ArgumentOutOfRangeException>(() => meta.ToGeometry());
    }

    // ─────────────────────────────── 열기

    /// <summary>★★ 같은 그림을 raw 와 BMP 로 저장해 읽으면 값이 같다 — 전체도, 일부도.</summary>
    [Theory]
    [InlineData(37, 23, 1)]
    [InlineData(37, 23, 3)]
    public void raw_로_읽은_값이_BMP_로_읽은_값과_같다(int width, int height, int bytesPerPixel)
    {
        using Mat image = Pattern(width, height, bytesPerPixel == 1 ? MatType.CV_8UC1 : MatType.CV_8UC3);

        string bmp = Temp(".bmp");
        Cv2.ImWrite(bmp, image);

        string raw = Temp(".raw");
        File.WriteAllBytes(raw, Bytes(image));
        string sidecar = raw + ".json";
        _files.Add(sidecar);
        File.WriteAllText(sidecar, Sidecar(width, height, bytesPerPixel));

        using IImageSource fromBmp = ImageSource.Open(bmp);
        using IImageSource fromRaw = ImageSource.Open(raw);

        Assert.Equal((width, height), (fromRaw.Width, fromRaw.Height));

        foreach (Roi roi in new[] { new Roi(0, 0, width, height), new Roi(5, 7, 11, 9), new Roi(width - 1, height - 1, 1, 1) })
        {
            using Mat a = fromBmp.ReadRegion(roi);
            using Mat b = fromRaw.ReadRegion(roi);

            Assert.True(MaxDiff(a, b) < 1e-6, $"{roi} 에서 값이 다르다");
        }
    }

    /// <summary>★ 크기가 다르면 사이드카가 다른 파일 것이다. 그대로 읽으면 행이 비스듬히 밀린 그림이 나온다.</summary>
    [Fact]
    public void 파일_크기가_사이드카와_다르면_거부하고_파일을_놓아준다()
    {
        string raw = Temp(".raw");
        File.WriteAllBytes(raw, new byte[8 * 4 + 1]);    // 한 바이트 더
        string sidecar = raw + ".json";
        _files.Add(sidecar);
        File.WriteAllText(sidecar, Sidecar(8, 4, 1));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ImageSource.Open(raw));
        Assert.Contains("사이드카와 다릅니다", error.Message);

        // 잠겨 있지 않아야 지워진다
        File.Delete(raw);
        Assert.False(File.Exists(raw));
    }

    /// <summary>사이드카가 없으면 예전처럼 연다 — BMP 는 BMP 로.</summary>
    [Fact]
    public void 사이드카가_없으면_BMP_는_그대로_BMP_로_연다()
    {
        using Mat image = Pattern(9, 5, MatType.CV_8UC1);
        string bmp = Temp(".bmp");
        Cv2.ImWrite(bmp, image);

        using IImageSource source = ImageSource.Open(bmp);

        Assert.IsType<ByteImageSource>(source);
        Assert.Equal((9, 5), (source.Width, source.Height));
    }
}