using DiePipeline.Core.Imaging;
using OpenCvSharp;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// <b>영역을 읽어 주는</b> 이미지 소스.
///
/// ★ 왜 "이미지 한 장"이 아니라 이 인터페이스에 기대나:
///   기가픽셀 원본은 <b>통째로 Mat에 올릴 수가 없다.</b> 572MB 파일이 F32로 바뀌면 2.3GB다.
///   그래서 위층(웨이퍼 러너·뷰어)은 "이미지"를 들고 다니는 대신
///   "필요한 영역을 달라"고 <b>요청</b>한다.
///
///   구현이 바뀌어도 위층은 모른다 —
///     <see cref="ByteImageSource"/> : 파일에서 영역만 뜯어 온다
///     <see cref="MatImageSource"/>  : 이미 메모리에 있는 것을 자른다 (테스트·작은 이미지)
///
/// 돌려주는 것은 늘 <b>GrayF32</b>([0,1] 실수)다. 8비트든 16비트든 24비트든,
/// 위층은 한 가지만 상대하면 된다.
/// </summary>
public interface IImageSource : IDisposable
{
    int Width { get; }

    int Height { get; }

    /// <summary>그 영역만 GrayF32 한 장으로. <b>호출자가 Dispose 한다.</b></summary>
    Mat ReadRegion(Roi region);

    /// <summary>
    /// 긴 변이 <paramref name="maxLongSide"/> 이하가 되게 줄인 전체 그림.
    ///
    /// ★ 평균을 내지 않고 <b>건너뛰며 솎아낸다.</b> 그래야 k행마다 한 줄만 읽어
    ///   파일의 1/k 만 건드린다 — 기가픽셀에서 미리보기를 띄우는 유일한 방법이다.
    ///   교수님도 같은 방식이다. 대신 가는 무늬는 어른거린다(에일리어싱).
    /// </summary>
    Mat ReadOverview(int maxLongSide);
}