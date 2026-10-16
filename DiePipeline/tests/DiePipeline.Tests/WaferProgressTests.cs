using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 실행 중 알림 — 격자 준비 → die 마다 한 번(행 우선) · 결과는 알림을 받든 안 받든 같다 · die 마다 골든 자리가 남는다 · 실행 중 맵 칸.
/// ★ 합성 견본(<c>samples/wafer/demo.wafer.json</c>)으로 본다.
/// </summary>
public sealed class WaferProgressTests
{
    private static readonly NodeRegistry Registry = WaferBackend.CreateRegistry();

    /// <summary>받은 순서 그대로 담는다 — <see cref="Progress{T}"/> 는 다른 스레드로 넘겨 순서가 흔들릴 수 있어 시험에는 이걸 쓴다.</summary>
    private sealed class Collect : IProgress<WaferProgress>
    {
        public List<WaferProgress> Items { get; } = [];

        public void Report(WaferProgress value) => Items.Add(value);
    }

    private static MatImageSource Source(SyntheticWafer wafer)
    {
        Mat f32 = new();
        wafer.Image.ConvertTo(f32, MatType.CV_32FC1, 1.0 / 255.0);
        return new MatImageSource(f32);
    }

    private static string Demo()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiePipeline.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", "wafer", "demo.wafer.json");
            }
        }

        throw new DirectoryNotFoundException("DiePipeline.slnx 가 있는 폴더를 못 찾았습니다");
    }

    private static WaferInspectionResult Run(SyntheticWafer wafer, IProgress<WaferProgress>? progress)
    {
        using MatImageSource source = Source(wafer);
        string file = Demo();

        return WaferRecipeRunner.RunIn(
            Core.Configuration.RecipeLoader.Load(file), Path.GetDirectoryName(file)!, source, Registry, progress: progress);
    }

    /// <summary>★ 격자 준비가 먼저, 그다음 die 마다 한 번 — 칸 수만큼, 행 우선 순서, 끝 번호 = 전체.</summary>
    [Fact]
    public void 격자_준비_뒤_die_마다_한_번_행_우선()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        Collect progress = new();

        WaferInspectionResult result = Run(wafer, progress);

        WaferGridReady ready = Assert.IsType<WaferGridReady>(progress.Items[0]);
        WaferDieDone[] dies = [.. progress.Items.Skip(1).Cast<WaferDieDone>()];
        DieOrigin[] order = [.. ready.Grid.InRowMajorOrder()];

        Assert.Equal(ready.Grid.Count, dies.Length);
        Assert.Equal(Enumerable.Range(1, dies.Length), dies.Select(d => d.Done));
        Assert.All(dies, d => Assert.Equal(ready.Grid.Count, d.Total));
        Assert.Equal(order.Select(d => (d.Col, d.Row)), dies.Select(d => (d.Die.Die.Col, d.Die.Die.Row)));
        Assert.Equal(result.Run!.Dies, dies.Select(d => d.Die));
    }

    /// <summary>★ 알림은 결과를 안 바꾼다 — 받든 안 받든 같은 결함.</summary>
    [Fact]
    public void 알림을_받아도_결과는_같다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());

        WaferInspectionResult quiet = Run(wafer, null);
        WaferInspectionResult watched = Run(wafer, new Collect());

        Assert.Equal(quiet.Alignment, watched.Alignment);
        Assert.Equal(quiet.Found!.Items.Select(d => (d.DieCol, d.DieRow, d.AbsX, d.AbsY, d.Defect.Area)),
            watched.Found!.Items.Select(d => (d.DieCol, d.DieRow, d.AbsX, d.AbsY, d.Defect.Area)));
    }

    /// <summary>정렬을 못 믿어 멈추면 격자도 없다 — 알림이 하나도 안 온다(빈 맵을 그리지 않게).</summary>
    [Fact]
    public void 멈추면_알림이_없다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        using MatImageSource source = Source(wafer);
        Collect progress = new();
        string file = Demo();

        // 표식 설계 자리를 틀리게 — 짝이 안 맞아 정렬을 거부한다(레시피 실행기 시험과 같은 수법)
        string json = File.ReadAllText(file).Replace("[ [ -40, -40 ], [ 410, 310 ] ]", "[ [ -40, -40 ], [ 300, 310 ] ]", StringComparison.Ordinal);
        Assert.Contains("[ 300, 310 ]", json, StringComparison.Ordinal);

        WaferInspectionResult result = WaferRecipeRunner.RunIn(
            Core.Configuration.RecipeLoader.Parse(json), Path.GetDirectoryName(file)!, source, Registry, progress: progress);

        Assert.True(result.Stopped);
        Assert.Empty(progress.Items);
    }

    /// <summary>★ die 마다 골든을 잘라 온 자리가 남는다 — E1 은 장수와 같은 수, 크기는 검사 ROI 와 같다.</summary>
    [Fact]
    public void die_마다_골든_자리가_남는다()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());

        WaferInspectionResult result = Run(wafer, null);

        Assert.All(result.Run!.Dies.Where(d => d.Die.Zone == WaferZone.E1 && !d.Failed), d =>
        {
            Assert.Equal(d.GoldenCount, d.GoldenRois.Count);
            Assert.NotEmpty(d.GoldenRois);
            Assert.All(d.GoldenRois, g => Assert.Equal((d.Roi.Width, d.Roi.Height), (g.Width, g.Height)));
            Assert.DoesNotContain(d.Roi, d.GoldenRois);
        });
    }

    /// <summary>실행 중 맵 — 처음엔 전부 '아직 안 봄', die 가 끝나면 그 칸만 결과로. 끝난 뒤 맵과 같은 칸이 된다.</summary>
    [Fact]
    public void 실행_중_맵_칸()
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(new SyntheticWaferSpec());
        Collect progress = new();

        WaferInspectionResult result = Run(wafer, progress);
        DieGrid grid = ((WaferGridReady)progress.Items[0]).Grid;

        IReadOnlyList<WaferMapCell> pending = WaferMap.Pending(grid);
        Assert.Equal(grid.Count, pending.Count);
        Assert.All(pending, c => Assert.Equal(c.Zone == WaferZone.Outside ? WaferMapState.Outside : WaferMapState.Pending, c.State));

        WaferMapCell[] live = [.. progress.Items.OfType<WaferDieDone>().Select(d => WaferMap.Cell(d.Die))];
        Assert.Equal(WaferMap.Of(result), live);
    }
}