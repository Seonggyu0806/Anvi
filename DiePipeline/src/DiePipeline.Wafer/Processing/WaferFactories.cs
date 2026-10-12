using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;

namespace DiePipeline.Wafer.Processing;

/// <summary>배선 — 블록 설정에서 입력 · 출력 이름표를 꺼낸다(Cv 의 같은 도우미와 같은 규칙).</summary>
internal static class Wire
{
    public static string In(NodeConfig c, string port)
        => c.Inputs.TryGetValue(port, out string? key)
            ? key
            : throw new ArgumentException($"node '{c.Id}'({c.Type}): 입력 포트 '{port}'가 연결되지 않았습니다");

    public static string Out(NodeConfig c, string port)
        => c.Outputs.TryGetValue(port, out string? key)
            ? key
            : throw new ArgumentException($"node '{c.Id}'({c.Type}): 출력 포트 '{port}'가 연결되지 않았습니다");
}

/// <summary>
/// 자세 계산 블록 설명.
///
/// ★ 정렬에 관한 값은 <b>이 블록 한 곳</b>에 모은다 — 표식의 설계 자리(<c>expected</c>) · 대략 원점(<c>firstDieX/Y</c>) ·
///   판정 기준. 교수님은 판정 기준을 InspectDies 에, 표식 자리를 두 블록에 적었다. 떨어진 짝은 언젠가 어긋나서 모았다.
/// ★ <c>expected</c> 는 <c>[[x,y], …]</c> 목록이라 서술자로 못 적는다 — Filter 처럼 팩토리가 직접 검사한다.
/// </summary>
public sealed class SolveAlignmentNodeFactory : INodeFactory, ICustomParamsValidator
{
    public string TypeName => "SolveAlignment";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("SolveAlignment", "정렬",
        inputs: [new PortDescriptor("matches", "찾은 표식 자리(사진 좌표)", PortKind.MatchList)],
        outputs: [new PortDescriptor("alignment", "놓인 자세 + 판정", PortKind.Alignment)],
        @params:
        [
            new ParamDescriptor("firstDieX", typeof(double), Default: 0.0,
                Description: "대략 원점 x — 정렬 전에 die(0,0) 이 있을 법한 사진 자리. 피치의 절반 안이어야 die 번호가 맞는다"),
            new ParamDescriptor("firstDieY", typeof(double), Default: 0.0, Description: "대략 원점 y"),
            new ParamDescriptor("minScore", typeof(double), Default: 0.8, Min: 0.0, Max: 1.0,
                Description: "짝지은 표식의 평균 점수가 이보다 낮으면 거부"),
            new ParamDescriptor("maxAngleDeg", typeof(double), Default: 2.0, Min: 0.0,
                Description: "이보다 많이 돌았다는 답이면 거부 — 웨이퍼는 살짝 삐뚤 뿐이다"),
            new ParamDescriptor("maxScaleError", typeof(double), Default: 0.02, Min: 0.0,
                Description: "배율이 1 에서 이만큼 넘게 벗어나면 거부 — 표식 2개일 때 유일하게 듣는 가드(4주차)"),
            new ParamDescriptor("maxResidualPx", typeof(double), Default: 3.0, Min: 0.0,
                Description: "되짚어 본 어긋남이 이보다 크면 거부(px) — 표식 3개부터 의미가 있다"),
            new ParamDescriptor("solveScale", typeof(bool), Default: false,
                Description: "배율까지 풀까. 카메라 배율이 고정이면 끈다"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new SolveAlignmentNode(
            config.Id,
            matchesKey: Wire.In(config, "matches"),
            outKey: Wire.Out(config, "alignment"),
            expected: Expected(config),
            prior: new PointF(
                ParamValues.GetDouble(config.Params, "firstDieX", 0.0),
                ParamValues.GetDouble(config.Params, "firstDieY", 0.0)),
            options: new AlignmentOptions
            {
                MinScore = ParamValues.GetDouble(config.Params, "minScore", 0.8),
                MaxAngleDeg = ParamValues.GetDouble(config.Params, "maxAngleDeg", 2.0),
                MaxScaleError = ParamValues.GetDouble(config.Params, "maxScaleError", 0.02),
                MaxResidualPx = ParamValues.GetDouble(config.Params, "maxResidualPx", 3.0),
                SolveScale = ParamValues.GetBool(config.Params, "solveScale", false),
            });

    /// <summary>표식의 설계 자리 — die(0,0) 을 (0,0) 으로 본 좌표. 실행기도 창을 자를 때 이걸 읽는다.</summary>
    public static IReadOnlyList<PointF> Expected(NodeConfig config)
        => [.. ParamValues.GetArray(config.Params, "expected").Where(IsPair).Select(e => new PointF(e[0].GetDouble(), e[1].GetDouble()))];

    /// <summary>대략 원점 — 실행기가 창을 자를 때도 같은 값을 쓴다.</summary>
    public static PointF Prior(NodeConfig config)
        => new(ParamValues.GetDouble(config.Params, "firstDieX", 0.0), ParamValues.GetDouble(config.Params, "firstDieY", 0.0));

    public IEnumerable<string> ValidateParams(NodeConfig config)
    {
        int index = 0;
        int valid = 0;

        foreach (JsonElement item in ParamValues.GetArray(config.Params, "expected"))
        {
            if (IsPair(item))
            {
                valid++;
            }
            else
            {
                yield return $"expected[{index}] 는 [x, y] 숫자 짝이어야 합니다";
            }

            index++;
        }

        // ★ 하나로는 밀린 양만 알고 돌아간 각도를 모른다.
        if (valid < 2)
        {
            yield return $"expected 에 표식 자리가 {valid}개입니다 — 2개 이상 적으세요 (예: [[-40,-40],[410,310]])";
        }
    }

    private static bool IsPair(JsonElement e)
        => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2
           && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number;
}

/// <summary>InspectDies 블록에 적힌 설정 — 실행기가 die 반복을 하려고 읽는다.</summary>
public sealed record InspectDiesSettings
{
    /// <summary>E1 die 레시피 경로 — 웨이퍼 레시피 파일 기준 상대 경로일 수 있다(푸는 것은 실행기).</summary>
    public required string E1Recipe { get; init; }

    /// <summary>E0 die 레시피 경로. 없으면 null.</summary>
    public string? E0Recipe { get; init; }

    /// <summary>격자 · 존 · 원판 · 수동 지정. 원점은 정렬이 없을 때만 여기 값을 쓴다.</summary>
    public required WaferSpec Spec { get; init; }

    public NeighborStrategy Neighbors { get; init; } = NeighborStrategy.SameRow1;

    public int GoldenWanted { get; init; } = 3;

    /// <summary>표식 조각 경로들. 정렬을 이었으면 하나 이상.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>표식 찾는 범위(px). 0 이면 null — 사진 전체.</summary>
    public int? SearchPadding { get; init; }
}

/// <summary>
/// die 전부 검사(경계 블록) 설명.
///
/// ★ 격자는 <b>이 블록 한 곳</b>에 적는다(교수님은 자세 계산 블록에도 미리보기용 격자를 적었다).
/// ★ <c>edgeExclusionPx</c> 는 <b>원판 가장자리 여유(px)</b> — 교수님 문서(WaferJob)의 뜻이다.
///   교수님 코드는 이 값을 '테두리 한 겹' 스위치로 썼는데, 그건 우리 <c>edgeRings</c> 다.
/// ★ <c>circle</c> · <c>zones</c> · <c>features</c> 는 묶음 · 목록이라 서술자로 못 적는다 — 팩토리가 직접 검사한다.
/// </summary>
public sealed class InspectDiesNodeFactory : INodeFactory, ICustomParamsValidator
{
    public string TypeName => "InspectDies";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("InspectDies", "웨이퍼",
        inputs: [new PortDescriptor("alignment", "놓인 자세 — 안 이으면 반듯하게 놓였다고 본다", PortKind.Alignment, Optional: true)],
        outputs:
        [
            new PortDescriptor("defects", "die 별 결함을 모은 것(웨이퍼 좌표 전)", PortKind.WaferDefectList),
            new PortDescriptor("grid", "die 원점 격자 + 존", PortKind.DieGrid),
            new PortDescriptor("alignment", "놓인 자세(받은 그대로 넘긴다)", PortKind.Alignment),
        ],
        @params:
        [
            new ParamDescriptor("e1Recipe", typeof(string), Default: "", IsPath: true,
                Description: "E1(온전한 die) 레시피 경로 — 필수. 이 파일 기준 상대 경로"),
            new ParamDescriptor("e0Recipe", typeof(string), Default: "", IsPath: true,
                Description: "E0(가장자리 die) 레시피 경로. 원판이면 원판 밖을 가리는 것(die.dark.masked)"),
            new ParamDescriptor("cols", typeof(int), Default: 1, Min: 1, Description: "격자 열 수"),
            new ParamDescriptor("rows", typeof(int), Default: 1, Min: 1, Description: "격자 행 수"),
            new ParamDescriptor("pitchX", typeof(int), Default: 100, Min: 1, Description: "die 원점 사이 가로 간격(px)"),
            new ParamDescriptor("pitchY", typeof(int), Default: 100, Min: 1, Description: "die 원점 사이 세로 간격(px)"),
            new ParamDescriptor("dieWidth", typeof(int), Default: 0, Min: 0, Description: "die 폭(px). 0 = 피치"),
            new ParamDescriptor("dieHeight", typeof(int), Default: 0, Min: 0, Description: "die 높이(px). 0 = 피치"),
            new ParamDescriptor("originX", typeof(int), Default: 0,
                Description: "die(0,0) 자리 — 정렬을 안 이을 때만. 정렬하면 자세 계산 블록의 firstDieX 를 쓴다"),
            new ParamDescriptor("originY", typeof(int), Default: 0, Description: "die(0,0) 자리 y — 정렬을 안 이을 때만"),
            new ParamDescriptor("edgeRings", typeof(int), Default: 0, Min: 0,
                Description: "격자 바깥 몇 겹을 E0 로 볼까. 원판(circle)과 같이 못 쓴다"),
            new ParamDescriptor("edgeExclusionPx", typeof(double), Default: 0.0, Min: 0.0,
                Description: "원판 가장자리에서 이만큼 안쪽까지는 안 본다(px). 원판(circle)을 줄 때만"),
            new ParamDescriptor("neighbors", typeof(string), Default: "same-row1", Choices: ["same-row1", "nearest"],
                Description: "골든 이웃 고르는 법 — 레시피의 합치는 법과 짝이다"),
            new ParamDescriptor("goldens", typeof(int), Default: 3, Min: 1, Description: "nearest 일 때 최대 장수"),
            new ParamDescriptor("searchPaddingPx", typeof(int), Default: 0, Min: 0,
                Description: "표식 찾는 범위 — 있을 법한 자리 ± 이만큼만 읽는다. 0 = 사진 전체"),
        ]);

    public IPipelineNode Create(NodeConfig config) => new InspectDiesNode(config.Id);

    /// <summary>블록에 적힌 설정을 읽는다. 검사(<see cref="ValidateParams"/>)를 통과한 설정이라고 본다.</summary>
    public static InspectDiesSettings Read(NodeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        JsonElement p = config.Params;

        string e0 = ParamValues.GetString(p, "e0Recipe", "");
        int padding = ParamValues.GetInt(p, "searchPaddingPx", 0);

        return new InspectDiesSettings
        {
            E1Recipe = ParamValues.GetString(p, "e1Recipe", ""),
            E0Recipe = string.IsNullOrWhiteSpace(e0) ? null : e0,
            Spec = new WaferSpec
            {
                Cols = ParamValues.GetInt(p, "cols", 1),
                Rows = ParamValues.GetInt(p, "rows", 1),
                PitchX = ParamValues.GetInt(p, "pitchX", 100),
                PitchY = ParamValues.GetInt(p, "pitchY", 100),
                DieWidth = ParamValues.GetInt(p, "dieWidth", 0),
                DieHeight = ParamValues.GetInt(p, "dieHeight", 0),
                OriginX = ParamValues.GetInt(p, "originX", 0),
                OriginY = ParamValues.GetInt(p, "originY", 0),
                EdgeRings = ParamValues.GetInt(p, "edgeRings", 0),
                EdgeExclusion = ParamValues.GetDouble(p, "edgeExclusionPx", 0.0),
                Circle = Circle(p),
                ZoneOverrides = [.. Zones(p).Where(z => z.Error is null).Select(z => z.Value!)],
            },
            Neighbors = ParamValues.GetString(p, "neighbors", "same-row1") == "nearest"
                ? NeighborStrategy.Nearest
                : NeighborStrategy.SameRow1,
            GoldenWanted = ParamValues.GetInt(p, "goldens", 3),
            Features = [.. Features(p).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!)],
            SearchPadding = padding > 0 ? padding : null,
        };
    }

    public IEnumerable<string> ValidateParams(NodeConfig config)
    {
        JsonElement p = config.Params;

        if (string.IsNullOrWhiteSpace(ParamValues.GetString(p, "e1Recipe", "")))
        {
            yield return "e1Recipe 가 비었습니다 — E1 die 레시피 경로를 적으세요";
        }

        // 원판 — 없거나(null) 숫자 셋짜리 묶음
        if (ParamValues.Has(p, "circle") && p.GetProperty("circle").ValueKind != JsonValueKind.Null)
        {
            JsonElement c = p.GetProperty("circle");

            if (c.ValueKind != JsonValueKind.Object
                || !Number(c, "centerX") || !Number(c, "centerY") || !Number(c, "radius"))
            {
                yield return "circle 은 null 이거나 { \"centerX\": …, \"centerY\": …, \"radius\": … } 숫자 셋이어야 합니다 — 사진 좌표";
            }
            else if (!(c.GetProperty("radius").GetDouble() > 0))
            {
                yield return "circle.radius 는 양수여야 합니다";
            }
        }

        foreach ((_, string? error) in Zones(p))
        {
            if (error is not null)
            {
                yield return error;
            }
        }

        int index = 0;

        foreach (JsonElement f in ParamValues.GetArray(p, "features"))
        {
            if (f.ValueKind != JsonValueKind.Object
                || !f.TryGetProperty("template", out JsonElement t) || t.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(t.GetString()))
            {
                yield return $"features[{index}] 는 {{ \"template\": \"조각 경로\" }} 여야 합니다";
            }

            index++;
        }

        // ★ 정렬을 이었는데 찾을 조각이 없으면 실행기가 아무것도 못 찾는다 — 실행 전에 막는다.
        bool aligned = config.Inputs.ContainsKey("alignment");

        if (aligned && !Features(p).Any(f => !string.IsNullOrWhiteSpace(f)))
        {
            yield return "alignment 를 이었는데 features 에 표식 조각이 없습니다 — { \"template\": \"…\" } 를 적으세요";
        }

        // ★ 정렬하면 대략 원점은 자세 계산 블록(firstDieX/Y) 한 곳 — 여기 원점까지 적으면 두 곳이 된다.
        if (aligned && (ParamValues.GetInt(p, "originX", 0) != 0 || ParamValues.GetInt(p, "originY", 0) != 0))
        {
            yield return "alignment 를 이었으면 originX/Y 는 0 이어야 합니다 — 대략 원점은 SolveAlignment 의 firstDieX/Y 한 곳에 적습니다";
        }
    }

    private static bool Number(JsonElement o, string name)
        => o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number;

    private static WaferCircle? Circle(JsonElement p)
    {
        if (!ParamValues.Has(p, "circle"))
        {
            return null;
        }

        JsonElement c = p.GetProperty("circle");

        return c.ValueKind == JsonValueKind.Object && Number(c, "centerX") && Number(c, "centerY") && Number(c, "radius")
            ? new WaferCircle(c.GetProperty("centerX").GetDouble(), c.GetProperty("centerY").GetDouble(), c.GetProperty("radius").GetDouble())
            : null;
    }

    private static IEnumerable<string?> Features(JsonElement p)
        => ParamValues.GetArray(p, "features")
            .Select(f => f.ValueKind == JsonValueKind.Object && f.TryGetProperty("template", out JsonElement t)
                         && t.ValueKind == JsonValueKind.String ? t.GetString() : null);

    /// <summary>수동 지정 — <c>{ "col": 7, "row": 15, "zone": "off" }</c>. 존 이름은 명령행 <c>--zone</c> 과 같다(E1 · E0 · off).</summary>
    private static IEnumerable<(ZoneOverride? Value, string? Error)> Zones(JsonElement p)
    {
        int index = 0;

        foreach (JsonElement z in ParamValues.GetArray(p, "zones"))
        {
            WaferZone? zone = z.ValueKind == JsonValueKind.Object && z.TryGetProperty("zone", out JsonElement name)
                              && name.ValueKind == JsonValueKind.String
                ? name.GetString()!.Trim().ToLowerInvariant() switch
                {
                    "e1" => WaferZone.E1,
                    "e0" => WaferZone.E0,
                    "off" => WaferZone.Outside,
                    _ => null,
                }
                : null;

            bool cell = z.ValueKind == JsonValueKind.Object
                        && z.TryGetProperty("col", out JsonElement col) && col.ValueKind == JsonValueKind.Number && col.TryGetInt32(out _)
                        && z.TryGetProperty("row", out JsonElement row) && row.ValueKind == JsonValueKind.Number && row.TryGetInt32(out _);

            yield return zone is { } known && cell
                ? (new ZoneOverride(z.GetProperty("col").GetInt32(), z.GetProperty("row").GetInt32(), known), null)
                : (null, $"zones[{index}] 는 {{ \"col\": 정수, \"row\": 정수, \"zone\": \"E1|E0|off\" }} 여야 합니다");

            index++;
        }
    }
}

/// <summary>웨이퍼 좌표 블록 설명 — 파라미터 없음.</summary>
public sealed class TransformAbsNodeFactory : INodeFactory
{
    public string TypeName => "TransformAbs";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("TransformAbs", "웨이퍼",
        inputs:
        [
            new PortDescriptor("defects", "die 별 결함 목록", PortKind.WaferDefectList),
            new PortDescriptor("grid", "die 원점 격자", PortKind.DieGrid),
            new PortDescriptor("alignment", "놓인 자세", PortKind.Alignment),
        ],
        outputs: [new PortDescriptor("result", "웨이퍼 좌표가 붙은 결함 목록", PortKind.WaferDefectList)],
        @params: []);

    public IPipelineNode Create(NodeConfig config)
        => new TransformAbsNode(
            config.Id,
            defectsKey: Wire.In(config, "defects"),
            gridKey: Wire.In(config, "grid"),
            alignmentKey: Wire.In(config, "alignment"),
            outKey: Wire.Out(config, "result"));
}

/// <summary>결과 추리기 블록 설명 — 기본값은 교수님 값(BigSize · 100 · 씨 0).</summary>
public sealed class SamplingNodeFactory : INodeFactory
{
    public string TypeName => "Sampling";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Sampling", "웨이퍼",
        inputs: [new PortDescriptor("defects", "결함 목록", PortKind.WaferDefectList)],
        outputs: [new PortDescriptor("result", "추린 결함 목록", PortKind.WaferDefectList)],
        @params:
        [
            new ParamDescriptor("mode", typeof(string), Default: "BigSize", Choices: ["All", "BigSize", "Range", "Random"],
                Description: "All=추리지 않음 · BigSize=큰 순 · Range=면적대 · Random=씨 고정 무작위"),
            new ParamDescriptor("count", typeof(int), Default: 100, Min: 1, Description: "남길 개수(BigSize · Random)"),
            new ParamDescriptor("seed", typeof(int), Default: 0, Description: "Random 의 씨 — 같은 씨면 같은 결과"),
            new ParamDescriptor("minArea", typeof(int), Default: 0, Min: 0, Description: "Range 면적 하한(px)"),
            new ParamDescriptor("maxArea", typeof(int), Default: 100_000_000, Min: 0, Description: "Range 면적 상한(px)"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new SamplingNode(
            config.Id,
            inKey: Wire.In(config, "defects"),
            outKey: Wire.Out(config, "result"),
            options: new SamplingOptions
            {
                Mode = ParamValues.GetString(config.Params, "mode", "BigSize") switch
                {
                    "All" => SamplingMode.All,
                    "BigSize" => SamplingMode.BigSize,
                    "Range" => SamplingMode.Range,
                    "Random" => SamplingMode.Random,
                    string other => throw new ArgumentException($"node '{config.Id}'(Sampling): 알 수 없는 mode '{other}'"),
                },
                Count = ParamValues.GetInt(config.Params, "count", 100),
                Seed = ParamValues.GetInt(config.Params, "seed", 0),
                MinArea = ParamValues.GetInt(config.Params, "minArea", 0),
                MaxArea = ParamValues.GetInt(config.Params, "maxArea", 100_000_000),
            });
}