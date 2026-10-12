using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>맵 칸의 상태 — 화면이 색을 고르는 기준.</summary>
public enum WaferMapState
{
    /// <summary>원판 밖 — die 가 아니다. 검사하지 않았다.</summary>
    Outside,

    /// <summary>변두리 die, 결함 없음.</summary>
    E0,

    /// <summary>유효 die, 결함 없음.</summary>
    E1,

    /// <summary>결함이 있는 die(E1 · E0 모두).</summary>
    Defective,

    /// <summary>검사가 터진 die — 결함 0개로 보이지만 <b>안 본 것</b>이다.</summary>
    Failed,
}

/// <summary>
/// 웨이퍼 맵의 칸 하나 = die 하나. 결함 수는 <b>샘플링 전</b>(<see cref="WaferInspectionResult.Found"/>) — 판정과 같은 것을 센다.
/// </summary>
public sealed record WaferMapCell(int Col, int Row, WaferZone Zone, int Defects, bool Failed)
{
    public WaferMapState State
        => Failed ? WaferMapState.Failed
            : Zone == WaferZone.Outside ? WaferMapState.Outside
            : Defects > 0 ? WaferMapState.Defective
            : Zone == WaferZone.E0 ? WaferMapState.E0
            : WaferMapState.E1;

    /// <summary>칸에 적을 글자 — 결함 수. 없으면 비운다(0 을 적으면 칸이 숫자로 가득해 결함 칸이 안 보인다).</summary>
    public string Label => Failed ? "!" : Defects > 0 ? Defects.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
}

/// <summary>
/// 검사 결과 → 웨이퍼 맵 칸들. 교수님 웨이퍼 맵(die 격자, 셀에 결함 수)과 같은 모양이다.
/// ★ 화면과 상관없는 계산이라 여기(웨이퍼 층)에 둔다 — 시험이 칸 색과 수를 지킨다.
/// </summary>
public static class WaferMap
{
    /// <summary>
    /// 행 우선(위 → 아래, 왼 → 오른)으로 열 × 행 칸 전부. 화면은 열 수만큼 끊어 놓으면 격자가 된다.
    /// ★ 멈췄으면(정렬을 못 믿음 등) <b>빈 목록</b> — 빈 맵을 그리면 "깨끗한 웨이퍼" 로 읽힌다.
    /// </summary>
    public static IReadOnlyList<WaferMapCell> Of(WaferInspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Stopped || result.Grid is not { } grid)
        {
            return [];
        }

        Dictionary<(int Col, int Row), int> counts = (result.Found?.Items ?? [])
            .GroupBy(d => (d.DieCol, d.DieRow))
            .ToDictionary(g => g.Key, g => g.Count());

        HashSet<(int Col, int Row)> failed =
            [.. (result.Run?.Dies ?? []).Where(d => d.Failed).Select(d => (d.Die.Col, d.Die.Row))];

        return
        [
            .. grid.InRowMajorOrder().Select(die => new WaferMapCell(
                die.Col,
                die.Row,
                die.Zone,
                counts.GetValueOrDefault((die.Col, die.Row)),
                failed.Contains((die.Col, die.Row)))),
        ];
    }
}