using System.Windows.Input;
using DiePipeline.Core.Imaging;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>원본 위에 그리는 영역의 종류 — 색과 쓰임이 다르다(교수님 앱과 같은 색).</summary>
public enum RegionKind
{
    /// <summary>검사 ROI — 1개. 파랑. 다이 미리보기의 <c>roi</c>.</summary>
    Roi,

    /// <summary>정상 die — 여러 개. 초록. 다이 미리보기의 <c>chipRegions</c>(골든을 만들 이웃).</summary>
    Chip,

    /// <summary>정렬 특징 — 여러 개. 보라. 웨이퍼 레시피의 표식 설계 자리(<c>expected</c>).</summary>
    Feature,
}

/// <summary>
/// 원본 위에 그린 영역 하나 — 원본 픽셀 사각형 + 이름표 + ✕.
///
/// ★ 숫자 칸으로 다듬는다 — 고치면 그림 위 상자가 바로 따라온다(같은 값을 본다).
///   그림 밖으로 나가는지 · 크기가 맞는지는 적용할 때 본다(<see cref="Viewer.RegionTool.CheckDieRegions"/>).
/// ★ 아무것도 저장하지 않는다 — 화면에만 있다.
/// </summary>
public sealed class RegionViewModel : ObservableObject
{
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private string _label = string.Empty;

    public RegionViewModel(RegionKind kind, Roi area, Action<RegionViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(remove);

        Kind = kind;
        _x = area.X;
        _y = area.Y;
        _width = Math.Max(1, area.Width);
        _height = Math.Max(1, area.Height);
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public RegionKind Kind { get; }

    /// <summary>선 · 이름표 색 — ROI 파랑 · 정상 die 초록 · 정렬 특징 보라(교수님 앱과 같다).</summary>
    public string Color => Kind switch
    {
        RegionKind.Roi => "#3B82F6",
        RegionKind.Chip => "#22C55E",
        _ => "#8B5CF6",
    };

    /// <summary>그림 위 · 목록에 적는 이름 — ROI · 1, 2, … · F1, F2, …. 지우면 다시 매긴다.</summary>
    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public int X
    {
        get => _x;
        set => SetField(ref _x, Math.Max(0, value));
    }

    public int Y
    {
        get => _y;
        set => SetField(ref _y, Math.Max(0, value));
    }

    public int Width
    {
        get => _width;
        set => SetField(ref _width, Math.Max(1, value));
    }

    public int Height
    {
        get => _height;
        set => SetField(ref _height, Math.Max(1, value));
    }

    public Roi Area => new(X, Y, Width, Height);

    public ICommand RemoveCommand { get; }
}