using DiePipeline.Core.Imaging;

namespace DiePipeline.Tests;

/// <summary>
/// 자리 계산. ★ 오늘 확인할 것은 "아래→위 뒤집기와 (long) 승격이 맞는가"다.
///
/// 파일을 하나도 안 연다. 그래서 원본이 없는 자리에서도 돈다.
/// 여기 숫자는 전부 <b>지어낸 것</b>이다 — 진짜 파일과 맞는지는 레포 밖 도구로 따로 본다.
/// </summary>
public sealed class GeometryTests
{
    // BMP 규격 상수. 어느 BMP에나 같다.
    private const int BmpHeader = 54;            // 파일헤더 14 + 정보헤더 40
    private const int BmpHeaderWithPalette = 54 + 1024;   // 8비트는 256색 팔레트가 더 붙는다

    /// <summary>24비트 BMP 모양. 폭 6 × 3바이트 = 18 → 4의 배수로 올려 20.</summary>
    private static ImageGeometry Bmp24()
        => new(6, 4, bytesPerPixel: 3, dataOffset: BmpHeader, rowStride: 20, bottomUp: true);

    /// <summary>8비트 BMP 모양. 폭 5 × 1바이트 = 5 → 4의 배수로 올려 8.</summary>
    private static ImageGeometry Bmp8()
        => new(5, 3, bytesPerPixel: 1, dataOffset: BmpHeaderWithPalette, rowStride: 8, bottomUp: true);

    [Fact]
    public void raw는_첫_픽셀이_0번_바이트다()
    {
        Assert.Equal(0, ImageGeometry.Raw(4, 3, 1).OffsetOf(0, 0));
    }

    [Fact]
    public void raw는_옆_픽셀이_바이트수만큼_뒤다()
    {
        Assert.Equal(3, ImageGeometry.Raw(4, 3, 3).OffsetOf(1, 0));
    }

    [Fact]
    public void raw는_아래_픽셀이_한_행만큼_뒤다()
    {
        Assert.Equal(4, ImageGeometry.Raw(4, 3, 1).OffsetOf(0, 1));
    }

    [Fact]
    public void 패딩이_있으면_다음_행은_패딩만큼_더_뒤다()
    {
        // 폭 3 × 1바이트 = 3인데 BMP는 4의 배수로 맞춘다 → 뒤에 1바이트가 남는다
        ImageGeometry g = new(3, 2, bytesPerPixel: 1, rowStride: 4);

        Assert.Equal(3, g.RowBytes);
        Assert.Equal(1, g.RowPadding);
        Assert.Equal(4, g.OffsetOf(0, 1));
    }

    [Fact]
    public void 아래위가_뒤집힌_파일은_맨_아래_행이_첫_행이다()
    {
        ImageGeometry g = new(4, 3, bytesPerPixel: 1, bottomUp: true);

        Assert.Equal(0, g.OffsetOf(0, 2));   // 이미지 맨 아래 = 파일의 처음
        Assert.Equal(8, g.OffsetOf(0, 0));   // 이미지 맨 위  = 파일의 마지막 행
    }

    [Fact]
    public void 뒤집기와_패딩이_겹쳐도_맞는다()
    {
        ImageGeometry g = Bmp24();

        // 맨 아래 행(y=3)이 헤더 바로 다음
        Assert.Equal(BmpHeader, g.OffsetOf(0, 3));

        // 그 행의 마지막 픽셀(x=5)은 3바이트씩 다섯 칸 뒤
        Assert.Equal(BmpHeader + 15, g.OffsetOf(5, 3));

        // 맨 위 행(y=0)은 세 행 뒤 — 패딩 포함 20바이트씩
        Assert.Equal(BmpHeader + 60, g.OffsetOf(0, 0));
    }

    [Fact]
    public void 팔레트가_있으면_픽셀이_그만큼_뒤에서_시작한다()
    {
        ImageGeometry g = Bmp8();

        Assert.Equal(BmpHeaderWithPalette, g.OffsetOf(0, 2));
        Assert.Equal(3, g.RowPadding);
    }

    [Fact]
    public void 기대_파일크기는_시작위치_더하기_행간격곱높이다()
    {
        Assert.Equal(BmpHeader + (20 * 4), Bmp24().ExpectedFileSize);
        Assert.Equal(BmpHeaderWithPalette + (8 * 3), Bmp8().ExpectedFileSize);

        // 패딩이 없으면 폭 × 높이 × 바이트수 그대로
        Assert.Equal(24, ImageGeometry.Raw(4, 2, 3).ExpectedFileSize);
    }

    [Fact]
    public void int를_넘는_이미지에서도_자리가_맞는다()
    {
        // 50000 × 50000 = 25억. int(21억 4748만)를 넘는다.
        ImageGeometry g = ImageGeometry.Raw(50000, 50000, 1);

        // ★ (long) 승격이 빠져 있으면 여기서 조용히 음수가 나온다
        Assert.Equal(2_499_950_000L, g.OffsetOf(0, 49999));
    }

    [Fact]
    public void 컬러_기가픽셀에서도_안_넘친다()
    {
        // 40000 × 40000 × 3바이트 = 48억. uint 범위(42억)마저 넘는다.
        ImageGeometry g = new(40000, 40000, bytesPerPixel: 3, rowStride: 120000);

        Assert.Equal(4_799_880_000L, g.OffsetOf(0, 39999));
    }

    [Fact]
    public void 이미지_밖_픽셀은_거부한다()
    {
        ImageGeometry g = ImageGeometry.Raw(4, 3, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => g.OffsetOf(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.OffsetOf(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.OffsetOf(-1, 0));
    }

    [Fact]
    public void 초기화를_빠뜨린_기하는_아무것도_못_한다()
    {
        // ★ record struct 는 default 가 늘 존재한다 — Width 0 짜리 '빈' 기하.
        //   그게 조용히 흘러다니지 않게, 쓰는 순간 터지게 해 둔다.
        ImageGeometry empty = default;

        Assert.Throws<ArgumentOutOfRangeException>(() => empty.OffsetOf(0, 0));
    }

    [Fact]
    public void 영역이_안에_들어가는지_본다()
    {
        ImageGeometry g = ImageGeometry.Raw(100, 100, 1);

        Assert.True(g.Contains(new Roi(0, 0, 100, 100)));
        Assert.False(g.Contains(new Roi(50, 50, 51, 10)));
        Assert.False(g.Contains(new Roi(-1, 0, 10, 10)));
    }

    [Fact]
    public void 영역을_이미지_안으로_밀어_넣는다()
    {
        ImageGeometry g = ImageGeometry.Raw(100, 100, 1);

        Assert.Equal(new Roi(50, 50, 50, 50), g.Clamp(new Roi(50, 50, 200, 200)));
        Assert.Equal(new Roi(0, 0, 10, 10), g.Clamp(new Roi(-5, -5, 10, 10)));
    }

    [Fact]
    public void 지원하지_않는_값은_거부한다()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageGeometry(4, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageGeometry(0, 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageGeometry(4, 4, 1, rowStride: 2));
    }
}