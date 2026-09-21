using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 정렬 풀이.
///
/// <para>★ 순수 계산이라 <b>사진 한 장 없이</b> 전부 시험된다 — 점 몇 개를 넣고 숫자를 본다.</para>
///
/// <para>★ 오늘 확인할 것은 둘이다 — <b>맞게 푸는가</b>, 그리고
/// <b>틀린 입력을 받았을 때 거부하는가.</b> 두 번째가 더 중요하다.
/// 정렬이 틀리면 웨이퍼 전체가 한 칸씩 밀려서 검사된다.</para>
/// </summary>
public sealed class AlignmentSolverTests
{
    private static PointF P(double x, double y) => new(x, y);

    /// <summary>설계 점들을 각도·이동으로 옮겨 "사진에서 찾은 자리" 를 만든다.</summary>
    private static MatchList Observe(
        IReadOnlyList<PointF> design, double degrees, double tx, double ty,
        double scale = 1.0, double score = 0.95)
    {
        SimilarityTransform transform = new(degrees, scale, tx, ty);

        return new MatchList
        {
            Items = [.. design.Select(p => new Match { Location = transform.Apply(p), Score = score })],
        };
    }

    private static MatchList Found(params (double X, double Y, double Score)[] spots)
        => new() { Items = [.. spots.Select(s => new Match { Location = P(s.X, s.Y), Score = s.Score })] };

    /// <summary>대각선으로 멀리 떨어진 표식 둘 — demo 명령이 그리는 배치와 같다.</summary>
    private static PointF[] TwoMarks => [P(10, 10), P(460, 360)];

    private static AlignmentOptions Options() => new();

    // ─────────────────────────────── 맞게 푼다

