using DiePipeline.Core.Domain;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 결함 샘플링.
///
/// <para>★ 오늘 확인할 것은 <b>같은 입력이면 같은 출력인가</b>다.
/// 무작위가 섞여 있어서, 여기가 흔들리면 "어제와 결과가 다르다" 를 영영 못 쫓는다.</para>
/// </summary>
public sealed class WaferSamplerTests
{
    private static WaferDefect Defect(int col, int row, int id, int area)
        => new()
        {
            DieCol = col,
            DieRow = row,
            Zone = WaferZone.E1,
            Defect = new Defect
            {
                Id = id,
                Bounds = new BoundingBox(0, 0, 3, 3),
                Area = area,
                Centroid = new PointF(1, 1),
            },
        };

    /// <summary>면적 10, 20, 30 … 인 결함 n개. die 는 하나씩 다르게.</summary>
    private static WaferDefectList Many(int n)
        => new() { Items = [.. Enumerable.Range(1, n).Select(i => Defect(i, 0, i, i * 10))] };

    private static SamplingOptions Options(SamplingMode mode) => new() { Mode = mode };

    private static int[] Areas(WaferDefectList list) => [.. list.Items.Select(d => d.Defect.Area)];

    // ─────────────────────────────── 전부

    [Fact]
    public void All_은_그대로_돌려준다()
        => Assert.Equal(10, WaferSampler.Sample(Many(10), Options(SamplingMode.All)).Count);

    [Fact]
    public void All_은_순서도_안_바꾼다()
        => Assert.Equal([10, 20, 30], Areas(WaferSampler.Sample(Many(3), Options(SamplingMode.All))));

    // ─────────────────────────────── 큰 것부터

