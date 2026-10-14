using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Core.Domain;

/// <summary>
/// 결함 목록 → CSV · JSON 보고서(교수님 <c>DefectReport</c>). px 값과 µm 환산 값을 같이 싣는다.
///
/// ★ 새 측정은 만들지 않는다 — 이미 잰 값(면적 · 경계 · 밝기 · 원형도 · 유형)을 환산해 옮겨 적을 뿐이다.
/// ★ 결함 좌표는 검사 ROI 기준이다. 원본 좌표로 돌리려면 JSON 의 <c>inspectionRoi</c> 를 더한다.
/// ★ 실데이터에서 나온 숫자라 회사 자료다 — 쓰는 쪽이 레포 밖인지 먼저 본다.
/// </summary>
public static class DefectReport
{
    public const string CsvHeader =
        "id,centroidX,centroidY,areaPx,areaUm2,bboxX,bboxY,widthPx,heightPx,widthUm,heightUm,meanIntensity,maxIntensity,circularity,classLabel";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // 한글(입력 이름)을 \uXXXX 로 바꾸지 않는다 — 사람이 여는 파일이다.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>CSV — 머리 한 줄 + 결함마다 한 줄(번호순). 빈 값(밝기를 안 쟀다 등)은 빈 칸.</summary>
    public static string ToCsv(IReadOnlyList<Defect> defects, Calibration calibration)
    {
        ArgumentNullException.ThrowIfNull(defects);

        StringBuilder csv = new();
        csv.Append(CsvHeader).Append('\n');

        foreach (Defect d in defects.OrderBy(d => d.Id))
        {
            csv.Append(string.Join(',',
                d.Id.ToString(CultureInfo.InvariantCulture),
                F(d.Centroid.X, 2), F(d.Centroid.Y, 2),
                d.Area.ToString(CultureInfo.InvariantCulture), F(calibration.Area(d.Area), 4),
                I(d.Bounds.X), I(d.Bounds.Y), I(d.Bounds.Width), I(d.Bounds.Height),
                F(calibration.Length(d.Bounds.Width), 3), F(calibration.Length(d.Bounds.Height), 3),
                N(d.MeanIntensity, 3), N(d.MaxIntensity, 3), N(d.Circularity, 3),
                Q(d.ClassLabel))).Append('\n');
        }

        return csv.ToString();
    }

    /// <summary>
    /// JSON — 환산 값 · 입력(<paramref name="source"/>) · 검사 ROI(원본 좌표, 모르면 null) · 결함 목록.
    /// </summary>
    public static string ToJson(IReadOnlyList<Defect> defects, Calibration calibration, string source, Roi? inspectionRoi)
    {
        ArgumentNullException.ThrowIfNull(defects);
        ArgumentNullException.ThrowIfNull(source);

        var report = new
        {
            source,
            calibration = new { micronsPerPixel = calibration.MicronsPerPixel, calibrated = calibration.IsCalibrated },
            inspectionRoi = inspectionRoi is { } r ? new { x = r.X, y = r.Y, width = r.Width, height = r.Height } : null,
            count = defects.Count,
            defects = defects.OrderBy(d => d.Id).Select(d => new
            {
                id = d.Id,
                centroid = new { x = R(d.Centroid.X, 2), y = R(d.Centroid.Y, 2) },
                areaPx = d.Area,
                areaUm2 = R(calibration.Area(d.Area), 4),
                bbox = new { x = d.Bounds.X, y = d.Bounds.Y, width = d.Bounds.Width, height = d.Bounds.Height },
                widthUm = R(calibration.Length(d.Bounds.Width), 3),
                heightUm = R(calibration.Length(d.Bounds.Height), 3),
                meanIntensity = d.MeanIntensity,
                maxIntensity = d.MaxIntensity,
                circularity = d.Circularity,
                classLabel = d.ClassLabel,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static double R(double value, int decimals) => Math.Round(value, decimals);

    private static string F(double value, int decimals) => Math.Round(value, decimals).ToString(CultureInfo.InvariantCulture);

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string N(double? value, int decimals) => value is { } v ? F(v, decimals) : string.Empty;

    // 유형 이름은 따옴표로 — 쉼표 · 따옴표가 들어 있어도 칸이 안 깨지게(따옴표는 둘로).
    private static string Q(string? text) => text is null ? string.Empty : "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}