using System.Globalization;
using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Config;

/// <summary>
/// 사람이 적은 글자를 웨이퍼 입력값으로 바꾼다 — <b>명령행과 화면이 같은 규칙</b>을 쓰게.
///
/// <code>
///   점 하나        "-40,-40"                   → (-40, -40)
///   점 여럿        "-40,-40; 410,310"           → 두 점
///   수동 지정 하나  "2,3=off"                   → die(2,3) 제외
///   수동 지정 여럿  "2,3=off; 3,3=E0"
/// </code>
///
/// ★ 같은 글자를 명령행과 화면이 따로 해석하면, 한쪽만 고친 날 같은 입력이 다르게 읽힌다.
/// ★ 틀린 글자는 <see cref="FormatException"/> 으로 <b>무엇이 왜</b> 틀렸는지 말한다. 조용히 건너뛰지 않는다.
/// ★ 여럿은 <c>;</c> 나 줄바꿈으로 나눈다. 빈 글자는 빈 목록이다.
/// </summary>
public static class WaferInputText
{
    private static readonly char[] ListSeparators = [';', '\n', '\r'];

    /// <summary>"x,y" — 소수를 받는다. 소수점은 언제나 '.' 이다(컴퓨터 지역 설정과 무관).</summary>
    public static PointF Point(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] parts = text.Split(',');

        if (parts.Length != 2)
        {
            throw new FormatException($"'{text.Trim()}' — 점은 x,y 형식이어야 합니다 (예: -40,-40)");
        }

        return new PointF(Decimal(parts[0], text), Decimal(parts[1], text));
    }

    /// <summary>"x,y; x,y" — 점 여럿.</summary>
    public static IReadOnlyList<PointF> Points(string? text) => Items(text).Select(Point).ToList();

    /// <summary>"열,행=E1|E0|off" — off 는 검사에서 뺀다(원판 밖과 같은 취급).</summary>
    public static ZoneOverride Zone(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] sides = text.Split('=');
        string[] cell = sides[0].Split(',');

        if (sides.Length != 2 || cell.Length != 2)
        {
            throw new FormatException($"'{text.Trim()}' — 수동 지정은 열,행=E1|E0|off 형식이어야 합니다 (예: 2,3=off)");
        }

        WaferZone zone = sides[1].Trim().ToLowerInvariant() switch
        {
            "e1" => WaferZone.E1,
            "e0" => WaferZone.E0,
            "off" => WaferZone.Outside,
            _ => throw new FormatException($"'{text.Trim()}' — 존 '{sides[1].Trim()}' 를 모릅니다. E1 · E0 · off 중 하나"),
        };

        return new ZoneOverride(Whole(cell[0], text), Whole(cell[1], text), zone);
    }

    /// <summary>"열,행=존; 열,행=존" — 수동 지정 여럿.</summary>
    public static IReadOnlyList<ZoneOverride> Zones(string? text) => Items(text).Select(Zone).ToList();

    /// <summary>수동 지정을 다시 글자로 — 결과에 "무엇을 덮어썼나" 를 보일 때.</summary>
    public static string Describe(ZoneOverride item)
        => $"c{item.Col}r{item.Row}→{(item.Zone == WaferZone.Outside ? "off" : item.Zone.ToString())}";

    private static IEnumerable<string> Items(string? text)
        => (text ?? string.Empty)
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static double Decimal(string part, string whole)
        => double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{whole.Trim()}' — '{part.Trim()}' 는 숫자가 아닙니다");

    private static int Whole(string part, string whole)
        => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"'{whole.Trim()}' — '{part.Trim()}' 는 정수가 아닙니다");
}