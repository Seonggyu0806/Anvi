using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// die 안 좌표 → 웨이퍼 절대 좌표. <b>좌표 변환의 단 하나뿐인 자리</b>다.
///
/// <code>
///   abs = dieOrigin + s·R(θ)·centroid
/// </code>
///
/// <para>★ 왜 오프셋에도 회전·확대를 거는가 — <b>die 를 펴서 읽기 때문이다.</b>
/// 기가픽셀 원본은 통째로 못 돌린다. 그래서 die 마다 네모를 넉넉히 읽은 뒤
/// <b>warpAffine 으로 반듯하게 펴서</b> 검사한다. 그러면 결함 중심이
/// <b>die 자기 축(도면 축)</b> 값으로 나오므로, 웨이퍼 어디였는지 알려면 <b>원래 각도로 되돌려야</b> 한다.</para>
///
/// <para><b>비유</b> — 비뚤게 걸린 액자를 사진 찍었다. 사진을 <b>반듯하게 펴서</b> 보면
/// "그림 왼쪽 위에서 3cm" 는 알 수 있지만 <b>벽의 어디인지는 모른다.</b>
/// 원래 기울기만큼 되돌려야 벽 좌표가 나온다.</para>
///
/// <para>★ <see cref="DieGridBuilder"/> 가 die 원점을 만들 때 쓴 <see cref="SimilarityTransform"/> 을
/// <b>그대로 재사용</b>한다. 원점과 오프셋에 같은 변환이 걸려야 짝이 맞는다.
/// 유도: 전체가 <c>abs = s·R(θ)·(ideal + offset) + t</c> 이고
/// <c>dieOrigin = s·R(θ)·ideal + t</c> 이므로 <c>abs = dieOrigin + s·R(θ)·offset</c>.</para>
///
/// <para>★★ <b>지금은 warp read 가 아직 없다</b> — ROI 를 축정렬로 그대로 잘라 온다.
/// 그 상태에서 회전이 들어오면 <b>사진에 이미 찍힌 기울기에 한 번 더 돌리는</b> 꼴이 되어
/// 두 배로 돌아간다. 그래서 <see cref="MaxRotationDegWithoutWarp"/> 가드를 뒀다.
/// <b>6-2 에서 warp read 를 붙이는 날 이 가드를 지운다. 둘은 짝이다.</b></para>
///
/// <para>★ 두 번 걸어도 안전하다 — <see cref="WaferDefect.Defect"/> 원본에서 매번 새로 계산하므로
/// <c>AbsX</c> 가 이미 차 있어도 같은 답이 나온다. 원본을 보존한 덕이다.</para>
/// </summary>
public static class AbsoluteTransform
{
    /// <summary>
    /// warp read 없이 믿을 수 있는 최대 기울기(도).
    ///
    /// ★ 교수님 문서의 "각도가 임계(예 0.05°) 이하면 warp 스킵" 을 그대로 쓴다.
    ///   1024px die 기준 오차가 0.6px 안쪽이라 결함 위치로 쓰기에 무해하다.
    /// </summary>
    public const double MaxRotationDegWithoutWarp = 0.05;

    public static WaferDefectList Transform(WaferDefectList input, DieGrid grid, WaferAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(alignment);

        // ★ warp read 가 붙기 전까지의 임시 가드. 여기서 안 멈추면 좌표가 "그럴듯하게" 틀린다.
        //   웨이퍼 맵의 점이 살짝 밀릴 뿐이라 아무도 못 알아챈다 — 2주차 정합 사고와 같은 모양이다.
        if (Math.Abs(alignment.RotationDeg) > MaxRotationDegWithoutWarp)
        {
            throw new InvalidOperationException(
                $"정렬 기울기가 {alignment.RotationDeg:F3}° 입니다 — "
                + $"die 를 펴서 읽는 warp read 가 아직 없어 {MaxRotationDegWithoutWarp}° 까지만 믿을 수 있습니다. "
                + "warp read 를 붙이거나, 기울기를 보정한 원본을 쓰세요");
        }

        // (열,행) → 정렬이 적용된 die 원점
        Dictionary<(int Col, int Row), PointF> origins = new(grid.Count);

        foreach (DieOrigin die in grid.Dies)
        {
            origins[(die.Col, die.Row)] = die.Origin;
        }

        List<WaferDefect> items = new(input.Count);

        foreach (WaferDefect defect in input.Items)
        {
            if (!origins.TryGetValue((defect.DieCol, defect.DieRow), out PointF origin))
            {
                // ★ 결함은 있는데 격자에 그 die 가 없다 = 서로 다른 실행 결과를 섞었다는 뜻.
                //   조용히 넘어가면 "그럴듯하지만 틀린 좌표" 가 웨이퍼 맵에 찍힌다.
                throw new ArgumentException(
                    $"die(c{defect.DieCol},r{defect.DieRow})가 격자에 없습니다 — "
                    + "검사할 때와 다른 격자를 넘겼는지 확인하세요",
                    nameof(grid));
            }

            // 이동분은 origin 에 이미 들어 있으므로 Tx·Ty 자리에 origin 을 넣는다.
            SimilarityTransform transform =
                new(alignment.RotationDeg, alignment.Scale, origin.X, origin.Y);

            PointF abs = transform.Apply(defect.Defect.Centroid);

            items.Add(defect with { AbsX = abs.X, AbsY = abs.Y });
        }

        return items.Count == 0 ? WaferDefectList.Empty : new WaferDefectList { Items = items };
    }
}