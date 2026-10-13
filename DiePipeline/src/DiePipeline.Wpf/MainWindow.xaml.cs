using System.Windows;
using System.Windows.Input;
using DiePipeline.Wpf.Preview;
using DiePipeline.Wpf.ViewModels;

namespace DiePipeline.Wpf;

/// <summary>
/// ★ 코드비하인드는 화면 동작 하나뿐이다 — 미리보기 썸네일 더블클릭. 판단은 뷰모델(<see cref="MainViewModel.OpenOverlayCommand"/>)이 한다.
///   블록 끌기 · 선 끊기는 편집기 화면(<c>Views/GraphEditorView</c>)에 있다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// 썸네일 더블클릭 → 그 단계 그림을 배경으로 다이 오버레이.
    /// ★ 마우스 묶음(MouseBinding)은 시각 트리 밖이라 창의 명령을 찾아가지 못한다 — 그래서 여기서 직접 넘긴다.
    /// </summary>
    private void OnThumbnailMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: PreviewItem item } && DataContext is MainViewModel vm)
        {
            vm.OpenOverlayCommand.Execute(item);
            e.Handled = true;
        }
    }
}
