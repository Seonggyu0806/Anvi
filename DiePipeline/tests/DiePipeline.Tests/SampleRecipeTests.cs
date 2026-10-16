using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 견본 die 레시피(<c>samples/recipes</c>) — 전부 검증을 지나고, 정합 레시피(<c>die.diff.align</c>)는 손으로 찍어 어긋난 이웃에도 결함만 잡는다.
/// ★ 합성 웨이퍼로만 본다.
/// </summary>
public sealed class SampleRecipeTests
{
    private static readonly NodeRegistry Registry = CvBackend.CreateRegistry();

    private static string Recipes()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiePipeline.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", "recipes");
            }
        }

        throw new DirectoryNotFoundException("DiePipeline.slnx 가 있는 폴더를 못 찾았습니다");
    }

    /// <summary>합성 웨이퍼의 die 한 칸(원본 픽셀) — 격자 원점 + 칸 × 피치. 기울기 0 이라 그대로 맞다.</summary>
    private static Roi DieAt(SyntheticWafer wafer, int col, int row, int dx = 0, int dy = 0)
        => new((int)Math.Round(wafer.OriginX) + (col * wafer.Spec.Pitch) + dx, (int)Math.Round(wafer.OriginY) + (row * wafer.Spec.Pitch) + dy,
            wafer.Spec.Pitch, wafer.Spec.Pitch);

    private static DefectList Inspect(string recipe, IImageSource source, Roi roi, IReadOnlyList<Roi> chips)
    {
        using Mat test = source.ReadRegion(roi);
        List<Mat> goldens = [.. chips.Select(source.ReadRegion)];

        try
        {
            PipelineContext context = new();
            context.SetInput(ReservedContext.Roi, test);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)goldens);

            return new PipelineEngine().Run(Registry.CreateAll(RecipeLoader.Load(Path.Combine(Recipes(), recipe))), context).Defects;
        }
        finally
        {
            goldens.ForEach(g => g.Dispose());
        }
    }

    /// <summary>견본 die 레시피는 전부 검증을 지난다 — 새 견본을 넣어도 블록 이름 · 배선이 맞는지.</summary>
    [Fact]
    public void 견본_die_레시피는_전부_검증을_지난다()
    {
        string[] files = Directory.GetFiles(Recipes(), "*.json");

        Assert.Contains(files, f => f.EndsWith("die.diff.align.json", StringComparison.Ordinal));
        Assert.All(files, f => RecipeValidator.EnsureValid(RecipeLoader.Load(f), Registry));
    }

    /// <summary>
    /// ★ 손으로 찍은 정상 die 처럼 이웃이 (8, 6) px 어긋나면 — 정합 없는 die.diff 는 무늬 윤곽을 결함으로 잡고,
    ///   die.diff.align 은 심은 결함 하나만 잡는다.
    /// ★ 합성 무늬는 단순해서 몇 px 어긋남은 MinMax 비교 · 면적 하한이 걸러 낸다(5 px 까지 둘 다 1개). 실데이터에서 몇 px 부터 남는지는 실데이터로 따로 본다.
    ///   정합도 만능은 아니다 — 크게(12 px) 어긋나면 이웃에 없는 테두리가 들어와 둘 다 남는다.
    /// </summary>
    [Fact]
    public void 이웃이_어긋나도_정합_레시피는_결함만_잡는다()
    {
        // 결함 하나 — 좌우 이웃이 다 있는 칸에 심긴 씨를 고른다(씨가 같으면 그림도 같다)
        static SyntheticWaferSpec Spec(int seed) => new() { Cols = 6, Rows = 4, Pitch = 120, Defects = 1, Seed = seed };

        int seed = Enumerable.Range(1, 50).First(s =>
        {
            using SyntheticWafer probe = SyntheticWafer.Draw(Spec(s));
            return probe.Defects[0].Col is > 0 and < 5;
        });

        using SyntheticWafer wafer = SyntheticWafer.Draw(Spec(seed));
        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        using MatImageSource source = new(f32);

        PlantedDefect target = wafer.Defects[0];
        Roi roi = DieAt(wafer, target.Col, target.Row);
        Roi[] shifted = [DieAt(wafer, target.Col - 1, target.Row, 8, 6), DieAt(wafer, target.Col + 1, target.Row, 8, 6)];
        Roi[] exact = [DieAt(wafer, target.Col - 1, target.Row), DieAt(wafer, target.Col + 1, target.Row)];

        DefectList plainExact = Inspect("die.diff.json", source, roi, exact);
        DefectList plainShifted = Inspect("die.diff.json", source, roi, shifted);
        DefectList alignShifted = Inspect("die.diff.align.json", source, roi, shifted);
        DefectList alignExact = Inspect("die.diff.align.json", source, roi, exact);

        Assert.Equal(1, plainExact.Count);
        Assert.True(plainShifted.Count > 1, $"정합 없이 어긋나면 윤곽이 결함으로 — {plainShifted.Count}개");
        Assert.Equal(1, alignShifted.Count);
        Assert.Equal(1, alignExact.Count);
        Assert.Equal(plainExact.Items[0].Area, alignExact.Items[0].Area, tolerance: plainExact.Items[0].Area * 0.1);
    }
}