using DiePipeline.Wafer.Domain;

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
    ///   원판을 반지름으로 판정하려면 <see cref="Circle"/> 을 쓴다.
    /// </summary>
    public int EdgeRings { get; init; }

    /// <summary>
    /// 원판 — <b>사진 좌표</b>의 중심과 반지름. 주면 칸을 원 기준으로 나눈다.
    ///
    /// <code>
    ///   die 네모가 원판 안에 온전히 들어감   → E1
    ///   원판 가장자리에 걸침                → E0   (교수님: "원판 밖과 겹치는 die만")
    ///   원판에 안 닿음                      → Outside — die 가 아니다
    /// </code>
    ///
    /// ★ 교수님은 이걸 다음에 할 일로 남겼다 — "원 기반 존 자동판정(엣지 die 의 원형 경계)".
    ///   원 정보는 노치·스테이지 좌표와 함께 <b>장비가 주는 값</b>으로 봤다. 지금은 사람이 넣는다.
    ///
    /// ★ <b>사진 좌표다.</b> <c>--expect</c>(die(0,0) 을 원점으로 본 설계 좌표)와 다르다.
    ///   웨이퍼가 밀리든 돌든 원판은 사진 속 그 자리에 있으니, 판정도 정렬을 적용한 실제 자리로 한다.
    ///
    /// ★ <see cref="EdgeRings"/> 와 같이 못 쓴다 — 가장자리를 정하는 방법이 둘이면 어느 쪽이 이겼는지 모른다.
    /// </summary>
    public WaferCircle? Circle { get; init; }

    /// <summary>
    /// 가장자리 여유(px) — 원판 가장자리에서 이만큼 안쪽까지는 <b>검사하지 않는다</b>(edge exclusion). 기본 0.
    ///
    /// ★ <b>잰 원(<see cref="Circle"/>)은 그대로 두고</b>, 검사에 쓰는 원만 줄인다(<see cref="InspectedCircle"/>).
    ///   반지름을 줄여 넣으면 "원판이 어디인가" 와 "어디까지 안 보나" 가 한 숫자에 섞인다.
    /// ★ 원형 실데이터에서 필요했다 — 원을 수 px 오차로 맞추면 그 틈의 배경이 가는 호로 남고,
    ///   노치(원판의 V 홈)는 원으로 보면 '원판 안' 이라 결함이 된다. 여유를 주면 둘 다 가려진다.
    /// ★ 이름·단위가 교수님 <c>edgeExclusionPx</c> 와 같다. 교수님 코드는 그 값을 테두리 한 겹 스위치로만 썼다
    ///   (<see cref="EdgeRings"/> 참고). 여기서는 이름 뜻 그대로 '원판 가장자리에서 몇 px' 다.
    /// ★ 원판(<see cref="Circle"/>)을 줬을 때만 쓴다.
    /// </summary>
    public double EdgeExclusion { get; init; }

    /// <summary>
    /// 검사에 쓰는 원 — 잰 원에서 <see cref="EdgeExclusion"/> 만큼 줄인 것. 칸 판정과 원판 마스크가 <b>같은 원</b>을 본다.
    /// 원판을 안 줬으면 <c>null</c>.
    /// </summary>
    public WaferCircle? InspectedCircle => Circle is { } circle ? circle with { Radius = circle.Radius - EdgeExclusion } : null;

    /// <summary>
    /// 수동 오버라이드 목록 — 자동 존 판정 <b>뒤에</b>, 적힌 die 만 사람이 정한 존으로 덮어쓴다. 기본은 비어 있다.
    ///
    /// ★ 교수님 Phase8 §10: "E0/E1 판정 파라미터화 — 웨이퍼 원 반경·엣지 제외·수동 오버라이드 목록.
    ///   '자동' 판정만 믿지 말 것(노치·플랫 존재)." 교수님 레포에는 문서에만 있고 구현은 없다.
    /// ★ 원판은 완전한 원이 아니다. 노치·플랫이 걸친 die 는 원 판정이 실제 모양과 다르다 — 그런 die 를 여기 적는다.
    /// ★ 제외(<see cref="WaferZone.Outside"/>)는 원판 밖과 똑같이 검사하지 않는다. <b>die 전체</b>가 빠지므로
    ///   명령행이 목록을 결과와 같이 보여 준다 — 사람이 뺀 die 가 '깨끗함' 으로 숨으면 안 된다.
    /// </summary>
    public IReadOnlyList<ZoneOverride> ZoneOverrides { get; init; } = [];
}

/// <summary>die 하나의 존을 사람이 정한다 — 열·행은 격자 번호(0부터), 존은 E1 · E0 · Outside(제외).</summary>
public sealed record ZoneOverride(int Col, int Row, WaferZone Zone);

/// <summary>웨이퍼 원판 — 사진 좌표의 중심과 반지름(px).</summary>
public sealed record WaferCircle(double CenterX, double CenterY, double Radius);