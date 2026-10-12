using System.Text.Json;
using System.Text.Json.Nodes;
using DiePipeline.Core.Configuration;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// 웨이퍼 레시피 안의 <b>파일 경로</b>를 다른 폴더 기준으로 고쳐 적는다 — 레시피를 다른 폴더에 저장할 때.
///
/// ★ 레시피 안의 상대 경로는 레시피 파일이 있는 폴더 기준이다(<see cref="WaferRecipeRunner.PathIn"/>).
///   파일만 다른 폴더에 쓰고 경로를 그대로 두면 저장한 레시피가 엉뚱한 곳을 가리킨다 —
///   화면에서는 돌았는데 명령행 <c>--recipe</c> 로는 안 도는 레시피가 된다.
/// ★ 경로인 값은 셋이다 — InspectDies 의 <c>e1Recipe</c> · <c>e0Recipe</c> · <c>features[].template</c>.
/// ★ 절대 경로와 빈 값은 그대로 둔다.
/// </summary>
public static class WaferRecipePaths
{
    private const string InspectDies = "InspectDies";

    private static readonly string[] RecipeKeys = ["e1Recipe", "e0Recipe"];

    /// <summary>
    /// <paramref name="fromFolder"/> 기준으로 적힌 경로를 <paramref name="toFolder"/> 기준으로 고친 <b>새</b> 레시피.
    /// 받은 레시피는 건드리지 않는다.
    /// </summary>
    public static Recipe Rebase(Recipe recipe, string fromFolder, string toFolder)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(toFolder);

        return new Recipe
        {
            SchemaVersion = recipe.SchemaVersion,
            RecipeId = recipe.RecipeId,
            Description = recipe.Description,
            ContextInputs = [.. recipe.ContextInputs],
            Pipeline = new PipelineSection
            {
                Nodes = [.. recipe.Pipeline.Nodes.Select(n => n.Type == InspectDies ? Moved(n, fromFolder, toFolder) : n)],
            },
        };
    }

    /// <summary>
    /// 경로 하나를 옮겨 적는다. 상대 경로만 고치고, 절대 경로 · 빈 값은 그대로.
    /// ★ 슬래시(<c>/</c>)로 적는다 — 견본과 같은 모양이고, 윈도에서도 그대로 풀린다.
    /// ★ 드라이브가 다르면 상대 경로가 없다 — 그때는 절대 경로가 된다. 그것도 맞게 풀리는 답이다.
    /// </summary>
    public static string Move(string reference, string fromFolder, string toFolder)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference))
        {
            return reference;
        }

        string full = WaferRecipeRunner.PathIn(fromFolder, reference);

        return Path.GetRelativePath(Path.GetFullPath(toFolder), full).Replace('\\', '/');
    }

    private static NodeConfig Moved(NodeConfig node, string fromFolder, string toFolder)
    {
        if (node.Params.ValueKind != JsonValueKind.Object)
        {
            return node;
        }

        JsonObject p = JsonNode.Parse(node.Params.GetRawText())!.AsObject();

        foreach (string key in RecipeKeys)
        {
            if (p[key] is JsonValue value && value.TryGetValue(out string? path))
            {
                p[key] = Move(path, fromFolder, toFolder);
            }
        }

        if (p["features"] is JsonArray features)
        {
            foreach (JsonObject feature in features.OfType<JsonObject>())
            {
                if (feature["template"] is JsonValue value && value.TryGetValue(out string? path))
                {
                    feature["template"] = Move(path, fromFolder, toFolder);
                }
            }
        }

        return new NodeConfig
        {
            Id = node.Id,
            Type = node.Type,
            Enabled = node.Enabled,
            Inputs = new Dictionary<string, string>(node.Inputs),
            Outputs = new Dictionary<string, string>(node.Outputs),
            Params = JsonSerializer.SerializeToElement(p),
        };
    }
}