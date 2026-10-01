using System.Text.Json;
using DiePipeline.Core.Registry;
using DiePipeline.Editor;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 앱이 <b>빈 캔버스 대신</b> 돌아가는 파이프라인으로 열리게 하는 시드 그래프.
///
/// ★★ <b>이 값들은 데모용이다. 실데이터에 쓰면 안 된다.</b>
///   미리보기용 합성 이미지(작고 깨끗함)에 맞춰져 있다. 실데이터는
///   <c>samples/recipes/die.diff.v2.json</c> 을 <b>불러오기</b>로 열어야 한다
///   (Combine=Mean · DarkOnly · MinMax · 임계 0.2 · minArea 500, 실데이터 32 die로 확정).
///
///   3주차에 반대 방향으로 한 번 데였다 — 데모용 레시피를 실데이터에 썼다가
///   오검 40개를 봤다. <b>용도가 다른 값은 섞으면 안 된다.</b>
///
/// ★ Normalize·Align 은 <b>블록만 놓고 method를 None 으로</b> 둔다(교수님 시드와 같다).
///   빼 버리면 쓰는 사람이 그런 단계가 있다는 걸 모른다. 자리를 보여 주고 끈 상태로 둔다.
///   <c>enabled: false</c> 로 안 하는 이유: Align 은 입력이 둘이라 <b>끌 수 없다</b>(검증기가 막는다).
/// </summary>
internal static class SampleGraph
{
    public static EditorGraph Standard()
    {
        EditorGraph graph = new() { RecipeId = "demo.die", Description = "미리보기용 시드 — 실데이터 값 아님" };

        graph.Nodes.Add(new EditorNode { Id = "golden", Type = "Combine", Params = Params("""{"strategy":"Median"}""") });
        graph.Nodes.Add(new EditorNode { Id = "normalize", Type = "Normalize", Params = Params("""{"method":"None"}""") });
        graph.Nodes.Add(new EditorNode { Id = "align", Type = "Align", Params = Params("""{"method":"None"}""") });
        graph.Nodes.Add(new EditorNode { Id = "diff", Type = "Difference", Params = Params("""{"windowSize":3,"diffPolicy":"Absolute","neighborMode":"None"}""") });
        graph.Nodes.Add(new EditorNode { Id = "binarize", Type = "Binarize", Params = Params("""{"method":"Threshold","value":0.2}""") });

        // ★ minArea 4 — 미리보기 결함이 5×5=25픽셀이다. 500으로 두면 0개가 잡힌다.
        graph.Nodes.Add(new EditorNode { Id = "label", Type = "Label", Params = Params("""{"connectivity":8,"minArea":4}""") });

        graph.Connections.AddRange(
        [
            new EditorConnection(null, ReservedContext.ChipRegions, "golden", "regions"),
            new EditorConnection("golden", "result", "normalize", "src"),

            // ★ 우리 Align 은 교수님과 방향이 반대다 — 골든을 움직여 die 에 맞춘다.
            //   src = 움직일 것(골든) · reference = 기준(검사할 die).
            new EditorConnection("normalize", "result", "align", "src"),
            new EditorConnection(null, ReservedContext.Roi, "align", "reference"),

            new EditorConnection("align", "result", "diff", "golden"),
            new EditorConnection(null, ReservedContext.Roi, "diff", "test"),

            new EditorConnection("diff", "result", "binarize", "src"),
            new EditorConnection("binarize", "result", "label", "mask"),
            new EditorConnection(null, ReservedContext.Roi, "label", "intensity"),
        ]);

        return graph;
    }

    private static Dictionary<string, JsonElement> Params(string json)
    {
        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);

        // ★ using — JsonDocument 는 쓰고 반납해야 한다.
        //   그래서 Clone() 으로 떼어 낸다. 안 떼면 문서가 사라진 뒤 값이 빈다.
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return values;
    }
}