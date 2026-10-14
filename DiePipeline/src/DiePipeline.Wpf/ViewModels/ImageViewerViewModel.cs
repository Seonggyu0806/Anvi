using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Cv.Imaging;
using DiePipeline.Viewer;
using DiePipeline.Wpf.Imaging;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;
using Microsoft.Win32;
using OpenCvSharp;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>뷰어의 왼쪽 버튼이 하는 일 — 보기(아무것도 안 그림) 또는 그 종류의 영역 그리기.</summary>
public enum ViewerTool
{
    View,
    Roi,
    Chip,
    Feature,
}

/// <summary>
/// 원본 이미지 뷰어 — 원본 한 장을 열어 축소본 + 선명 조각으로 보여 주고, 커서 아래 원본 좌표 · 픽셀값을 알린다
/// (교수님 <c>ImageViewerModel</c>).
///
/// ★ 영역 그리기 — 검사 ROI(1) · 정상 die(N) · 정렬 특징(N). 다이 · 웨이퍼 에디터에 넘긴다(<see cref="ApplyToDie"/> · <see cref="ApplyToWafer"/>).
///   die-to-die 는 같은 크기끼리 비교하므로, 기준 크기가 생기면 다음 영역은 <b>같은 크기로 찍는다</b>(교수님 규칙).
/// ★ 결함 표시 — 웨이퍼 결함을 원본 위에 얹고 그 자리로 간다(<see cref="ShowMarkers"/> · <see cref="CenterOn"/>).
///
/// ★ 그림은 '원본 픽셀 크기의 판' 위에 놓인다. 확대는 판 전체에 배율을 거는 것이라, 판 위의 좌표는 <b>늘 원본 픽셀</b>이다 —
///   다음 단계(영역 그리기 · 결함 표시)도 좌표를 다시 계산하지 않는다.
/// ★ 보이는 방법(축소 배율 · 선명 조각이 필요한가 · 보이는 영역)은 <see cref="ViewportPlanner"/> 가 정한다 — 화면 없이 시험된다.
///   여기는 이미지 소스에서 읽어 그림으로 바꾸는 일만 한다.
/// ★ 아무것도 저장하지 않는다 — 영역은 화면에만 있다(저장은 다음 단계, 레포 밖에만).
/// </summary>
public sealed class ImageViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>배율 범위 — 원본 1 px 이 화면 0.005 ~ 16 px.</summary>
    public const double MinZoom = 0.005;

    public const double MaxZoom = 16;

    private IImageSource? _source;
    private string? _path;
    private string _title = string.Empty;
    private string? _message;
    private BitmapSource? _display;
    private BitmapSource? _sharpImage;
    private Roi _sharpRegion;
    private int _proxyFactor = 1;
    private double _zoom = 1;
    private const string DrawHint = "도구를 고르고 그림 위를 왼쪽 버튼으로 끌어 영역을 그립니다";

    private readonly RelayCommand _applyDieCommand;
    private readonly RelayCommand _applyWaferCommand;
    private string _cursorText = string.Empty;
    private ViewerTool _tool = ViewerTool.View;
    private string _statusText = DrawHint;
    private double _originX;
    private double _originY;
    private Func<PreviewInput, string>? _applyToDie;
    private Func<IReadOnlyList<PointF>, PointF, string>? _applyToWafer;

    public ImageViewerViewModel()
    {
        OpenCommand = new RelayCommand(OpenWithDialog);
        ClearChipsCommand = new RelayCommand(() => Clear(ChipRegions));
        ClearFeaturesCommand = new RelayCommand(() => Clear(FeatureRegions));
        ClearMarkersCommand = new RelayCommand(ClearMarkers);
        _applyDieCommand = new RelayCommand(ApplyDie, () => _applyToDie is not null && RoiRegions.Count > 0 && ChipRegions.Count > 0);
        _applyWaferCommand = new RelayCommand(ApplyWafer, () => _applyToWafer is not null && FeatureRegions.Count >= 2);
    }

    /// <summary>새 원본을 열었다 — 화면이 그림 전체가 보이게 맞춘다(배율은 화면 크기를 아는 쪽이 정한다).</summary>
    public event Action? FitRequested;

    /// <summary>원본 (x, y) 를 화면 가운데에 — 배율 · 스크롤은 화면이 정한다.</summary>
    public event Action<double, double>? CenterRequested;

    /// <summary>
    /// 원본이 바뀌었다(새로 열거나 올렸다). ★ 이 뷰어의 영역으로 돌던 다이 미리보기는 옛 원본을 보고 있으니 되돌려야 한다.
    /// </summary>
    public event Action? SourceChanged;

    public ICommand OpenCommand { get; }

    public ICommand ClearChipsCommand { get; }

    public ICommand ClearFeaturesCommand { get; }

    public ICommand ClearMarkersCommand { get; }

    /// <summary>'다이 에디터에 적용' — 검사 ROI 와 정상 die 가 있어야 누를 수 있다.</summary>
    public ICommand ApplyDieCommand => _applyDieCommand;

    /// <summary>'웨이퍼 에디터에 적용' — 정렬 특징이 2개 이상이어야 누를 수 있다.</summary>
    public ICommand ApplyWaferCommand => _applyWaferCommand;

    /// <summary>
    /// 다이 미리보기를 이 영역으로 돌리는 곳 — 창이 이어 준다. 돌려준 글자를 아래에 띄운다.
    /// ★ 뷰어는 다이 에디터를 모른다 — 넘기기만 한다(교수님 앱과 같다).
    /// </summary>
    public Func<PreviewInput, string>? ApplyToDie
    {
        get => _applyToDie;
        set
        {
            _applyToDie = value;
            _applyDieCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>웨이퍼 레시피에 표식 설계 자리 · die(0,0) 원점을 쓰는 곳 — 창이 이어 준다.</summary>
    public Func<IReadOnlyList<PointF>, PointF, string>? ApplyToWafer
    {
        get => _applyToWafer;
        set
        {
            _applyToWafer = value;
            _applyWaferCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>왼쪽 버튼이 하는 일.</summary>
    public ViewerTool Tool
    {
        get => _tool;
        set => SetField(ref _tool, value);
    }

    /// <summary>검사 ROI — 0개 또는 1개. 목록 칸을 다른 영역과 같은 모양으로 그리려고 목록으로 둔다.</summary>
    public ObservableCollection<RegionViewModel> RoiRegions { get; } = [];

    public ObservableCollection<RegionViewModel> ChipRegions { get; } = [];

    public ObservableCollection<RegionViewModel> FeatureRegions { get; } = [];

    /// <summary>그림 위에 그릴 영역 전부 — 셋을 합친 것.</summary>
    public ObservableCollection<RegionViewModel> Regions { get; } = [];

    /// <summary>결함 표시 — 빨강, 고른 것은 노랑. 누를 수 없다(그리기를 막지 않게).</summary>
    public ObservableCollection<OverlayBoxViewModel> Markers { get; } = [];

    public bool HasMarkers => Markers.Count > 0;

    public string MarkerText => $"결함 {Markers.Count}개 표시 중";

    /// <summary>die(0,0) 원점(원본 px) — 표식 설계 자리 = 특징 왼쪽 위 − 이 값. 웨이퍼 레시피의 <c>firstDieX/Y</c> 와 같은 값.</summary>
    public double OriginX
    {
        get => _originX;
        set => SetField(ref _originX, value);
    }

    public double OriginY
    {
        get => _originY;
        set => SetField(ref _originY, value);
    }

    /// <summary>영역 · 적용에 대한 알림 — 왼쪽 판 맨 아래.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>영역 · 결함 선 두께, 이름표 글자 크기(원본 px) — 배율이 바뀌어도 화면에서 같은 굵기로 보이게.</summary>
    public double RegionLine => 1.5 / Zoom;

    public double RegionLabelSize => 12 / Zoom;

    public bool HasImage => _source is not null;

    /// <summary>지금 연 원본 파일 — 다음 단계(결함 위치로 이동)가 '같은 사진인가' 를 볼 때 쓴다. 열지 않았으면 null.</summary>
    public string? SourcePath => _path;

    /// <summary>원본 픽셀 크기 = 판의 크기.</summary>
    public int ImageWidth => _source?.Width ?? 1;

    public int ImageHeight => _source?.Height ?? 1;

    /// <summary>축소 배율 — 축소본 1 px 이 원본 몇 px 인가. 1 이면 원본을 그대로 올렸다.</summary>
    public int ProxyFactor => _proxyFactor;

    /// <summary>판 전체에 까는 그림 — 원본 그대로 또는 축소본. 판 크기로 늘여 그린다.</summary>
    public BitmapSource? Display
    {
        get => _display;
        private set => SetField(ref _display, value);
    }

    /// <summary>
    /// 선명 조각 — 축소본이 흐려질 만큼 확대했을 때 <b>보이는 영역만</b> 원본에서 읽은 것. 판 위 (<see cref="SharpX"/>, <see cref="SharpY"/>) 에 덮는다.
    /// 필요 없으면 null.
    /// </summary>
    public BitmapSource? SharpImage
    {
        get => _sharpImage;
        private set => SetField(ref _sharpImage, value);
    }

    public double SharpX => _sharpRegion.X;

    public double SharpY => _sharpRegion.Y;

    public double SharpWidth => Math.Max(1, _sharpRegion.Width);

    public double SharpHeight => Math.Max(1, _sharpRegion.Height);

    /// <summary>확대 배율 — 원본 1 px 이 화면 몇 px 인가. 화면(휠 · 단추)이 바꾼다.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetField(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom)))
            {
                OnPropertyChanged(nameof(ZoomText));
                OnPropertyChanged(nameof(RegionLine));
                OnPropertyChanged(nameof(RegionLabelSize));
            }
        }
    }

    public string ZoomText => (Zoom * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>커서 아래 원본 좌표 · 픽셀값. 그림 밖이면 빈 글자.</summary>
    public string CursorText
    {
        get => _cursorText;
        private set => SetField(ref _cursorText, value);
    }

    /// <summary>아래 줄 — 파일 이름 · 원본 크기 · 축소 배율. 못 열었으면 그 까닭.</summary>
    public string InfoText
        => _message ?? (HasImage
            ? $"{_title} · {ImageWidth} × {ImageHeight} px · " + (ProxyFactor > 1 ? $"축소본 1/{ProxyFactor} + 확대하면 보이는 영역만 원본" : "원본 그대로")
            : "원본 없음");

    /// <summary>원본 파일을 연다 — raw(+ 설명 파일) · bmp · png · tif. 못 열면 까닭을 아래 줄에 띄우고 지금 그림은 그대로 둔다.</summary>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IImageSource source;

        try
        {
            source = ImageSource.Open(path);
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or InvalidDataException)
        {
            _message = $"열지 못했습니다 — {error.Message}";
            OnPropertyChanged(nameof(InfoText));
            return;
        }

        Load(source, Path.GetFileName(path), path);
    }

    /// <summary>
    /// 이미지 소스를 올린다. ★ 옛 소스는 놓는다 — 파일이면 손잡이를 붙잡고 있다.
    /// ★ 받은 소스는 이 뷰어가 놓는다.
    /// </summary>
    public void Load(IImageSource source, string title, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source?.Dispose();
        _source = source;
        _path = path;
        _title = title;
        _message = null;
        _proxyFactor = ViewportPlanner.ProxyFactor(source.Width, source.Height);

        // ★ 원본이 작으면 그대로, 크면 긴 변 4096 으로 솎아 낸 축소본 — 솎는 배율은 축소 배율과 같다.
        using (Mat overview = source.ReadOverview(_proxyFactor == 1 ? Math.Max(source.Width, source.Height) : ViewportPlanner.MaxProxyLong))
        {
            Display = BitmapConvert.ToBitmap(overview);
        }

        ClearSharp();
        CursorText = string.Empty;

        // ★ 영역 · 결함 표시는 옛 원본의 좌표다 — 새 원본에서는 뜻이 없다.
        RoiRegions.Clear();
        ChipRegions.Clear();
        FeatureRegions.Clear();
        RebuildRegions();
        ClearMarkers();
        StatusText = DrawHint;

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        OnPropertyChanged(nameof(ProxyFactor));
        OnPropertyChanged(nameof(InfoText));

        SourceChanged?.Invoke();
        FitRequested?.Invoke();
    }

    // ───────────────────────── 영역 그리기 ─────────────────────────

    /// <summary>
    /// 왼쪽 버튼으로 (x0, y0) → (x1, y1)(원본 px) 을 끌었다 — 지금 도구의 영역을 하나 놓는다.
    /// ★ 기준 크기가 있으면 끈 크기는 버리고 <b>끈 자리(왼쪽 위)에 같은 크기로 찍는다</b> —
    ///   정상 die 는 ROI 크기, ROI 는 첫 정상 die 크기, 정렬 특징은 첫 특징 크기(교수님 규칙).
    /// </summary>
    public void PlaceRegion(double x0, double y0, double x1, double y1)
    {
        if (_source is null || Tool == ViewerTool.View)
        {
            return;
        }

        (int Width, int Height)? size = Tool switch
        {
            ViewerTool.Roi => ChipRegions.Count > 0 ? SizeOf(ChipRegions[0]) : null,
            ViewerTool.Chip => RoiRegions.Count > 0 ? SizeOf(RoiRegions[0]) : ChipRegions.Count > 0 ? SizeOf(ChipRegions[0]) : null,
            _ => FeatureRegions.Count > 0 ? SizeOf(FeatureRegions[0]) : null,
        };

        Roi? area = size is { } s
            ? RegionTool.Stamp(Math.Min(x0, x1), Math.Min(y0, y1), s.Width, s.Height, ImageWidth, ImageHeight)
            : RegionTool.FromDrag(x0, y0, x1, y1, ImageWidth, ImageHeight);

        if (area is not { } a)
        {
            StatusText = "끌어서 크기를 정하세요 — 첫 영역은 끈 크기가 그 종류의 크기가 됩니다";
            return;
        }

        switch (Tool)
        {
            case ViewerTool.Roi:
                RoiRegions.Clear();
                RoiRegions.Add(new RegionViewModel(RegionKind.Roi, a, Remove));
                break;
            case ViewerTool.Chip:
                ChipRegions.Add(new RegionViewModel(RegionKind.Chip, a, Remove));
                break;
            default:
                FeatureRegions.Add(new RegionViewModel(RegionKind.Feature, a, Remove));
                break;
        }

        RebuildRegions();
        StatusText = $"ROI {RoiRegions.Count} · 정상 die {ChipRegions.Count} · 정렬 특징 {FeatureRegions.Count}";
    }

    /// <summary>원본 (x, y) 를 화면 가운데에 둔다. 그림 밖이면 가장자리로.</summary>
    public void CenterOn(double x, double y)
    {
        if (_source is null)
        {
            return;
        }

        CenterRequested?.Invoke(Math.Clamp(x, 0, ImageWidth - 1), Math.Clamp(y, 0, ImageHeight - 1));
    }

    /// <summary>결함 표시를 바꿔 얹는다. 상자는 원본 px.</summary>
    public void ShowMarkers(IEnumerable<OverlayBoxViewModel> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        Markers.Clear();

        foreach (OverlayBoxViewModel marker in markers)
        {
            Markers.Add(marker);
        }

        RaiseMarkers();
    }

    /// <summary>이 결함의 표시만 노랑으로.</summary>
    public void Highlight(object? item)
    {
        foreach (OverlayBoxViewModel marker in Markers)
        {
            marker.IsSelected = ReferenceEquals(marker.Item, item);
        }
    }

    public void ClearMarkers()
    {
        Markers.Clear();
        RaiseMarkers();
    }

    private static (int Width, int Height) SizeOf(RegionViewModel region) => (region.Width, region.Height);

    private void Remove(RegionViewModel region)
    {
        _ = RoiRegions.Remove(region) || ChipRegions.Remove(region) || FeatureRegions.Remove(region);
        RebuildRegions();
    }

    private void Clear(ObservableCollection<RegionViewModel> regions)
    {
        regions.Clear();
        RebuildRegions();
    }

    /// <summary>그림 위 목록을 다시 모으고 이름표를 다시 매긴다 — 지운 뒤에도 1, 2, 3 으로 이어지게.</summary>
    private void RebuildRegions()
    {
        Regions.Clear();

        foreach (RegionViewModel roi in RoiRegions)
        {
            roi.Label = "ROI";
            Regions.Add(roi);
        }

        for (int i = 0; i < ChipRegions.Count; i++)
        {
            ChipRegions[i].Label = (i + 1).ToString(CultureInfo.InvariantCulture);
            Regions.Add(ChipRegions[i]);
        }

        for (int i = 0; i < FeatureRegions.Count; i++)
        {
            FeatureRegions[i].Label = "F" + (i + 1).ToString(CultureInfo.InvariantCulture);
            Regions.Add(FeatureRegions[i]);
        }

        _applyDieCommand.RaiseCanExecuteChanged();
        _applyWaferCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 다이 미리보기를 지금 ROI · 정상 die 로 돌린다. ★ 지금 숫자를 <b>찍어서</b> 넘긴다 — 넘긴 뒤 영역을 고쳐도 미리보기는 그대로다
    ///   (다시 적용하면 바뀐다). 크기가 다르거나 그림 밖이면 넘기지 않고 까닭을 띄운다.
    /// </summary>
    private void ApplyDie()
    {
        if (_source is null || _applyToDie is null || RoiRegions.Count == 0)
        {
            return;
        }

        Roi roi = RoiRegions[0].Area;
        Roi[] chips = [.. ChipRegions.Select(c => c.Area)];

        if (RegionTool.CheckDieRegions(roi, chips, ImageWidth, ImageHeight) is { } problem)
        {
            StatusText = "적용 못 함 — " + problem;
            return;
        }

        StatusText = _applyToDie(new PreviewInput(_source, _title, roi, chips));
    }

    /// <summary>표식 설계 자리(특징 왼쪽 위 − 원점)를 계산해 웨이퍼 레시피에 쓴다.</summary>
    private void ApplyWafer()
    {
        if (_source is null || _applyToWafer is null)
        {
            return;
        }

        Roi[] features = [.. FeatureRegions.Select(f => f.Area)];

        if (RegionTool.CheckFeatures(features, ImageWidth, ImageHeight) is { } problem)
        {
            StatusText = "적용 못 함 — " + problem;
            return;
        }

        PointF[] expected = [.. features.Select(f => RegionTool.Expected(f, OriginX, OriginY))];
        StatusText = _applyToWafer(expected, new PointF(OriginX, OriginY));
    }

    private void RaiseMarkers()
    {
        OnPropertyChanged(nameof(HasMarkers));
        OnPropertyChanged(nameof(MarkerText));
    }

    /// <summary>
    /// 지금 화면(배율 · 보이는 곳)에 맞춰 선명 조각을 읽거나 치운다. 화면이 확대 · 이동을 멈춘 뒤에 부른다.
    /// ★ 같은 영역이면 다시 읽지 않는다.
    /// </summary>
    public void ShowSharp(ViewportTransform transform, double screenWidth, double screenHeight)
    {
        if (_source is null)
        {
            return;
        }

        Roi? region = ViewportPlanner.SharpRegion(transform, screenWidth, screenHeight, ImageWidth, ImageHeight, ProxyFactor);

        if (region is not { } r)
        {
            ClearSharp();
            return;
        }

        if (SharpImage is not null && r == _sharpRegion)
        {
            return;
        }

        using Mat crop = _source.ReadRegion(r);

        _sharpRegion = r;
        SharpImage = BitmapConvert.ToBitmap(crop);
        RaiseSharpPlace();
    }

    /// <summary>커서가 판 위 (x, y)(원본 픽셀)에 있다 — 그 픽셀의 값을 원본에서 읽어 알린다. 그림 밖이면 지운다.</summary>
    public void PointAt(double x, double y)
    {
        if (_source is null || x < 0 || y < 0 || x >= ImageWidth || y >= ImageHeight)
        {
            CursorText = string.Empty;
            return;
        }

        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);

        using Mat pixel = _source.ReadRegion(new Roi(ix, iy, 1, 1));
        float value = pixel.At<float>(0, 0);

        CursorText = string.Create(CultureInfo.InvariantCulture, $"({ix}, {iy}) · 값 {value:F3} ({value * 255:F0}/255)");
    }

    public void ClearCursor() => CursorText = string.Empty;

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }

    private void OpenWithDialog()
    {
        OpenFileDialog dialog = new()
        {
            Filter = "원본 (*.raw;*.bmp;*.png;*.tif)|*.raw;*.bmp;*.png;*.tif;*.tiff|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            Open(dialog.FileName);
        }
    }

    private void ClearSharp()
    {
        SharpImage = null;
        _sharpRegion = default;
        RaiseSharpPlace();
    }

    private void RaiseSharpPlace()
    {
        OnPropertyChanged(nameof(SharpX));
        OnPropertyChanged(nameof(SharpY));
        OnPropertyChanged(nameof(SharpWidth));
        OnPropertyChanged(nameof(SharpHeight));
    }
}