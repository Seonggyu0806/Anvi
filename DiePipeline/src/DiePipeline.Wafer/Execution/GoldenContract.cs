using System.Text.Json;
using DiePipeline.Core.Configuration;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// 이 레시피가 골든을 <b>몇 장 필요로 하는지</b> 레시피에서 읽어낸다.
///
/// <para>★ 3주차에 잰 것 — <b>이웃 고르는 법과 합치는 법은 짝이다.</b>
/// 그런데 둘을 정하는 자리가 떨어져 있다: 합치는 법은 <b>레시피</b>에, 이웃 고르는 법은 <b>명령행</b>에.
/// 사람이 매번 맞춰 넣어야 하면 언젠가 틀린다 — 그리고 <b>틀려도 안 터진다.</b>
/// 결함 수가 조용히 부풀 뿐이다.</para>
///
/// <para>★ 그래서 <b>레시피에서 유도한다.</b> 사람이 "골든 3장 필요"라고 따로 적게 하면
/// 그 값도 틀리게 적을 수 있다. 합치는 법만 보면 필요한 장수는 이미 정해져 있다.
/// 레시피 형식을 안 건드리므로 교수님 레시피도 그대로 읽힌다.</para>
///
/// <para>★ 이 클래스는 노드 <b>이름</b>만 안다("Combine"·"Median"). 알고리즘도 OpenCV 도 모른다 —
/// 검증기가 하는 일과 같은 수준의 지식이다.</para>
/// </summary>
public static class GoldenContract
{
    /// <param name="Minimum">이 장수보다 적으면 검사를 믿을 수 없다.</param>
    /// <param name="Reason">왜 그 장수인가. 사람이 읽을 문장.</param>
    /// <param name="Warning">장수로는 못 막는 문제가 있을 때. 없으면 <c>null</c>.</param>
    public readonly record struct Requirement(int Minimum, string Reason, string? Warning = null);

    public static Requirement Of(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        string? strategy = null;
        string? policy = null;

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            if (Same(node.Type, "Combine"))
            {
                strategy = Text(node.Params, "strategy");
            }
            else if (Same(node.Type, "Difference"))
            {
                policy = Text(node.Params, "diffPolicy");
            }
        }

        if (strategy is null)
        {
            // 골든을 안 만드는 레시피다 — E0 용(die.dark) 이 여기 해당한다.
            return new Requirement(0, "골든을 합치는 단계가 없습니다 — 골든이 필요 없는 레시피입니다");
        }

        if (Same(strategy, "Median"))
        {
            return new Requirement(3,
                "Median 은 3장부터 중앙값이 됩니다 — 2장이면 평균과 같아져 "
                + "이웃의 결함이 골든에 섞이고, 그게 내 die 에 되비칩니다(미러)");
        }

        if (Same(strategy, "Percentile") || Same(strategy, "Min") || Same(strategy, "Max"))
        {
            return new Requirement(2,
                $"{strategy} 는 여러 장 중에서 고르는 방식입니다 — 한 장이면 고를 것이 없습니다");
        }

        if (Same(strategy, "Mean"))
        {
            if (policy is not null && (Same(policy, "DarkOnly") || Same(policy, "BrightOnly")))
            {
                return new Requirement(1,
                    $"Mean 이지만 차이의 부호({policy})가 미러를 막으므로 장수는 안 따집니다");
            }

            // ★ 여기서 "3장이면 된다" 고 하면 안 된다.
            //   3주차 측정에서 3장 Mean 의 미러가 0으로 나왔지만, 그건 막아서가 아니라
            //   평균에 묽어져 임계를 못 넘은 것이다. 심는 세기를 올리자 드러났다.
            //   희석을 차단이라고 부르면 언젠가 세기가 센 결함에서 터진다.
            return new Requirement(1,
                "Mean 은 장수와 무관하게 골든이 한 장은 있어야 합니다",
                "미러를 막는 것이 없습니다 — Mean 으로 합치는데 차이를 부호 없이(Absolute) 봅니다. "
                + "이웃의 결함이 내 die 에 되비칠 수 있습니다. "
                + "골든을 늘리면 평균에 묽어져 대개 임계를 못 넘지만, 그건 차단이 아니라 희석입니다. "
                + "DarkOnly 를 쓰거나 Median 으로 합치세요");
        }

        return new Requirement(1, $"모르는 합치기 방식({strategy})입니다 — 최소 한 장으로만 봅니다");
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>파라미터에서 문자열 하나를 꺼낸다. 대소문자는 안 따진다.</summary>
    private static string? Text(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (Same(property.Name, name) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}