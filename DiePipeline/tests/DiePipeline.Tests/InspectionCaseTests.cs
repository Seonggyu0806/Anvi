using DiePipeline.Core.Configuration;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Tests;

/// <summary>
/// 검사 케이스 파일(<c>*.case.json</c>, 교수님 형식) — 쓰고 읽으면 제자리 · 사진 경로는 케이스 기준 상대 · 깨진 파일은 까닭과 함께 거부.
/// ★ 임시 폴더에만 쓴다(레포 밖).
/// </summary>
public sealed class InspectionCaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dp_case_" + Guid.NewGuid().ToString("N"));

    public InspectionCaseTests() => Directory.CreateDirectory(Path.Combine(_folder, "cases"));

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string CasePath => Path.Combine(_folder, "cases", "a.case.json");

    private string Write(string json)
    {
        File.WriteAllText(CasePath, json);
        return CasePath;
    }

    /// <summary>★ 쓰고 읽으면 제자리 — 영역 · 사진 경로(절대로 풀림).</summary>
    [Fact]
    public void 쓰고_읽으면_제자리()
    {
        string image = Path.Combine(_folder, "img", "w.bmp");
        Roi roi = new(150, 100, 110, 110);
        Roi[] chips = [new(20, 90, 110, 110), new(260, 90, 110, 110)];

        InspectionCaseFile.Save(CasePath, image, roi, chips);
        (InspectionCase read, string imagePath) = InspectionCaseFile.Read(CasePath);

        Assert.Equal(roi, read.Roi);
        Assert.Equal(chips, read.ChipRegions);
        Assert.Equal(Path.GetFullPath(image), imagePath);
    }

    /// <summary>사진 경로는 케이스 파일 기준 상대 · 나눔표 '/' · 형식에 있는 칸만(계산 값 Right · Bottom 은 안 적는다).</summary>
    [Fact]
    public void 사진은_상대_경로로_형식_칸만_적는다()
    {
        InspectionCaseFile.Save(CasePath, Path.Combine(_folder, "img", "w.bmp"), new Roi(1, 2, 3, 4), [new Roi(5, 6, 3, 4)]);
        string json = File.ReadAllText(CasePath);

        Assert.Contains("\"image\": \"../img/w.bmp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"width\": 3", json, StringComparison.Ordinal);
        Assert.DoesNotContain("right", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("area", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>다른 드라이브의 사진은 상대로 못 적는다 — 절대 경로 그대로.</summary>
    [Fact]
    public void 다른_드라이브면_절대_경로()
    {
        string drive = Path.GetPathRoot(Path.GetFullPath(_folder))!.StartsWith('Z') ? "Y:\\" : "Z:\\";
        string image = drive + "data\\w.bmp";

        InspectionCaseFile.Save(CasePath, image, new Roi(0, 0, 4, 4), [new Roi(4, 0, 4, 4)]);
        (InspectionCase read, string imagePath) = InspectionCaseFile.Read(CasePath);

        Assert.Equal(drive.Replace('\\', '/') + "data/w.bmp", read.Image);
        Assert.Equal(Path.GetFullPath(image), imagePath);
    }

    /// <summary>교수님 앱이 쓴 파일(대소문자 · 주석 · 끝 쉼표)도 읽는다.</summary>
    [Fact]
    public void 교수님_파일도_읽는다()
    {
        (InspectionCase read, _) = InspectionCaseFile.Read(Write("""
            { // 교수님 앱
              "Image": "w.bmp",
              "ROI": { "X": 1, "Y": 2, "Width": 3, "Height": 4 },
              "chipRegions": [ { "x": 5, "y": 6, "width": 3, "height": 4 }, ],
            }
            """));

        Assert.Equal(new Roi(1, 2, 3, 4), read.Roi);
        Assert.Single(read.ChipRegions);
    }

    /// <summary>없는 파일 — 운영체제 문구가 아니라 '케이스 파일이 없습니다: 푼 경로'.</summary>
    [Fact]
    public void 없는_파일은_경로를_적어_알린다()
    {
        string missing = Path.Combine(_folder, "cases", "없음.case.json");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => InspectionCaseFile.Read(missing));

        Assert.Equal($"케이스 파일이 없습니다: {missing}", error.Message);
    }

    /// <summary>★ 깨진 파일은 무엇이 틀렸는지 적어서 거부한다.</summary>
    [Theory]
    [InlineData("""{ "roi": { "x": 0, "y": 0, "width": 4, "height": 4 } }""", "image")]
    [InlineData("""{ "image": "w.bmp" }""", "roi 에 x · y · width · height")]
    [InlineData("""{ "image": "w.bmp", "roi": { "x": 0, "y": 0, "width": 4 } }""", "roi 에 x · y · width · height")]
    [InlineData("""{ "image": "w.bmp", "roi": { "x": 0, "y": 0, "width": 4, "height": 4 }, "chipRegions": [ { "x": 0, "y": 0, "width": 0, "height": 4 } ] }""", "chipRegions[0] 크기가 0 이하")]
    [InlineData("""{ "image": "w.bmp", "roi": """, "읽지 못했습니다")]
    public void 깨진_파일은_까닭과_함께_거부(string json, string why)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => InspectionCaseFile.Read(Write(json)));

        Assert.Contains(why, error.Message, StringComparison.Ordinal);
    }
}