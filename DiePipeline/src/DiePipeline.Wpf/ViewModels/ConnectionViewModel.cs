using System.ComponentModel;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 선 하나를 화면에 그리기 위한 것. 노드가 움직이면 <b>끝점을 따라 옮긴다.</b>
/// </summary>
public sealed class ConnectionViewModel : ObservableObject, IDisposable
{
    // ★ XAML 의 블록 크기와 맞아야 한다. 안 맞으면 선이 블록 옆구리에 어정쩡하게 붙는다.
    private const double NodeWidth = 150;
    private const double PortRowY = 34;

    private readonly NodeViewModel? _source;    // null = 컨텍스트 입력(roi 같은 것)
    private readonly NodeViewModel _target;

    public ConnectionViewModel(EditorConnection model, NodeViewModel? source, NodeViewModel target)
    {
        Model = model;
        _source = source;
        _target = target;

        if (_source is not null)
        {
            _source.PropertyChanged += OnNodeMoved;
        }

        _target.PropertyChanged += OnNodeMoved;
    }

    public EditorConnection Model { get; }

    public string Label => $"{Model.SourceNodeId ?? "ctx"}.{Model.SourcePort} → {Model.TargetNodeId}.{Model.TargetPort}";

    public double StartX => _source is null ? 12 : _source.X + NodeWidth;

    public double StartY => (_source?.Y ?? _target.Y) + PortRowY;

    public double EndX => _target.X;

    public double EndY => _target.Y + PortRowY;

    private void OnNodeMoved(object? sender, PropertyChangedEventArgs e)
    {
        // 블록이 움직였을 때만. 체크박스를 껐다고 선을 다시 그릴 필요는 없다.
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y))
        {
            OnPropertyChanged(nameof(StartX));
            OnPropertyChanged(nameof(StartY));
            OnPropertyChanged(nameof(EndX));
            OnPropertyChanged(nameof(EndY));
        }
    }

    /// <summary>
    /// ★★ 구독을 끊는다. <b>이걸 빼면 메모리가 샌다.</b>
    ///   선을 지웠는데 노드가 아직 이 객체를 붙들고 있으면 죽은 선이 영영 안 사라진다.
    ///   2주차에 "예외 경로 메모리 누수 9곳" 을 잡았던 것과 같은 종류의 실수다.
    /// </summary>
    public void Dispose()
    {
        if (_source is not null)
        {
            _source.PropertyChanged -= OnNodeMoved;
        }

        _target.PropertyChanged -= OnNodeMoved;
    }
}