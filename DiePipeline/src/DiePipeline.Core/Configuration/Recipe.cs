using System.Text.Json;

namespace DiePipeline.Core.Configuration;

/// <summary>레시피 파일 하나 = 검사 한 가지의 전체 정의.</summary>
public sealed class Recipe
{
    /// <summary>파일 형식 버전. 나중에 형식을 바꿀 때 옛 파일을 구분하려고 둔다.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    public required string RecipeId { get; init; }

    public string Description { get; init; } = "";

    /// <summary>
    /// 호출자가 칠판에 미리 올려줘야 하는 이름표들(예: "roi").
    /// ★ 검증기의 출발점이다 — "누가 만들지 않은 키를 읽는가"를 판단하려면 시작점이 필요하다.
    /// </summary>
    public List<string> ContextInputs { get; init; } = [];

    public required PipelineSection Pipeline { get; init; }
}

public sealed class PipelineSection
{
    /// <summary>★ 적힌 순서가 실행 순서다(PipelineEngine의 계약). 검증기가 이 가정을 강제한다.</summary>
    public List<NodeConfig> Nodes { get; init; } = [];
}

/// <summary>레시피를 읽지 못했을 때.</summary>
public sealed class RecipeLoadException : Exception
{
    public RecipeLoadException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public static class RecipeLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // JSON은 camelCase("recipeId"), C#은 PascalCase(RecipeId). 대소문자를 무시해 둘을 잇는다.
        PropertyNameCaseInsensitive = true,

        // ★ JSON 표준에는 주석이 없다. 하지만 이 파일은 사람이 손으로 쓰고
        //   "왜 이 값인가"를 남겨야 하는 파일이다 — 표준을 일부러 벗어난다.
        ReadCommentHandling = JsonCommentHandling.Skip,

        AllowTrailingCommas = true,
    };

    public static Recipe Load(string path)
    {
        if (!File.Exists(path))
        {
            // ★ 전체 경로를 알려준다. 상대경로만 보여주면 "어느 폴더 기준인데?"가 된다.
            throw new RecipeLoadException($"레시피 파일이 없습니다: {Path.GetFullPath(path)}");
        }

        return Parse(File.ReadAllText(path), Path.GetFullPath(path));
    }

    /// <param name="origin">오류 메시지에 붙일 출처. 파일에서 읽었으면 경로.</param>
    public static Recipe Parse(string json, string? origin = null)
    {
        try
        {
            return JsonSerializer.Deserialize<Recipe>(json, Options)
                   ?? throw new RecipeLoadException($"레시피가 비어 있습니다(null). {origin}");
        }
        catch (JsonException ex)
        {
            // ★ 경로와 줄 번호를 메시지에 넣는다.
            //   "레시피가 잘못됐다"보다 "이 파일 12번째 줄"이 훨씬 유용하다.
            throw new RecipeLoadException($"레시피를 읽지 못했습니다: {origin}\n  {ex.Message}", ex);
        }
    }
}