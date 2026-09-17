namespace DiePipeline.Core.Imaging;

/// <summary>
/// <c>byte[]</c> 하나를 파일인 척하는 <b>가짜</b> 리더.
///
/// ★ 이게 있어서 파일도 디스크도 없이 자리 계산과 조립을 검증할 수 있다.
///   교수님도 <c>ArrayByteReader</c>를 같은 이유로 뒀다 —
///   "offset 산술·ReadRegion 조립 로직을 mmap/디스크 없이 헤드리스로 검증하게 한다."
///
/// 작은 이미지를 실제로 다룰 때도 쓸 수 있다. 가짜라고 못 쓰는 게 아니다.
/// </summary>
public sealed class ArrayByteReader : IByteReader
{
    private readonly byte[] _data;

    public ArrayByteReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    public long Length => _data.Length;

    public void ReadInto(long offset, byte[] buffer, int bufferOffset, int count)
    {
        ByteRange.Require(Length, offset, buffer, bufferOffset, count);

        Array.Copy(_data, offset, buffer, bufferOffset, count);
    }

    /// <summary>놓을 게 없다. 인터페이스를 맞추려고 있는 빈 메서드다.</summary>
    public void Dispose()
    {
    }
}