using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiePipeline.Editor;

/// <summary>목록 값의 모양 — JSON 에 어떻게 적히나.</summary>
public enum ListLayout
{
    /// <summary>묶음의 목록 — <c>[{ "col": 1, "row": 2 }, …]</c>. 한 행 = 묶음 하나.</summary>
    Objects,

    /// <summary>짝의 목록 — <c>[[x, y], …]</c>. 한 행 = 숫자 배열 하나.</summary>
    Tuples,

    /// <summary>묶음 하나 또는 없음 — <c>{ "centerX": …, … }</c>. 행이 0개면 레시피에서 뺀다.</summary>
    Single,
}

/// <summary>행의 칸 하나. <paramref name="Key"/> 는 JSON 이름(짝이면 순서만 쓴다), <paramref name="Header"/> 는 화면 글자.</summary>
public sealed record ListColumn(
    string Key,
    string Header,
    FieldControl Control,
    string Default = "",
    IReadOnlyList<string>? Choices = null);

/// <summary>
/// 블록 설명(서술자)으로 못 적는 목록 · 묶음 값 하나 — 어느 블록의 어느 값을 어떤 칸들로 고치나.
/// </summary>
public sealed record ListParam(
    string NodeType,
    string Param,
    string Title,
    ListLayout Layout,
    IReadOnlyList<ListColumn> Columns,
    string Description,
    bool Reorderable = false);

/// <summary>
/// 목록 · 묶음 값 ↔ 화면의 행(칸 글자들). 속성 칸의 '행 목록' 부품이 이것만 보고 그린다.
///
/// ★ 교수님 방식(전용 편집 칸) — 교수님도 표식 목록 · 필터 체인을 전용 편집기로 만들었다.
///   서술자로 못 적는 값이 넷뿐이라 서술자를 키우지 않고 <b>여기 표 하나</b>에 적는다.
///   새 목록 값이 생기면 이 표에 한 줄을 넣는다 — 화면 코드는 그대로다.
/// ★ 값이 맞는지는 여기서 안 본다 — 블록 팩토리(<c>ICustomParamsValidator</c>)가 검사하고 검증칸이 알린다.
///   여기는 글자 ↔ JSON 바꾸기만 한다. 숫자 칸을 못 읽으면 <b>안 쓴다</b>(치는 중인 "0." 을 0 으로 쓰지 않는다).
/// ★ 아는 칸만 쓴다 — 행에 적힌 다른 이름의 값은 고칠 때 빠진다. 우리 레시피 형식에는 그런 칸이 없다.
/// </summary>
public static class ListParams
{
    private static readonly string[] Zones = ["E1", "E0", "off"];

    /// <summary>목록 · 묶음 값 전부. 블록 종류 + 값 이름으로 찾는다.</summary>
    public static IReadOnlyList<ListParam> All { get; } =
    [
        new("SolveAlignment", "expected", "표식 설계 자리", ListLayout.Tuples,
            [new("x", "x", FieldControl.Number, "0"), new("y", "y", FieldControl.Number, "0")],
            "die(0,0) 을 (0,0) 으로 본 표식 자리(px) — 사진 좌표가 아니다. 2개 이상"),

        new("InspectDies", "features", "표식 조각", ListLayout.Objects,
            [new("template", "조각 파일", FieldControl.Path)],
            "정렬에 쓰는 표식 그림 — 지금은 1개만 쓴다. 왼쪽 alignTemplate 입구로 들어간다",
            Reorderable: true),

        new("InspectDies", "circle", "원판", ListLayout.Single,
            [
                new("centerX", "중심 x", FieldControl.Number, "0"),
                new("centerY", "중심 y", FieldControl.Number, "0"),
                new("radius", "반지름", FieldControl.Number, "0"),
            ],
            "사진 좌표(px). 쓰면 원판 밖 칸은 die 가 아니다(맵에 옅은 회색)"),

        new("InspectDies", "zones", "수동 지정", ListLayout.Objects,
            [
                new("col", "열", FieldControl.Integer, "0"),
                new("row", "행", FieldControl.Integer, "0"),
                new("zone", "존", FieldControl.Choice, "E1", Zones),
            ],
            "자동 판정(E1 · E0)을 사람이 덮어쓴다 — off 는 die 가 아님"),
    ];

