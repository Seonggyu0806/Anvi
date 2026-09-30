using DiePipeline.Core.Registry;

namespace DiePipeline.Editor;

/// <summary>팔레트의 한 묶음 — 디스크립터의 Category로 묶는다.</summary>
public sealed record PaletteCategory(string Category, IReadOnlyList<INodeDescriptor> Nodes);

/// <summary>
/// 명부 → 카테고리별 팔레트.
///
/// ★★ <b>이 파일이 블록 에디터의 존재 이유다.</b>
///   새 알고리즘을 명부에 등록하면 팔레트에 <b>저절로</b> 뜬다 — 화면 코드는 한 줄도 안 고친다.
///   여기서 타입 이름을 손으로 나열하는 순간 그 효용이 통째로 사라진다.
/// </summary>
public static class PaletteModel
{
    public static IReadOnlyList<PaletteCategory> From(NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // ★ 두 번 정렬한다 — 카테고리끼리, 그리고 카테고리 안에서 타입 이름끼리.
        //   Dictionary는 순서를 보장하지 않아서, 안 하면 실행할 때마다 팔레트 차례가 바뀐다.
        return
        [
            .. registry.All
                .GroupBy(d => d.Category, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PaletteCategory(
                    g.Key,
                    [.. g.OrderBy(d => d.TypeName, StringComparer.Ordinal)]))
        ];
    }
}