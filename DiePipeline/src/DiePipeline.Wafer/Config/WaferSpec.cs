namespace DiePipeline.Wafer.Config;

/// <summary>
/// 웨이퍼 형상 — die 를 어떻게 배치했는가.
///
/// ★ 이건 <b>설계 정보</b>다. "웨이퍼가 실제로 어떻게 놓였는가"(<c>WaferAlignment</c>)와 나눠 둔다.
///   설계는 제품마다 정해져 있고, 놓인 자세는 웨이퍼 한 장마다 다르다.
/// </summary>
public sealed record WaferSpec
{
    public required int Cols { get; init; }

    public required int Rows { get; init; }

    /// <summary>die 원점 사이의 가로 간격. <b>die 폭이 아니다.</b></summary>
    public required int PitchX { get; init; }

    public required int PitchY { get; init; }

    /// <summary>die 하나의 폭. <b>0이면 피치와 같게</b> 본다(스크라이브를 무시).</summary>
    public int DieWidth { get; init; }

    public int DieHeight { get; init; }

    /// <summary>첫 die(0,0)의 왼쪽 위가 설계 좌표의 어디인가.</summary>
    public int OriginX { get; init; }

    public int OriginY { get; init; }

    /// <summary>
    /// 바깥에서 몇 겹을 E0(변두리)로 볼 것인가. <b>0이면 전부 E1.</b>
    ///
    /// ★ 교수님은 <c>edgeExclusionPx</c>(픽셀 단위)를 쓰는데, 실제 코드에서는
    ///   <c>&gt; 0 이면 테두리 한 겹</c> 으로만 쓰인다 — 값이 픽셀인데 <b>켜고 끄는 스위치</b>로 동작한다.
    ///   우리는 뜻이 그대로 드러나게 <b>겹 수</b>로 받는다. 0 = 교수님의 "끔", 1 = 교수님의 "켬"이고,
    ///   2 이상도 표현할 수 있다.
    ///
    /// ★ 그리고 이건 <b>근사</b>다. 진짜 웨이퍼는 원판이라 반지름으로 판정해야 맞는다.
    ///   E0 는 "이미지 테두리"가 아니라 <b>"원판 가장자리"</b> 개념이라는 점을 헷갈리면 안 된다.
    ///   (교수님도 초기 버전에서 이걸 잘못 봐서 안쪽 die 를 E0 로 오인한 적이 있다.)
    /// </summary>
    public int EdgeRings { get; init; }
}