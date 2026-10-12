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

        if (spec.Circle is { } circle)
        {
            // ★ 0 이하면 모든 칸이 '원판 밖' 이 된다. 증상이 "검사할 die 가 없다" 뿐이라 원인을 못 찾는다.
            if (!(circle.Radius > 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spec), $"원판 반지름은 양수여야 합니다 (현재 {circle.Radius})");
            }

            if (spec.EdgeRings > 0)
            {
                throw new ArgumentException(
                    "원판(circle)과 edgeRings 는 같이 못 씁니다 — 가장자리를 정하는 방법이 둘이면 "
                    + "어느 쪽이 이겼는지 모릅니다. 원판이면 원만 주세요",
                    nameof(spec));
            }

            if (!(spec.EdgeExclusion >= 0) || spec.EdgeExclusion >= circle.Radius)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spec),
                    $"가장자리 여유는 0 이상, 반지름({circle.Radius})보다 작아야 합니다 (현재 {spec.EdgeExclusion})");
            }
        }
        else if (spec.EdgeExclusion != 0)
        {
            // ★ 조용히 무시하면 "여유를 줬는데 왜 그대로지" 가 된다.
            throw new ArgumentException(
                $"가장자리 여유({spec.EdgeExclusion})는 원판(circle)을 줬을 때만 씁니다 — 무엇의 가장자리인지가 없습니다",
                nameof(spec));
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

        Dictionary<(int Col, int Row), WaferZone> manual = Overrides(spec);

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

                // ★ 자동 판정이 먼저, 사람이 적은 die 만 덮어쓴다.
                WaferZone zone = manual.TryGetValue((col, row), out WaferZone chosen)
                    ? chosen
                    : ZoneOf(col, row, spec, transform, ideal, dieWidth, dieHeight);

                dies.Add(new DieOrigin(col, row, transform.Apply(ideal), zone));
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

    /// <summary>
    /// 수동 오버라이드 목록을 (열,행) → 존 으로 — 격자 밖 · 정하지 않은 존 · 같은 die 두 번은 거부한다.
    ///
    /// ★ 조용히 무시하면 "지정했는데 왜 그대로지" 가 되고, 같은 die 를 두 번 적으면 어느 쪽이 이겼는지 모른다.
    /// </summary>
    private static Dictionary<(int Col, int Row), WaferZone> Overrides(WaferSpec spec)
    {
        Dictionary<(int Col, int Row), WaferZone> manual = [];

        foreach (ZoneOverride item in spec.ZoneOverrides)
        {
            if (item.Col < 0 || item.Col >= spec.Cols || item.Row < 0 || item.Row >= spec.Rows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spec), $"수동 지정 die({item.Col},{item.Row})가 격자 {spec.Cols}×{spec.Rows} 밖입니다");
            }

            if (item.Zone is not (WaferZone.E1 or WaferZone.E0 or WaferZone.Outside))
            {
                throw new ArgumentException(
                    $"수동 지정 die({item.Col},{item.Row})의 존({item.Zone})이 정해지지 않았습니다 — E1 · E0 · Outside 중 하나",
                    nameof(spec));
            }

            if (!manual.TryAdd((item.Col, item.Row), item.Zone))
            {
                throw new ArgumentException(
                    $"die({item.Col},{item.Row})를 두 번 지정했습니다 — 하나만 남기세요", nameof(spec));
            }
        }

        return manual;
    }

    /// <summary>존을 정한다. 원판이 있으면 원 기준, 없으면 테두리에서 몇 칸 안쪽인가.</summary>
    private static WaferZone ZoneOf(
        int col, int row, WaferSpec spec, SimilarityTransform transform, PointF ideal, int dieWidth, int dieHeight)
    {
        // ★ 잰 원이 아니라 '검사에 쓰는 원'(가장자리 여유만큼 줄인 것)으로 판정한다 — 원판 마스크와 같은 원이다.
        if (spec.InspectedCircle is { } circle)
        {
            return CircleZone(circle, transform, ideal, dieWidth, dieHeight);
        }

        if (spec.EdgeRings == 0)
        {
            return WaferZone.E1;
        }

        int toBorder = Math.Min(
            Math.Min(col, spec.Cols - 1 - col),
            Math.Min(row, spec.Rows - 1 - row));

        return toBorder < spec.EdgeRings ? WaferZone.E0 : WaferZone.E1;
    }

    /// <summary>
    /// 원 기준 존 — die 네모가 원판 안에 <b>온전히</b> 들어가면 E1, <b>걸치면</b> E0, 안 닿으면 원판 밖.
    ///
    /// <para>★ 네 귀퉁이가 모두 원 안이면 네모 전체가 원 안이다 — 원은 볼록하니까.</para>
    ///
    /// <para>★ 귀퉁이가 하나도 안 들어가도 걸칠 수 있다 — 원의 끝이 die <b>변 가운데로</b> 파고드는 경우다.
    /// 귀퉁이만 보면 그 칸이 '원판 밖' 으로 버려진다. 그래서 원 중심에서 네모까지
    /// <b>가장 가까운 거리</b>로 한 번 더 본다.</para>
    ///
    /// <para>★ 귀퉁이는 정렬을 적용한 <b>실제 자리</b>다. 원이 사진 좌표라 비교도 사진 좌표로 한다.</para>
    /// </summary>
    private static WaferZone CircleZone(
        WaferCircle circle, SimilarityTransform transform, PointF ideal, int dieWidth, int dieHeight)
    {
        PointF[] corners =
        [
            ideal,
            new(ideal.X + dieWidth, ideal.Y),
            new(ideal.X, ideal.Y + dieHeight),
            new(ideal.X + dieWidth, ideal.Y + dieHeight),
        ];

        int inside = corners.Count(corner =>
        {
            PointF placed = transform.Apply(corner);
            return Distance(placed.X - circle.CenterX, placed.Y - circle.CenterY) <= circle.Radius;
        });

        if (inside == corners.Length)
        {
            return WaferZone.E1;
        }

        if (inside > 0)
        {
            return WaferZone.E0;
        }

        // 원 중심을 die 자기 좌표(정렬 전 설계 좌표)로 되돌린다 — p = R(-θ)·(p' - t) / s.
        // 거기서는 die 가 반듯한 네모라 '가장 가까운 점' 이 좌표를 자르기(clamp)만 하면 나온다.
        double radians = transform.RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double dx = circle.CenterX - transform.Tx;
        double dy = circle.CenterY - transform.Ty;
        double ux = ((cos * dx) + (sin * dy)) / transform.Scale;
        double uy = ((-sin * dx) + (cos * dy)) / transform.Scale;

        double nearX = Math.Clamp(ux, ideal.X, ideal.X + dieWidth);
        double nearY = Math.Clamp(uy, ideal.Y, ideal.Y + dieHeight);

        // 설계 좌표의 거리에 배율을 곱해야 사진 좌표의 거리가 된다.
        double gap = Distance(ux - nearX, uy - nearY) * Math.Abs(transform.Scale);

        return gap < circle.Radius ? WaferZone.E0 : WaferZone.Outside;
    }

    private static double Distance(double dx, double dy) => Math.Sqrt((dx * dx) + (dy * dy));
}