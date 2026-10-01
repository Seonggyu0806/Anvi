using System.Windows.Input;

namespace DiePipeline.Wpf.Mvvm;

/// <summary>
/// 버튼 하나 = 객체 하나.
///
/// ★ 버튼 누름을 <b>값으로</b> 만드는 것이 핵심이다.
///   코드비하인드에 <c>Button_Click</c>을 쓰면 그 동작은 창이 떠야만 부를 수 있다 —
///   테스트에서 부를 방법이 없다. 객체로 만들면 <c>vm.RunCommand.Execute(null)</c>이 된다.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>false면 WPF가 버튼을 <b>알아서 회색으로</b> 만든다. 손으로 IsEnabled를 안 만져도 된다.</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    /// <summary>조건이 바뀌었으니 다시 물어보라고 알린다(예: 이미지를 열어서 이제 실행 가능해짐).</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
