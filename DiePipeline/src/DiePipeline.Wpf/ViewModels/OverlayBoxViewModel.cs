using System.Windows.Input;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 오버레이의 결함 네모 하나 — 그림 위 자리(원본 픽셀) + 번호 + 골라졌나.
///
/// ★ 다이 오버레이와 웨이퍼 오버레이가 같이 쓴다. <see cref="Item"/> 이 그 네모의 결함이다(die 결함 · 웨이퍼 결함).
/// ★ 네모를 누르면 그 결함을 고르고(<see cref="SelectCommand"/>), 결함 목록에서 고르면 네모가 강조된다(<see cref="IsSelected"/>) —
///   고른 결함은 하나뿐이라 그 값은 오버레이 주인이 들고, 네모는 표시만 따라간다.
/// </summary>
public sealed class OverlayBoxViewModel : ObservableObject
{
    private bool _isSelected;

    public OverlayBoxViewModel(object item, double x, double y, double width, double height, string label, string tooltip, Action<object> select)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(select);

        Item = item;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Label = label;
        Tooltip = tooltip;
        SelectCommand = new RelayCommand(() => select(item));
    }

    public object Item { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>네모 옆에 적는 번호 — 결함 목록의 # 와 같다.</summary>
    public string Label { get; }

    public string Tooltip { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public ICommand SelectCommand { get; }
}