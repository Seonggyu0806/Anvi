using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Imaging;
using DiePipeline.Cv.Imaging;
using DiePipeline.Viewer;
using DiePipeline.Wpf.Imaging;
using DiePipeline.Wpf.Mvvm;
using Microsoft.Win32;
using OpenCvSharp;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 원본 이미지 뷰어 — 원본 한 장을 열어 축소본 + 선명 조각으로 보여 주고, 커서 아래 원본 좌표 · 픽셀값을 알린다
/// (교수님 <c>ImageViewerModel</c> 의 '보기' 부분).
///
/// ★ 그림은 '원본 픽셀 크기의 판' 위에 놓인다. 확대는 판 전체에 배율을 거는 것이라, 판 위의 좌표는 <b>늘 원본 픽셀</b>이다 —
///   다음 단계(영역 그리기 · 결함 표시)도 좌표를 다시 계산하지 않는다.
/// ★ 보이는 방법(축소 배율 · 선명 조각이 필요한가 · 보이는 영역)은 <see cref="ViewportPlanner"/> 가 정한다 — 화면 없이 시험된다.
///   여기는 이미지 소스에서 읽어 그림으로 바꾸는 일만 한다.
/// ★ 보기만 한다 — 아무것도 저장하지 않는다.
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
    private string _cursorText = string.Empty;

    public ImageViewerViewModel()
    {
        OpenCommand = new RelayCommand(OpenWithDialog);
    }

    /// <summary>새 원본을 열었다 — 화면이 그림 전체가 보이게 맞춘다(배율은 화면 크기를 아는 쪽이 정한다).</summary>
    public event Action? FitRequested;

    public ICommand OpenCommand { get; }

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

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        OnPropertyChanged(nameof(ProxyFactor));
        OnPropertyChanged(nameof(InfoText));

        FitRequested?.Invoke();
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