using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Tests;

/// <summary>
/// 정상 die 자동 맞춤 — 어긋난 만큼 옮기라고 하고 · 이미 맞으면 0 · 다른 무늬 · 너무 큰 양은 거부. ★ 합성 무늬만.
/// </summary>
public sealed class ChipSnapTests : IDisposable
{
    private const int Size = 160;

    private readonly List<Mat> _made = [];

    public void Dispose()
    {
        foreach (Mat mat in _made) mat.Dispose();
    }

    private Mat Keep(Mat mat)
    {
        _made.Add(mat);
        return mat;
    }

    /// <summary>무늬 판 — 잡음을 흐려 만든 무늬(씨가 같으면 같다). 같은 무늬를 판의 두 자리에 깐다.</summary>
    private Mat Board(int seed)
    {
        Random random = new(seed);
        const int rows = Size * 2;
        const int cols = Size * 3;
        Mat noise = Keep(new Mat(rows, cols, MatType.CV_32FC1));

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                noise.Set(y, x, (float)random.NextDouble());
            }
        }

        Cv2.GaussianBlur(noise, noise, new Size(0, 0), 2.0);
        return noise;
    }

    private Mat Cut(Mat board, int x, int y) => Keep(new Mat(board, new Rect(x, y, Size, Size)).Clone());

    /// <summary>
    /// ★ 정상 die 상자가 (+7, −5) 어긋나 그려졌으면 (−7, +5) 옮기라고 한다 — 옮긴 상자는 검사 die 와 같은 무늬를 덮는다.
    /// </summary>
    [Fact]
    public void 어긋난_만큼_옮기라고_한다()
    {
        Mat board = Board(1);

        // 같은 무늬를 (20,20) 과 (200,20) 에 — 검사 die 는 앞, 정상 die 는 뒤
        Mat test = Cut(board, 20, 20);
        using (Mat right = new(board, new Rect(200, 20, Size, Size)))
        {
            test.CopyTo(right);
        }

        Mat drawn = Cut(board, 207, 15);
        ChipSnapResult result = ChipSnap.Measure(test, drawn, maxShiftPx: 20);

        Assert.Null(result.Refused);
        Assert.True(result.Moves);
        Assert.Equal((-7, 5), (result.Dx, result.Dy));
        Assert.True(result.Response > 0.5, $"응답 {result.Response:F2}");

        Mat snapped = Cut(board, 207 + result.Dx, 15 + result.Dy);
        using Mat gap = new();
        Cv2.Absdiff(test, snapped, gap);
        Cv2.MinMaxLoc(gap, out _, out double worst);
        Assert.Equal(0, worst, 6);
    }

    /// <summary>이미 맞으면 옮기지 않는다.</summary>
    [Fact]
    public void 이미_맞으면_0()
    {
        Mat test = Cut(Board(2), 30, 30);
        ChipSnapResult result = ChipSnap.Measure(test, test.Clone(), maxShiftPx: 20);

        Assert.Null(result.Refused);
        Assert.False(result.Moves);
        Assert.Equal((0, 0), (result.Dx, result.Dy));
    }

    /// <summary>★ 다른 무늬면 응답이 낮다 — 옮기지 않고 까닭을 말한다.</summary>
    [Fact]
    public void 다른_무늬면_옮기지_않는다()
    {
        Mat test = Cut(Board(3), 10, 10);
        Mat other = Cut(Board(4), 10, 10);

        ChipSnapResult result = ChipSnap.Measure(test, other, maxShiftPx: 20);

        Assert.False(result.Moves);
        Assert.Contains("무늬가 잘 안 맞습니다", result.Refused, StringComparison.Ordinal);
    }

    /// <summary>옮길 양이 상한을 넘으면 옮기지 않는다 — 반복 무늬에서 엉뚱한 자리를 잡는 것을 막는다.</summary>
    [Fact]
    public void 너무_멀면_옮기지_않는다()
    {
        Mat board = Board(5);
        Mat test = Cut(board, 20, 20);
        using (Mat right = new(board, new Rect(200, 20, Size, Size)))
        {
            test.CopyTo(right);
        }

        ChipSnapResult result = ChipSnap.Measure(test, Cut(board, 230, 20), maxShiftPx: 20);

        Assert.False(result.Moves);
        Assert.Contains("너무 큽니다", result.Refused, StringComparison.Ordinal);
    }

    /// <summary>크기 · 포맷이 다르면 거부.</summary>
    [Fact]
    public void 크기나_포맷이_다르면_거부()
    {
        Mat test = Cut(Board(6), 0, 0);

        Assert.Throws<ArgumentException>(() => ChipSnap.Measure(test, Keep(new Mat(Size, Size + 1, MatType.CV_32FC1)), 10));
        Assert.Throws<ArgumentException>(() => ChipSnap.Measure(test, Keep(new Mat(Size, Size, MatType.CV_8UC1)), 10));
    }
}