namespace DiePipeline.Core.Configuration;

/// <summary>범위(Min/Max)만으로 표현 못 하는 추가 규칙.</summary>
public enum ParamConstraint
{
    None,

    /// <summary>홀수만 — 컨볼루션 커널은 중심 픽셀이 있어야 하므로.</summary>
    OddOnly,

    Positive,
}

/// <summary>
/// 파라미터 하나의 메타데이터 = <b>단일 진실원</b>.
///
/// ★ 이 선언 하나에서 넷이 파생된다:
///     ① 레시피 검증   — "kernel=4 는 홀수여야 한다"  (Step 3)
///     ② WPF 속성 UI  — 1~31 스핀박스를 자동 생성     (Step 8)
///     ③ JSON Schema  — 에디터 자동완성
///     ④ 문서         — 파라미터 표
///
///   이 넷을 손으로 따로 만들면 <b>반드시 어긋난다</b>.
///   코드는 홀수를 요구하는데 UI는 짝수를 받고 문서엔 최대 15라고 적힌 상태 —
///   실무에서 가장 흔한 종류의 버그다. 선언을 한 군데로 모아 원천 차단한다.
/// </summary>
public sealed record ParamDescriptor(
    string Name,
    Type Type,
    object? Default = null,
    object? Min = null,
    object? Max = null,
    string[]? Choices = null,
    string? Description = null,
    bool Required = false,
    ParamConstraint Constraint = ParamConstraint.None);