    /// <summary>이 블록 종류의 목록 · 묶음 값들. 없으면 빈 목록.</summary>
    public static IReadOnlyList<ListParam> For(string nodeType)
        => [.. All.Where(p => p.NodeType == nodeType)];

    /// <summary>
    /// JSON 값 → 행들(칸 글자). 값이 없으면(<c>null</c>) 행 0개.
    /// 모양이 어긋난 행도 읽을 수 있는 칸만 채워 보여 준다 — 버리면 사람이 고칠 기회도 없다.
    /// </summary>
    public static IReadOnlyList<string[]> Read(ListParam param, JsonElement? value)
    {
        ArgumentNullException.ThrowIfNull(param);

        if (value is not { } v)
        {
            return [];
        }

        return param.Layout switch
        {
            ListLayout.Single => v.ValueKind == JsonValueKind.Object ? [Row(param, v)] : [],
            _ when v.ValueKind != JsonValueKind.Array => [],
            _ => [.. v.EnumerateArray().Select(item => Row(param, item))],
        };
    }

    /// <summary>
    /// 행들 → JSON 값. 숫자 칸을 하나라도 못 읽으면 <c>false</c> — 부르는 쪽은 모델을 건드리지 않는다.
    /// <paramref name="value"/> 가 <c>null</c> 이면 '레시피에서 뺀다'(묶음 하나짜리에 행이 없을 때).
    /// </summary>
    public static bool TryWrite(ListParam param, IReadOnlyList<IReadOnlyList<string>> rows, out JsonElement? value)
    {
        ArgumentNullException.ThrowIfNull(param);
        ArgumentNullException.ThrowIfNull(rows);

        value = null;

        if (param.Layout == ListLayout.Single && rows.Count == 0)
        {
            return true;
        }

        List<JsonNode> written = [];

        foreach (IReadOnlyList<string> row in rows)
        {
            JsonNode?[] cells = [.. param.Columns.Select((c, i) => Cell(c, i < row.Count ? row[i] : c.Default))];

            if (cells.Any(c => c is null))
            {
                return false;
            }

            written.Add(param.Layout == ListLayout.Tuples
                ? new JsonArray(cells)
                : new JsonObject(param.Columns.Select((c, i) => KeyValuePair.Create(c.Key, cells[i]))));
        }

        JsonNode result = param.Layout == ListLayout.Single ? written[0] : new JsonArray([.. written]);

        value = JsonSerializer.SerializeToElement(result);
        return true;
    }

    /// <summary>새 행 — 칸마다 기본 글자.</summary>
    public static string[] NewRow(ListParam param)
    {
        ArgumentNullException.ThrowIfNull(param);

        return [.. param.Columns.Select(c => c.Default)];
    }

    private static string[] Row(ListParam param, JsonElement item)
        => [.. param.Columns.Select((c, i) => Text(c, param.Layout == ListLayout.Tuples ? At(item, i) : Field(item, c.Key)))];

    private static JsonElement? At(JsonElement array, int index)
        => array.ValueKind == JsonValueKind.Array && index < array.GetArrayLength() ? array[index] : null;

    private static JsonElement? Field(JsonElement obj, string key)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) ? v : null;

    private static string Text(ListColumn column, JsonElement? value)
    {
        string text = value switch
        {
            { ValueKind: JsonValueKind.Number } n => n.GetDouble().ToString(CultureInfo.InvariantCulture),
            { ValueKind: JsonValueKind.String } s => s.GetString() ?? string.Empty,
            _ => string.Empty,
        };

        // ★ 고르는 칸은 고를 수 있는 글자로 맞춘다 — 레시피에 "e1" 이라 적혀 있어도 콤보에서 "E1" 이 골라져 보이게.
        return column.Choices?.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase)) ?? text;
    }

    /// <summary>칸 글자 → JSON. 숫자 칸을 못 읽으면 <c>null</c>.</summary>
    private static JsonNode? Cell(ListColumn column, string text)
        => column.Control switch
        {
            FieldControl.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                ? JsonValue.Create(i)
                : null,
            FieldControl.Number => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? JsonValue.Create(d)
                : null,
            _ => JsonValue.Create(text),
        };
}