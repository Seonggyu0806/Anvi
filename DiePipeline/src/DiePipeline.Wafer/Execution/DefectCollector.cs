using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// die 별 검사 결과를 <b>웨이퍼 한 목록</b>으로 모은다.
///
/// <para>★ 여기서 하는 일은 <b>꼬리표 달기뿐</b>이다 — "이 결함은 die(c1,r0) 의 것" 이라고 적는다.
/// 좌표는 손대지 않는다. 그건 <see cref="AbsoluteTransform"/> 한 곳에서만 한다.</para>
///
/// <para>★ 왜 둘로 나눴나: 좌표를 더하는 코드가 러너·뷰어·리포트에 흩어지면
/// <b>반드시 한 군데가 어긋난다.</b> 그때 증상은 "웨이퍼 맵의 점이 살짝 밀려 있다" 뿐이라
/// 원인을 못 찾는다. <see cref="DieGrid.RoiOf"/> 를 한 곳에 둔 것과 같은 이유다.</para>
///
/// <para>★ 검사가 <b>터진 die 는 결함 0개</b>로 들어온다 — 여기서는 "깨끗한 die" 와 구분이 안 된다.
/// 구분하려면 <see cref="WaferRunResult.Failures"/> 를 따로 봐야 한다.
/// "검사했는데 깨끗했다" 와 "검사를 못 했다" 는 다르다.</para>
/// </summary>
public static class DefectCollector
{
    /// <param name="perDie">러너가 낸 die 별 결과. <c>run.Dies</c> 를 그대로 넘기면 된다.</param>
    public static WaferDefectList Collect(IReadOnlyList<DieInspection> perDie)
    {
        ArgumentNullException.ThrowIfNull(perDie);

        List<WaferDefect> items = [];

        // 러너가 준 순서(row-major) 그대로 쌓는다.
        // ★ 다시 정렬하지 않는 이유: 같은 웨이퍼를 두 번 돌렸을 때 목록이 줄 단위로 똑같아야
        //   "달라진 게 있나" 를 눈으로 비교할 수 있다.
        foreach (DieInspection inspection in perDie)
        {
            foreach (Defect defect in inspection.Defects.Items)
            {
                items.Add(new WaferDefect
                {
                    DieCol = inspection.Die.Col,
                    DieRow = inspection.Die.Row,
                    Zone = inspection.Die.Zone,

                    // 원본을 그대로 보존한다. ROI 상대 좌표 그대로다.
                    // ★ 여기서 절대좌표로 덮어쓰면 "원래 무엇이었나" 를 영영 잃는다.
                    //   die 레시피의 테스트가 전부 이 값 기준으로 굳어 있기도 하다.
                    Defect = defect,
                });
            }
        }

        return items.Count == 0 ? WaferDefectList.Empty : new WaferDefectList { Items = items };
    }
}