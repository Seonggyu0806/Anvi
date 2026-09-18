using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// 웨이퍼 형상 + 정렬 → die 원점 격자.
///
/// <code>
///   설계 격자          정렬                실제 격자
///   (0,0) (100,0) …  ──s·R(θ)+t──▶   (50,20) (150,20) …
/// </code>
///
/// ★ 두 가지를 곱해서 하나를 만든다 —
///   <see cref="WaferSpec"/>   "die 를 이렇게 배치했다"   (제품마다 고정)
///   <see cref="WaferAlignment"/> "웨이퍼가 이렇게 놓였다" (한 장마다 다름)
///
/// ★ 이 클래스는 OpenCV 도 파일도 모른다 — 순수 계산이다.
///   그래서 이미지 한 장 없이 밀리초 단위로 테스트된다.
/// </summary>
public static class DieGridBuilder
{
    /// <param name="alignment"><c>null</c> 이면 <see cref="WaferAlignment.Identity"/> — 설계 격자 그대로.</param>
    public static DieGrid Build(WaferSpec spec, WaferAlignment? alignment = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // 입구에서 다 막는다. 여기서 안 막으면 증상이 "결함이 왜 이렇게 많지?"로만 나타난다.
        if (spec.Cols < 1 || spec.Rows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec), $"격자는 1×1 이상이어야 합니다 (현재 {spec.Cols}×{spec.Rows})");
        }

        if (spec.PitchX < 1 || spec.PitchY < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec), $"피치는 양수여야 합니다 (현재 {spec.PitchX},{spec.PitchY})");
        }

        if (spec.EdgeRings < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "edgeRings 는 0 이상이어야 합니다");
        }

        int dieWidth = spec.DieWidth > 0 ? spec.DieWidth : spec.PitchX;
        int dieHeight = spec.DieHeight > 0 ? spec.DieHeight : spec.PitchY;

        // ★ die 가 피치보다 크면 이웃 die 의 ROI 와 겹친다 →
        //   같은 결함이 두 die 에서 각각 잡혀 개수가 부풀고, 절대좌표가 어긋난다.
        if (dieWidth > spec.PitchX || dieHeight > spec.PitchY)
        {
            throw new ArgumentException(
                $"die({dieWidth}×{dieHeight})가 피치({spec.PitchX}×{spec.PitchY})보다 큽니다 — ROI 가 서로 겹칩니다",
                nameof(spec));
        }

        WaferAlignment placed = alignment ?? WaferAlignment.Identity;

        // ★ Scale 0 은 모든 좌표를 원점으로 무너뜨린다. 증상이 "전부 (0,0)"이라 원인을 못 찾는다.
        if (placed.Scale == 0)
        {
            throw new ArgumentException("정렬의 Scale 이 0입니다 — 모든 좌표가 원점으로 무너집니다", nameof(alignment));
        }

        SimilarityTransform transform =
            new(placed.RotationDeg, placed.Scale, placed.Origin.X, placed.Origin.Y);

        List<DieOrigin> dies = new(spec.Cols * spec.Rows);

        // row-major — 바깥이 행, 안쪽이 열. 이 순서가 결과의 순서가 된다.
        for (int row = 0; row < spec.Rows; row++)
        {
            for (int col = 0; col < spec.Cols; col++)
            {
                // ★ (double) 로 올려서 곱한다. die 수가 많고 피치가 크면 int 로는 넘칠 수 있다.
                PointF ideal = new(
                    spec.OriginX + ((double)col * spec.PitchX),
                    spec.OriginY + ((double)row * spec.PitchY));

                dies.Add(new DieOrigin(col, row, transform.Apply(ideal), ZoneOf(col, row, spec)));
            }
        }

        return new DieGrid
        {
            Dies = dies,
            Pitch = new PointF(spec.PitchX, spec.PitchY),
            Cols = spec.Cols,
            Rows = spec.Rows,
            DieWidth = dieWidth,
            DieHeight = dieHeight,
        };
    }

    /// <summary>테두리에서 몇 칸 안쪽인가로 존을 정한다.</summary>
    private static WaferZone ZoneOf(int col, int row, WaferSpec spec)
    {
        if (spec.EdgeRings == 0)
        {
            return WaferZone.E1;
        }

        int toBorder = Math.Min(
            Math.Min(col, spec.Cols - 1 - col),
            Math.Min(row, spec.Rows - 1 - row));

        return toBorder < spec.EdgeRings ? WaferZone.E0 : WaferZone.E1;
    }
}