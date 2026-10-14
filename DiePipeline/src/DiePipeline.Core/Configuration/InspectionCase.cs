using System.Text.Json;
using DiePipeline.Core.Imaging;

namespace DiePipeline.Core.Configuration;

/// <summary>
/// 검사 케이스 하나 — 사진 + 검사 ROI + 정상 die(골든을 만들 이웃). 원본 뷰어에서 그린 영역을 남기는 단위다.
///
/// ★ 교수님 <c>InspectionCase</c>(<c>*.case.json</c>)와 <b>같은 형식</b>이다 — 새 형식을 만들지 않는다.
///   <c>{ "image": "사진 경로", "roi": { "x", "y", "width", "height" }, "chipRegions": [ { … }, … ] }</c>
/// ★ <see cref="Image"/> 는 파일에 적힌 그대로다(케이스 파일 기준 상대 경로). 푼 경로는 <see cref="InspectionCaseFile.Read"/> 가 따로 준다.
/// </summary>
public sealed record InspectionCase
{
    public required string Image { get; init; }

    public required Roi Roi { get; init; }

    public required IReadOnlyList<Roi> ChipRegions { get; init; }
}

/// <summary>
/// 케이스 파일 읽기 · 쓰기.
///
/// ★ 사진 경로는 케이스 파일 기준 <b>상대</b>로 적는다 — 케이스와 사진을 같이 옮겨도 풀린다(다른 드라이브면 절대 경로).
/// ★ 영역이 사진 안에 있는지는 여기서 안 본다 — 사진 크기는 사진을 연 쪽이 안다.
/// ★ 케이스는 원본 좌표라 회사 자료다 — 쓰는 쪽이 <see cref="RepositoryGuard"/> 로 레포 밖인지 먼저 본다.
/// </summary>
public static class InspectionCaseFile
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// 케이스 파일을 읽는다 — 케이스 + 푼 사진 경로(절대). 모양이 틀리면 <see cref="InvalidDataException"/>(무엇이 틀렸는지 적어서),
    /// 파일이 없으면 <see cref="FileNotFoundException"/>(푼 경로를 적어서).
    /// </summary>
    public static (InspectionCase Case, string ImagePath) Read(string caseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseFilePath);

        // ★ 운영체제 문구(영어) 대신 사람 말로 — 어느 경로를 찾았는지까지 적는다.
        string full = Path.GetFullPath(caseFilePath);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"케이스 파일이 없습니다: {full}", full);
        }

        CaseShape shape;

        try
        {
            shape = JsonSerializer.Deserialize<CaseShape>(File.ReadAllText(caseFilePath), ReadOptions)
                    ?? throw new InvalidDataException("케이스 파일이 비었습니다");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"케이스 파일을 읽지 못했습니다 — {error.Message}", error);
        }

        if (string.IsNullOrWhiteSpace(shape.Image))
        {
            throw new InvalidDataException("케이스 파일에 image(사진 경로)가 없습니다");
        }

        InspectionCase @case = new()
        {
            Image = shape.Image,
            Roi = ToRoi(shape.Roi, "roi"),
            ChipRegions = [.. (shape.ChipRegions ?? []).Select((r, i) => ToRoi(r, $"chipRegions[{i}]"))],
        };

        string folder = Path.GetDirectoryName(Path.GetFullPath(caseFilePath))!;

        return (@case, Path.GetFullPath(Path.Combine(folder, shape.Image)));
    }

    /// <summary>케이스 파일을 쓴다. 사진 경로는 케이스 파일 기준 상대로(다른 드라이브면 절대), 나눔표는 '/'.</summary>
    public static void Save(string caseFilePath, string imagePath, Roi roi, IReadOnlyList<Roi> chipRegions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(chipRegions);

        string folder = Path.GetDirectoryName(Path.GetFullPath(caseFilePath))!;
        string reference = Path.GetRelativePath(folder, Path.GetFullPath(imagePath)).Replace('\\', '/');

        // ★ Roi 를 그대로 쓰면 계산 값(Right · Bottom · Area)까지 들어간다 — 형식에 있는 넷만.
        static object Shape(Roi r) => new { x = r.X, y = r.Y, width = r.Width, height = r.Height };

        string json = JsonSerializer.Serialize(
            new { image = reference, roi = Shape(roi), chipRegions = chipRegions.Select(Shape) },
            WriteOptions);

        File.WriteAllText(caseFilePath, json);
    }

    private static Roi ToRoi(RoiShape? shape, string name)
    {
        if (shape is not { X: { } x, Y: { } y, Width: { } w, Height: { } h })
        {
            throw new InvalidDataException($"케이스 파일의 {name} 에 x · y · width · height 가 다 있어야 합니다");
        }

        if (w <= 0 || h <= 0)
        {
            throw new InvalidDataException($"케이스 파일의 {name} 크기가 0 이하입니다({w}×{h})");
        }

        return new Roi(x, y, w, h);
    }

    // 읽기용 모양 — 빠진 칸을 알아채려고 전부 null 이 될 수 있게 둔다.
    private sealed class CaseShape
    {
        public string? Image { get; init; }

        public RoiShape? Roi { get; init; }

        public List<RoiShape>? ChipRegions { get; init; }
    }

    private sealed class RoiShape
    {
        public int? X { get; init; }

        public int? Y { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }
    }
}