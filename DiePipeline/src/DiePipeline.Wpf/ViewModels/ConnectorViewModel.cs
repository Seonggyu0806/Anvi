using System.Windows;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 블록의 포트 점 하나 — 왼쪽은 입력, 오른쪽은 출력(Nodify 커넥터).
///
/// ★ <see cref="Anchor"/> 는 Nodify 가 화면 위치를 계산해 <b>써 준다</b>. 선은 그 두 점을 따라 그려진다 —
///   블록 크기 · 포트 줄 높이를 우리가 계산할 일이 없다(전에는 상수로 맞춰야 선이 제자리에 붙었다).
/// </summary>
public sealed class ConnectorViewModel : ObservableObject
{
    private Point _anchor;
    private bool _isConnected;

    public ConnectorViewModel(NodeViewModel node, string title, bool isInput)
    {
        Node = node;
        Title = title;
        IsInput = isInput;
    }

    public NodeViewModel Node { get; }

    /// <summary>포트 이름. 컨텍스트 입력 블록의 출력은 키 그 자체다(예: "roi").</summary>
    public string Title { get; }

    public bool IsInput { get; }

    public Point Anchor
    {
        get => _anchor;
        set => SetField(ref _anchor, value);
    }

    /// <summary>선이 붙어 있나 — 점을 채워 그린다.</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }
}