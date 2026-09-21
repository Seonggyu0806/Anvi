using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>정렬을 풀 때의 설정과 가드.</summary>
public sealed record AlignmentOptions
{
    /// <summary>
    /// 배율까지 풀까. <b>기본은 안 푼다</b>(1로 고정).
    ///
    /// ★ 카메라 배율이 고정이면 웨이퍼가 커지거나 작아질 리가 없다.
    ///   표현할 수 없는 것을 표현하지 않으면 <b>측정 노이즈에 그만큼 덜 흔들린다.</b>
    /// </summary>
    public bool SolveScale { get; init; }

    /// <summary>짝지은 표식들의 평균 점수가 이보다 낮으면 거부.</summary>
    public double MinScore { get; init; } = 0.8;

    /// <summary>이보다 많이 돌았다는 답이 나오면 거부. 웨이퍼는 살짝 삐뚤 뿐이다.</summary>
    public double MaxAngleDeg { get; init; } = 2.0;

    /// <summary>
    /// 배율이 1에서 이만큼 넘게 벗어나면 거부. <b>2%.</b>
    ///
    /// ★★ <b>이게 표식 2개일 때 유일하게 듣는 가드다.</b>
    ///   웨이퍼는 돌아가고 밀릴 뿐 <b>찌그러지지 않는다</b> — 두 표식 사이 거리는 그대로여야 한다.
    ///   거리가 변했다는 건 짝을 잘못 지었거나 <b>표식을 엉뚱한 자리에서 찾았다</b>는 뜻이다.
    ///   실측: 조각이 작을 때 81px 옆을 1등으로 집었고, 그때 배율이 1.14 로 나왔다.
    ///
    /// ★ <see cref="SolveScale"/> 을 켜면 이 검사는 안 한다 — 배율을 푸는 게 목적이니까.
    /// </summary>
    public double MaxScaleError { get; init; } = 0.02;

    /// <summary>
    /// 되짚어 본 어긋남이 이보다 크면 거부(px).
    ///
    /// ★ <b>표식이 2개면 이 값은 언제나 0 이다</b> — 자유도 4개를 식 4개로 푸니 완벽히 맞는다.
    ///   점 두 개를 지나는 직선이 반드시 있는 것과 같다. <b>3개부터 의미가 생긴다.</b>
    /// </summary>
    public double MaxResidualPx { get; init; } = 3.0;
}

/// <summary>정렬을 풀어 본 결과. <b>받아들였는지와 그 까닭까지</b> 같이 들고 온다.</summary>
public sealed record AlignmentResult
{
    /// <summary>풀어낸 자세. <b>거부됐으면 쓰면 안 된다</b> — 까닭을 보여주려고 들고 있을 뿐이다.</summary>
    public required WaferAlignment Alignment { get; init; }

    /// <summary>몇 쌍으로 풀었나.</summary>
    public required int PairCount { get; init; }

    /// <summary>되짚어 봤을 때 제일 많이 어긋난 양(px).</summary>
    public required double ResidualPx { get; init; }

    /// <summary>배율을 풀었다면 얼마였을까. 1에서 멀면 짝짓기가 틀린 것이다.</summary>
    public required double ImpliedScale { get; init; }

    /// <summary>거부됐으면 그 까닭. <c>null</c> 이면 받아들인 것이다.</summary>
    public string? Rejection { get; init; }

    public bool Accepted => Rejection is null;

    public override string ToString()
        => $"{Alignment}  짝 {PairCount}  어긋남 {ResidualPx:F2}px  배율추정 {ImpliedScale:F4}"
           + (Accepted ? string.Empty : $"  ✗ {Rejection}");
}

