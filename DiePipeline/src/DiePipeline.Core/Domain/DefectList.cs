namespace DiePipeline.Core.Domain;

/// <summary>검사 한 번의 결함 목록.</summary>
public sealed record DefectList
{
    public required IReadOnlyList<Defect> Items { get; init; }

    public int Count => Items.Count;

    /// <summary>결함이 하나도 없는 목록. 깨끗한 die의 정상적인 결과다.</summary>
    public static DefectList Empty { get; } = new() { Items = [] };
}