    [Fact]
    public void BigSize_는_면적_큰_순서로_자른다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.BigSize, Count = 3 });

        Assert.Equal([50, 40, 30], Areas(result));
    }

    [Fact]
    public void BigSize_는_개수보다_적으면_다_준다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(2), new SamplingOptions { Mode = SamplingMode.BigSize, Count = 100 });

        Assert.Equal(2, result.Count);
    }

    /// <summary>★ 면적이 같으면 행 → 열 → 번호로 가른다. 안 가르면 "상위 N개" 가 매번 바뀐다.</summary>
    [Fact]
    public void BigSize_는_동률을_끝까지_가른다()
    {
        WaferDefectList tied = new()
        {
            Items =
            [
                Defect(col: 5, row: 9, id: 1, area: 100),
                Defect(col: 1, row: 2, id: 7, area: 100),
                Defect(col: 0, row: 2, id: 3, area: 100),
            ],
        };

        WaferDefectList result = WaferSampler.Sample(
            tied, new SamplingOptions { Mode = SamplingMode.BigSize, Count = 3 });

        // 행 2 가 먼저, 그 안에서 열 0 이 먼저
        Assert.Equal([(0, 2), (1, 2), (5, 9)], result.Items.Select(d => (d.DieCol, d.DieRow)));
    }

    [Fact]
    public void BigSize_는_두_번_돌려도_같다()
    {
        SamplingOptions options = new() { Mode = SamplingMode.BigSize, Count = 4 };

        Assert.Equal(
            Areas(WaferSampler.Sample(Many(20), options)),
            Areas(WaferSampler.Sample(Many(20), options)));
    }

    // ─────────────────────────────── 면적 범위

    [Fact]
    public void Range_는_사이에_든_것만_남긴다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.Range, MinArea = 20, MaxArea = 40 });

        Assert.Equal([20, 30, 40], Areas(result));
    }

    /// <summary>경계값은 <b>포함</b>이다 — 이걸 안 적어 두면 다음 사람이 반드시 헷갈린다.</summary>
    [Fact]
    public void Range_의_경계는_포함이다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.Range, MinArea = 30, MaxArea = 30 });

        Assert.Equal([30], Areas(result));
    }

    [Fact]
    public void Range_는_순서를_안_바꾼다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.Range, MinArea = 0, MaxArea = 1000 });

        Assert.Equal([10, 20, 30, 40, 50], Areas(result));
    }

    [Fact]
    public void Range_에_아무것도_안_들면_빈_목록()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.Range, MinArea = 900, MaxArea = 1000 });

        Assert.Equal(0, result.Count);
    }

    // ─────────────────────────────── 무작위

    [Fact]
    public void Random_은_개수만큼_준다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(50), new SamplingOptions { Mode = SamplingMode.Random, Count = 7 });

        Assert.Equal(7, result.Count);
    }

    /// <summary>★★ 이게 이 단계의 핵심 — 씨가 같으면 결과가 같아야 한다.</summary>
    [Fact]
    public void Random_은_씨가_같으면_결과가_같다()
    {
        SamplingOptions options = new() { Mode = SamplingMode.Random, Count = 10, Seed = 42 };

        Assert.Equal(
            Areas(WaferSampler.Sample(Many(100), options)),
            Areas(WaferSampler.Sample(Many(100), options)));
    }

    [Fact]
    public void Random_은_씨가_다르면_대개_다르다()
    {
        int[] one = Areas(WaferSampler.Sample(
            Many(100), new SamplingOptions { Mode = SamplingMode.Random, Count = 10, Seed = 1 }));

        int[] two = Areas(WaferSampler.Sample(
            Many(100), new SamplingOptions { Mode = SamplingMode.Random, Count = 10, Seed = 2 }));

        Assert.NotEqual(one, two);
    }

    /// <summary>★ 뽑은 뒤 원래 순서로 되돌린다 — 안 그러면 눈으로 비교할 수가 없다.</summary>
    [Fact]
    public void Random_은_원래_순서를_지킨다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(100), new SamplingOptions { Mode = SamplingMode.Random, Count = 10, Seed = 7 });

        int[] areas = Areas(result);

        Assert.Equal([.. areas.Order()], areas);
    }

    [Fact]
    public void Random_도_개수보다_적으면_다_준다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(3), new SamplingOptions { Mode = SamplingMode.Random, Count = 10, Seed = 1 });

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void 뽑은_것은_원본_그대로다()
    {
        WaferDefectList result = WaferSampler.Sample(
            Many(5), new SamplingOptions { Mode = SamplingMode.BigSize, Count = 1 });

        Assert.Equal(5, result.Items[0].DieCol);
        Assert.Equal(WaferZone.E1, result.Items[0].Zone);
    }

    // ─────────────────────────────── 거부

    [Fact]
    public void 방식을_안_정하면_거부한다()
        => Assert.Throws<ArgumentException>(
            () => WaferSampler.Sample(Many(3), Options(SamplingMode.Unknown)));

    /// <summary>★ 0개를 남기는 건 거의 언제나 실수다. "결함이 없다" 와 구분이 안 된다.</summary>
    [Fact]
    public void 개수가_0_이면_거부한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => WaferSampler.Sample(Many(3), new SamplingOptions { Mode = SamplingMode.BigSize, Count = 0 }));

    [Fact]
    public void 면적_범위가_뒤집히면_거부한다()
        => Assert.Throws<ArgumentException>(
            () => WaferSampler.Sample(
                Many(3), new SamplingOptions { Mode = SamplingMode.Range, MinArea = 100, MaxArea = 10 }));

    [Fact]
    public void 면적_하한이_음수면_거부한다()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => WaferSampler.Sample(
                Many(3), new SamplingOptions { Mode = SamplingMode.Range, MinArea = -1 }));

    [Fact]
    public void null_은_거부한다()
    {
        Assert.Throws<ArgumentNullException>(
            () => WaferSampler.Sample(null!, Options(SamplingMode.All)));
        Assert.Throws<ArgumentNullException>(
            () => WaferSampler.Sample(Many(3), null!));
    }

    [Fact]
    public void 빈_목록은_빈_채로_나온다()
        => Assert.Equal(0, WaferSampler.Sample(
            WaferDefectList.Empty, new SamplingOptions { Mode = SamplingMode.BigSize, Count = 5 }).Count);
}