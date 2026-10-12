using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>MaskApply 설정.</summary>
public sealed record MaskApplyOptions
{
    /// <summary>true 면 반대로 — 마스크가 <b>켜진</b> 곳을 지우고, 꺼진 곳만 남긴다.</summary>
    public bool Invert { get; init; }

    /// <summary>
    /// 지운 곳을 채울 밝기(0~1). <b>기본 0 = 교수님 동작.</b>
    ///
    /// ★ 교수님 블록은 지운 곳을 0(검정)으로만 채운다. 그런데 E0 레시피는 0~0.15 를 <b>결함</b>으로 본다 —
    ///   0 으로 지우면 지운 곳이 통째로 결함이 된다. 어두운 결함을 찾는 레시피 앞에서는 1.0(흰색)을 준다.
    /// </summary>
    public double Fill { get; init; }
}

/// <summary>
/// 이미지에서 <b>볼 곳만 남기고</b> 나머지를 지운다 — 검사 영역 한정.
///
/// <para>★ 교수님 Phase 4 의 <c>MaskApply</c> 다 — "검사영역 한정/제외용".
/// 검사할 곳을 러너가 몰래 고치지 않고 <b>레시피 블록</b>으로 정한다.
/// 그래야 레시피 파일만 보고 무엇을 검사했는지 알 수 있다.</para>
///
/// <para>★ 교수님과 다른 점 하나 — <see cref="MaskApplyOptions.Fill"/>.
/// 기본값이 0 이라 안 주면 교수님 동작과 똑같다.</para>
///
/// <para><b>비유</b> — 시험지에서 문제가 없는 칸을 종이로 덮고 채점하는 것이다.
/// 검정 종이로 덮으면 "빈칸 = 오답" 규칙에 걸려 전부 틀린 것으로 채점된다. 그래서 덮는 종이 색을 고른다.</para>
/// </summary>
public static class MaskAppliers
{
    /// <param name="src">가릴 이미지 — 작업 포맷(GrayF32).</param>
    /// <param name="mask">볼 곳 — Gray8. <b>0 이 아니면 켜짐</b>(Binarize 가 내는 0/255 그대로 쓸 수 있다).</param>
    public static Mat Apply(Mat src, Mat mask, MaskApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(options);

        if (src.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException(
                $"가릴 이미지는 작업 포맷(GrayF32)이어야 합니다 — 받은 것: {src.Type()}", nameof(src));
        }

        if (mask.Type() != MatType.CV_8UC1)
        {
            throw new ArgumentException(
                $"마스크는 Gray8 이어야 합니다(Binarize 결과처럼 0/255) — 받은 것: {mask.Type()}", nameof(mask));
        }

        if (src.Rows != mask.Rows || src.Cols != mask.Cols)
        {
            throw new ArgumentException(
                $"이미지와 마스크의 크기가 같아야 합니다 — 이미지 {src.Cols}x{src.Rows}, 마스크 {mask.Cols}x{mask.Rows}",
                nameof(mask));
        }

        // ★ NaN 은 어느 비교에도 false 라 '!(범위 안)' 으로 써야 같이 걸린다.
        if (!(options.Fill >= 0 && options.Fill <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"fill 은 0~1 이어야 합니다 (현재 {options.Fill})");
        }

        // ★ 남길 곳을 '0 인가 아닌가' 로 직접 고른다.
        //   교수님은 반전을 BitwiseNot 으로 한다 — 마스크가 0/255 일 때만 맞다. 1 은 254 가 되어 여전히 '켜짐' 이다.
        using Mat keep = new();
        Cv2.Compare(mask, Scalar.All(0), keep, options.Invert ? CmpTypes.EQ : CmpTypes.NE);

        // 전부 fill 로 칠해 놓고, 남길 곳에만 원본을 덮어 쓴다.
        Mat result = new(src.Size(), MatType.CV_32FC1, Scalar.All(options.Fill));

        try
        {
            src.CopyTo(result, keep);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}