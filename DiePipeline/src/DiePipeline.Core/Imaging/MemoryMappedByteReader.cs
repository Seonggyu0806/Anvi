using System.IO.MemoryMappedFiles;

namespace DiePipeline.Core.Imaging;

/// <summary>
/// <b>메모리 매핑</b>으로 큰 파일을 읽는 진짜 리더.
///
/// ★ 메모리 매핑이 뭔가 — 파일을 "메모리 주소로 취급하겠다"고 운영체제에 신고하는 것이다.
///   신고만 할 뿐 <b>아무것도 안 읽는다.</b> 나중에 어떤 자리를 실제로 건드리면,
///   그때 운영체제가 그 부분(보통 4KB 한 쪽)만 몰래 디스크에서 가져온다.
///
///   도서관 비유: 책 1,000권을 통째로 빌려 오는 대신 <b>열람 신청서만</b> 내 둔다.
///   펼친 쪽만 사서가 가져다준다. 1.3GB 파일이어도 die 한 칸만 보면 그만큼만 읽힌다.
///
/// ★ 그리고 <b>mmap이 이 프로젝트에서 등장하는 유일한 곳</b>이다.
///   나중에 네트워크 스토리지든 타일 캐시든 바꿔도 이 파일 하나만 고친다.
///   나머지 코드는 <see cref="IByteReader"/>만 알고 있으니까.
///
/// 64비트 전제다. 32비트에서는 주소 공간이 모자라 큰 파일을 통째로 매핑할 수 없다.
/// </summary>
public sealed class MemoryMappedByteReader : IByteReader
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;

    public MemoryMappedByteReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 파일이 없으면 여기서 FileNotFoundException이 난다 — 매핑을 시도하기 전에.
        Length = new FileInfo(path).Length;

        if (Length == 0)
        {
            throw new InvalidDataException($"빈 파일입니다: {path}");
        }

        // capacity 0 = "파일 크기 그대로". Read 로 열어 실수로도 원본을 못 건드리게 한다.
        _file = MemoryMappedFile.CreateFromFile(
            path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);

        // (0, 0) = 파일 전체를 창으로 잡는다. 잡기만 할 뿐 읽지는 않는다.
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public long Length { get; }

    public void ReadInto(long offset, byte[] buffer, int bufferOffset, int count)
    {
        // ★ 가짜와 똑같은 검사를 쓴다. 둘이 다르게 굴면 테스트가 거짓말을 하게 된다.
        ByteRange.Require(Length, offset, buffer, bufferOffset, count);

        _view.ReadArray(offset, buffer, bufferOffset, count);
    }

    public void Dispose()
    {
        // 순서가 있다 — 창을 먼저 닫고 파일을 닫는다. 반대로 하면 창이 사라진 파일을 붙들고 있게 된다.
        _view.Dispose();
        _file.Dispose();
    }
}