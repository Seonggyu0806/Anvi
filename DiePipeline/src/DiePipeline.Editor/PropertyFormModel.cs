using System.Globalization;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Registry;

namespace DiePipeline.Editor;

/// <summary>속성폼이 그릴 칸의 종류. <see cref="ParamDescriptor"/>의 Type과 Choices에서 정해진다.</summary>
public enum FieldControl
{
    /// <summary>체크박스</summary>
    Bool,

    /// <summary>정수 스피너 — Min/Max가 범위가 된다</summary>
    Integer,

    /// <summary>실수 입력칸</summary>
    Number,

    /// <summary>콤보박스 — Choices가 항목이 된다</summary>
    Choice,

    /// <summary>자유 입력칸</summary>
    Text,

    /// <summary>파일 경로 — 입력칸 + 찾아보기. 고른 파일은 레시피 폴더 기준 상대 경로로 적는다.</summary>
    Path,
}

/// <summary>디스크립터에서 파생된 편집 칸 하나. 화면은 <b>이것만</b> 보고 그린다.</summary>
public sealed record PropertyField(
    string Name,
    FieldControl Control,
    object? Default,
    double? Min,
    double? Max,
    IReadOnlyList<string>? Choices,
    string? Description,
    bool Required,
    ParamConstraint Constraint)
{
    /// <summary>
    /// 슬라이더를 붙일 수 있는가 — <b>위아래 끝이 둘 다 정해져 있을 때만.</b>
    ///
    /// ★ 끝이 없는 값(<c>minArea</c>는 Min 0 만 있다)에 화면이 멋대로 끝을 정하면 안 된다.
    ///   "면적은 1000까지" 를 화면이 정하는 순간, 디스크립터가 아닌 곳에 규칙이 생긴다.
    ///   필요하면 디스크립터에 Max 를 적는 게 맞다 — 그러면 검증도 같이 따라온다.
    /// </summary>
    public bool HasRange => Control is FieldControl.Integer or FieldControl.Number
                            && Min is { } low && Max is { } high && high > low;

    /// <summary>
    /// 슬라이더 한 칸. ★ 홀수만 받는 값은 <b>2칸씩</b> — 짝수에 아예 못 멈추게 한다.
    /// 검증기가 어차피 잡지만, 고르고 나서 혼나는 것과 애초에 못 고르는 것은 쓰는 사람에게 다르다.
    /// </summary>
    public double Step => !HasRange ? 0
        : Constraint == ParamConstraint.OddOnly ? 2
        : Control == FieldControl.Integer ? 1
        : (Max!.Value - Min!.Value) / 100;
}

/// <summary>
/// ParamDescriptor[] → PropertyField[].
///
/// ★ <see cref="ParamDescriptor"/> 주석에 적어 둔 <b>"② WPF 속성 UI 자동 생성"이 이 파일</b>이다.
///   노드 타입마다 폼을 손으로 짜기 시작하면, 코드는 홀수를 요구하는데 화면은 짝수를 받고
///   문서엔 최대 15라고 적힌 상태가 된다 — 선언 한 군데에서만 파생시켜 그걸 막는다.
/// </summary>
public static class PropertyFormModel
{
    public static IReadOnlyList<PropertyField> For(INodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return [.. descriptor.Params.Select(FromParam)];
    }

    public static PropertyField FromParam(ParamDescriptor param)
    {
        ArgumentNullException.ThrowIfNull(param);

        // ★ 순서가 중요하다. Choices가 있으면 타입보다 콤보박스가 먼저다 —
        //   method는 string이지만 자유 입력이 아니라 정해진 것 중 고르는 칸이다.
        //   이 순서를 뒤집으면 "Gaussian" 을 사람이 손으로 타이핑하게 된다.
        FieldControl control =
            param.Type == typeof(bool) ? FieldControl.Bool :
            param.Choices is { Length: > 0 } ? FieldControl.Choice :
            param.Type == typeof(int) ? FieldControl.Integer :
            param.Type == typeof(double) ? FieldControl.Number :
            param.IsPath ? FieldControl.Path :
            FieldControl.Text;

        return new PropertyField(
            param.Name,
            control,
            param.Default,
            ToDouble(param.Min),
            ToDouble(param.Max),
            param.Choices,
            param.Description,
            param.Required,

            // ★ 제약(홀수만 등)을 화면까지 들고 간다. 검증기가 어차피 잡지만,
            //   <b>고르고 나서 혼나는 것</b>과 <b>애초에 못 고르는 것</b>은 쓰는 사람에게 다르다.
            param.Constraint);
    }

    // Min/Max는 object?라 int일 수도 double일 수도 있다. 화면은 double 하나로만 다루면 된다.
    private static double? ToDouble(object? value)
        => value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
}