/// <summary>
/// 찾아낸 표식 자리들로 <b>웨이퍼가 어떻게 놓였는지</b>를 역산한다.
///
/// <para>★ 푸는 것 — 설계 도면의 점들을 사진 속 점들로 옮기는 <b>회전 + 이동 (+ 배율)</b>.
/// 닫힌 식이 있어서 반복 계산 없이 한 번에 나온다(Umeyama).
/// 각도를 조금씩 바꿔 가며 다시 맞춰 보는 방법보다 정확하고 싸다.</para>
///
/// <para>★ 이 층은 <b>사진을 안 만진다.</b> 점 몇 개를 받아 숫자를 내는 순수 계산이라
/// 이미지 한 장 없이 전부 시험된다.</para>
///
/// <para>★★ <b>거부됐는데 그냥 쓰면 안 된다.</b> 2주차에 "정합이 조용히 틀린" 사고를 겪었고,
/// 여기는 그보다 더 위험하다 — 정렬이 틀리면 <b>웨이퍼 전체가 한 칸씩 밀려서</b> 검사된다.</para>
/// </summary>
public static class AlignmentSolver
{
    /// <param name="matches">표식을 찾아낸 자리들(사진 좌표).</param>
    /// <param name="expected">그 표식들이 <b>설계 도면상</b> 어디에 있어야 하는가. 순서가 기준이다.</param>
    public static AlignmentResult Solve(
        MatchList matches, IReadOnlyList<PointF> expected, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(options);

        if (expected.Count < 2)
        {
            throw new ArgumentException(
                $"기대 위치가 {expected.Count}개입니다 — 표식은 2개 이상이어야 각도를 풀 수 있습니다 "
                + "(하나로는 얼마나 밀렸는지만 알고 얼마나 돌았는지를 모릅니다)",
                nameof(expected));
        }

        (List<PointF> observed, List<PointF> paired, double meanScore) = Pair(matches, expected);

        if (paired.Count < 2)
        {
            return Rejected(
                $"표식을 {paired.Count}개밖에 못 찾았습니다 — 2개 이상이어야 각도를 풀 수 있습니다",
                paired.Count);
        }

        // ── 무게중심을 빼고(중심화) 회전·배율을 먼저, 이동은 마지막에.
        (double ex, double ey) = Centroid(paired);
        (double ox, double oy) = Centroid(observed);

        double dot = 0;      // Σ 기대·관측     — 같은 방향일수록 크다
        double cross = 0;    // Σ 기대×관측     — 돌아간 만큼 커진다
        double spread = 0;   // Σ |기대|²       — 기대점들이 얼마나 퍼져 있나

        for (int i = 0; i < paired.Count; i++)
        {
            double dex = paired[i].X - ex;
            double dey = paired[i].Y - ey;
            double dox = observed[i].X - ox;
            double doy = observed[i].Y - oy;

            dot += (dex * dox) + (dey * doy);
            cross += (dex * doy) - (dey * dox);
            spread += (dex * dex) + (dey * dey);
        }

        if (spread <= double.Epsilon)
        {
            return Rejected("기대 위치가 모두 같은 자리입니다 — 각도를 풀 수 없습니다", paired.Count);
        }

        // ★ 각도는 "얼마나 돌았나(cross) : 얼마나 그대로인가(dot)" 의 비다.
        double radians = Math.Atan2(cross, dot);
        double degrees = radians * 180.0 / Math.PI;

        // ★ 배율을 안 풀기로 했어도 "풀었으면 얼마였을까" 는 계산해 둔다 — 가드로 쓴다.
        double impliedScale = Math.Sqrt((dot * dot) + (cross * cross)) / spread;
        double scale = options.SolveScale ? impliedScale : 1.0;

        // t = 관측 무게중심 − s·R(θ)·기대 무게중심
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double tx = ox - (scale * ((cos * ex) - (sin * ey)));
        double ty = oy - (scale * ((sin * ex) + (cos * ey)));

        SimilarityTransform transform = new(degrees, scale, tx, ty);

        double residual = WorstResidual(transform, paired, observed);

        WaferAlignment alignment = new()
        {
            Origin = new PointF(tx, ty),
            RotationDeg = degrees,
            Scale = scale,
            MeanScore = meanScore,
        };

        return new AlignmentResult
        {
            Alignment = alignment,
            PairCount = paired.Count,
            ResidualPx = residual,
            ImpliedScale = impliedScale,
            Rejection = Judge(options, meanScore, degrees, impliedScale, residual),
        };
    }

