using System.Runtime.InteropServices;
using DiePipeline.Core.Imaging;
using OpenCvSharp;

namespace DiePipeline.Cv.Imaging;

/// <summary>
/// 평균 축소본 — 칸(step × step) 안의 픽셀을 <b>전부 평균</b> 낸다. <see cref="IImageSource.ReadOverview"/>(건너뛰며 솎아내기)와 크기가 같다.
///
/// <para>★ 솎아내기는 칸마다 한 픽셀만 남긴다. 칸보다 가는 회로선은 남은 픽셀에 걸리면 보이고 아니면 사라져
/// 선이 끊기고 점선처럼 보인다(앨리어싱 — 사진이 '깨져' 보인다). 평균은 선이 옅어질 뿐 끊기지 않는다.</para>
///
/// <para>★ 대신 원본을 <b>끝까지</b> 읽는다 — 솎아내기는 1/step 행만 읽는다. 그래서 뷰어는 솎아낸 것을 먼저 보이고
/// 이것을 뒤에서 만들어 바꿔 끼운다. 한 번에 칸 높이(step 행)만큼만 읽어 메모리는 일꾼마다 띠 하나만 쓴다.</para>
///
/// <para>★ 보기 전용이다. 검사(웨이퍼 정렬 · die 검사)는 이것을 쓰지 않는다.</para>
/// </summary>
public static class SmoothOverview
{
    /// <param name="step">칸 크기(원본 px) — 솎아내기의 배율과 같은 값을 줘야 두 축소본이 같은 크기다.</param>
    /// <param name="ct">띠를 읽을 때마다 본다 — 다른 사진을 열면 뷰어가 멈춘다.</param>
    public static Mat Read(IImageSource source, int step, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        int outW = (source.Width + step - 1) / step;
        int outH = (source.Height + step - 1) / step;
        Mat result = new(outH, outW, MatType.CV_32FC1);

        try
        {
            // ★ 띠마다 따로 읽어 결과의 서로 다른 행에 쓴다 — 나눠 돌려도 겹치지 않는다(읽기는 동시에 해도 된다).
            //   한 줄로 돌리면 큰 원본에서 20초 가까이 걸렸다. 코어 하나는 화면에 남긴다.
            ParallelOptions options = new()
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            };

            Parallel.For(0, outH, options, () => new Strip(outW), (y, _, work) =>
            {
                int top = y * step;
                using Mat strip = source.ReadRegion(new Roi(0, top, source.Width, Math.Min(step, source.Height - top)));

                // 띠를 세로로 평균 → 한 줄, 그 줄을 가로로 step 칸씩 평균 → 축소본 한 줄. 마지막 칸이 모자라면 있는 만큼으로.
                Cv2.Reduce(strip, work.Column, ReduceDimension.Row, ReduceTypes.Avg, -1);
                Cv2.Resize(work.Column, work.Line, new Size(outW, 1), 0, 0, InterpolationFlags.Area);

                // 이미지 소스와 같은 방식 — 줄 하나를 배열로 꺼내 결과의 y 행에 넣는다
                Marshal.Copy(work.Line.Ptr(0), work.Values, 0, outW);
                Marshal.Copy(work.Values, 0, result.Ptr(y), outW);
                return work;
            }, work => work.Dispose());
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    /// <summary>일꾼 하나가 띠마다 다시 쓰는 그릇.</summary>
    private sealed class Strip(int width) : IDisposable
    {
        public Mat Column { get; } = new();

        public Mat Line { get; } = new();

        public float[] Values { get; } = new float[width];

        public void Dispose()
        {
            Column.Dispose();
            Line.Dispose();
        }
    }
}