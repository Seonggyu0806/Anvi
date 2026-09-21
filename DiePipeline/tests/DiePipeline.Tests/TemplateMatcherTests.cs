using DiePipeline.Core.Domain;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 표식 찾기.
///
/// <para>★ 오늘 확인할 것 — <b>찾을 것이 없을 때 "없다" 고 말하는가.</b>
/// "제일 닮은 자리" 는 아무 사진에나 반드시 하나 있다. 2주차에 그 함정으로 오검 11개를 겪었다.</para>
/// </summary>
public sealed class TemplateMatcherTests
{
    private const double Background = 0.5;

    /// <summary>비대칭 표식 — demo 명령이 그리는 것과 같은 모양이다.</summary>
    private static Mat MarkTemplate(int size = 20)
    {
        Mat mark = new(size, size, MatType.CV_32FC1, Scalar.All(Background));

        Cv2.Rectangle(mark, new Rect(0, 0, size, size), Scalar.All(1.0), -1);
        Cv2.Rectangle(mark, new Rect(size / 6, size / 6, size / 3, size / 3), Scalar.All(0.0), -1);

        return mark;
    }

    /// <summary>빈 사진에 표식을 지정한 자리들에 찍어 넣는다.</summary>
    private static Mat Scene(int width, int height, params (int X, int Y)[] spots)
    {
        Mat scene = new(height, width, MatType.CV_32FC1, Scalar.All(Background));

        using Mat mark = MarkTemplate();

        foreach ((int x, int y) in spots)
        {
            using Mat target = new(scene, new Rect(x, y, mark.Cols, mark.Rows));
            mark.CopyTo(target);
        }

        return scene;
    }

    private static MatchOptions Options(
        MatchMethod method = MatchMethod.CCoeffNormed, double minScore = 0.6, int max = 8)
        => new() { Method = method, MinScore = minScore, MaxMatches = max };

    private static (int X, int Y) At(Match match) => ((int)match.Location.X, (int)match.Location.Y);

    // ─────────────────────────────── 찾는다

    [Fact]
    public void 표식_하나를_찾는다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        MatchList found = TemplateMatchers.FindAll(scene, mark, Options());

