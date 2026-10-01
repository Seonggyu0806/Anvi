using System.Windows.Input;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 팔레트의 블록 한 개. 누르면 캔버스에 노드가 추가된다.
///
/// ★ 이 클래스는 <b>"Binarize" 가 뭔지 모른다.</b> 글자와 "누르면 할 일"만 들고 있다.
///   그래서 노드가 늘어도 이 파일은 안 늘어난다.
/// </summary>
public sealed class PaletteItemViewModel
{
    public PaletteItemViewModel(string category, string typeName, Action<string> add)
    {
        Category = category;
        TypeName = typeName;
        AddCommand = new RelayCommand(() => add(typeName));
    }

    public string Category { get; }

    public string TypeName { get; }

    public ICommand AddCommand { get; }
}