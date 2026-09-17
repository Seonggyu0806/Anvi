using System.Buffers.Binary;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Tests;

/// <summary>
/// BMP 헤더 읽기. ★ 오늘 확인할 것은 "아래→위·패딩·팔레트를 제대로 푸는가"다.
///
/// 파일을 열지 않는다. 헤더를 <b>손으로 조립해서</b> 가짜 리더에 담아 먹인다.
/// 그래서 원본 없이도 파서를 전부 검증할 수 있다.
/// </summary>
public sealed class BmpHeaderTests
{
    /// <summary>
    /// 합성 BMP 한 장. 픽셀은 0,1,2,… 로 채운다.
    /// 높이가 음수면 위→아래 BMP가 된다.
    /// </summary>
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
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14, 4), 40);      // DIB 헤더 크기
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26, 2), 1);       // 평면 수
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28, 2), (ushort)bits);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30, 4), 0);       // 압축 없음

        // 회색 램프 팔레트
        for (int i = 0; i < paletteBytes / 4; i++)
        {
            file[54 + (i * 4)] = (byte)i;
            file[54 + (i * 4) + 1] = (byte)i;
            file[54 + (i * 4) + 2] = (byte)i;
        }

        for (int i = 0; i < stride * rows; i++)
        {
            file[dataOffset + i] = (byte)(i % 256);
        }

        return file;
    }

    private static byte[] Mutate(byte[] source, int at, byte value)
    {
        byte[] copy = (byte[])source.Clone();
        copy[at] = value;
        return copy;
    }

    private static BmpInfo Read(byte[] file)
    {
        using IByteReader reader = new ArrayByteReader(file);
        return BmpHeader.Read(reader);
    }

    [Fact]
    public void 이십사비트_헤더를_푼다()
    {
        // 폭 6 × 3바이트 = 18 → 4의 배수로 올려 20. 남는 2바이트가 패딩.
        BmpInfo info = Read(MakeBmp(6, 4, 24));

        Assert.Equal(6, info.Geometry.Width);
        Assert.Equal(4, info.Geometry.Height);
        Assert.Equal(3, info.Geometry.BytesPerPixel);
        Assert.Equal(54, info.Geometry.DataOffset);
        Assert.Equal(20, info.Geometry.RowStride);
        Assert.Equal(2, info.Geometry.RowPadding);
        Assert.Equal(134, info.Geometry.ExpectedFileSize);
    }

    [Fact]
    public void 팔비트는_팔레트만큼_뒤에서_시작한다()
    {
        // 54 + 256색 × 4바이트 = 1078
        BmpInfo info = Read(MakeBmp(5, 3, 8));

        Assert.Equal(1, info.Geometry.BytesPerPixel);
        Assert.Equal(1078, info.Geometry.DataOffset);
        Assert.Equal(8, info.Geometry.RowStride);    // 폭 5 → 4의 배수로 8
        Assert.Equal(256, info.PaletteColors);
    }

    [Fact]
    public void 이십사비트는_팔레트가_없다()
    {
        Assert.Equal(0, Read(MakeBmp(6, 4, 24)).PaletteColors);
    }

    [Fact]
    public void 회색_램프_팔레트를_알아본다()
    {
        Assert.True(Read(MakeBmp(5, 3, 8)).GrayPalette);
    }

    [Fact]
    public void 회색이_아닌_팔레트를_잡아낸다()
    {
        // ★ 이게 왜 중요한가 — 램프가 아니면 바이트 값은 밝기가 아니라 '색 번호'다.
        //   모르고 그냥 읽으면 엉뚱한 그림이 나오는데, 예외는 안 난다.
        byte[] file = MakeBmp(5, 3, 8);
        file[54 + 4] = 9;   // 1번 색의 파랑만 바꿔 램프를 깬다

        Assert.False(Read(file).GrayPalette);
    }

    [Fact]
    public void 높이가_양수면_아래에서_위로_저장된_것이다()
    {
        Assert.True(Read(MakeBmp(6, 4, 24)).Geometry.BottomUp);
    }

    [Fact]
    public void 높이가_음수면_위에서_아래로_저장된_것이다()
    {
        // ★ BMP의 약속 — 음수 높이는 "거꾸로가 아니라 똑바로 저장했다"는 표시다.
        BmpInfo info = Read(MakeBmp(6, -4, 24));

        Assert.False(info.Geometry.BottomUp);
        Assert.Equal(4, info.Geometry.Height);   // 높이 자체는 양수로
    }

    [Fact]
    public void 헤더와_읽기를_이어_붙이면_행이_나온다()
    {
        // ★ 7-1 + 7-2 + 7-3 = 진짜 부분 읽기. 여기까지가 오늘의 목적지다.
        byte[] file = MakeBmp(5, 3, 8);
        using IByteReader reader = new ArrayByteReader(file);

        ImageGeometry g = BmpHeader.Read(reader).Geometry;
        byte[] row = new byte[g.RowBytes];

        // 아래→위라서 이미지 맨 아래 행(y=2)이 파일의 첫 행이다.
        reader.ReadInto(g.OffsetOf(0, 2), row, 0, g.RowBytes);
        Assert.Equal(0, row[0]);

        // 이미지 맨 위 행(y=0)은 두 행 뒤 — 패딩 포함 8바이트씩
        reader.ReadInto(g.OffsetOf(0, 0), row, 0, g.RowBytes);
        Assert.Equal(16, row[0]);
    }

    [Fact]
    public void BMP가_아니면_거부한다()
    {
        Assert.Throws<InvalidDataException>(() => Read(Mutate(MakeBmp(6, 4, 24), 0, (byte)'P')));
    }

    [Fact]
    public void 압축된_BMP는_거부한다()
    {
        // 압축은 행마다 길이가 달라서 자리 계산이 성립하지 않는다 — 부분 읽기가 불가능하다.
        Assert.Throws<InvalidDataException>(() => Read(Mutate(MakeBmp(6, 4, 24), 30, 1)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(32)]
    public void 지원하지_않는_비트수는_거부한다(byte bits)
    {
        Assert.Throws<InvalidDataException>(() => Read(Mutate(MakeBmp(6, 4, 24), 28, bits)));
    }

    [Fact]
    public void 오래된_형식은_거부한다()
    {
        // DIB 헤더 12바이트는 1990년대 BITMAPCOREHEADER다. 칸 배치가 다르다.
        Assert.Throws<InvalidDataException>(() => Read(Mutate(MakeBmp(6, 4, 24), 14, 12)));
    }

    [Fact]
    public void 잘린_파일은_거부한다()
    {
        // 헤더는 134바이트라고 하는데 실제로는 100바이트뿐
        Assert.Throws<InvalidDataException>(() => Read(MakeBmp(6, 4, 24).AsSpan(0, 100).ToArray()));
    }

    [Fact]
    public void 헤더보다_짧으면_거부한다()
    {
        Assert.Throws<InvalidDataException>(() => Read(new byte[20]));
    }
}