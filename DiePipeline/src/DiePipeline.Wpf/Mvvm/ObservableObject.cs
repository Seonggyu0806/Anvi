using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiePipeline.Wpf.Mvvm;

/// <summary>
/// "값이 바뀌었다"를 화면에 알리는 최소 구현.
///
/// ★ 외부 MVVM 패키지를 안 쓴다. 25줄이면 되는 걸 패키지로 끌어오면
///   <b>왜 화면이 안 갱신되는지</b>를 남의 코드 속에서 찾게 된다.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ★ [CallerMemberName] — 호출한 속성 이름을 컴파일러가 채워 준다.
    //   손으로 "Threshold"라고 적으면 나중에 속성 이름을 바꿔도 문자열은 안 바뀐다.
    //   그러면 화면이 조용히 갱신을 멈춘다 — 오류도 안 난다.
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        // ★ 같은 값이면 알리지 않는다. 슬라이더를 끌면 초당 수십 번 들어오는데,
        //   매번 알리면 그때마다 화면이 다시 그려진다.
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
