using System.Globalization;

namespace DiePipeline.Core.Domain;

/// <summary>
/// 픽셀 ↔ µm 환산(교수님 <c>Calibration</c>). <see cref="MicronsPerPixel"/> = 원본 픽셀 한 변이 몇 µm 인가.
///
/// ★ 1.0 은 '미보정' 이다 — µm 숫자가 px 와 같아져 뜻이 없으니, 글자에는 px 만 적는다.
/// ★ 값은 raw 원본 옆 설명 파일(<c>micronsPerPixel</c>)에서 오거나 사람이 넣는다. 장비 정보라 어디에도 저장하지 않는다.
/// </summary>
public readonly record struct Calibration
{
    public Calibration(double micronsPerPixel)
    {
        // 0 · 음수 · NaN · 무한대는 환산이 아니다 — 미보정으로 둔다(µm 숫자를 지어내지 않는다).
        MicronsPerPixel = double.IsFinite(micronsPerPixel) && micronsPerPixel > 0 ? micronsPerPixel : 1.0;
    }

    public static Calibration Uncalibrated => new(1.0);

    public double MicronsPerPixel { get; }

    public bool IsCalibrated => MicronsPerPixel != 1.0;

    /// <summary>px → µm.</summary>
    public double Length(double pixels) => pixels * MicronsPerPixel;

    /// <summary>px² → µm².</summary>
    public double Area(double areaPixels) => areaPixels * MicronsPerPixel * MicronsPerPixel;

    /// <summary>두 점(원본 px) 사이 곧은 거리(px).</summary>
    public static double Distance(PointF a, PointF b) => double.Hypot(b.X - a.X, b.Y - a.Y);

    /// <summary>
    /// 거리 글자 — <c>거리 123.4 px · 45.6 µm (Δx 120 · Δy -30)</c>. 미보정이면 µm 를 빼고 px 만.
    /// ★ Δx · Δy 도 적는다 — die 간격처럼 가로 · 세로로 잴 때는 곧은 거리보다 그쪽이 필요하다.
    /// </summary>
    public string DistanceText(PointF a, PointF b)
    {
        double px = Distance(a, b);
        CultureInfo c = CultureInfo.InvariantCulture;
        string length = IsCalibrated
            ? string.Create(c, $"{px:0.0} px · {Length(px):0.0} µm")
            : string.Create(c, $"{px:0.0} px");

        return string.Create(c, $"거리 {length} (Δx {b.X - a.X:0.#} · Δy {b.Y - a.Y:0.#})");
    }
}