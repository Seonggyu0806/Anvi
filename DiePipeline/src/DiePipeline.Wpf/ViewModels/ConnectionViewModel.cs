using DiePipeline.Editor;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 선 하나 — 출력 점(<see cref="Source"/>)에서 입력 점(<see cref="Target"/>)으로.
///
/// ★ 선은 두 점의 <c>Anchor</c> 를 따라 Nodify 가 그린다. 블록이 움직여도 우리가 끝점을 다시 계산하지 않는다 —
///   전에는 블록 이동을 구독해 끝점을 옮겼고, 구독을 안 끊으면 죽은 선이 남는(메모리가 새는) 위험이 있었다. 그 길이 통째로 없어졌다.
/// </summary>
public sealed class ConnectionViewModel
{
    public ConnectionViewModel(EditorConnection model, ConnectorViewModel source, ConnectorViewModel target)
    {
        Model = model;
        Source = source;
        Target = target;
    }

    public EditorConnection Model { get; }

    /// <summary>출력 점. 컨텍스트 입력(roi 같은 것)이면 그 블록의 점.</summary>
    public ConnectorViewModel Source { get; }

    /// <summary>입력 점.</summary>
    public ConnectorViewModel Target { get; }

    public string Label => $"{Model.SourceNodeId ?? "ctx"}.{Model.SourcePort} → {Model.TargetNodeId}.{Model.TargetPort}";
}
