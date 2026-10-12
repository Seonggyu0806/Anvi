using System.Text.Json;
using System.Text.Json.Nodes;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Registry;
using DiePipeline.Editor;
using DiePipeline.Wafer.Processing;

namespace DiePipeline.Tests;

/// <summary>
/// 속성 칸의 목록 · 묶음 값(표식 자리 · 표식 조각 · 원판 · 수동 지정) ↔ 행. ★ 화면 없이 글자와 JSON 만 본다.
/// </summary>
public sealed class ListParamsTests
{
    private static readonly NodeRegistry Registry = WaferBackend.CreateRegistry();

    private static ListParam Param(string type, string name) => ListParams.For(type).Single(p => p.Param == name);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static JsonElement Write(ListParam param, params string[][] rows)
    {
        Assert.True(ListParams.TryWrite(param, rows, out JsonElement? value));
        return value!.Value;
    }

    /// <summary>표에 적힌 값은 전부 <b>서술자로 못 적는 값</b>이다 — 서술자에 있는 값을 또 적으면 칸이 두 번 나온다.</summary>
    [Fact]
    public void 표는_서술자에_없는_값만_적는다()
    {
        foreach (ListParam p in ListParams.All)
        {
            INodeDescriptor descriptor = Registry.Describe(p.NodeType);
            Assert.DoesNotContain(descriptor.Params, d => d.Name == p.Param);
        }
    }

    /// <summary>견본 레시피의 값 → 행. 짝은 순서대로, 묶음은 이름대로.</summary>
    [Fact]
    public void 값을_행으로_읽는다()
    {
        Assert.Equal(
            [["-40", "-40"], ["410", "310"]],
            ListParams.Read(Param("SolveAlignment", "expected"), Json("[[-40,-40],[410,310]]")));

        Assert.Equal(
            [["features/demo_mark.bmp"]],
            ListParams.Read(Param("InspectDies", "features"), Json("""[{ "template": "features/demo_mark.bmp" }]""")));

        Assert.Equal(
            [["500", "400.5", "300"]],
            ListParams.Read(Param("InspectDies", "circle"), Json("""{ "centerX": 500, "centerY": 400.5, "radius": 300 }""")));
    }

