using System.Text.Json;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Tests;

/// <summary>결함 보고서(CSV · JSON, 교수님 형식) — px 값 + µm 환산 · 빈 값 · 번호순 · 입력과 검사 ROI.</summary>
public sealed class DefectReportTests
{
    private static readonly Defect Big = new()
    {
        Id = 2,
        Bounds = new BoundingBox(45, 40, 36, 30),
        Area = 1080,
        Centroid = new PointF(62.456, 57.5),
        MeanIntensity = 0.03912,
        MaxIntensity = 0.1,
        Circularity = 0.8,
        ClassLabel = "Blob, \"큰\"",
    };

    // 밝기 · 모양 · 유형을 안 잰 결함 — 빈 칸이 된다.
    private static readonly Defect Bare = new()
    {
        Id = 1,
        Bounds = new BoundingBox(3, 4, 5, 6),
        Area = 20,
        Centroid = new PointF(5, 7),
    };

    /// <summary>★ CSV — 머리 · 번호순 · µm 환산 · 안 잰 값은 빈 칸 · 유형 이름은 따옴표(안의 따옴표는 둘로).</summary>
    [Fact]
    public void CSV_는_번호순_환산_빈칸()
    {
        string[] lines = DefectReport.ToCsv([Big, Bare], new Calibration(0.5)).Split('\n');

        Assert.Equal(DefectReport.CsvHeader, lines[0]);
        Assert.Equal("1,5,7,20,5,3,4,5,6,2.5,3,,,,", lines[1]);
        Assert.Equal("2,62.46,57.5,1080,270,45,40,36,30,18,15,0.039,0.1,0.8,\"Blob, \"\"큰\"\"\"", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
    }

    /// <summary>미보정이면 µm 값이 px 값과 같다(환산 1.0).</summary>
    [Fact]
    public void 미보정이면_µm_가_px_와_같다()
    {
        string row = DefectReport.ToCsv([Bare], Calibration.Uncalibrated).Split('\n')[1];

        Assert.Equal("1,5,7,20,20,3,4,5,6,5,6,,,,", row);
    }

    /// <summary>★ JSON — 입력 · 환산 · 검사 ROI(원본 좌표) · 결함 값.</summary>
    [Fact]
    public void JSON_은_입력_환산_ROI_결함()
    {
        using JsonDocument doc = JsonDocument.Parse(
            DefectReport.ToJson([Big, Bare], new Calibration(0.5), "원본 w.bmp", new Roi(184, 304, 120, 120)));
        JsonElement root = doc.RootElement;

        Assert.Equal("원본 w.bmp", root.GetProperty("source").GetString());
        Assert.Equal(0.5, root.GetProperty("calibration").GetProperty("micronsPerPixel").GetDouble());
        Assert.True(root.GetProperty("calibration").GetProperty("calibrated").GetBoolean());
        Assert.Equal(184, root.GetProperty("inspectionRoi").GetProperty("x").GetInt32());
        Assert.Equal(2, root.GetProperty("count").GetInt32());

        JsonElement first = root.GetProperty("defects")[0];
        JsonElement second = root.GetProperty("defects")[1];
        Assert.Equal(1, first.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("meanIntensity").ValueKind);
        Assert.Equal(270, second.GetProperty("areaUm2").GetDouble());
        Assert.Equal(18, second.GetProperty("widthUm").GetDouble());
        Assert.Equal(62.46, second.GetProperty("centroid").GetProperty("x").GetDouble());
    }

    /// <summary>합성 견본은 검사 ROI 가 원본 좌표가 아니다 — null · 미보정으로 적는다. 한글은 그대로(\uXXXX 아님).</summary>
    [Fact]
    public void 합성_견본은_ROI_없이_미보정()
    {
        string json = DefectReport.ToJson([Bare], Calibration.Uncalibrated, "합성 견본", null);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("inspectionRoi").ValueKind);
        Assert.False(doc.RootElement.GetProperty("calibration").GetProperty("calibrated").GetBoolean());
        Assert.Contains("합성 견본", json, StringComparison.Ordinal);
    }
}