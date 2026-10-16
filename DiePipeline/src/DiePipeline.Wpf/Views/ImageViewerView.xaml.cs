using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DiePipeline.Viewer;
using DiePipeline.Wpf.ViewModels;

namespace DiePipeline.Wpf.Views;

/// <summary>
/// 원본 이미지 뷰어 화면 — 휠 확대 · 오른쪽 끌기 이동 · 왼쪽 끌기 영역 그리기 · 화면 맞춤 · 가운데 두기 · 선명 조각 요청.
/// DataContext 는 <see cref="ImageViewerViewModel"/>.
///
/// ★ 여기 있는 것은 <b>화면만 아는 값</b>(스크롤 위치 · 보이는 크기 · 마우스)을 다루는 일뿐이다.
///   커서 기준 확대 · 맞춤 배율 · 보이는 영역 · 끈 사각형 · 가운데 두기 계산은
///   <see cref="ViewportTransform"/> · <see cref="ViewportPlanner"/> · <see cref="RegionTool"/>(시험됨)가 한다.
/// </summary>
public partial class ImageViewerView : UserControl
{
    // ★ 선명 조각은 확대 · 이동을 멈추고 잠깐 뒤에 읽는다 — 끌고 있는 동안 매번 원본을 읽으면 화면이 멈춘다.
    private readonly DispatcherTimer _sharpTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private Point? _panFrom;
    private double _panX;
    private double _panY;

    // 왼쪽 버튼으로 끄는 중 — 시작점(판 위 = 원본 px).
    private Point? _drawFrom;

    // ★ 아직 못 한 화면 맞추기 · 가운데 두기 — 화면이 보이고 크기가 생기면 한다(블록 캔버스와 같은 이유).
    //   한 칸뿐이라 나중 요청이 이긴다: 결함 자리로 가면서 새 원본을 열면 '맞춤' 대신 '가운데 두기' 가 남는다.
    private Action? _pending;

    // 새 원본을 올리고 아직 한 번도 맞추거나 옮기지 않았다 — 이때의 배율은 옛 원본의 것이라 가운데 두기가 따르지 않는다.
    private bool _freshImage;

    public ImageViewerView()
    {
        InitializeComponent();

        _sharpTimer.Tick += (_, _) =>
        {
            _sharpTimer.Stop();
            Viewer?.ShowSharp(Transform, Scroll.ViewportWidth, Scroll.ViewportHeight);
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ImageViewerViewModel old)
            {
                old.FitRequested -= OnNewImage;
                old.CenterRequested -= CenterOn;
            }

            if (e.NewValue is ImageViewerViewModel viewer)
            {
                viewer.FitRequested += OnNewImage;
                viewer.CenterRequested += CenterOn;
            }
        };

