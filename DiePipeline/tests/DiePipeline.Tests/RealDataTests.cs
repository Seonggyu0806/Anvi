using System.Diagnostics;
using System.Text.Json;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using Xunit.Abstractions;

namespace DiePipeline.Tests;

/// <summary>
/// 실데이터 시험 — 사람이 눈으로 확인한 실데이터 결과를 <b>기준 파일</b>로 굳혀 두고, 매번 다시 돌려 비교한다
/// (교수님 <c>WaferRealDataTests</c> · Phase8 8.7 과 같은 방식).
///
/// <para>★ 회사 자료라 <b>이 파일에는 실데이터에 관한 것이 하나도 없다</b> — 사진 · 웨이퍼 레시피 · 기준 파일 · 케이스 이름은
/// 전부 레포 밖 '케이스 목록 파일' 에서 읽는다. 환경 변수 <c>DIEPIPE_REALDATA</c> 가 그 파일을 가리킨다.
/// 없으면 조용히 통과한다 — 원본이 있는 컴퓨터에서만 돈다.</para>
///
/// <para>★ 비교는 <b>허용오차</b>로 한다(Lesson09 — 완전일치 금지). OpenCV 판이 바뀌면 끝자리는 달라질 수 있다.
/// 정렬은 같은 입력 30번이 끝자리까지 같았으므로(6주차 §5.3) 좁게 잡는다.</para>
///
/// <para>★ 기준 갱신: 결과가 <b>일부러</b> 바뀌는 수정을 했을 때만 <c>UPDATE_BASELINE=1</c> 로 돌려 다시 쓴다 — 쓴 뒤 사람이 검토한다.</para>
///
/// <code>
/// realdata.cases.json  (레포 밖 · 경로는 이 파일 기준)
/// { "cases": [ { "name": "…", "image": "…", "recipe": "….wafer.json", "baseline": "….baseline.json" } ] }
/// </code>
/// </summary>
public sealed class RealDataTests(ITestOutputHelper output)
{
    private const string CasesVariable = "DIEPIPE_REALDATA";
    private const string UpdateVariable = "UPDATE_BASELINE";

    private const double OriginTolerancePx = 0.01;
    private const double AngleToleranceDeg = 0.0001;
    private const double ScaleTolerance = 0.00001;
    private const double PositionTolerancePx = 0.5;
    private const double AreaToleranceRatio = 0.01;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void RealData_웨이퍼_레시피로_돌린_결과가_기준과_같다()
    {
        string? casesPath = Environment.GetEnvironmentVariable(CasesVariable);

        if (string.IsNullOrWhiteSpace(casesPath))
        {
            output.WriteLine($"건너뜀 — {CasesVariable} 가 없습니다(원본이 있는 컴퓨터에서만 돈다)");
            return;
        }

        Assert.True(File.Exists(casesPath), $"{CasesVariable} 가 가리키는 케이스 목록 파일이 없습니다: {casesPath}");

        string folder = Path.GetDirectoryName(Path.GetFullPath(casesPath))!;
        CaseList list = JsonSerializer.Deserialize<CaseList>(File.ReadAllText(casesPath), Json)
                        ?? throw new InvalidDataException("케이스 목록이 비어 있습니다(null)");

        Assert.NotEmpty(list.Cases);

        bool update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
        NodeRegistry registry = WaferBackend.CreateRegistry();
        List<string> problems = [];

        foreach (RealCase item in list.Cases)
        {
            Stopwatch clock = Stopwatch.StartNew();

            using IImageSource source = ImageSource.Open(Path.Combine(folder, item.Image));
            WaferInspectionResult result = WaferRecipeRunner.RunFile(Path.Combine(folder, item.Recipe), source, registry);
            Snapshot actual = Snapshot.Of(result);

            clock.Stop();
            output.WriteLine($"{item.Name}: {actual.Describe()} · {clock.Elapsed.TotalSeconds:F1}초");

            string baselinePath = Path.Combine(folder, item.Baseline);

            if (update)
            {
                File.WriteAllText(baselinePath, JsonSerializer.Serialize(actual, Json));
                output.WriteLine($"{item.Name}: 기준을 새로 썼습니다 — 사람이 검토하세요");
                continue;
            }

            if (!File.Exists(baselinePath))
            {
                problems.Add($"{item.Name}: 기준 파일이 없습니다 — {UpdateVariable}=1 로 한 번 돌려 만드세요");
                continue;
            }

            Snapshot expected = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(baselinePath), Json)
                                ?? throw new InvalidDataException($"{item.Name}: 기준 파일이 비어 있습니다(null)");

            problems.AddRange(Compare(expected, actual).Select(p => $"{item.Name}: {p}"));
        }

