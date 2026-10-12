using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiePipeline.Core.Imaging;

/// <summary>
/// 헤더 없는 raw 이미지를 해석하는 정보 — <b>사이드카 JSON</b>(<c>&lt;이미지&gt;.json</c>)에서 읽는다.
///
/// ★ raw 파일은 픽셀 바이트만 나열돼 있어서, 파일만으로는 폭·높이를 알 수 없다.
///   1,000바이트가 100×10 인지 50×20 인지 정해 주는 것이 이 사이드카다.
///   장비가 원본과 같이 남기는 파일이고, 교수님 <c>RawImageMeta</c> 와 키 이름이 같다.
///
/// ★ 자리 계산은 이미 있는 <see cref="ImageGeometry"/> 가 한다. 이 레코드는
///   "어떤 기하로 읽을지" 를 파일에서 꺼내 주는 일만 한다.
/// </summary>
public sealed record RawImageMeta
{
    /// <summary>지원하는 유일한 행 순서 — 첫 행이 이미지 맨 위.</summary>
    public const string TopDown = "TopDown";

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>1 = 회색 8비트 · 2 = 회색 16비트 · 3 = BGR 24비트.</summary>
    public int BytesPerPixel { get; init; } = 1;

    /// <summary>행 저장 순서. 교수님과 같이 <c>TopDown</c>(첫 행 = 이미지 맨 위)만 받는다.</summary>
    public string RowOrder { get; init; } = TopDown;

    /// <summary>픽셀 하나의 물리 크기. 읽기에는 안 쓰고 들고만 있는다.</summary>
    public double? MicronsPerPixel { get; init; }

    /// <summary>이 정보가 맞다면 raw 파일은 정확히 이만큼이어야 한다 — 헤더도 패딩도 없다.</summary>
    [JsonIgnore]
    public long ExpectedFileSize => (long)Width * Height * BytesPerPixel;

    /// <summary>
    /// 자리 계산용 기하. 폭·높이·바이트 수가 틀리면 <see cref="ImageGeometry"/> 가 입구에서 거부한다.
    /// </summary>
    /// <exception cref="NotSupportedException">행 순서가 <c>TopDown</c> 이 아닐 때.</exception>
    public ImageGeometry ToGeometry()
    {
        // ★ 다른 순서를 조용히 받아 위→아래로 읽으면 그림이 통째로 뒤집히고 die 번호가 전부 어긋난다.
        if (!string.Equals(RowOrder, TopDown, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"rowOrder '{RowOrder}' 는 지원하지 않습니다 — 지금은 {TopDown}(첫 행 = 맨 위)만 읽습니다");
        }

        return ImageGeometry.Raw(Width, Height, BytesPerPixel);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>JSON 글자를 읽는다. <c>width</c>·<c>height</c> 가 없으면 <see cref="JsonException"/>.</summary>
    public static RawImageMeta Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize<RawImageMeta>(json, Options)
               ?? throw new InvalidDataException("사이드카 JSON 이 비어 있습니다(null)");
    }

    /// <summary>사이드카 파일을 읽는다.</summary>
    public static RawImageMeta Load(string jsonPath) => Parse(File.ReadAllText(jsonPath));

    /// <summary>
    /// <paramref name="imagePath"/> 의 사이드카를 찾는다. 없으면 <c>null</c>.
    ///
    /// <code>
    ///   scan.raw  →  scan.raw.json  (먼저)
    ///             →  scan.json      (확장자를 바꾼 이름)
    /// </code>
    /// </summary>
    public static string? FindSidecar(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        string appended = imagePath + ".json";

        if (File.Exists(appended))
        {
            return appended;
        }

        string swapped = Path.ChangeExtension(imagePath, ".json");

        return !string.Equals(swapped, imagePath, StringComparison.OrdinalIgnoreCase) && File.Exists(swapped)
            ? swapped
            : null;
    }
}