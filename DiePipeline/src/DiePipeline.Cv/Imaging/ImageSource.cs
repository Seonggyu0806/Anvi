using DiePipeline.Core.Imaging;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// 경로 하나로 알맞은 소스를 연다.
///
/// <code>
///   .bmp  →  메모리 매핑 + 헤더 해석  →  필요한 영역만 읽는다
///   그 외  →  OpenCV 로 통째 읽기      →  작은 파일이면 이게 더 낫다
/// </code>
///
/// ★ 부르는 쪽은 어느 길로 갔는지 몰라도 된다. <see cref="IImageSource"/>만 받으니까.
///   나중에 raw+사이드카를 붙일 때도 여기 한 줄만 는다.
/// </summary>
public static class ImageSource
{
    public static IImageSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"이미지를 못 찾았습니다: {path}", path);
        }

        if (!Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            return new MatImageSource(ImageFile.LoadGrayF32(path));
        }

        IByteReader reader = new MemoryMappedByteReader(path);

        try
        {
            return new ByteImageSource(reader, BmpHeader.Read(reader).Geometry);
        }
        catch
        {
            // ★ 헤더 해석이 실패하면 리더를 놓아 줘야 한다. 안 그러면 파일이 계속 잠겨 있다.
            reader.Dispose();
            throw;
        }
    }
}