using System.Text.Json;

namespace DiePipeline.Core.Configuration;

/// <summary>
/// ParamDescriptor 하나를 기준으로 실제 값을 검사해 오류 메시지를 낸다.
///
/// ★ 메시지에 노드 id를 붙이지 않는다 — 호출자가 붙인다.
///   이 함수는 '값 하나'만 안다. 어느 노드의 값인지는 부르는 쪽이 안다. 책임을 섞지 않는다.
/// </summary>
public static class ParamCheck
{
    // yield return — 오류를 리스트에 모으는 대신 하나씩 흘려보낸다(반복기 메서드).
    public static IEnumerable<string> Validate(ParamDescriptor param, JsonElement values)
    {
        ArgumentNullException.ThrowIfNull(param);

        if (!ParamValues.Has(values, param.Name))
        {
            if (param.Required)
            {
                yield return $"필수 파라미터 '{param.Name}'이 없습니다";
            }

            // ★ 미지정은 오류가 아니다 — 기본값을 쓰겠다는 뜻. 여기서 끝낸다.
            yield break;
        }

        if (param.Type == typeof(int) || param.Type == typeof(double))
        {
            double value = ParamValues.GetDouble(values, param.Name, double.NaN);

            // NaN — 숫자로 못 읽었다는 뜻(문자열이 들어왔거나).
            // ★ double.IsNaN으로 검사한다. NaN == NaN 은 false라서 == 로는 절대 못 잡는다.
            if (double.IsNaN(value))
            {
                yield return $"파라미터 '{param.Name}'은 숫자여야 합니다";
                yield break;
            }

            if (param.Min is { } min && value < Convert.ToDouble(min))
            {
                yield return $"파라미터 '{param.Name}'={value} 가 최솟값 {min} 보다 작습니다";
            }

            if (param.Max is { } max && value > Convert.ToDouble(max))
            {
                yield return $"파라미터 '{param.Name}'={value} 가 최댓값 {max} 보다 큽니다";
            }

            if (param.Constraint == ParamConstraint.OddOnly && ((int)value & 1) == 0)
            {
                yield return $"파라미터 '{param.Name}'={value} 는 홀수여야 합니다";
            }

            if (param.Constraint == ParamConstraint.Positive && value <= 0)
            {
                yield return $"파라미터 '{param.Name}'={value} 는 양수여야 합니다";
            }
        }
        else if (param.Type == typeof(string) && param.Choices is { Length: > 0 })
        {
            string value = ParamValues.GetString(values, param.Name, string.Empty);

            if (!param.Choices.Contains(value))
            {
                yield return
                    $"파라미터 '{param.Name}'='{value}' 는 [{string.Join(", ", param.Choices)}] 중 하나여야 합니다";
            }
        }
    }
}