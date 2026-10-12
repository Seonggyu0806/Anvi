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

        // ★ 부호를 <b>한 번만</b> 본다. 4주차엔 Mean 갈래에서만 봤다 — 그래서 Median 이 빠졌다.
        //   장수는 이웃의 결함을 묽히거나 다수결로 이기는 것이지 <b>막는 게 아니다.</b>
        //   5주차 화면에서 실제로 봤다: Median 3장인데 이웃 둘의 결함이 겹치자 중앙값이 넘어가
        //   가운데 die 에 겹친 넓이(728)만큼 결함이 생겼다. 부호(DarkOnly)로 바꾸자 사라졌다.
        //   ★ diffPolicy 를 안 적으면 Absolute 다(노드 기본값) — 안 적은 것도 '안 막는다' 로 본다.
        bool signed = policy is not null && (Same(policy, "DarkOnly") || Same(policy, "BrightOnly"));
        string? mirror = signed ? null : Mirror(strategy);

        if (Same(strategy, "Median"))
        {
            return new Requirement(3,
                "Median 은 3장부터 중앙값이 됩니다 — 2장이면 평균과 같아져 "
                + "이웃의 결함이 골든에 섞이고, 그게 내 die 에 되비칩니다(미러)",
                mirror);
        }

        if (Same(strategy, "Percentile") || Same(strategy, "Min") || Same(strategy, "Max"))
        {
            return new Requirement(2,
                $"{strategy} 는 여러 장 중에서 고르는 방식입니다 — 한 장이면 고를 것이 없습니다",
                mirror);
        }

        if (Same(strategy, "Mean"))
        {
            if (signed)
            {
                return new Requirement(1,
                    $"Mean 이지만 차이의 부호({policy})가 미러를 막으므로 장수는 안 따집니다");
            }

            // ★ 여기서 "3장이면 된다" 고 하면 안 된다.
            //   3주차 측정에서 3장 Mean 의 미러가 0으로 나왔지만, 그건 막아서가 아니라
            //   평균에 묽어져 임계를 못 넘은 것이다. 심는 세기를 올리자 드러났다.
            //   희석을 차단이라고 부르면 언젠가 세기가 센 결함에서 터진다.
            return new Requirement(1, "Mean 은 장수와 무관하게 골든이 한 장은 있어야 합니다", mirror);
        }

        return new Requirement(1, $"모르는 합치기 방식({strategy})입니다 — 최소 한 장으로만 봅니다", mirror);
    }

    /// <summary>
    /// 부호 없이 볼 때의 경고. <b>합치는 법마다 장수로 못 막는 까닭이 다르다</b> — 그걸 같이 적는다.
    ///
    /// ★ 4주차 문구는 Mean 에게 "Median 으로 합치세요" 를 권했다. Median 도 겹치면 넘어가므로
    ///   그 조언은 틀렸다. 막는 건 부호뿐이라 권하는 것도 부호 하나다.
    /// </summary>
    private static string Mirror(string strategy)
    {
        string why = Same(strategy, "Median")
            ? "Median 은 이웃 중 하나만 결함일 때까지 막습니다 — 이웃 둘의 결함이 같은 자리에서 겹치면 "
              + "중앙값이 그쪽으로 넘어가고, 겹친 넓이만큼 내 die 에 결함이 생깁니다"
            : Same(strategy, "Mean")
                ? "골든을 늘리면 평균에 묽어져 대개 임계를 못 넘지만, 그건 차단이 아니라 희석입니다"
                : $"{strategy} 로 고른 값에 이웃의 결함이 섞일 수 있습니다";

        return $"미러를 막는 것이 없습니다 — 차이를 부호 없이(Absolute) 봅니다. {why}. "
               + "장수가 아니라 부호가 막습니다 — 결함이 어두우면 DarkOnly 를 쓰세요";
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