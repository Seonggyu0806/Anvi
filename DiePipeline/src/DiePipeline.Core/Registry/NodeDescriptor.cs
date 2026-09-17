using DiePipeline.Core.Configuration;

namespace DiePipeline.Core.Registry;

/// <summary>포트가 주고받는 데이터 종류. 검증기가 "만든 것을 받을 수 있나"를 본다.</summary>
public enum PortKind
{
    Image,
    ImageList,      // ← 추가: 골든을 만들 정상 die들
    DefectList,
}

/// <summary>포트 하나의 메타데이터.</summary>
public sealed record PortDescriptor(
    string Name,
    string Description = "",
    PortKind Kind = PortKind.Image,
    bool Optional = false);        // ← 추가

/// <summary>노드 타입 하나의 포트·파라미터 메타. UI·검증·문서의 단일원.</summary>
public interface INodeDescriptor
{
    string TypeName { get; }

    /// <summary>UI 팔레트에서 묶을 그룹. 예: "전처리" · "검출".</summary>
    string Category { get; }

    IReadOnlyList<PortDescriptor> Inputs { get; }

    IReadOnlyList<PortDescriptor> Outputs { get; }

    IReadOnlyList<ParamDescriptor> Params { get; }
}

/// <summary>기본 구현. 팩토리들이 하나씩 들고 있는다.</summary>
public sealed class NodeDescriptor : INodeDescriptor
{
    public NodeDescriptor(
        string typeName,
        string category,
        IReadOnlyList<PortDescriptor> inputs,
        IReadOnlyList<PortDescriptor> outputs,
        IReadOnlyList<ParamDescriptor> @params)   // params는 C# 키워드라 @를 붙인다(축자 식별자)
    {
        TypeName = typeName;
        Category = category;
        Inputs = inputs;
        Outputs = outputs;
        Params = @params;
    }

    public string TypeName { get; }

    public string Category { get; }

    public IReadOnlyList<PortDescriptor> Inputs { get; }

    public IReadOnlyList<PortDescriptor> Outputs { get; }

    public IReadOnlyList<ParamDescriptor> Params { get; }
}