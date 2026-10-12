using DiePipeline.Core.Imaging;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// 경로 하나로 알맞은 소스를 연다.
///
/// <code>
///   사이드카 있음  →  메모리 매핑 + 사이드카 기하(raw)  →  필요한 영역만 읽는다
///   .bmp         →  메모리 매핑 + 헤더 해석          →  필요한 영역만 읽는다
///   그 외         →  OpenCV 로 통째 읽기              →  작은 파일이면 이게 더 낫다
/// </code>
///
/// ★ 부르는 쪽은 어느 길로 갔는지 몰라도 된다. <see cref="IImageSource"/>만 받으니까.
///   명령행(run · probe · wafer)과 화면이 전부 여기를 거친다.
/// ★ 사이드카를 맨 먼저 본다 — 교수님 <c>ImageSource.Open</c> 과 같은 순서다.
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

        // ★ raw 는 헤더가 없어 폭·높이를 파일에서 알 수 없다 — 옆의 사이드카 JSON 이 알려 준다.
        string? sidecar = RawImageMeta.FindSidecar(path);

        if (sidecar is not null)
        {
            return OpenRaw(path, RawImageMeta.Load(sidecar));
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

    /// <summary>
    /// raw 파일을 사이드카 정보로 연다.
    ///
    /// ★ 파일 크기가 <b>폭 × 높이 × 바이트 수와 정확히 같아야</b> 한다. raw 에는 헤더도 패딩도 없어서
    ///   크기가 다르면 사이드카가 다른 파일 것이거나 값이 틀린 것이다. 그대로 읽으면 행이 비스듬히 밀린
    ///   그림이 오류 없이 나온다.
    /// </summary>
    public static IImageSource OpenRaw(string path, RawImageMeta meta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(meta);

        // 행 순서 · 폭 · 높이 · 바이트 수는 파일을 열기 전에 검사한다.
        ImageGeometry geometry = meta.ToGeometry();

        IByteReader reader = new MemoryMappedByteReader(path);

        try
        {
            if (reader.Length != meta.ExpectedFileSize)
            {
                throw new InvalidDataException(
                    $"raw 크기가 사이드카와 다릅니다 — 파일 {reader.Length}바이트, "
                    + $"사이드카 {meta.Width}×{meta.Height}×{meta.BytesPerPixel} = {meta.ExpectedFileSize}바이트");
            }

            return new ByteImageSource(reader, geometry);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}