using DiePipeline.Core.Configuration;

namespace DiePipeline.Core.Registry;

/// <summary>포트가 주고받는 데이터 종류. 검증기가 "만든 것을 받을 수 있나"를 본다.</summary>
public enum PortKind
{
    Image,
    ImageList,      // ← 추가: 골든을 만들 정상 die들
    DefectList,

    // ── 웨이퍼 레시피(5주차 §8) — 블록 사이로 흐르는 웨이퍼 쪽 데이터. 이름은 교수님과 같다.
    //    ★ Core 는 이름만 안다. 실제 모양(정렬 결과 · 격자 · 웨이퍼 결함)은 Wafer 층에 있다.
    //    ★ 맨 뒤에 붙인다 — 앞의 세 값의 번호가 바뀌지 않게.
    MatchList,          // 표식 찾기가 찾은 자리 목록
    Alignment,          // 웨이퍼가 놓인 자세(원점 · 회전 · 배율)
    DieGrid,            // die 원점 격자 + 존
    WaferDefectList,    // 웨이퍼 전체 결함 목록
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