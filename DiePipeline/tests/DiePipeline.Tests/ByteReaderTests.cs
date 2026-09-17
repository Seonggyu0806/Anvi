using DiePipeline.Core.Imaging;

namespace DiePipeline.Tests;

/// <summary>
/// 바이트 읽기. ★ 오늘 확인할 것은 "가짜가 진짜와 똑같이 구는가"다.
///
/// 그게 보장돼야 가짜로 한 테스트를 믿을 수 있다.
/// 쓰는 파일은 테스트가 직접 만든 임시 파일이다 — 회사 자료를 끌어들이지 않는다.
/// </summary>
public sealed class ByteReaderTests : IDisposable
{
    // 0,1,2,…,255,0,1,… 로 채운 1000바이트.
    // ★ 값이 곧 자리라서, 한 칸이라도 어긋나면 눈에 바로 보인다.
    private static readonly byte[] Pattern = MakePattern(1000);

    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"diepipe-{Guid.NewGuid():N}.bin");

    public ByteReaderTests() => File.WriteAllBytes(_path, Pattern);

    public void Dispose() => File.Delete(_path);

    private static byte[] MakePattern(int size)
    {
        byte[] data = new byte[size];

        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 256);
        }

        return data;
    }

    [Fact]
    public void 원하는_구간을_준다()
    {
        using IByteReader reader = new ArrayByteReader(Pattern);
        byte[] buffer = new byte[4];

        reader.ReadInto(10, buffer, 0, 4);

        Assert.Equal(new byte[] { 10, 11, 12, 13 }, buffer);
    }

    [Fact]
    public void 버퍼_중간부터_채운다()
    {
        // ★ 행을 큰 버퍼에 이어 붙일 때 쓰는 기능이다. 앞뒤는 건드리지 않아야 한다.
        using IByteReader reader = new ArrayByteReader(Pattern);
        byte[] buffer = new byte[6];

        reader.ReadInto(100, buffer, 2, 3);

        Assert.Equal(new byte[] { 0, 0, 100, 101, 102, 0 }, buffer);
    }

    [Fact]
    public void 길이를_알려준다()
    {
        using IByteReader fake = new ArrayByteReader(Pattern);
        using IByteReader real = new MemoryMappedByteReader(_path);

        Assert.Equal(1000, fake.Length);
        Assert.Equal(1000, real.Length);
    }

    [Fact]
    public void 파일_끝까지_읽는다()
    {
        using IByteReader reader = new MemoryMappedByteReader(_path);
        byte[] buffer = new byte[3];

        reader.ReadInto(997, buffer, 0, 3);

        Assert.Equal(new byte[] { 229, 230, 231 }, buffer);
    }

    [Theory]
    [InlineData(-1, 4)]      // 음수 자리
    [InlineData(998, 4)]     // 끝을 4바이트 넘어간다
    [InlineData(1000, 1)]    // 아예 밖
    [InlineData(0, -1)]      // 음수 개수
    public void 파일_밖은_거부한다(long offset, int count)
    {
        using IByteReader fake = new ArrayByteReader(Pattern);
        using IByteReader real = new MemoryMappedByteReader(_path);
        byte[] buffer = new byte[16];

        // ★ 둘이 똑같이 거부해야 한다. 한쪽만 막으면 테스트가 거짓말을 한다.
        Assert.Throws<ArgumentOutOfRangeException>(() => fake.ReadInto(offset, buffer, 0, count));
        Assert.Throws<ArgumentOutOfRangeException>(() => real.ReadInto(offset, buffer, 0, count));
    }

    [Fact]
    public void 버퍼보다_많이_달라면_거부한다()
    {
        using IByteReader reader = new ArrayByteReader(Pattern);
        byte[] buffer = new byte[4];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadInto(0, buffer, 2, 4));
    }

    [Fact]
    public void 진짜와_가짜가_같은_답을_준다()
    {
        // ★ 오늘의 핵심. 이게 깨지면 가짜로 한 모든 테스트가 무의미해진다.
        using IByteReader fake = new ArrayByteReader(Pattern);
        using IByteReader real = new MemoryMappedByteReader(_path);

        foreach ((long offset, int count) in new[] { (0L, 1), (0L, 1000), (37L, 128), (999L, 1) })
        {
            byte[] fromFake = new byte[count];
            byte[] fromReal = new byte[count];

            fake.ReadInto(offset, fromFake, 0, count);
            real.ReadInto(offset, fromReal, 0, count);

            Assert.Equal(fromFake, fromReal);
        }
    }

    [Fact]
    public void 여러_번_읽어도_같다()
    {
        // mmap은 운영체제가 페이지를 들였다 내렸다 한다. 그래도 결과는 늘 같아야 한다.
        using IByteReader reader = new MemoryMappedByteReader(_path);
        byte[] first = new byte[64];
        byte[] second = new byte[64];

        reader.ReadInto(200, first, 0, 64);
        reader.ReadInto(200, second, 0, 64);

        Assert.Equal(first, second);
    }

    [Fact]
    public void 없는_파일은_거부한다()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"diepipe-none-{Guid.NewGuid():N}.bin");

        Assert.Throws<FileNotFoundException>(() => new MemoryMappedByteReader(missing).Dispose());
    }

    [Fact]
    public void 빈_파일은_거부한다()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"diepipe-empty-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(empty, []);

        try
        {
            Assert.Throws<InvalidDataException>(() => new MemoryMappedByteReader(empty).Dispose());
        }
        finally
        {
            File.Delete(empty);
        }
    }

    [Fact]
    public void 자리계산과_이어_붙이면_한_행이_나온다()
    {
        // ★ 7-1 + 7-2 = 부분 읽기. 여기까지가 오늘의 목적지다.
        //   폭 10 · 높이 4 · 1바이트 · 헤더 2 · 아래→위 인 '파일'을 흉내 낸다.
        ImageGeometry geometry = new(10, 4, bytesPerPixel: 1, dataOffset: 2, rowStride: 10, bottomUp: true);
        using IByteReader reader = new ArrayByteReader(Pattern);

        // 이미지 맨 위 행(y=0)은 아래→위라서 파일의 마지막 행이다.
        // 2 + (4-1-0) × 10 = 32
        long where = geometry.OffsetOf(0, 0);
        Assert.Equal(32, where);

        byte[] row = new byte[geometry.RowBytes];
        reader.ReadInto(where, row, 0, geometry.RowBytes);

        // 값이 곧 자리이므로 32부터 41까지 나와야 한다
        Assert.Equal(32, row[0]);
        Assert.Equal(41, row[9]);
    }
}