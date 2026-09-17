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

    /// <summary>레시피의 노드들을 적힌 순서대로 만든다.</summary>
    public IReadOnlyList<IPipelineNode> CreateAll(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        return [.. recipe.Pipeline.Nodes.Select(Create)];
    }

    /// <summary>등록된 모든 디스크립터. WPF 팔레트·문서 생성이 여기서 출발한다.</summary>
    public IReadOnlyCollection<INodeDescriptor> All => [.. _factories.Values.Select(f => f.Descriptor)];

    public IReadOnlyCollection<string> TypeNames => [.. _factories.Keys];
}