namespace DiePipeline.Core.Imaging;

/// <summary>
/// 파일에서 <b>바이트 구간</b>을 읽어 오는 입구.
///
/// ★ 이 인터페이스는 이미지가 뭔지 모른다. 픽셀도, 행도, BMP도 모른다.
///   "몇 번째부터 몇 바이트"만 안다. 그 위(<see cref="ImageGeometry"/>)에서 의미를 붙인다.
///
/// 구현이 둘이다 —
///   <see cref="MemoryMappedByteReader"/> : 진짜 파일. 기가바이트를 RAM 없이 읽는다.
///   <see cref="ArrayByteReader"/>        : byte[] 하나를 파일인 척한다. 테스트용.
///
/// 가짜가 있어서 <b>원본 파일 없이</b> 부분 읽기 로직을 전부 검증할 수 있다.
/// 회사 자료를 테스트에 끌어들이지 않아도 된다는 뜻이기도 하다.
/// </summary>
public interface IByteReader : IDisposable
{
    /// <summary>전체 바이트 수.</summary>
    long Length { get; }

    /// <summary>
    /// <paramref name="offset"/>부터 <paramref name="count"/>바이트를
    /// <paramref name="buffer"/>의 <paramref name="bufferOffset"/> 자리에 채운다.
    ///
    /// ★ 왜 <c>byte[] Read(offset, count)</c> 로 만들지 않았나:
    ///   그러면 부를 때마다 배열을 새로 만든다. die 한 칸이 수천 행이면 <b>배열도 수천 개</b>다.
    ///   버퍼를 받아서 채우면 하나를 계속 다시 쓴다. GC가 할 일이 사라진다.
    ///
    /// ★ <paramref name="bufferOffset"/>이 왜 있나:
    ///   큰 버퍼 하나에 행을 <b>차례로 이어 붙일</b> 때 쓴다.
    ///   0행은 0자리에, 1행은 폭만큼 뒤에… 이렇게.
    /// </summary>
    void ReadInto(long offset, byte[] buffer, int bufferOffset, int count);
}

/// <summary>
/// 두 리더가 <b>함께 쓰는</b> 범위 검사.
///
/// ★ 같은 규칙을 두 군데 적으면 반드시 갈라진다 — 가짜는 막는데 진짜는 통과시키는
///   상황이 오면, 테스트는 초록불인데 실데이터에서만 터진다. 그게 제일 잡기 어렵다.
/// </summary>
internal static class ByteRange
{
    public static void Require(long length, long offset, byte[] buffer, int bufferOffset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"읽을 바이트 수는 0 이상이어야 합니다 (현재 {count})");
        }

        // ★ 여기서 안 막으면 파일 밖을 읽는다. mmap은 그걸 예외로 알려주지 않고
        //   운이 나쁘면 쓰레기 값을, 운이 더 나쁘면 프로세스를 죽인다.
        if (offset < 0 || offset + count > length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), $"{offset}부터 {count}바이트는 파일 {length}바이트를 벗어납니다");
        }

        if (bufferOffset < 0 || bufferOffset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bufferOffset), $"버퍼 {buffer.Length}바이트에 {bufferOffset}부터 {count}바이트를 담을 수 없습니다");
        }
    }
}