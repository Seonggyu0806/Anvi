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
    /// <summary>
    /// 웨이퍼 쪽 데이터 종류. die 탭 팔레트는 이 종류를 주고받는 블록을 숨긴다(교수님 Phase8 8.5a 와 같다).
    /// ★ 이름을 손으로 나열하지 않고 <b>포트 종류로</b> 거른다 — 웨이퍼 블록이 늘어도 여기는 안 고친다.
    /// </summary>
    public static readonly IReadOnlySet<PortKind> WaferKinds =
        new HashSet<PortKind> { PortKind.MatchList, PortKind.Alignment, PortKind.DieGrid, PortKind.WaferDefectList };

    /// <summary>입력이나 출력 중 하나라도 <paramref name="kinds"/> 를 주고받는가.</summary>
    public static bool TouchesPortKind(INodeDescriptor descriptor, IReadOnlySet<PortKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(kinds);

        return descriptor.Inputs.Concat(descriptor.Outputs).Any(port => kinds.Contains(port.Kind));
    }

    public static IReadOnlyList<PaletteCategory> From(NodeRegistry registry) => Filtered(registry, _ => true);

    /// <summary>명부에서 <paramref name="include"/> 를 통과한 블록만 카테고리별로.</summary>
    public static IReadOnlyList<PaletteCategory> Filtered(NodeRegistry registry, Func<INodeDescriptor, bool> include)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(include);

        // ★ 두 번 정렬한다 — 카테고리끼리, 그리고 카테고리 안에서 타입 이름끼리.
        //   Dictionary는 순서를 보장하지 않아서, 안 하면 실행할 때마다 팔레트 차례가 바뀐다.
        return
        [
            .. registry.All
                .Where(include)
                .GroupBy(d => d.Category, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PaletteCategory(
                    g.Key,
                    [.. g.OrderBy(d => d.TypeName, StringComparer.Ordinal)]))
        ];
    }
}