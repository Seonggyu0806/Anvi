using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>정상 die들을 하나로 겹치는 방법.</summary>
public enum CombineStrategy
{
    /// <summary>산술 평균. 노이즈를 √N배 줄이지만 <b>오염도 1/N만큼 섞인다</b>.</summary>
    Mean,

    /// <summary>
    /// 가운데 값. <b>이 프로젝트의 기본값</b>.
    /// 이웃 한 장이 오염돼도 골든에 <b>아예 안 들어간다</b>(3장 이상일 때).
    /// </summary>
    Median,

    /// <summary>가장 어두운 값.</summary>
    Min,

    /// <summary>가장 밝은 값.</summary>
    Max,
}

/// <summary>
/// 정상 die 여러 장 → 골든 한 장.
///
/// ★ 작업 포맷은 <b>GrayF32</b>([0,1] 실수)로 고정한다.
///   8비트(0~255)로 두면 원본이 16비트로 바뀔 때 레시피의 임계값을 전부 다시 잡아야 한다.
/// </summary>
public static class CombineStrategies
{
    public static Mat Combine(IReadOnlyList<Mat> sources, CombineStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            throw new ArgumentException("합칠 이미지가 없습니다", nameof(sources));
        }

        RequireSameShape(sources);

        int width = sources[0].Cols;
        int height = sources[0].Rows;
        int count = sources.Count;

        // ★ 왜 Mat.At<float>(y, x) 루프를 안 쓰나:
        //   OpenCvSharp의 At은 부를 때마다 네이티브 호출(P/Invoke)이 일어난다.
        //   2500×2500 × 3장이면 1,800만 번이라 분 단위로 느려진다.
        //   한 번에 배열로 꺼내 두면 그다음은 순수 C# 배열 접근이라 비교가 안 되게 빠르다.
        float[][] planes = new float[count][];

        for (int i = 0; i < count; i++)
        {
            sources[i].GetArray(out planes[i]);
        }

        float[] result = new float[width * height];

        // 같은 좌표의 값 N개를 담을 그릇. 픽셀마다 새로 만들지 않고 재사용한다.
        float[] scratch = new float[count];

        for (int p = 0; p < result.Length; p++)
        {
            for (int i = 0; i < count; i++)
            {
                scratch[i] = planes[i][p];
            }

            result[p] = strategy switch
            {
                CombineStrategy.Mean => Mean(scratch),
                CombineStrategy.Median => Median(scratch),
                CombineStrategy.Min => Min(scratch),
                CombineStrategy.Max => Max(scratch),
                _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "알 수 없는 합성 방법"),
            };
        }

        return Mat.FromPixelData(height, width, MatType.CV_32FC1, result);
    }

    private static float Mean(float[] values)
    {
        // ★ double로 더한다. float으로 수백 장을 더하면 뒤쪽 값이 반올림에 먹힌다.
        double sum = 0;

        foreach (float v in values) sum += v;

        return (float)(sum / values.Length);
    }

    private static float Median(float[] values)
    {
        // Clone — 정렬은 순서를 바꾸는데, scratch는 다음 픽셀에서 다시 쓴다.
        float[] sorted = (float[])values.Clone();
        Array.Sort(sorted);

        int n = sorted.Length;

        return n % 2 == 1
            ? sorted[n / 2]                              // 홀수개면 정가운데
            : 0.5f * (sorted[(n / 2) - 1] + sorted[n / 2]);   // 짝수개면 가운데 둘의 평균
    }

    private static float Min(float[] values)
    {
        float min = values[0];

        foreach (float v in values) if (v < min) min = v;

        return min;
    }

    private static float Max(float[] values)
    {
        float max = values[0];

        foreach (float v in values) if (v > max) max = v;

        return max;
    }

    /// <summary>
    /// 크기·포맷이 같은가. ★ 여기서 안 막으면 배열 인덱스가 엇나가
    /// <b>엉뚱한 픽셀끼리 비교되는데 예외는 안 난다</b> — 가장 찾기 어려운 종류의 버그다.
    /// </summary>
    private static void RequireSameShape(IReadOnlyList<Mat> sources)
    {
        Mat first = sources[0];

        if (first.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException($"작업 포맷은 GrayF32여야 합니다 — 받은 것: {first.Type()}");
        }

        for (int i = 1; i < sources.Count; i++)
        {
            if (sources[i].Rows != first.Rows || sources[i].Cols != first.Cols)
            {
                throw new ArgumentException(
                    $"[{i}]번 이미지의 크기가 다릅니다 — {first.Cols}x{first.Rows} vs {sources[i].Cols}x{sources[i].Rows}");
            }

            if (sources[i].Type() != MatType.CV_32FC1)
            {
                throw new ArgumentException($"[{i}]번 이미지가 GrayF32가 아닙니다 — {sources[i].Type()}");
            }
        }
    }
}