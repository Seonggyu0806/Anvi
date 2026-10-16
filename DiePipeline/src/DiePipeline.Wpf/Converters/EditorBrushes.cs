using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Wpf.Converters;

/// <summary>
/// 블록 카테고리 → 머리 색. 팔레트 묶음 머리와 캔버스 블록 머리가 같은 색이라 한눈에 짝이 보인다.
/// ★ 색은 화면 일이라 디스크립터에 두지 않는다 — 카테고리 이름에서 여기서만 정한다(교수님과 같다).
/// </summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Map = new(StringComparer.Ordinal)
    {
        ["입력"] = Make(0x64, 0x74, 0x8B),      // 컨텍스트 입력 — 슬레이트
        ["골든 생성"] = Make(0xD9, 0x9A, 0x2B),  // 앰버
        ["전처리"] = Make(0x14, 0xA5, 0x9A),     // 틸
        ["정합"] = Make(0x8B, 0x5C, 0xF6),       // 퍼플
        ["비교"] = Make(0xF1, 0x7A, 0x2C),       // 오렌지
        ["영역"] = Make(0xDB, 0x27, 0x77),       // 마젠타
        ["판정"] = Make(0x3B, 0x82, 0xF6),       // 블루
        ["출력"] = Make(0x1F, 0xA0, 0x66),       // 그린
        ["정렬"] = Make(0x7C, 0x3A, 0xED),       // 바이올렛 — 웨이퍼 레시피의 표식 · 자세
        ["웨이퍼"] = Make(0x0E, 0x74, 0x90),     // 짙은 시안 — 웨이퍼 레시피의 die 검사 · 후처리
    };

    // 모르는 카테고리 — 회색. 새 카테고리를 만들면 위 표에 한 줄 넣는다.
    private static readonly SolidColorBrush Fallback = Make(0x6B, 0x72, 0x80);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && Map.TryGetValue(key, out SolidColorBrush? brush) ? brush : Fallback;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>검증 오류가 이 블록을 가리키면 빨간 테두리, 아니면 옅은 회색.</summary>
public sealed class ErrorToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Error = Make(0xD3, 0x2F, 0x2F);
    private static readonly SolidColorBrush Normal = Make(0xD9, 0xDF, 0xEA);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Error : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// 웨이퍼 맵 칸 상태 → 칸 색. E1 초록 · E0 청회 · 결함 빨강 · 터짐 주황 · 원판 밖 옅은 회색(교수님 맵과 같은 짝).
/// </summary>
public sealed class WaferMapStateToBrushConverter : IValueConverter
{
    private static readonly Dictionary<WaferMapState, SolidColorBrush> Map = new()
    {
        [WaferMapState.E1] = Make(0xC8, 0xE6, 0xC9),
        [WaferMapState.E0] = Make(0xCF, 0xD8, 0xDC),
        [WaferMapState.Defective] = Make(0xE5, 0x73, 0x73),
        [WaferMapState.Failed] = Make(0xFF, 0xB7, 0x4D),
        [WaferMapState.Outside] = Make(0xF3, 0xF4, 0xF6),

        // 실행 중 아직 안 본 칸 — 옅은 회색(원판 밖보다 조금 진하게, 검사할 칸이라는 표시)
        [WaferMapState.Pending] = Make(0xE5, 0xE7, 0xEB),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is WaferMapState state && Map.TryGetValue(state, out SolidColorBrush? brush) ? brush : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>true → 숨김, false → 보임. 컨텍스트 입력 블록에서 '끄지 않음' 체크박스를 숨긴다.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}