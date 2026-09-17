using System.Text.Json;

namespace DiePipeline.Core.Configuration;

/// <summary>
/// 약타입 params(JsonElement)에서 값을 안전하게 꺼낸다. 없거나 타입이 다르면 기본값.
///
/// ★ 왜 예외를 안 던지고 기본값으로 넘어가나:
///   "레시피에 안 적었다"는 오류가 아니라 <b>"기본값을 쓰겠다"</b>는 뜻이다.
///   진짜 오류(kernel="abc", kernel=4 같은 짝수)는 실행 전 검증기가 따로 잡는다.
///   → <b>읽기와 검증을 분리한다.</b> 섞으면 둘 다 못 한다.
/// </summary>
public static class ParamValues
{
    public static bool Has(JsonElement p, string name)
        => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out _);

    public static int GetInt(JsonElement p, string name, int fallback)
        => TryGet(p, name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
            ? i
            : fallback;

    public static double GetDouble(JsonElement p, string name, double fallback)
        => TryGet(p, name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    public static string GetString(JsonElement p, string name, string fallback)
        => TryGet(p, name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;

    public static bool GetBool(JsonElement p, string name, bool fallback)
        => TryGet(p, name, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    /// <summary>
    /// 배열 파라미터를 읽는다. 없거나 배열이 아니면 <b>빈 목록</b>이다.
    ///
    /// ★ Filter의 filters[] 처럼 <b>중첩된</b> 파라미터를 위해 있다.
    ///   ParamDescriptor로는 배열을 표현할 수 없어서, 그런 파라미터는 팩토리가 직접 읽고 직접 검증한다.
    /// </summary>
    public static IEnumerable<JsonElement> GetArray(JsonElement p, string name)
    {
        if (TryGet(p, name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray();
        }

        return [];
    }

    private static bool TryGet(JsonElement p, string name, out JsonElement value)
    {
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}