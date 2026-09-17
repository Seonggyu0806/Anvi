using System.Buffers.Binary;

namespace DiePipeline.Core.Imaging;

/// <summary>BMP 헤더에서 읽어낸 것.</summary>
public sealed record BmpInfo
{
    /// <summary>자리 계산에 필요한 것 전부. 이게 본체다.</summary>
    public required ImageGeometry Geometry { get; init; }

    /// <summary>8 또는 24.</summary>
    public required int BitsPerPixel { get; init; }

    /// <summary>팔레트 색 수. 24비트는 0.</summary>
    public required int PaletteColors { get; init; }

    /// <summary>
    /// 팔레트가 0,0,0 / 1,1,1 / … 회색 램프인가.
    ///
    /// ★ true 여야 <b>바이트 값을 그대로 밝기로</b> 쓸 수 있다.
    ///   false면 팔레트를 거쳐야 하는데, 그건 이미지가 회색이 아니라는 뜻이다.
    /// </summary>
    public required bool GrayPalette { get; init; }
}

/// <summary>
/// BMP 헤더를 읽어 <see cref="ImageGeometry"/>로 바꾼다.
///
/// ★ 이 파서는 <b>헤더만</b> 읽는다. 픽셀은 한 장도 안 읽는다.
///   기가바이트 파일이어도 앞쪽 54바이트(+팔레트 1KB)만 건드린다.
///
/// ★ 그리고 <see cref="IByteReader"/>만 받는다 — 파일 경로가 아니라.
///   그래서 <b>가짜 리더에 손으로 만든 헤더를 담아</b> 전부 검증할 수 있다.
///   회사 자료가 없어도, 디스크가 없어도 테스트가 돈다.
/// </summary>
public static class BmpHeader
{
    /// <summary>파일 헤더 14바이트.</summary>
    public const int FileHeaderSize = 14;

    /// <summary>요즘 쓰는 DIB 헤더(BITMAPINFOHEADER) 40바이트.</summary>
    public const int MinDibHeaderSize = 40;

    /// <summary>둘을 합친 최소 헤더 크기.</summary>
    public const int MinHeaderSize = FileHeaderSize + MinDibHeaderSize;

    public static BmpInfo Read(IByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.Length < MinHeaderSize)
        {
            throw new InvalidDataException($"BMP 헤더보다 짧습니다 — {reader.Length}바이트 (최소 {MinHeaderSize})");
        }

        byte[] head = new byte[MinHeaderSize];
        reader.ReadInto(0, head, 0, MinHeaderSize);

        // 'B','M' — 매직 넘버. 확장자를 믿지 않고 내용을 본다.
        if (head[0] != (byte)'B' || head[1] != (byte)'M')
        {
            throw new InvalidDataException(
                $"BMP가 아닙니다 — 첫 두 글자가 'BM'이어야 하는데 0x{head[0]:X2} 0x{head[1]:X2} 입니다");
        }

        long dataOffset = ReadUInt32(head, 10);
        int dibSize = (int)ReadUInt32(head, 14);

        if (dibSize < MinDibHeaderSize)
        {
            throw new InvalidDataException($"오래된 BMP 형식입니다 — DIB 헤더 {dibSize}바이트 (40 이상만 지원)");
        }

        int width = ReadInt32(head, 18);
        int rawHeight = ReadInt32(head, 22);
        int bitsPerPixel = ReadUInt16(head, 28);
        int compression = (int)ReadUInt32(head, 30);
        int paletteColors = (int)ReadUInt32(head, 46);

        // ★ 압축된 BMP는 자리 계산이 성립하지 않는다 — 행마다 길이가 달라서
        //   "몇 번째 바이트냐"를 곱셈으로 구할 수 없다. 부분 읽기 자체가 불가능하다.
        if (compression != 0)
        {
            throw new InvalidDataException($"압축된 BMP는 지원하지 않습니다 (compression {compression}, 0만 지원)");
        }

        if (width < 1 || rawHeight == 0)
        {
            throw new InvalidDataException($"크기가 이상합니다 — {width} × {rawHeight}");
        }

        int bytesPerPixel = bitsPerPixel switch
        {
            8 => 1,
            24 => 3,

            // 16비트 BMP는 회색 16비트가 아니라 색을 5-5-5로 욱여넣은 것이라 다르게 풀어야 한다.
            // 32비트는 알파가 붙는다. 지금 필요 없으니 명확히 거부한다.
            _ => throw new InvalidDataException(
                $"{bitsPerPixel}비트 BMP는 지원하지 않습니다 (8비트 회색·24비트만)"),
        };

        // ★ 높이가 음수 = 위→아래로 저장했다는 표시. 양수면 BMP 기본인 아래→위.
        bool bottomUp = rawHeight > 0;
        int height = Math.Abs(rawHeight);

        // ★ 한 행은 반드시 4의 배수로 맞춘다. 남는 자리가 패딩이다.
        //   +31 후 32로 나누는 건 "올림"을 정수 연산으로 하는 흔한 방법이다.
        int rowStride = ((width * bitsPerPixel) + 31) / 32 * 4;

        ImageGeometry geometry = new(width, height, bytesPerPixel, dataOffset, rowStride, bottomUp);

        // 헤더가 약속한 크기보다 파일이 작으면, 읽다가 파일 밖으로 나간다.
        // 나중에 17번째 die에서 터지느니 지금 터지는 게 낫다.
        if (geometry.ExpectedFileSize > reader.Length)
        {
            throw new InvalidDataException(
                $"파일이 잘려 있습니다 — 헤더는 {geometry.ExpectedFileSize}바이트를 기대하는데 실제는 {reader.Length}바이트");
        }

        // colorsUsed가 0이면 "쓸 수 있는 만큼 다" — 8비트면 256색이다.
        int colors = paletteColors > 0 ? paletteColors : (bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0);

        return new BmpInfo
        {
            Geometry = geometry,
            BitsPerPixel = bitsPerPixel,
            PaletteColors = colors,
            GrayPalette = colors > 0 && IsGrayPalette(reader, FileHeaderSize + dibSize, colors),
        };
    }

    /// <summary>
    /// 팔레트가 0,0,0 / 1,1,1 / … 회색 램프인가.
    ///
    /// 팔레트 한 칸은 4바이트다 — 파랑, 초록, 빨강, 안 쓰는 1바이트.
    /// (순서가 BGR인 것도 BMP의 오래된 관습이다.)
    /// </summary>
    private static bool IsGrayPalette(IByteReader reader, long at, int colors)
    {
        int bytes = colors * 4;

        if (at + bytes > reader.Length)
        {
            return false;
        }

        byte[] palette = new byte[bytes];
        reader.ReadInto(at, palette, 0, bytes);

        for (int i = 0; i < colors; i++)
        {
            int p = i * 4;

            // 세 채널이 같고, 그 값이 곧 번호여야 "그대로 밝기로 써도 된다".
            if (palette[p] != palette[p + 1] || palette[p + 1] != palette[p + 2] || palette[p] != i)
            {
                return false;
            }
        }

        return true;
    }

    // ★ BMP는 숫자를 거꾸로 적는다(little-endian). 값 513은 파일에 01 02 00 00 으로 들어 있다.
    //   손으로 b[0] + b[1]*256 + … 하면 부호·자릿수에서 실수한다. 표준 도구를 쓴다.
    private static uint ReadUInt32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));

    private static int ReadInt32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));

    private static int ReadUInt16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
}