        Scroll.SizeChanged += (_, _) => RunPending();
        IsVisibleChanged += (_, _) => RunPending();
    }

    private ImageViewerViewModel? Viewer => DataContext as ImageViewerViewModel;

    /// <summary>
    /// 지금 화면 — 화면 왼쪽 위가 가리키는 원본 좌표 + 배율.
    /// ★ 스크롤 위치 ÷ 배율로 셈하지 않고 <b>판에 직접 묻는다</b> — 그림이 화면보다 작으면 판이 가운데에 놓여
    ///   화면 왼쪽 위가 판 바깥(원본 좌표가 음수)이 된다. 스크롤 위치만 보면 그만큼 어긋난다.
    /// </summary>
    private ViewportTransform Transform
    {
        get
        {
            Point origin = Scroll.TranslatePoint(new Point(0, 0), Board);
            return new ViewportTransform(origin.X, origin.Y, Viewer?.Zoom ?? 1);
        }
    }

    // ───────────────────────── 확대 · 맞춤 ─────────────────────────

    /// <summary>
    /// 화면 (x, y) 아래 원본 점을 그 자리에 둔 채 배율을 바꾼다. ★ 배율을 바꾸면 판 크기가 바뀌므로, 판 크기를 다시 잰 뒤 스크롤 위치를 놓는다.
    /// </summary>
    private void ZoomAt(double screenX, double screenY, double newZoom)
    {
        if (Viewer is not { HasImage: true } viewer)
        {
            return;
        }

        newZoom = Math.Clamp(newZoom, ImageViewerViewModel.MinZoom, ImageViewerViewModel.MaxZoom);
        ViewportTransform next = Transform.ZoomAtScreen(screenX, screenY, newZoom);

        viewer.Zoom = newZoom;
        Scroll.UpdateLayout();
        Scroll.ScrollToHorizontalOffset(next.OriginX * newZoom);
        Scroll.ScrollToVerticalOffset(next.OriginY * newZoom);
    }

    private void ZoomAtCenter(double newZoom) => ZoomAt(Scroll.ViewportWidth / 2, Scroll.ViewportHeight / 2, newZoom);

    private void OnNewImage()
    {
        _freshImage = true;
        FitToScreen();
    }

    private void FitToScreen()
    {
        _pending = Fit;
        RunPending();
    }

    /// <summary>
    /// 원본 (x, y) 를 화면 가운데에. 100% 보다 작게 보고 있었으면 100% 로 — 결함이 실제 픽셀로 보이게(이미 더 크면 그대로).
    /// ★ 막 올린 원본이면 100% — 그때 배율은 옛 원본을 보던 값이다.
    /// </summary>
    private void CenterOn(double x, double y)
    {
        _pending = () => Center(x, y);
        RunPending();
    }

    private void RunPending()
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_pending is not { } action || Viewer is not { HasImage: true } || !IsVisible || Scroll.ActualWidth <= 0 || Scroll.ActualHeight <= 0)
            {
                return;
            }

            _pending = null;
            action();
        }));

    private void Fit()
    {
        if (Viewer is not { } viewer)
        {
            return;
        }

        _freshImage = false;

        // 스크롤 막대가 없는 크기로 맞춘다 — 맞춘 뒤에는 막대가 사라진다.
        viewer.Zoom = ViewportTransform.FitScale(viewer.ImageWidth, viewer.ImageHeight, Scroll.ActualWidth, Scroll.ActualHeight);
        Scroll.UpdateLayout();
        Scroll.ScrollToHorizontalOffset(0);
        Scroll.ScrollToVerticalOffset(0);
    }

    private void Center(double x, double y)
    {
        if (Viewer is not { } viewer)
        {
            return;
        }

        double zoom = _freshImage ? 1 : Math.Max(viewer.Zoom, 1);
        _freshImage = false;
        viewer.Zoom = zoom;
        Scroll.UpdateLayout();

        ViewportTransform target = RegionTool.CenteredOn(x, y, zoom, Scroll.ViewportWidth, Scroll.ViewportHeight);
        Scroll.ScrollToHorizontalOffset(target.OriginX * zoom);
        Scroll.ScrollToVerticalOffset(target.OriginY * zoom);
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitToScreen();

    private void OnActualSize(object sender, RoutedEventArgs e) => ZoomAtCenter(1);

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomAtCenter((Viewer?.Zoom ?? 1) * 1.25);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomAtCenter((Viewer?.Zoom ?? 1) / 1.25);

    // ───────────────────────── 마우스 ─────────────────────────

    /// <summary>휠 = 커서 기준 확대 · 축소(한 칸에 1.25배). ★ 스크롤로 넘기지 않는다.</summary>
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Viewer is not { HasImage: true } viewer)
        {
            return;
        }

        Point at = e.GetPosition(Scroll);
        ZoomAt(at.X, at.Y, e.Delta > 0 ? viewer.Zoom * 1.25 : viewer.Zoom / 1.25);
        e.Handled = true;
    }

    /// <summary>오른쪽 버튼 끌기 = 이동 — 블록 캔버스와 같은 조작.</summary>
    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        _panFrom = e.GetPosition(Scroll);
        _panX = Scroll.HorizontalOffset;
        _panY = Scroll.VerticalOffset;
        Scroll.CaptureMouse();
        e.Handled = true;
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (_panFrom is null)
        {
            return;
        }

        _panFrom = null;
        Scroll.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is { } from)
        {
            Point now = e.GetPosition(Scroll);
            Scroll.ScrollToHorizontalOffset(_panX - (now.X - from.X));
            Scroll.ScrollToVerticalOffset(_panY - (now.Y - from.Y));
        }

        // ★ 판 위 좌표 = 원본 픽셀(배율은 판에 걸려 있다).
        Point onBoard = e.GetPosition(Board);
        Viewer?.PointAt(onBoard.X, onBoard.Y);

        if (_drawFrom is { } start)
        {
            Stretch(start, onBoard);
        }

        Viewer?.MeasureHover(onBoard.X, onBoard.Y);
    }

    /// <summary>왼쪽 버튼 누름 — 그리기 도구면 끌기 시작, '거리' 면 점 찍기. '보기' 면 아무것도 안 한다.</summary>
    private void OnDrawStart(object sender, MouseButtonEventArgs e)
    {
        if (Viewer is not { HasImage: true, Tool: not ViewerTool.View } viewer)
        {
            return;
        }

        if (viewer.Tool == ViewerTool.Measure)
        {
            Point at = e.GetPosition(Board);
            viewer.MeasureClick(at.X, at.Y);
            e.Handled = true;
            return;
        }

        _drawFrom = e.GetPosition(Board);
        viewer.BeginDraw();
        Stretch(_drawFrom.Value, _drawFrom.Value);
        Rubber.Visibility = Visibility.Visible;
        Board.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>왼쪽 버튼 뗌 — 끈 사각형(원본 px)을 뷰모델에 넘긴다. 크기 · 도장 규칙은 뷰모델이 정한다.</summary>
    private void OnDrawEnd(object sender, MouseButtonEventArgs e)
    {
        if (_drawFrom is not { } start)
        {
            return;
        }

        Point end = e.GetPosition(Board);
        EndDraw();
        Viewer?.PlaceRegion(start.X, start.Y, end.X, end.Y);
        e.Handled = true;
    }

    /// <summary>끄는 중에 마우스를 놓쳤다(다른 창으로 넘어감 등) — 그리지 않고 접는다.</summary>
    private void OnDrawLost(object sender, MouseEventArgs e) => EndDraw();

    private void EndDraw()
    {
        _drawFrom = null;
        Rubber.Visibility = Visibility.Collapsed;

        if (Board.IsMouseCaptured)
        {
            Board.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// 고무줄 사각형을 두 점(원본 px) 사이로 — 거꾸로 끌어도 같은 사각형.
    /// ★ 크기가 정해졌으면 끈 크기가 아니라 실제로 찍힐 상자를 마우스 자리에 — 놓으면 보이던 그대로 잡힌다.
    /// </summary>
    private void Stretch(Point a, Point b)
    {
        if (Viewer?.StampAt(b.X, b.Y) is { } stamp)
        {
            Canvas.SetLeft(Rubber, stamp.X);
            Canvas.SetTop(Rubber, stamp.Y);
            Rubber.Width = stamp.Width;
            Rubber.Height = stamp.Height;
            return;
        }

        Canvas.SetLeft(Rubber, Math.Min(a.X, b.X));
        Canvas.SetTop(Rubber, Math.Min(a.Y, b.Y));
        Rubber.Width = Math.Abs(b.X - a.X);
        Rubber.Height = Math.Abs(b.Y - a.Y);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => Viewer?.ClearCursor();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _sharpTimer.Stop();
        _sharpTimer.Start();
    }
}