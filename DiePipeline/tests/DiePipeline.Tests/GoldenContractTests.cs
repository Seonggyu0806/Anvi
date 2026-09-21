using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Tests;

/// <summary>
/// 레시피가 요구하는 골든 장수.
///
/// <para>★ 오늘 확인할 것 — <b>이웃 고르는 법과 합치는 법이 안 맞으면 조용히 틀리는 대신 멈추는가.</b>
/// 3주차에 "짝에서 하나를 떼면 부당하게 나빠 보인다" 를 배웠는데,
/// 실제로 명령행에서 이 짝이 어긋나 미러 오검이 6개 나왔다. 그걸 기계가 잡게 한다.</para>
/// </summary>
public sealed class GoldenContractTests
{
    private static Recipe Recipe(string? combineStrategy, string? diffPolicy = null)
    {
        List<NodeConfig> nodes = [];

        if (combineStrategy is not null)
        {
            nodes.Add(Node("golden", "Combine",
                ("regions", "chipRegions"), ("result", "golden"),
                $$"""{"strategy":"{{combineStrategy}}"}"""));
        }

        if (diffPolicy is not null)
        {
            nodes.Add(Node("diff", "Difference",
                ("test", "roi"), ("result", "diff"),
                $$"""{"windowSize":3,"diffPolicy":"{{diffPolicy}}"}"""));
        }

        return new Recipe
        {
            RecipeId = "test",
            ContextInputs = ["roi", "chipRegions"],
            Pipeline = new PipelineSection { Nodes = nodes },
        };
    }

    private static NodeConfig Node(
        string id, string type, (string Port, string Key) input, (string Port, string Key) output, string json)
        => new()
        {
            Id = id,
            Type = type,
            Inputs = new Dictionary<string, string> { [input.Port] = input.Key },
            Outputs = new Dictionary<string, string> { [output.Port] = output.Key },
            Params = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(),
        };

    // ─────────────────────────────── 레시피에서 장수를 읽어낸다

    /// <summary>★ 2장이면 중앙값이 평균이 되어 미러가 샌다. 이게 실제로 터진 자리다.</summary>
    [Fact]
    public void Median_은_3장이_필요하다()
        => Assert.Equal(3, GoldenContract.Of(Recipe("Median", "Absolute")).Minimum);

    [Fact]
    public void Median_은_까닭을_같이_알려준다()
        => Assert.Contains("중앙값", GoldenContract.Of(Recipe("Median")).Reason);

    [Fact]
    public void 대소문자는_안_따진다()
        => Assert.Equal(3, GoldenContract.Of(Recipe("median")).Minimum);

    /// <summary>★ 부호가 막으면 장수는 안 따진다 — 교수님 짝이 이것이다.</summary>
    [Fact]
    public void Mean_에_DarkOnly_면_한_장이면_된다()
        => Assert.Equal(1, GoldenContract.Of(Recipe("Mean", "DarkOnly")).Minimum);

    [Fact]
    public void Mean_에_BrightOnly_도_마찬가지()
        => Assert.Equal(1, GoldenContract.Of(Recipe("Mean", "BrightOnly")).Minimum);

    [Fact]
    public void 부호로_막으면_경고가_없다()
        => Assert.Null(GoldenContract.Of(Recipe("Mean", "DarkOnly")).Warning);

    /// <summary>★ 장수로는 못 막는 짝 — 경고를 낸다. "3장이면 된다"고 하면 안 된다.</summary>
    [Fact]
    public void Mean_에_Absolute_면_경고한다()
    {
        GoldenContract.Requirement need = GoldenContract.Of(Recipe("Mean", "Absolute"));

        Assert.NotNull(need.Warning);
        Assert.Contains("희석", need.Warning);
    }

    [Fact]
    public void 차이_단계가_없는_Mean_도_경고한다()
        => Assert.NotNull(GoldenContract.Of(Recipe("Mean")).Warning);

    [Fact]
    public void Percentile_은_두_장이_필요하다()
        => Assert.Equal(2, GoldenContract.Of(Recipe("Percentile")).Minimum);

    /// <summary>★ 골든을 안 만드는 레시피 — E0 용(die.dark)이 여기 해당한다.</summary>
    [Fact]
    public void 합치는_단계가_없으면_골든이_필요_없다()
        => Assert.Equal(0, GoldenContract.Of(Recipe(null)).Minimum);

    [Fact]
    public void 모르는_방식이면_최소_한_장으로_본다()
        => Assert.Equal(1, GoldenContract.Of(Recipe("Wibble")).Minimum);

    [Fact]
    public void null_은_거부한다()
        => Assert.Throws<ArgumentNullException>(() => GoldenContract.Of(null!));

    // ─────────────────────────────── 러너가 모자란 골든을 멈춘다

    private static DieGrid Grid(int cols, int rows)
        => DieGridBuilder.Build(new WaferSpec { Cols = cols, Rows = rows, PitchX = 100, PitchY = 100 });

    private static WaferRunResult Run(DieGrid grid, int minimum, NeighborStrategy strategy, int want = 3)
        => WaferRunner.Run(
            grid,
            (_, _) => DefectList.Empty,
            null,
            new WaferRunOptions
            {
                Strategy = strategy,
                GoldenWanted = want,
                ImageWidth = 10_000,
                ImageHeight = 10_000,
                MinimumGoldens = minimum,
            });

    /// <summary>★ 끝 열은 이웃이 하나뿐이다 — 3장을 요구하면 여기서 걸린다.</summary>
    [Fact]
    public void 골든이_모자라면_그_die_를_실패로_남긴다()
    {
        WaferRunResult run = Run(Grid(4, 1), minimum: 3, NeighborStrategy.SameRow1);

        // 같은 행 1칸 → 끝 열 2개는 1장, 가운데 2개는 2장. 3장을 넘기는 die 가 없다.
        Assert.Equal(4, run.Failures.Count());
    }

    [Fact]
    public void 실패_까닭에_몇_장인지_적힌다()
    {
        WaferRunResult run = Run(Grid(4, 1), minimum: 3, NeighborStrategy.SameRow1);

        Assert.Contains("3장이 필요", run.Dies[0].Failure);
    }

    /// <summary>★ 멈춘 die 는 결함 0개가 아니라 <b>실패</b>다. 둘은 다르다.</summary>
    [Fact]
    public void 멈춘_die_는_검사_함수를_안_부른다()
    {
        int calls = 0;

        WaferRunner.Run(
            Grid(4, 1),
            (_, _) => { calls++; return DefectList.Empty; },
            null,
            new WaferRunOptions
            {
                Strategy = NeighborStrategy.SameRow1,
                ImageWidth = 10_000,
                ImageHeight = 10_000,
                MinimumGoldens = 3,
            });

        Assert.Equal(0, calls);
    }

    [Fact]
    public void 장수가_넉넉하면_그냥_돈다()
    {
        WaferRunResult run = Run(Grid(5, 1), minimum: 3, NeighborStrategy.Nearest);

        Assert.Empty(run.Failures);
    }

    /// <summary>0 이면 안 따진다 — 예전처럼 돈다.</summary>
    [Fact]
    public void 최소치가_0_이면_안_따진다()
    {
        WaferRunResult run = Run(Grid(4, 1), minimum: 0, NeighborStrategy.SameRow1);

        Assert.Empty(run.Failures);
    }
}