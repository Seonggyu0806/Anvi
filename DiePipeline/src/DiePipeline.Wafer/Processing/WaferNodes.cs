using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Wafer.Processing;

// ★ 웨이퍼 레시피의 블록 넷 — 교수님 Phase 8 과 같은 이름 · 같은 자리(Wafer/Processing).
//   표식 찾기(TemplateMatchAll)는 그림을 다뤄서 Cv 층에 있다.
//
//   [TemplateMatchAll] → [SolveAlignment] → [InspectDies] → [TransformAbs] → [Sampling]
//    표식 찾기            자세 계산          die 전부 검사(경계)  웨이퍼 좌표로     결과 추리기
//
// ★ 블록 사이로 흐르는 '놓인 자세'(PortKind.Alignment) 의 값은 AlignmentResult 다 —
//   자세에 판정(받아들였나 · 짝 수 · 어긋남 · 배율 추정 · 거부 까닭)까지 붙어 있다.
//   그래서 실행기가 "정렬을 믿을 수 없으면 검사를 시작조차 안 한다" 를 지키고, 화면도 까닭을 띄운다.

/// <summary>
/// 자세 계산 — 찾은 표식 자리와 설계 자리로 웨이퍼가 놓인 자세(회전 · 이동)를 풀고, 믿을 만한지 판정한다.
///
/// ★ 계산은 <see cref="AlignmentSolver.SolveAround"/> 그대로 — 대략 원점(<c>firstDieX/Y</c>) 둘레에서 짝짓고,
///   die(0,0) 의 실제 자리까지 낸다. 명령행 · 화면이 쓰는 것과 같은 함수다.
/// ★ 거부돼도 던지지 않는다. 판정을 결과에 담아 내고, 멈추는 것은 실행기가 한다.
/// </summary>
public sealed class SolveAlignmentNode : IPipelineNode
{
    private readonly string _matchesKey;
    private readonly string _outKey;
    private readonly IReadOnlyList<PointF> _expected;
    private readonly PointF _prior;
    private readonly AlignmentOptions _options;

    public SolveAlignmentNode(
        string id, string matchesKey, string outKey, IReadOnlyList<PointF> expected, PointF prior, AlignmentOptions options)
    {
        Id = id;
        _matchesKey = matchesKey;
        _outKey = outKey;
        _expected = expected;
        _prior = prior;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "SolveAlignment";

    public void Execute(IPipelineContext context)
    {
        MatchList matches = context.Get<MatchList>(_matchesKey);

        context.Set(_outKey, AlignmentSolver.SolveAround(matches, _expected, _prior, _options));
    }
}

/// <summary>
/// die 전부 검사 — <b>경계 블록</b>. 엔진이 돌리지 않는다.
///
/// ★ die 반복은 엔진 밖이다(4주차) — 엔진은 정해진 블록을 한 번씩만 돌린다. 그래서 이 블록은
///   설정(격자 · die 레시피 경로 · 원판 · 수동 지정 · 표식 조각)만 담고, 실행기가 여기서 그래프를 잘라
///   앞(정렬) → die 반복 → 뒤(후처리)로 돌린다. 교수님 <c>InspectDies</c> 도 같다.
/// ★ 엔진이 실수로 돌리면 조용히 넘어가지 않고 멈춘다 — 빈 결과가 "결함 0개" 로 읽히면 안 된다.
/// </summary>
public sealed class InspectDiesNode : IPipelineNode
{
    public InspectDiesNode(string id) => Id = id;

    public string Id { get; }

    public string TypeName => "InspectDies";

    public void Execute(IPipelineContext context)
        => throw new InvalidOperationException(
            $"'{Id}'(InspectDies) 는 경계 블록이라 엔진이 돌리지 않습니다 — 웨이퍼 레시피 실행기가 여기서 die 반복을 합니다");
}

/// <summary>
/// 웨이퍼 좌표 — die 안 좌표를 웨이퍼(사진) 좌표로 바꾼다.
/// ★ 계산은 <see cref="AbsoluteTransform.Transform"/> 그대로 — 좌표 변환은 한 곳에서만 한다(4주차).
/// </summary>
public sealed class TransformAbsNode : IPipelineNode
{
    private readonly string _defectsKey;
    private readonly string _gridKey;
    private readonly string _alignmentKey;
    private readonly string _outKey;

    public TransformAbsNode(string id, string defectsKey, string gridKey, string alignmentKey, string outKey)
    {
        Id = id;
        _defectsKey = defectsKey;
        _gridKey = gridKey;
        _alignmentKey = alignmentKey;
        _outKey = outKey;
    }

    public string Id { get; }

    public string TypeName => "TransformAbs";

    public void Execute(IPipelineContext context)
    {
        WaferDefectList defects = context.Get<WaferDefectList>(_defectsKey);
        DieGrid grid = context.Get<DieGrid>(_gridKey);
        AlignmentResult alignment = context.Get<AlignmentResult>(_alignmentKey);

        context.Set(_outKey, AbsoluteTransform.Transform(defects, grid, alignment.Alignment));
    }
}

/// <summary>
/// 결과 추리기 — 결함이 많을 때 보여 줄 것만 남긴다. 검사를 덜 하는 게 아니라 다 하고 결과만 추린다.
/// ★ 계산은 <see cref="WaferSampler.Sample"/> 그대로.
/// </summary>
public sealed class SamplingNode : IPipelineNode
{
    private readonly string _inKey;
    private readonly string _outKey;
    private readonly SamplingOptions _options;

    public SamplingNode(string id, string inKey, string outKey, SamplingOptions options)
    {
        Id = id;
        _inKey = inKey;
        _outKey = outKey;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "Sampling";

    public void Execute(IPipelineContext context)
        => context.Set(_outKey, WaferSampler.Sample(context.Get<WaferDefectList>(_inKey), _options));
}