    /// <summary>★ 읽고 다시 쓰면 같은 값 — 고치지 않은 칸이 슬쩍 바뀌면 레시피 파일이 더러워진다.</summary>
    [Theory]
    [InlineData("SolveAlignment", "expected", "[[-40,-40],[410,310]]")]
    [InlineData("InspectDies", "features", """[{"template":"features/demo_mark.bmp"}]""")]
    [InlineData("InspectDies", "circle", """{"centerX":500,"centerY":400.5,"radius":300}""")]
    [InlineData("InspectDies", "zones", """[{"col":2,"row":3,"zone":"off"},{"col":3,"row":3,"zone":"E0"}]""")]
    public void 읽고_다시_쓰면_같은_값(string type, string name, string json)
    {
        ListParam param = Param(type, name);

        JsonElement written = Write(param, [.. ListParams.Read(param, Json(json))]);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(written.GetRawText())), written.GetRawText());
    }

    /// <summary>★ 숫자 칸을 못 읽으면 안 쓴다 — 지우는 중인 빈 칸 · 잘못 친 글자를 0 으로 쓰지 않는다.</summary>
    [Fact]
    public void 숫자_칸을_못_읽으면_안_쓴다()
    {
        ListParam expected = Param("SolveAlignment", "expected");

        Assert.False(ListParams.TryWrite(expected, [["", "0"]], out _));
        Assert.False(ListParams.TryWrite(expected, [["1", "abc"]], out _));
        Assert.False(ListParams.TryWrite(Param("InspectDies", "zones"), [["1.5", "0", "E1"]], out _));
    }

    /// <summary>존 이름은 콤보에서 고를 수 있는 글자로 — 레시피에 소문자로 적혀 있어도.</summary>
    [Fact]
    public void 존_이름은_고를_수_있는_글자로_읽는다()
    {
        IReadOnlyList<string[]> rows = ListParams.Read(
            Param("InspectDies", "zones"), Json("""[{"col":1,"row":2,"zone":"e1"},{"col":0,"row":0,"zone":"OFF"}]"""));

        Assert.Equal([["1", "2", "E1"], ["0", "0", "off"]], rows);
    }

    /// <summary>원판은 하나 또는 없음 — 행이 없으면 레시피에서 뺀다(<c>null</c>). 없거나 null 이면 행 0개.</summary>
    [Fact]
    public void 원판은_행이_없으면_뺀다()
    {
        ListParam circle = Param("InspectDies", "circle");

        Assert.True(ListParams.TryWrite(circle, [], out JsonElement? value));
        Assert.Null(value);

        Assert.Empty(ListParams.Read(circle, null));
        Assert.Empty(ListParams.Read(circle, Json("null")));
    }

    /// <summary>새 행은 칸마다 기본 글자 — 존은 E1.</summary>
    [Fact]
    public void 새_행은_기본_글자()
    {
        Assert.Equal(["0", "0", "E1"], ListParams.NewRow(Param("InspectDies", "zones")));
        Assert.Equal([""], ListParams.NewRow(Param("InspectDies", "features")));
    }

    /// <summary>
    /// ★★ 쓴 값은 블록 검사를 통과한다 — 행 목록이 쓰는 모양과 블록 팩토리가 받는 모양이 같다.
    ///   한쪽만 바뀌면 화면에서 고칠 때마다 빨간 줄이 뜬다.
    /// </summary>
    [Fact]
    public void 쓴_값은_블록_검사를_통과한다()
    {
        JsonObject inspect = new()
        {
            ["e1Recipe"] = "die.json",
            ["features"] = JsonNode.Parse(Write(Param("InspectDies", "features"), ["m.bmp"]).GetRawText()),
            ["circle"] = JsonNode.Parse(Write(Param("InspectDies", "circle"), ["500", "400", "300"]).GetRawText()),
            ["zones"] = JsonNode.Parse(Write(Param("InspectDies", "zones"), ["2", "3", "off"], ["3", "3", "E0"]).GetRawText()),
        };

        JsonObject solve = new()
        {
            ["expected"] = JsonNode.Parse(Write(Param("SolveAlignment", "expected"), ["-40", "-40"], ["410", "310"]).GetRawText()),
        };

        Assert.Empty(new InspectDiesNodeFactory().ValidateParams(Config("InspectDies", inspect)));
        Assert.Empty(new SolveAlignmentNodeFactory().ValidateParams(Config("SolveAlignment", solve)));
    }

    private static NodeConfig Config(string type, JsonObject @params) => new()
    {
        Id = "n",
        Type = type,
        Params = JsonSerializer.SerializeToElement(@params),
    };

    // ─────────────────────────────── 찾아보기로 고른 파일 → 레시피에 적을 경로

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "dp", "samples", "wafer");

    /// <summary>레시피 폴더 기준 상대 경로 — 아래는 그대로, 위는 <c>../</c>. 슬래시로 적는다.</summary>
    [Fact]
    public void 고른_파일은_레시피_폴더_기준으로_적는다()
    {
        Assert.Equal("features/m.bmp", EditorPaths.Relative(Folder, Path.Combine(Folder, "features", "m.bmp")));
        Assert.Equal("../recipes/die.json",
            EditorPaths.Relative(Folder, Path.Combine(Path.GetTempPath(), "dp", "samples", "recipes", "die.json")));
    }

    /// <summary>기준 폴더가 없으면(아직 저장 안 한 레시피) 절대 경로.</summary>
    [Fact]
    public void 폴더가_없으면_절대_경로()
    {
        string file = Path.Combine(Folder, "features", "m.bmp");

        Assert.Equal(file, EditorPaths.Relative(null, file));
    }
}