using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DiePipeline.Wpf.ViewModels;
using Nodify;
using Nodify.Interactivity;

namespace DiePipeline.Wpf.Views;

/// <summary>
/// 블록 편집기 화면. DataContext 는 <see cref="GraphEditorViewModel"/>.
/// ★ die 탭과 웨이퍼 편집 탭이 같이 쓴다 — 탭마다 다른 것은 오른쪽 아래 한 칸(<see cref="SideContent"/>)뿐이다.
/// ★ 블록 끌기 · 선 잇기 · 확대 · 이동은 Nodify 가 한다. 여기 남은 것은 '그래프 전체 보기' 하나 —
///   확대 비율과 화면 위치는 화면만 아는 값이라 뷰모델이 직접 못 한다.
/// </summary>
public partial class GraphEditorView : UserControl
{
    /// <summary>오른쪽 아래 칸에 끼울 것 — die 탭은 미리보기, 웨이퍼 편집 탭은 실행.</summary>
    public static readonly DependencyProperty SideContentProperty = DependencyProperty.Register(
        nameof(SideContent), typeof(object), typeof(GraphEditorView), new PropertyMetadata(null));

    // 그래프 전체 보기를 아직 못 했다 — 캔버스가 보이고 크기가 생기면 한다.
    private bool _fitPending;

    /// <summary>
    /// ★ Nodify 기본은 선 더블클릭 = '선 나누기' 다. 우리는 선 나누기를 안 쓰니 풀어 둔다 — 더블클릭은 끊기에 쓴다(<see cref="OnLineMouseDown"/>).
    /// ★ 손짓 설정은 앱 전체에 한 번 — 화면이 처음 만들어질 때.
    /// </summary>
    static GraphEditorView() => EditorGestures.Mappings.Connection.Split.Unbind();

    public GraphEditorView()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is GraphEditorViewModel old)
            {
                old.FitRequested -= FitToScreen;
            }

            if (e.NewValue is GraphEditorViewModel editor)
            {
                editor.FitRequested += FitToScreen;
            }
        };

        Loaded += (_, _) => FitToScreen();

        // ★ 맞추기를 미뤄 둔 채 탭이 보이고 크기가 생기면 — 또는 Nodify 의 '확대 금지' 가 풀리면 — 그때 맞춘다(아래 FitToScreen).
        Canvas.SizeChanged += (_, _) => FitIfPending();
        IsVisibleChanged += (_, _) => FitIfPending();
        DependencyPropertyDescriptor.FromProperty(NodifyEditor.DisableZoomingProperty, typeof(NodifyEditor))
            .AddValueChanged(Canvas, (_, _) => FitIfPending());
    }

    public object? SideContent
    {
        get => GetValue(SideContentProperty);
        set => SetValue(SideContentProperty, value);
    }

    /// <summary>
    /// 선 더블클릭 → 끊기. Alt+클릭 끊기는 Nodify 기본 그대로다.
    /// ★ Nodify 의 끊기 손짓은 마우스를 <b>뗄 때</b> 판정하는데, 실제 더블클릭은 뗄 때 '두 번째 클릭' 으로 안 잡혀서
    ///   손짓에 더블클릭을 걸어도 안 끊겼다. 그래서 <b>누를 때</b> 클릭 횟수를 직접 본다.
    ///   <c>Remove()</c> 는 Nodify 가 끊을 때 부르는 것과 같다 — 편집기의 RemoveConnectionCommand 로 간다.
    /// </summary>
    private void OnLineMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is BaseConnection line)
        {
            line.Remove();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 그래프 전체가 보이게 맞춘다. ★ 블록 크기를 잰 <b>뒤</b>라야 맞출 수 있어서, 화면 배치가 끝난 다음 차례로 미룬다.
    /// ★ 캔버스가 안 보이거나 크기가 0 이면(아직 안 연 탭) 맞출 화면이 없다 — 그때 맞추면 가장 작게(0.1배) 줄어든다.
    ///   탭이 처음 보이는 순간에는 Nodify 가 잠깐 확대를 막아 두기도 한다(<c>DisableZooming</c>) — 그때 맞추면 무시된다.
    ///   그래서 '맞춰야 함' 만 적어 두고, 보이고 크기가 생기고 확대가 풀린 뒤 한 번 맞춘다.
    /// </summary>
    private void FitToScreen()
    {
        _fitPending = true;
        FitIfPending();
    }

    private void FitIfPending()
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_fitPending || !Canvas.IsVisible || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0 || Canvas.DisableZooming)
            {
                return;
            }

            _fitPending = false;
            Canvas.FitToScreen(null);
        }));
}
