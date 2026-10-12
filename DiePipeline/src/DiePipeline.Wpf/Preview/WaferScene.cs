using System.IO;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Domain;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wpf.Imaging;
using OpenCvSharp;

namespace DiePipeline.Wpf.Preview;

/// <summary>
/// 화면에 올린 웨이퍼 한 장 — 영역을 읽어 주는 소스 + 미리보기 그림 + (합성이면) 정렬 재료와 정답.
///
/// ★ <b>IDisposable</b> 이다. 파일이면 소스가 파일을 붙잡고(메모리 매핑) 있다.
///   새 웨이퍼를 올릴 때 옛 것을 안 놓으면 파일 손잡이가 쌓인다.
/// </summary>
public sealed class WaferScene : IDisposable
{
    /// <summary>
    /// 미리보기 그림의 긴 변. ★ 원본은 수만 픽셀일 수 있다 — 통째로 화면에 올리지 않고 솎아 낸 것만 올린다.
    /// 좌표는 원본 픽셀 그대로 쓰고, 그림만 늘여 붙인다(Viewbox) — 그래서 솎아도 칸이 안 어긋난다.
    /// </summary>
    private const int OverviewLongSide = 2048;

    private WaferScene(
        IImageSource source, string title, Mat? template, IReadOnlyList<PointF> expected, IReadOnlyList<PlantedDefect> truth,
        WaferCircle? circle = null)
    {
        Source = source;
        Title = title;
        Template = template;
        Expected = expected;
        Truth = truth;
        Circle = circle;

        using Mat overview = source.ReadOverview(OverviewLongSide);
        Overview = BitmapConvert.ToBitmap(overview);
    }

    public IImageSource Source { get; }

    public string Title { get; }

    public BitmapSource Overview { get; }

    /// <summary>원본 픽셀 크기. 결함·die 좌표가 이 자로 적혀 있다.</summary>
    public int Width => Source.Width;

    public int Height => Source.Height;

    /// <summary>표식 조각(GrayF32). 합성이면 있고, 파일이면 null — 정렬 없이 격자 원점을 쓴다.</summary>
    public Mat? Template { get; }

    public IReadOnlyList<PointF> Expected { get; }

    /// <summary>심은 결함(정답). 파일이면 비어 있다 — 정답을 모른다.</summary>
    public IReadOnlyList<PlantedDefect> Truth { get; }

    /// <summary>원판(정답). 합성 원판이면 있고, 아니면 null — 파일이면 사람이 넣는다.</summary>
    public WaferCircle? Circle { get; }

    public MarkAlignment? Alignment
        => Template is null ? null : new MarkAlignment { Template = Template, Expected = Expected };

    /// <summary>합성 웨이퍼를 그려 올린다 — 명령행 <c>demo</c> 와 <b>같은 그림</b>이다.</summary>
    public static WaferScene Synthetic(SyntheticWaferSpec spec)
    {
        using SyntheticWafer wafer = SyntheticWafer.Draw(spec);

        // ★ 파일에서 읽을 때와 같은 배율(1/255). 명령행(파일로 저장 후 읽기)과 화면(메모리)이
        //   같은 숫자를 봐야 결과가 같다.
        Mat image = new();
        wafer.Image.ConvertTo(image, MatType.CV_32FC1, 1.0 / 255.0);

        MatImageSource source = new(image);     // 이제 image 는 source 가 놓는다
        Mat template = new();

        try
        {
            using (Mat raw = wafer.DrawMarkTemplate())
            {
                raw.ConvertTo(template, MatType.CV_32FC1, 1.0 / 255.0);
            }

            PointF[] expected = [.. wafer.Marks.Select(wafer.ExpectedAt).Select(p => new PointF(p.X, p.Y))];

            return new WaferScene(
                source,
                $"합성 {spec.Cols}×{spec.Rows} · 피치 {spec.Pitch} · 기울기 {spec.AngleDeg:F2}° · 씨 {spec.Seed}"
                + (spec.Round ? " · 원판" : string.Empty),
                template,
                expected,
                [.. wafer.Defects],
                wafer.Circle is { } c ? new WaferCircle(c.CenterX, c.CenterY, c.Radius) : null);
        }
        catch
        {
            source.Dispose();
            template.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 이미지 파일을 올린다. ★ 통째로 읽지 않는다 — 소스는 필요한 영역만 그때그때 뜯어 온다.
    /// 정렬 재료(표식)가 없으니 격자 원점을 사람이 넣어야 한다.
    /// </summary>
    public static WaferScene Open(string path)
    {
        IImageSource source = ImageSource.Open(path);

        try
        {
            return new WaferScene(source, Path.GetFileName(path), null, [], []);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Source.Dispose();
        Template?.Dispose();
    }
}