        Assert.Equal((80, 40), At(Assert.Single(found.Items)));
    }

    /// <summary>★ 자리는 <b>왼쪽 위</b>다. 가운데가 아니다 — matchTemplate 의 규약이다.</summary>
    [Fact]
    public void 자리는_왼쪽_위다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Match match = TemplateMatchers.FindAll(scene, mark, Options()).Items[0];

        Assert.Equal(80.0, match.Location.X);   // 가운데면 90 이 나왔을 것이다
    }

    [Fact]
    public void 표식_둘을_둘_다_찾는다()
    {
        using Mat scene = Scene(300, 220, (20, 20), (240, 170));
        using Mat mark = MarkTemplate();

        MatchList found = TemplateMatchers.FindAll(scene, mark, Options());

        Assert.Equal(2, found.Count);
        Assert.Contains((20, 20), found.Items.Select(At));
        Assert.Contains((240, 170), found.Items.Select(At));
    }

    /// <summary>★ 봉우리 하나에서 하나만 — 안 누르면 1px 옆을 계속 집는다.</summary>
    [Fact]
    public void 한_표식에서_하나만_나온다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        MatchList found = TemplateMatchers.FindAll(scene, mark, Options(max: 8));

        Assert.Single(found.Items);
    }

    [Fact]
    public void 점수가_높은_순서로_나온다()
    {
        using Mat scene = Scene(300, 220, (20, 20), (240, 170));
        using Mat mark = MarkTemplate();

        MatchList found = TemplateMatchers.FindAll(scene, mark, Options());

        Assert.True(found.Items[0].Score >= found.Items[1].Score);
    }

    [Fact]
    public void 딱_맞는_자리는_점수가_1에_가깝다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Assert.True(TemplateMatchers.FindAll(scene, mark, Options()).Items[0].Score > 0.95);
    }

    [Fact]
    public void 두_번_돌려도_같다()
    {
        using Mat scene = Scene(300, 220, (20, 20), (240, 170));
        using Mat mark = MarkTemplate();

        Assert.Equal(
            TemplateMatchers.FindAll(scene, mark, Options()).Items.Select(At),
            TemplateMatchers.FindAll(scene, mark, Options()).Items.Select(At));
    }

    // ─────────────────────────────── 없으면 없다고 한다

    /// <summary>★★ 여기가 핵심 — 빈 사진에도 "제일 닮은 자리" 는 반드시 있다.</summary>
    [Fact]
    public void 표식이_없으면_빈_목록()
    {
        using Mat empty = new(200, 150, MatType.CV_32FC1, Scalar.All(Background));
        using Mat mark = MarkTemplate();

        MatchList found = TemplateMatchers.FindAll(empty, mark, Options());

        Assert.Equal(0, found.Count);
    }

    [Fact]
    public void 문턱을_낮추면_엉뚱한_것도_주워_온다()
    {
        using Mat empty = new(200, 150, MatType.CV_32FC1, Scalar.All(Background));
        using Mat mark = MarkTemplate();

        // 문턱이 없으면 아무 자리나 "제일 닮은 자리" 로 뽑힌다 — 그래서 문턱이 필요하다.
        MatchList found = TemplateMatchers.FindAll(empty, mark, Options(minScore: -1.0, max: 1));

        Assert.Equal(1, found.Count);
    }

    [Fact]
    public void 개수_제한을_지킨다()
    {
        using Mat scene = Scene(400, 300, (10, 10), (150, 10), (10, 150), (150, 150));
        using Mat mark = MarkTemplate();

        Assert.Equal(2, TemplateMatchers.FindAll(scene, mark, Options(max: 2)).Count);
    }

    // ─────────────────────────────── 방식

    /// <summary>★ 방식이 달라도 "클수록 좋다" 하나로 맞춰야 한다.</summary>
    [Fact]
    public void SqDiff_도_클수록_좋게_뒤집힌다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Match match = TemplateMatchers
            .FindAll(scene, mark, Options(MatchMethod.SqDiffNormed)).Items[0];

        Assert.Equal((80, 40), At(match));
        Assert.True(match.Score > 0.95);   // 안 뒤집었으면 0 에 가까웠을 것이다
    }

    [Fact]
    public void CCorr_로도_찾는다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Assert.Equal((80, 40),
            At(TemplateMatchers.FindAll(scene, mark, Options(MatchMethod.CCorrNormed)).Items[0]));
    }

    // ─────────────────────────────── 거부

    /// <summary>★ 조각이 더 크면 파일을 바꿔 넣은 것이다. 조용히 "없다" 고 하면 안 된다.</summary>
    [Fact]
    public void 조각이_사진보다_크면_거부한다()
    {
        using Mat small = new(30, 30, MatType.CV_32FC1, Scalar.All(Background));
        using Mat big = new(100, 100, MatType.CV_32FC1, Scalar.All(Background));

        ArgumentException error =
            Assert.Throws<ArgumentException>(() => TemplateMatchers.FindAll(small, big, Options()));

        Assert.Contains("바꿔 넣지", error.Message);
    }

    [Fact]
    public void 타입이_다르면_거부한다()
    {
        using Mat bytes = new(200, 150, MatType.CV_8UC1, Scalar.All(128));
        using Mat mark = MarkTemplate();

        Assert.Throws<ArgumentException>(() => TemplateMatchers.FindAll(bytes, mark, Options()));
    }

    [Fact]
    public void 방법을_안_정하면_거부한다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Assert.Throws<ArgumentException>(
            () => TemplateMatchers.FindAll(scene, mark, Options(MatchMethod.Unknown)));
    }

    [Fact]
    public void 개수가_0_이면_거부한다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TemplateMatchers.FindAll(scene, mark, Options(max: 0)));
    }

    [Fact]
    public void null_은_거부한다()
    {
        using Mat scene = Scene(200, 150, (80, 40));
        using Mat mark = MarkTemplate();

        Assert.Throws<ArgumentNullException>(() => TemplateMatchers.FindAll(null!, mark, Options()));
        Assert.Throws<ArgumentNullException>(() => TemplateMatchers.FindAll(scene, null!, Options()));
        Assert.Throws<ArgumentNullException>(() => TemplateMatchers.FindAll(scene, mark, null!));
    }
}