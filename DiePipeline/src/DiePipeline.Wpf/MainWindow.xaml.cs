using System.Windows;
using System.Windows.Controls.Primitives;
using DiePipeline.Wpf.ViewModels;

namespace DiePipeline.Wpf;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// 블록 제목줄을 끌면 모델 좌표를 바꾼다. 선은 X/Y 변경을 구독하고 있어 저절로 따라온다.
    ///
    /// ★ 코드비하인드에 남은 <b>유일한 동작</b>이다. 마우스 끌기는 순수한 화면 일이라
    ///   뷰모델로 내려 봤자 테스트할 것이 없다. 나머지 판단은 전부 MainViewModel 에 있다.
    ///
    /// ★ Math.Max(0, …) — 왼쪽/위로 밀어내 화면 밖으로 사라지는 것을 막는다.
    /// </summary>
    private void NodeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NodeViewModel node })
        {
            node.X = Math.Max(0, node.X + e.HorizontalChange);
            node.Y = Math.Max(0, node.Y + e.VerticalChange);
        }
    }
}