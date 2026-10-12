using System.Windows.Input;

namespace DiePipeline.Wpf.Mvvm;

/// <summary>
/// 화면이 <b>무엇을</b> 눌렀는지 같이 넘기는 버튼 — 끌기 시작한 포트, 더블클릭한 선.
/// ★ <see cref="RelayCommand"/> 와 같고, 받은 값이 <typeparamref name="T"/> 가 아니면 아무것도 안 한다.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;

    public RelayCommand(Action<T> execute) => _execute = execute;

    // ★ 늘 누를 수 있는 버튼이라 상태가 안 바뀐다 — 구독은 받되 부르지 않는다.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            _execute(value);
        }
    }
}