    [Fact]
    public void 안_돌고_안_밀렸으면_그대로_나온다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 0, 0, 0), TwoMarks, Options());

        Assert.True(result.Accepted);
        Assert.Equal(0.0, result.Alignment.RotationDeg, 6);
        Assert.Equal(0.0, result.Alignment.Origin.X, 6);
    }

    [Fact]
    public void 밀린_양을_되찾는다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 0, 8, 5), TwoMarks, Options());

        Assert.Equal(8.0, result.Alignment.Origin.X, 6);
        Assert.Equal(5.0, result.Alignment.Origin.Y, 6);
    }

    /// <summary>★ demo 명령이 그리는 그 값이다 — 0.7도 돌리고 (28,25) 밀기.</summary>
    [Fact]
    public void 각도와_이동을_같이_되찾는다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 0.7, 28, 25), TwoMarks, Options());

        Assert.True(result.Accepted);
        Assert.Equal(0.7, result.Alignment.RotationDeg, 6);
        Assert.Equal(28.0, result.Alignment.Origin.X, 6);
        Assert.Equal(25.0, result.Alignment.Origin.Y, 6);
    }

    [Fact]
    public void 반대로_돌아도_되찾는다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, -1.2, 0, 0), TwoMarks, Options());

        Assert.Equal(-1.2, result.Alignment.RotationDeg, 6);
    }

    /// <summary>★ 기본은 배율을 안 푼다 — 카메라 배율이 고정이면 웨이퍼가 커질 리 없다.</summary>
    [Fact]
    public void 기본은_배율을_1로_둔다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 0.5, 10, 10), TwoMarks, Options());

        Assert.Equal(1.0, result.Alignment.Scale);
    }

    [Fact]
    public void 켜면_배율도_푼다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 0, 0, 0, scale: 1.05), TwoMarks,
            new AlignmentOptions { SolveScale = true });

        Assert.True(result.Accepted);
        Assert.Equal(1.05, result.Alignment.Scale, 6);
    }

    [Fact]
    public void 표식_셋으로도_푼다()
    {
        PointF[] three = [P(10, 10), P(460, 10), P(235, 360)];

        AlignmentResult result = AlignmentSolver.Solve(
            Observe(three, 0.4, 12, 7), three, Options());

        Assert.True(result.Accepted);
        Assert.Equal(3, result.PairCount);
        Assert.Equal(0.4, result.Alignment.RotationDeg, 6);
    }

    /// <summary>찾은 목록은 점수 순이라 기대 순서와 다르다 — 가까운 것끼리 짝지어야 한다.</summary>
    [Fact]
    public void 순서가_뒤바뀌어_들어와도_짝을_맞춘다()
    {
        MatchList shuffled = Found((468.0, 365.0, 0.95), (18.0, 15.0, 0.99));

        AlignmentResult result = AlignmentSolver.Solve(shuffled, TwoMarks, Options());

        Assert.True(result.Accepted);
        Assert.Equal(8.0, result.Alignment.Origin.X, 3);
    }

    [Fact]
    public void 짝지은_점수의_평균을_남긴다()
    {
        MatchList found = Found((10.0, 10.0, 0.90), (460.0, 360.0, 1.00));

        AlignmentResult result = AlignmentSolver.Solve(found, TwoMarks, Options());

        Assert.Equal(0.95, result.Alignment.MeanScore, 6);
    }

    // ─────────────────────────────── 거부한다

    /// <summary>
    /// ★★ 이게 핵심 — 실데이터에서 본 실패다. 점수는 높고 각도도 멀쩡한데 <b>거리만</b> 틀렸다.
    ///
    /// <para>두 표식을 <b>잇는 선 방향으로</b> 80px 밀어 놓는다. 방향이 그대로라 각도는 0이고,
    /// 달라지는 건 <b>거리뿐</b>이다 — 그래서 거리 가드만 단독으로 걸린다.</para>
    /// </summary>
    [Fact]
    public void 거리가_달라지면_거부한다()
    {
        // (10,10)→(460,360) 방향 단위벡터 ≈ (0.7894, 0.6139). 그 방향으로 80px.
        MatchList wrong = Found((10.0, 10.0, 0.99), (523.148, 409.115, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(wrong, TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Contains("찌그러지지 않으므로", result.Rejection);
    }

    /// <summary>★ 짝을 잘못 지으면 대개 각도부터 말이 안 되게 나온다.</summary>
    [Fact]
    public void 짝을_잘못_지으면_각도가_말이_안_된다()
    {
        // 두 번째 표식을 가로로만 81px 옆에서 잡았다. 거리도 방향도 같이 틀어진다.
        MatchList wrong = Found((10.0, 10.0, 0.99), (541.0, 360.0, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(wrong, TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Contains("돌아갈 리 없", result.Rejection);
    }

    /// <summary>
    /// ★★ <b>배율까지 풀면 표식 2개로는 아무것도 못 걸러낸다.</b>
    ///
    /// <para>자유도 4개(이동2+회전1+배율1)를 식 4개로 푸니 <b>언제나 완벽히 맞는다</b> —
    /// 잔차가 0이라 "잘 맞았다" 로 보이지만 답은 틀렸다.
    /// 점 두 개를 지나는 직선이 반드시 있는 것과 같다.</para>
    ///
    /// <para>그래서 기본값은 배율을 <b>안 푼다.</b> 배율을 1로 묶어 두면 자유도가 3으로 줄어
    /// 식 하나가 남고, 그 남는 식이 <b>거리가 변했다</b>는 사실을 고발한다.</para>
    /// </summary>
    [Fact]
    public void 배율까지_풀면_표식_둘로는_못_걸러낸다()
    {
        MatchList wrong = Found((10.0, 10.0, 0.99), (523.148, 409.115, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(
            wrong, TwoMarks, new AlignmentOptions { SolveScale = true });

        Assert.Equal(0.0, result.ResidualPx, 6);   // 완벽히 맞는다 — 그런데 답은 틀렸다
        Assert.True(result.Accepted);              // 그래서 통과해 버린다
    }

    /// <summary>★ 배율을 1로 묶어 두면 같은 입력에서 어긋남이 남는다 — 그게 고발장이다.</summary>
    [Fact]
    public void 배율을_안_풀면_어긋남이_남는다()
    {
        MatchList wrong = Found((10.0, 10.0, 0.99), (523.148, 409.115, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(wrong, TwoMarks, Options());

        Assert.True(result.ResidualPx > 1.0);
    }

    [Fact]
    public void 거리가_변한_정도를_숫자로_남긴다()
    {
        MatchList wrong = Found((10.0, 10.0, 0.99), (523.148, 409.115, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(wrong, TwoMarks, Options());

        Assert.True(result.ImpliedScale > 1.05);
    }

    /// <summary>
    /// ★ 표식이 셋이면 되짚어 보기가 듣는다 — 하나만 틀려도 <b>나머지 둘이 고발한다.</b>
    ///
    /// <para>여기서는 거리 가드를 느슨하게 풀어 두고 <b>어긋남만</b> 걸리게 했다.
    /// 실제로는 둘 다 걸리는 경우가 많고, 그때는 먼저 보는 거리 쪽 까닭이 나온다.</para>
    /// </summary>
    [Fact]
    public void 표식_셋_중_하나가_틀리면_어긋남으로_잡는다()
    {
        PointF[] three = [P(10, 10), P(460, 10), P(235, 360)];

        MatchList wrong = Found((10.0, 10.0, 0.99), (460.0, 10.0, 0.99), (235.0, 390.0, 0.98));

        AlignmentResult result = AlignmentSolver.Solve(
            wrong, three, new AlignmentOptions { MaxScaleError = 0.10 });

        Assert.False(result.Accepted);
        Assert.Contains("어긋납니다", result.Rejection);
    }

    [Fact]
    public void 점수가_낮으면_거부한다()
    {
        MatchList weak = Found((10.0, 10.0, 0.5), (460.0, 360.0, 0.5));

        AlignmentResult result = AlignmentSolver.Solve(weak, TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Contains("점수", result.Rejection);
    }

    [Fact]
    public void 너무_많이_돌았다는_답은_거부한다()
    {
        AlignmentResult result = AlignmentSolver.Solve(
            Observe(TwoMarks, 30, 0, 0), TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Contains("돌아갈 리 없", result.Rejection);
    }

    [Fact]
    public void 표식을_하나밖에_못_찾으면_거부한다()
    {
        MatchList only = Found((10.0, 10.0, 0.99));

        AlignmentResult result = AlignmentSolver.Solve(only, TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Contains("2개 이상", result.Rejection);
    }

    [Fact]
    public void 하나도_못_찾으면_거부한다()
    {
        AlignmentResult result = AlignmentSolver.Solve(MatchList.Empty, TwoMarks, Options());

        Assert.False(result.Accepted);
        Assert.Equal(0, result.PairCount);
    }

    /// <summary>★ 거부됐으면 자세를 쓰면 안 된다 — 항등으로 돌려줘서 실수를 줄인다.</summary>
    [Fact]
    public void 못_찾으면_항등을_돌려준다()
    {
        AlignmentResult result = AlignmentSolver.Solve(MatchList.Empty, TwoMarks, Options());

        Assert.Equal(0.0, result.Alignment.RotationDeg);
        Assert.Equal(1.0, result.Alignment.Scale);
    }

    [Fact]
    public void 기대_위치가_겹쳐_있으면_거부한다()
    {
        PointF[] same = [P(100, 100), P(100, 100)];

        AlignmentResult result = AlignmentSolver.Solve(
            Found((100.0, 100.0, 0.99), (100.0, 100.0, 0.99)), same, Options());

        Assert.False(result.Accepted);
        Assert.Contains("같은 자리", result.Rejection);
    }

    // ─────────────────────────────── 입구에서 막는다

    [Fact]
    public void 기대_위치가_하나면_아예_못_푼다()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => AlignmentSolver.Solve(Found((10.0, 10.0, 0.99)), [P(10, 10)], Options()));

        Assert.Contains("얼마나 돌았는지", error.Message);
    }

    [Fact]
    public void null_은_거부한다()
    {
        Assert.Throws<ArgumentNullException>(
            () => AlignmentSolver.Solve(null!, TwoMarks, Options()));
        Assert.Throws<ArgumentNullException>(
            () => AlignmentSolver.Solve(MatchList.Empty, null!, Options()));
        Assert.Throws<ArgumentNullException>(
            () => AlignmentSolver.Solve(MatchList.Empty, TwoMarks, null!));
    }
}