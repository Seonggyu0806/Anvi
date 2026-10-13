using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiePipeline.Wpf.Converters;

/// <summary>
/// 작업공간 바의 단추 하나 ↔ 고른 작업공간. 단추의 <c>ConverterParameter</c> 가 그 단추의 칸이다.
/// ★ 눌리면 그 칸을 고른 값으로 돌려주고, 풀릴 때는 아무것도 안 한다 — 다른 단추가 눌려 풀린 것이라 그쪽이 값을 쓴다.
/// </summary>
public sealed class WorkspaceToCheckedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

/// <summary>
/// 작업공간 칸 하나 ↔ 고른 작업공간 — 고른 칸만 보인다.
/// ★ 칸은 전부 떠 있고 <b>보이기만</b> 바꾼다(교수님 앱과 같다) — 칸을 오갈 때 캔버스 확대 · 입력 값이 그대로 남는다.
/// </summary>
public sealed class WorkspaceToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}