using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DiePipeline.Wpf.ViewModels;
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

    /// <summary>
    /// ★ 선 끊기 = <b>선 더블클릭</b>(또는 Alt+클릭). Nodify 기본은 Alt+클릭뿐이고, 더블클릭은 '선 나누기' 에 걸려 있다 —
    ///   우리는 선 나누기를 안 쓴다. 써 보니 더블클릭이 자연스러워 바꿨다(교수님 앱은 기본 그대로라 Alt+클릭뿐이다).
    /// ★ 손짓 설정은 앱 전체에 한 번 — 화면이 처음 만들어질 때.
    /// </summary>
    static GraphEditorView()
    {
        EditorGestures.ConnectionGestures line = EditorGestures.Mappings.Connection;

        line.Split.Unbind();
        line.Disconnect.Value = new MultiGesture(
            MultiGesture.Match.Any,
            new Nodify.Interactivity.MouseGesture(MouseAction.LeftDoubleClick),
            new Nodify.Interactivity.MouseGesture(MouseAction.LeftClick, ModifierKeys.Alt));
    }

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
    }

    public object? SideContent
    {
        get => GetValue(SideContentProperty);
        set => SetValue(SideContentProperty, value);
    }

    /// <summary>
    /// 그래프 전체가 보이게 맞춘다. ★ 블록 크기를 잰 <b>뒤</b>라야 맞출 수 있어서, 화면 배치가 끝난 다음 차례로 미룬다.
    /// </summary>
    private void FitToScreen()
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Canvas.FitToScreen(null)));
}
