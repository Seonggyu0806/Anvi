using OpenCvSharp;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 정상 die 한 장을 검사 die 무늬에 맞춘 결과 — 상자를 (<see cref="Dx"/>, <see cref="Dy"/>) px 옮기면 무늬가 맞는다.
/// <see cref="Refused"/> 가 있으면 옮기지 않는다(무늬가 잘 안 맞거나 옮길 양이 너무 크다).
/// </summary>
/// <param name="ShiftX">잰 어긋남(소수점) — 상자는 정수로 옮기고, 남은 소수점은 레시피의 정합(Align)이 맞춘다.</param>
/// <param name="Response">위상 상관의 응답(0~1) — 무늬가 얼마나 또렷하게 맞았나.</param>
public sealed record ChipSnapResult(int Dx, int Dy, double ShiftX, double ShiftY, double Response, string? Refused)
{
    /// <summary>옮길 것인가 — 받아들였고 0 이 아니다.</summary>
    public bool Moves => Refused is null && (Dx != 0 || Dy != 0);
}

/// <summary>
/// 정상 die 자동 맞춤 — 손으로 그린 정상 die 상자가 검사 die 와 몇 px 어긋났는지 잰다(위상 상관).
///
/// <para>★ 왜 — 손으로 그린 정상 die 는 제각각 다른 방향으로 몇~십몇 px 어긋난다. 레시피가 정상 die 를 <b>먼저 합친 뒤</b>
/// 한 번 맞추면, 합친 골든이 이미 번져 있어 무늬 윤곽이 결함으로 남는다(실데이터: 결함 없는 die 에서 수십 개).
/// 상자를 하나씩 맞춰 두면 남는 어긋남은 1 px 안이고, 그것은 레시피의 정합이 맞춘다.</para>
///
/// <para>★ 상자를 <b>정수 px</b> 로만 옮긴다 — 화면의 상자가 실제로 움직여 사람이 볼 수 있고, 다시 읽은 그림은 보간으로 흐려지지 않는다.</para>
/// <para>★ die 안 무늬가 반복되면 엉뚱한 봉우리를 잡을 수 있다 — 옮길 양에 상한을 두고, 넘으면 옮기지 않고 알린다.</para>
/// </summary>
public static class ChipSnap
{
    /// <summary>이보다 응답이 낮으면 무늬가 안 맞은 것으로 보고 옮기지 않는다 — 다른 무늬 · 빈 곳 · 너무 다른 밝기.</summary>
    public const double MinResponse = 0.2;

    /// <param name="test">검사 die(작업 포맷 GrayF32).</param>
    /// <param name="chip">정상 die — 검사 die 와 같은 크기.</param>
    /// <param name="maxShiftPx">이보다 많이 옮겨야 하면 옮기지 않는다.</param>
    public static ChipSnapResult Measure(Mat test, Mat chip, int maxShiftPx)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShiftPx);

        if (test.Type() != MatType.CV_32FC1 || chip.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException($"작업 포맷(GrayF32)이어야 합니다 — 받은 것: {test.Type()} · {chip.Type()}");
        }

        if (test.Size() != chip.Size())
        {
            throw new ArgumentException($"크기가 다릅니다 — 검사 die {test.Cols}x{test.Rows}, 정상 die {chip.Cols}x{chip.Rows}");
        }

        // ★ 복사본을 넘긴다 — 위상 상관이 넘겨받은 그림에 창을 곱해 버리는 판이 있다(동기 확인). 원본을 망가뜨리지 않게.
        using Mat a = test.Clone();
        using Mat b = chip.Clone();
        using Mat window = new();
        Cv2.CreateHanningWindow(window, test.Size(), MatType.CV_32FC1);

        Point2d shift = Cv2.PhaseCorrelate(a, b, window, out double response);
        int dx = (int)Math.Round(shift.X);
        int dy = (int)Math.Round(shift.Y);

        string? refused =
            response < MinResponse ? $"무늬가 잘 안 맞습니다(응답 {response:F2}) — 그대로 둡니다"
            : Math.Abs(dx) > maxShiftPx || Math.Abs(dy) > maxShiftPx ? $"옮길 양이 너무 큽니다({dx}, {dy} px) — 다른 자리를 잡았는지 보세요"
            : null;

        return new ChipSnapResult(dx, dy, shift.X, shift.Y, response, refused);
    }
}