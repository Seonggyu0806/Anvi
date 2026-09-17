namespace DiePipeline.Core.Imaging;

/// <summary>
/// 파일 안에서 픽셀 (x, y)가 <b>몇 번째 바이트</b>인지 계산한다.
///
/// ★ 이 구조체는 파일도 OpenCV도 모른다 — 순수 산수다.
///   그래서 원본을 열지 않고도, 이미지 한 장 없이도 테스트가 밀리초 단위로 돈다.
///   교수님이 <c>RawImageGeometry</c>를 따로 떼어 놓고 "틀리기 쉬운 부분을 격리"라고
///   적어 둔 자리가 여기다.
///
/// <b>raw 와 BMP 를 한 타입으로 표현한다.</b> 둘은 픽셀 데이터가 똑같고,
/// 다른 것은 단 두 가지뿐이다 —
///   ① 픽셀이 <b>몇 번째 바이트부터</b> 시작하나 (<see cref="DataOffset"/>)
///   ② 파일의 첫 행이 <b>이미지의 맨 아래</b> 행인가 (<see cref="BottomUp"/>)
/// </summary>
public readonly record struct ImageGeometry
{
    /// <param name="dataOffset">픽셀이 시작되는 자리. raw는 0, BMP는 헤더(+팔레트) 다음.</param>
    /// <param name="rowStride">
    /// 한 행이 차지하는 바이트. <b>0을 주면 패딩 없음</b>(width × bytesPerPixel)으로 본다.
    /// BMP는 한 행을 4의 배수로 맞추느라 뒤에 남는 자리가 붙을 수 있어서 따로 받는다.
    /// </param>
    /// <param name="bottomUp">파일의 첫 행이 이미지 맨 아래 행인가. BMP의 기본이 이것이다.</param>
    public ImageGeometry(
        int width,
        int height,
        int bytesPerPixel,
        long dataOffset = 0,
        int rowStride = 0,
        bool bottomUp = false)
    {
        // 입구에서 막는다. 여기서 안 막으면 증상이 "결함이 왜 이렇게 많지?"로만 나타난다.
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"크기는 1 이상이어야 합니다 (현재 {width}×{height})");
        }

        if (bytesPerPixel is not (1 or 2 or 3))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesPerPixel),
                $"바이트/픽셀은 1(회색8)·2(회색16)·3(BGR24)만 지원합니다 (현재 {bytesPerPixel})");
        }

        if (dataOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataOffset), "픽셀 시작 위치는 0 이상이어야 합니다");
        }

        int packed = width * bytesPerPixel;

        // 행 간격이 한 행보다 좁으면 행끼리 겹친다 — 읽으면 그림이 비스듬히 밀린다.
        if (rowStride != 0 && rowStride < packed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowStride), $"행 간격 {rowStride}가 한 행 {packed}바이트보다 좁습니다");
        }

        Width = width;
        Height = height;
        BytesPerPixel = bytesPerPixel;
        DataOffset = dataOffset;
        RowStride = rowStride == 0 ? packed : rowStride;
        BottomUp = bottomUp;
    }

    public int Width { get; }

    public int Height { get; }

    public int BytesPerPixel { get; }

    /// <summary>픽셀 데이터가 시작되는 바이트 위치.</summary>
    public long DataOffset { get; }

    /// <summary>한 행에서 다음 행까지의 바이트 거리. 패딩을 포함한다.</summary>
    public int RowStride { get; }

    /// <summary>파일의 첫 행이 이미지의 맨 아래 행인가.</summary>
    public bool BottomUp { get; }

    /// <summary>패딩을 뺀, 진짜 픽셀이 들어 있는 한 행의 바이트 수.</summary>
    public int RowBytes => Width * BytesPerPixel;

    /// <summary>한 행 끝에 붙는 남는 바이트 수. raw는 0.</summary>
    public int RowPadding => RowStride - RowBytes;

    /// <summary>이 기하가 맞다면 파일은 최소 이만큼이어야 한다.</summary>
    public long ExpectedFileSize => DataOffset + ((long)RowStride * Height);

    /// <summary>
    /// 픽셀 (x, y)가 파일의 몇 번째 바이트인가.
    ///
    /// ★ <c>(long)</c> 캐스팅이 왜 붙어 있나:
    ///   <c>int</c>는 21억 4748만까지다. 요즘 스캔 한 장은 수억~십수억 픽셀이라
    ///   지금 들어가더라도 <b>여유가 얼마 없다</b>.
    ///   폭이 1.5배만 커져도 넘치는데, 넘칠 때 C#은 예외를 던지지 않고
    ///   <b>조용히 음수로 감는다.</b> 그러면 파일 앞쪽 엉뚱한 데를 읽는다.
    ///   곱하기 전에 (long)을 붙여야 계산 자체가 64비트로 일어난다.
    /// </summary>
    public long OffsetOf(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x), $"픽셀 ({x},{y})는 이미지 {Width}×{Height} 밖입니다");
        }

        // ★ 아래→위 저장이면 y를 뒤집는다. 이 한 줄을 빠뜨리면
        //   웨이퍼 맵이 통째로 위아래 뒤집히고, die 인덱스가 전부 어긋난다.
        int fileRow = BottomUp ? Height - 1 - y : y;

        return DataOffset + ((long)fileRow * RowStride) + ((long)x * BytesPerPixel);
    }

    /// <summary>이 영역이 이미지 안에 온전히 들어가는가.</summary>
    public bool Contains(Roi r)
        => r.Width > 0 && r.Height > 0
           && r.X >= 0 && r.Y >= 0
           && r.Right <= Width && r.Bottom <= Height;

    /// <summary>영역을 이미지 안으로 밀어 넣는다. 최소 1×1.</summary>
    public Roi Clamp(Roi r)
    {
        int x = Math.Clamp(r.X, 0, Width - 1);
        int y = Math.Clamp(r.Y, 0, Height - 1);

        return new Roi(x, y, Math.Clamp(r.Width, 1, Width - x), Math.Clamp(r.Height, 1, Height - y));
    }

    /// <summary>헤더 없는 raw — 첫 바이트부터, 패딩 없음, 위→아래.</summary>
    public static ImageGeometry Raw(int width, int height, int bytesPerPixel)
        => new(width, height, bytesPerPixel);

    public override string ToString()
        => $"{Width}×{Height} {BytesPerPixel}B/px  시작 {DataOffset}  행간격 {RowStride}"
           + (BottomUp ? "  아래→위" : "  위→아래");
}