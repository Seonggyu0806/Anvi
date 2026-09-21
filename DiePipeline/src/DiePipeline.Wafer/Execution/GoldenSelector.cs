using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>골든으로 쓸 이웃 die 를 고르는 방법.</summary>
/// <remarks>
/// 고르는 법은 <b>합치는 법과 짝</b>이다. 따로 정하면 안 된다.
/// <list type="bullet">
/// <item><see cref="SameRow1"/> 은 <c>Mean</c> + <c>DarkOnly</c> 와 짝이다.
///       이웃이 1~2장뿐이라 <c>Mean</c> 과 <c>Median</c> 이 사실상 같은 계산이 된다</item>
/// <item><see cref="Nearest"/> 는 <c>Median</c> 과 짝이다 — <c>Mean</c> 과 쓰면 오히려 나빠진다.
///       먼 이웃일수록 덜 닮았는데 <c>Mean</c> 은 그것까지 섞기 때문이다</item>
/// </list>
/// </remarks>
public enum NeighborStrategy
{
    /// <summary>안 정했다. 기본값이 조용히 한쪽으로 쏠리는 것을 막는다.</summary>
    Unknown = 0,

    /// <summary>같은 행 좌우 1칸. 최대 2장, 끝 열은 1장. (교수님 방식)</summary>
    SameRow1 = 1,

    /// <summary>가까운 순서로 좌·우·상·하. 모자라면 거리를 늘려 want 장을 채운다. (Lesson16 방식)</summary>
    Nearest = 2,
}

/// <summary>
/// die-to-die 검사에서 <b>골든으로 쓸 이웃 die</b> 를 고른다.
///
/// <para>왜 이웃이 골든이 되나 — 같은 마스크로 찍었으니 회로 무늬가 똑같다.
/// 다른 것은 결함뿐이므로, 이웃과 비교하면 무늬는 지워지고 결함만 남는다.</para>
///
/// <para>OpenCV 를 모르는 순수 계산이라 <b>이미지 없이 테스트된다.</b></para>
/// </summary>
public static class GoldenSelector
{
    /// <summary>이 die 의 골든을 만들 이웃을 고른다.</summary>
    /// <param name="want"><see cref="NeighborStrategy.Nearest"/> 에서만 쓴다. 몇 장을 채울까.</param>
    public static IReadOnlyList<DieOrigin> Pick(
        DieGrid grid, DieOrigin die, NeighborStrategy strategy, int want = 3)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (!grid.Contains(die.Col, die.Row))
        {
            throw new ArgumentOutOfRangeException(
                nameof(die), $"die(c{die.Col},r{die.Row})는 격자 {grid.Cols}×{grid.Rows} 밖입니다");
        }

        if (die.Zone != WaferZone.E1)
        {
            throw new ArgumentException(
                $"die(c{die.Col},r{die.Row})는 {die.Zone} 입니다 — 골든은 E1 die 에만 만듭니다. "
                + "변두리(E0)는 골든 없는 레시피로 따로 돌립니다.",
                nameof(die));
        }

        return strategy switch
        {
            NeighborStrategy.SameRow1 => SameRow1(grid, die),
            NeighborStrategy.Nearest => Nearest(grid, die, want),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy), $"이웃 고르기 방법 '{strategy}' 를 모릅니다"),
        };
    }

    /// <summary>
    /// 고른 이웃을 <b>잘라 읽을 ROI</b> 로 바꾼다.
    ///
    /// <para>⚠ 골든 ROI 는 <b>검사 ROI 와 같은 크기</b>여야 한다.
    /// 가장자리에서 이미지 밖으로 나가면 <b>잘라내지 말고 안으로 밀어 넣는다.</b>
    /// 크기가 한 픽셀이라도 다르면 겹칠 수가 없어서 <c>Combine</c> 이 거부하고,
    /// 그 die 가 통째로 건너뛰어진다.</para>
    /// </summary>
    public static IReadOnlyList<Roi> RoisOf(
        IReadOnlyList<DieOrigin> neighbours, Roi testRoi, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(neighbours);

        if (imageWidth < testRoi.Width || imageHeight < testRoi.Height)
        {
            throw new ArgumentException(
                $"이미지({imageWidth}×{imageHeight})가 검사 ROI({testRoi.Width}×{testRoi.Height})보다 작습니다",
                nameof(testRoi));
        }

        List<Roi> rois = new(neighbours.Count);

        foreach (DieOrigin neighbour in neighbours)
        {
            int x = (int)Math.Round(neighbour.Origin.X);
            int y = (int)Math.Round(neighbour.Origin.Y);

            rois.Add(new Roi(
                Math.Clamp(x, 0, imageWidth - testRoi.Width),
                Math.Clamp(y, 0, imageHeight - testRoi.Height),
                testRoi.Width,
                testRoi.Height));
        }

        return rois;
    }

    /// <summary>같은 행 좌 → 우. 순서를 고정해야 같은 웨이퍼가 늘 같은 답을 낸다.</summary>
    private static List<DieOrigin> SameRow1(DieGrid grid, DieOrigin die)
    {
        List<DieOrigin> picked = new(2);

        foreach (int col in new[] { die.Col - 1, die.Col + 1 })
        {
            if (grid.Contains(col, die.Row) && grid.At(col, die.Row).Zone == WaferZone.E1)
            {
                picked.Add(grid.At(col, die.Row));
            }
        }

        return picked;
    }

    /// <summary>거리 1의 좌·우·상·하부터, 모자라면 거리를 늘려 want 장을 채운다.</summary>
    private static List<DieOrigin> Nearest(DieGrid grid, DieOrigin die, int want)
    {
        if (want < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(want), "이웃은 1장 이상이어야 합니다");
        }

        List<DieOrigin> picked = new(want);
        int reach = Math.Max(grid.Cols, grid.Rows);

        for (int distance = 1; distance <= reach && picked.Count < want; distance++)
        {
            foreach ((int col, int row) in new[]
                     {
                         (die.Col - distance, die.Row),
                         (die.Col + distance, die.Row),
                         (die.Col, die.Row - distance),
                         (die.Col, die.Row + distance),
                     })
            {
                if (picked.Count >= want)
                {
                    break;
                }

                if (grid.Contains(col, row) && grid.At(col, row).Zone == WaferZone.E1)
                {
                    picked.Add(grid.At(col, row));
                }
            }
        }

        return picked;
    }
}