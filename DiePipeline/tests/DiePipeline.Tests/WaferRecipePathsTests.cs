using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 레시피를 다른 폴더에 저장할 때 안의 경로를 옮겨 적는다. ★ 핵심은 <b>옮긴 뒤에도 같은 파일을 가리키는가</b>다.
/// </summary>
public sealed class WaferRecipePathsTests
{
    private static readonly string From = Path.Combine(Path.GetTempPath(), "dp", "a", "b");
    private static readonly string To = Path.Combine(Path.GetTempPath(), "dp", "a", "c", "d");
    private static readonly string Absolute = Path.Combine(Path.GetTempPath(), "dp", "x", "die.json");

    private static Recipe Recipe(string e1, string e0, string template) => RecipeLoader.Parse($$"""
        { "recipeId": "w", "contextInputs": [ "alignSrc", "alignTemplate" ], "pipeline": { "nodes": [
          { "id": "solve", "type": "SolveAlignment", "inputs": { "matches": "matches" },
            "outputs": { "alignment": "alignment" }, "params": { "expected": [ [ -40, -40 ], [ 410, 310 ] ] } },
          { "id": "inspect", "type": "InspectDies", "inputs": { "alignment": "alignment" },
            "outputs": { "defects": "d", "grid": "g", "alignment": "a" },
            "params": { "e1Recipe": {{JsonSerializer.Serialize(e1)}}, "e0Recipe": {{JsonSerializer.Serialize(e0)}},
                        "cols": 4, "features": [ { "template": {{JsonSerializer.Serialize(template)}} } ] } }
        ] } }
        """);

    private static JsonElement Inspect(Recipe recipe) => recipe.Pipeline.Nodes.Single(n => n.Id == "inspect").Params;

    /// <summary>★★ 옮겨 적은 경로를 새 폴더에서 풀면, 옛 경로를 옛 폴더에서 푼 것과 같은 파일이다.</summary>
    [Fact]
    public void 옮겨_적은_경로는_같은_파일을_가리킨다()
    {
        Recipe moved = WaferRecipePaths.Rebase(Recipe("../recipes/die.json", "e0/edge.json", "features/m.bmp"), From, To);
        JsonElement p = Inspect(moved);

        Assert.Equal("../../recipes/die.json", p.GetProperty("e1Recipe").GetString());
        Assert.Equal("../../b/e0/edge.json", p.GetProperty("e0Recipe").GetString());
        Assert.Equal("../../b/features/m.bmp", p.GetProperty("features")[0].GetProperty("template").GetString());

        Assert.Equal(WaferRecipeRunner.PathIn(From, "../recipes/die.json"),
            WaferRecipeRunner.PathIn(To, p.GetProperty("e1Recipe").GetString()!));
    }

    /// <summary>절대 경로 · 빈 값은 그대로 — 실데이터 레시피는 절대 경로를 쓴다.</summary>
    [Fact]
    public void 절대_경로와_빈_값은_그대로()
    {
        JsonElement p = Inspect(WaferRecipePaths.Rebase(Recipe(Absolute, "", Absolute), From, To));

        Assert.Equal(Absolute, p.GetProperty("e1Recipe").GetString());
        Assert.Equal("", p.GetProperty("e0Recipe").GetString());
        Assert.Equal(Absolute, p.GetProperty("features")[0].GetProperty("template").GetString());
    }

    /// <summary>경로가 아닌 값 · 다른 블록 · 받은 레시피는 건드리지 않는다.</summary>
    [Fact]
    public void 경로가_아닌_것은_건드리지_않는다()
    {
        Recipe original = Recipe("../recipes/die.json", "", "features/m.bmp");
        string before = RecipeLoader.Serialize(original);

        Recipe moved = WaferRecipePaths.Rebase(original, From, To);

        Assert.Equal(before, RecipeLoader.Serialize(original));
        Assert.Equal(4, Inspect(moved).GetProperty("cols").GetInt32());
        Assert.Equal(
            original.Pipeline.Nodes[0].Params.GetRawText(),
            moved.Pipeline.Nodes[0].Params.GetRawText());
        Assert.Equal(["alignSrc", "alignTemplate"], moved.ContextInputs);
    }

    /// <summary>같은 폴더로 옮기면 경로 모양만 슬래시로 맞춰지고 가리키는 파일은 같다.</summary>
    [Fact]
    public void 같은_폴더면_그대로다()
    {
        JsonElement p = Inspect(WaferRecipePaths.Rebase(Recipe("../recipes/die.json", "", "features/m.bmp"), From, From));

        Assert.Equal("../recipes/die.json", p.GetProperty("e1Recipe").GetString());
        Assert.Equal("features/m.bmp", p.GetProperty("features")[0].GetProperty("template").GetString());
    }
}