    /// <summary>
    /// 기대점마다 <b>제일 가까운</b> 관측을 하나씩 짝지운다.
    ///
    /// <para>★ 왜 순서대로 안 짝짓나 — 찾은 목록은 <b>점수 높은 순</b>이라
    /// 기대 순서와 아무 상관이 없다. 표식이 더 나오거나 덜 나오는 것도 정상이다.</para>
    ///
    /// <para>★ <b>한계</b> — 웨이퍼가 표식 간격의 절반보다 많이 밀려 있으면
    /// 옆 표식에 잘못 달라붙는다. 그러면 격자는 맞아 보이는데 <b>die 번호가 통째로 밀린다.</b>
    /// 그 경우는 배율 가드가 잡아낸다.</para>
    /// </summary>
    private static (List<PointF> Observed, List<PointF> Expected, double MeanScore) Pair(
        MatchList matches, IReadOnlyList<PointF> expected)
    {
        bool[] taken = new bool[matches.Count];
        List<PointF> observed = new(expected.Count);
        List<PointF> paired = new(expected.Count);
        double scoreSum = 0;

        foreach (PointF want in expected)
        {
            int best = -1;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < matches.Count; i++)
            {
                if (taken[i])
                {
                    continue;
                }

                PointF at = matches.Items[i].Location;
                double distance = Square(at.X - want.X) + Square(at.Y - want.Y);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            if (best < 0)
            {
                break;   // 찾은 것이 다 떨어졌다 — 남은 기대점은 짝이 없다
            }

            taken[best] = true;
            observed.Add(matches.Items[best].Location);
            paired.Add(want);
            scoreSum += matches.Items[best].Score;
        }

        return (observed, paired, paired.Count > 0 ? scoreSum / paired.Count : 0);
    }

    /// <summary>풀어낸 변환으로 기대점을 옮겨 보고, 관측과 제일 많이 어긋난 양.</summary>
    private static double WorstResidual(
        SimilarityTransform transform, List<PointF> expected, List<PointF> observed)
    {
        double worst = 0;

        for (int i = 0; i < expected.Count; i++)
        {
            PointF predicted = transform.Apply(expected[i]);

            double gap = Math.Sqrt(
                Square(predicted.X - observed[i].X) + Square(predicted.Y - observed[i].Y));

            worst = Math.Max(worst, gap);
        }

        return worst;
    }

    /// <summary>믿을 만한가. 믿을 수 없으면 <b>왜</b> 인지를 돌려준다.</summary>
    private static string? Judge(
        AlignmentOptions options, double meanScore, double degrees, double impliedScale, double residual)
    {
        if (meanScore < options.MinScore)
        {
            return $"표식 평균 점수 {meanScore:F3} 가 문턱 {options.MinScore:F3} 보다 낮습니다 "
                   + "— 표식을 제대로 못 찾았을 수 있습니다";
        }

        if (Math.Abs(degrees) > options.MaxAngleDeg)
        {
            return $"각도 {degrees:F3}° 가 한계 {options.MaxAngleDeg:F3}° 를 넘습니다 "
                   + "— 웨이퍼가 이만큼 돌아갈 리 없으니 짝짓기를 의심하세요";
        }

        // ★ 표식 2개일 때 유일하게 듣는 가드다. 웨이퍼는 찌그러지지 않는다.
        if (!options.SolveScale && Math.Abs(impliedScale - 1.0) > options.MaxScaleError)
        {
            return $"두 표식 사이 거리가 {(impliedScale - 1.0) * 100:F1}% 달라졌습니다 "
                   + "— 웨이퍼는 찌그러지지 않으므로, 표식을 엉뚱한 자리에서 찾았거나 짝을 잘못 지었습니다";
        }

        if (residual > options.MaxResidualPx)
        {
            return $"되짚어 보니 {residual:F2}px 어긋납니다 (한계 {options.MaxResidualPx:F2}px) "
                   + "— 표식 중 하나가 틀린 자리입니다";
        }

        return null;
    }

    private static AlignmentResult Rejected(string reason, int pairCount)
        => new()
        {
            Alignment = WaferAlignment.Identity,
            PairCount = pairCount,
            ResidualPx = 0,
            ImpliedScale = 1,
            Rejection = reason,
        };

    private static (double X, double Y) Centroid(List<PointF> points)
    {
        double x = 0;
        double y = 0;

        foreach (PointF point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return (x / points.Count, y / points.Count);
    }

    private static double Square(double value) => value * value;
}