namespace DiePipeline.Core.Imaging;

/// <summary>
/// 이미지 안의 사각형 한 조각. 단위는 픽셀.
///
/// ★ 왜 (x, y, w, h) 네 개를 따로 들고 다니지 않고 묶는가:
///   네 개를 따로 넘기면 순서를 바꿔 넣어도 컴파일이 된다.
///   Crop(x, y, width, height) 와 Crop(x, width, y, height) 는 둘 다 int 넷이라
///   컴파일러가 못 잡는다. 묶어 두면 통째로 하나라 섞일 수가 없다.
/// </summary>
public readonly record struct Roi(int X, int Y, int Width, int Height)
{
    /// <summary>오른쪽 끝의 <b>다음</b> 칸. 마지막 픽셀은 Right - 1 이다.</summary>
    public int Right => X + Width;

    /// <summary>아래 끝의 <b>다음</b> 칸.</summary>
    public int Bottom => Y + Height;

    public long Area => (long)Width * Height;

    public override string ToString() => $"({X},{Y}) {Width}×{Height}";
}