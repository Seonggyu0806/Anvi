using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;

namespace DiePipeline.Core.Registry;

/// <summary>노드 타입 하나를 만들 줄 아는 '장인'. 키 = TypeName == JSON "type".</summary>
public interface INodeFactory
{
    string TypeName { get; }

    INodeDescriptor Descriptor { get; }

    /// <summary>주문서 하나 → 노드 하나.</summary>
    IPipelineNode Create(NodeConfig config);
}

public sealed class UnknownNodeTypeException : Exception
{
    public UnknownNodeTypeException(string typeName, IEnumerable<string> known)
        // ★ 아는 타입 목록을 같이 준다. 오타("Blurr")를 즉시 알아볼 수 있게.
        : base($"알 수 없는 노드 타입 '{typeName}'. 등록된 것: [{string.Join(", ", known.Order())}]")
        => TypeName = typeName;

    public string TypeName { get; }
}

public sealed class DuplicateNodeTypeException : Exception
{
    public DuplicateNodeTypeException(string typeName)
        : base($"노드 타입 '{typeName}'이 이미 등록돼 있습니다") => TypeName = typeName;

    public string TypeName { get; }
}

/// <summary>
/// 타입명 → 팩토리 명부. <b>이것이 전부다 — Dictionary 하나.</b>
///
/// ★ "새 알고리즘 = 클래스 1개 + 등록 1줄, 코어 불변"의 실체가 이 클래스다.
///   알고리즘을 추가할 때 고치는 곳은 PipelineEngine이 아니라 이 명부에 한 줄이다.
/// </summary>
public sealed class NodeRegistry
{
    // 대소문자 무시 — 레시피에 "blur"라고 써도 동작하게. 사람이 쓰는 파일이라 관대하게 받는다.
    private readonly Dictionary<string, INodeFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(INodeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // ★ 중복 등록을 조용히 덮어쓰지 않는다.
        //   덮어쓰면 "분명 등록했는데 다른 노드가 만들어지는" 추적 불가능한 버그가 된다.
        if (!_factories.TryAdd(factory.TypeName, factory))
        {
            throw new DuplicateNodeTypeException(factory.TypeName);
        }
    }

    public bool Contains(string typeName) => _factories.ContainsKey(typeName);

    public INodeFactory Get(string typeName)
        => _factories.TryGetValue(typeName, out INodeFactory? factory)
            ? factory
            : throw new UnknownNodeTypeException(typeName, _factories.Keys);

    public INodeDescriptor Describe(string typeName) => Get(typeName).Descriptor;

    public IPipelineNode Create(NodeConfig config) => Get(config.Type).Create(config);

    /// <summary>
    /// 레시피의 노드들을 적힌 순서대로 만든다. <b>꺼진 노드는 빼고.</b>
    ///
    /// ★ 빼기만 하면 뒷 노드가 없는 이름표를 읽는다. 그래서 <b>별칭</b>을 건다 —
    ///   꺼진 노드의 '출력 이름표'를 그 노드의 '입력 이름표'로 바꿔치기해서,
    ///   뒷 노드가 <b>레시피를 한 글자도 안 고치고</b> 윗 것을 그대로 읽게 한다.
    ///
    ///   <code>
    ///   켜짐   roi → [Normalize] → normed → [Difference]
    ///   꺼짐   roi ┄┄┄┄┄┄┄┄┄┄┄┄┄→ normed → [Difference]   ← normed를 roi로 바꿔 읽는다
    ///   </code>
    ///
    ///   화면에서 체크박스를 껐다 켤 때 레시피 파일을 다시 쓰지 않아도 되는 이유가 이것이다.
    /// </summary>
    public IReadOnlyList<IPipelineNode> CreateAll(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        Dictionary<string, string> alias = Aliases(recipe);

        return [.. recipe.Pipeline.Nodes.Where(n => n.Enabled).Select(n => Create(Rewire(n, alias)))];
    }

    /// <summary>꺼진 노드들의 '출력 이름표 → 입력 이름표' 표.</summary>
    private static Dictionary<string, string> Aliases(Recipe recipe)
    {
        Dictionary<string, string> alias = new(StringComparer.Ordinal);

        foreach (NodeConfig node in recipe.Pipeline.Nodes)
        {
            // ★ 입력 1개·출력 1개일 때만 별칭이 성립한다.
            //   둘 이상이면 "어느 쪽을 물려줄지"가 정해지지 않는다 — 검증기가 미리 오류로 잡는다.
            //   여기서는 조용히 건너뛴다(검증을 거친 레시피만 온다는 가정).
            if (!node.Enabled && node.Inputs.Count == 1 && node.Outputs.Count == 1)
            {
                alias[node.Outputs.Values.First()] = node.Inputs.Values.First();
            }
        }

        return alias;
    }

    /// <summary>꺼진 노드가 연달아 있을 수 있으므로 별칭을 끝까지 따라간다.</summary>
    private static string Resolve(string key, Dictionary<string, string> alias)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        // ★ seen이 없으면 별칭이 고리를 이룰 때 여기서 영원히 돈다.
        //   검증기가 먼저 막지만, <b>멈추는 조건은 도는 쪽에도 둔다</b> — 무한 루프는 증상이 없다.
        while (alias.TryGetValue(key, out string? next) && seen.Add(key))
        {
            key = next;
        }

        return key;
    }

    /// <summary>입력 이름표만 별칭으로 바꾼 사본. 바꿀 게 없으면 원본을 그대로 준다.</summary>
    private static NodeConfig Rewire(NodeConfig node, Dictionary<string, string> alias)
    {
        if (alias.Count == 0 || !node.Inputs.Values.Any(alias.ContainsKey))
        {
            return node;
        }

        return new NodeConfig
        {
            Id = node.Id,
            Type = node.Type,
            Enabled = node.Enabled,
            Inputs = node.Inputs.ToDictionary(kv => kv.Key, kv => Resolve(kv.Value, alias), StringComparer.Ordinal),
            Outputs = node.Outputs,
            Params = node.Params,
        };
    }

    /// <summary>등록된 모든 디스크립터. WPF 팔레트·문서 생성이 여기서 출발한다.</summary>
    public IReadOnlyCollection<INodeDescriptor> All => [.. _factories.Values.Select(f => f.Descriptor)];

    public IReadOnlyCollection<string> TypeNames => [.. _factories.Keys];
}