        // ★ 케이스마다 멈추지 않고 다 돌린 뒤 한꺼번에 알린다 — 하나가 틀리면 다른 것도 봐야 원인이 좁혀진다.
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>기준과 다른 곳을 사람이 읽는 문장으로. 같으면 빈 목록.</summary>
    private static IEnumerable<string> Compare(Snapshot expected, Snapshot actual)
    {
        if (expected.Stop != actual.Stop)
        {
            yield return $"멈춤 {expected.Stop} → {actual.Stop}";
        }

        if (expected.Aligned != actual.Aligned)
        {
            yield return $"정렬 받아들임 {expected.Aligned} → {actual.Aligned}";
        }

        foreach (string p in Near("원점 x", expected.OriginX, actual.OriginX, OriginTolerancePx)
                     .Concat(Near("원점 y", expected.OriginY, actual.OriginY, OriginTolerancePx))
                     .Concat(Near("각도", expected.AngleDeg, actual.AngleDeg, AngleToleranceDeg))
                     .Concat(Near("배율", expected.Scale, actual.Scale, ScaleTolerance)))
        {
            yield return p;
        }

        foreach ((string name, int before, int after) in new[]
                 {
                     ("die", expected.Dies, actual.Dies),
                     ("E1", expected.E1, actual.E1),
                     ("E0", expected.E0, actual.E0),
                     ("원판 밖 · 제외", expected.Outside, actual.Outside),
                     ("검사 실패", expected.Failed, actual.Failed),
                     ("결함", expected.Defects.Count, actual.Defects.Count),
                 })
        {
            if (before != after)
            {
                yield return $"{name} {before}개 → {after}개";
            }
        }

        if (expected.Defects.Count != actual.Defects.Count)
        {
            yield break;
        }

        // 같은 순서로 놓고 하나씩 맞댄다 — die 칸 → 자리 순.
        DefectRow[] before2 = [.. expected.Defects.OrderBy(d => (d.Col, d.Row, d.X, d.Y))];
        DefectRow[] after2 = [.. actual.Defects.OrderBy(d => (d.Col, d.Row, d.X, d.Y))];

        for (int i = 0; i < before2.Length; i++)
        {
            DefectRow e = before2[i];
            DefectRow a = after2[i];
            string at = $"결함 {i + 1}(die c{e.Col},r{e.Row})";

            if ((e.Col, e.Row, e.Zone) != (a.Col, a.Row, a.Zone))
            {
                yield return $"{at} 자리 · 존 → die c{a.Col},r{a.Row} {a.Zone}";
                continue;
            }

            foreach (string p in Near($"{at} x", e.X, a.X, PositionTolerancePx)
                         .Concat(Near($"{at} y", e.Y, a.Y, PositionTolerancePx)))
            {
                yield return p;
            }

            if (Math.Abs(e.Area - a.Area) > Math.Max(1, e.Area * AreaToleranceRatio))
            {
                yield return $"{at} 면적 {e.Area} → {a.Area}";
            }
        }
    }

    private static IEnumerable<string> Near(string name, double expected, double actual, double tolerance)
    {
        if (!(Math.Abs(expected - actual) <= tolerance))
        {
            yield return $"{name} {expected} → {actual} (허용 ±{tolerance})";
        }
    }

    private sealed record CaseList(List<RealCase> Cases);

    /// <param name="Name">사람이 읽는 이름 — 결과 문장에만 쓴다.</param>
    /// <param name="Image">사진(raw 면 옆의 설명 파일도). 케이스 목록 파일 기준 경로.</param>
    /// <param name="Recipe">웨이퍼 레시피 — 사진 경로는 안 들어간다.</param>
    /// <param name="Baseline">기준 파일 — 처음엔 없다. <c>UPDATE_BASELINE=1</c> 로 만든다.</param>
    private sealed record RealCase(string Name, string Image, string Recipe, string Baseline);

    /// <summary>결함 하나 — 판정은 추리기 <b>전</b> 목록(<c>Found</c>)으로 한다.</summary>
    private sealed record DefectRow(int Col, int Row, string Zone, double X, double Y, int Area);

    /// <summary>기준 파일에 적는 것 — 정렬 · die 수 · 결함 목록.</summary>
    private sealed record Snapshot
    {
        public string Stop { get; init; } = "";

        public bool Aligned { get; init; }

        public double OriginX { get; init; }

        public double OriginY { get; init; }

        public double AngleDeg { get; init; }

        public double Scale { get; init; }

        public int Dies { get; init; }

        public int E1 { get; init; }

        public int E0 { get; init; }

        public int Outside { get; init; }

        public int Failed { get; init; }

        public List<DefectRow> Defects { get; init; } = [];

        public static Snapshot Of(WaferInspectionResult result)
        {
            IReadOnlyList<DieOrigin> dies = result.Grid?.Dies ?? [];

            return new Snapshot
            {
                Stop = result.Stop.ToString(),
                Aligned = result.Solved?.Accepted ?? false,
                OriginX = result.Alignment.Origin.X,
                OriginY = result.Alignment.Origin.Y,
                AngleDeg = result.Alignment.RotationDeg,
                Scale = result.Alignment.Scale,
                Dies = dies.Count(d => d.Zone != WaferZone.Outside),
                E1 = dies.Count(d => d.Zone == WaferZone.E1),
                E0 = dies.Count(d => d.Zone == WaferZone.E0),
                Outside = dies.Count(d => d.Zone == WaferZone.Outside),
                Failed = result.Run?.Failures.Count() ?? 0,
                Defects =
                [
                    .. (result.Found?.Items ?? []).Select(d => new DefectRow(
                        d.DieCol, d.DieRow, d.Zone.ToString(), d.AbsX ?? double.NaN, d.AbsY ?? double.NaN, d.Defect.Area)),
                ],
            };
        }

        public string Describe()
            => $"멈춤 {Stop} · 정렬 {(Aligned ? "받아들임" : "없음/거부")} · 원점 ({OriginX:F2},{OriginY:F2}) · 각도 {AngleDeg:F4}° · "
               + $"die {Dies}(E1 {E1} · E0 {E0}) · 원판 밖·제외 {Outside} · 실패 {Failed} · 결함 {Defects.Count}";
    }
}