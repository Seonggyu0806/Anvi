using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Algorithms;
using DiePipeline.Cv.Nodes;
using DiePipeline.Cv.Stages;

namespace DiePipeline.Cv;

internal static class Wire
{
    public static string In(NodeConfig c, string port)
        => c.Inputs.TryGetValue(port, out string? key)
            ? key
            : throw new ArgumentException($"node '{c.Id}'({c.Type}): 입력 포트 '{port}'가 연결되지 않았습니다");

    /// <summary>선택 입력 — 안 이어져 있으면 null. Label의 intensity 같은 것.</summary>
    public static string? OptIn(NodeConfig c, string port)
        => c.Inputs.TryGetValue(port, out string? key) ? key : null;

    public static string Out(NodeConfig c, string port)
        => c.Outputs.TryGetValue(port, out string? key)
            ? key
            : throw new ArgumentException($"node '{c.Id}'({c.Type}): 출력 포트 '{port}'가 연결되지 않았습니다");
}

internal static class Names
{
    public static AlignMethod Align(NodeConfig c, string value) => value switch
    {
        "None" => AlignMethod.None,
        "Translation" => AlignMethod.Translation,
        _ => throw new ArgumentException($"node '{c.Id}'({c.Type}): 알 수 없는 method '{value}'"),
    };

    public static NormalizeMethod Normalize(NodeConfig c, string value) => value switch
    {
        "None" => NormalizeMethod.None,
        "GaussianBlur" => NormalizeMethod.GaussianBlur,
        "MedianBlur" => NormalizeMethod.MedianBlur,
        _ => throw new ArgumentException($"node '{c.Id}'({c.Type}): 알 수 없는 method '{value}'"),
    };

    public static CombineStrategy Combine(NodeConfig c, string value) => value switch
    {
        "Mean" => CombineStrategy.Mean,
        "Median" => CombineStrategy.Median,
        "Min" => CombineStrategy.Min,
        "Max" => CombineStrategy.Max,
        _ => throw new ArgumentException($"node '{c.Id}'({c.Type}): 알 수 없는 strategy '{value}'"),
    };
}

public sealed class CombineNodeFactory : INodeFactory
{
    public string TypeName => "Combine";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Combine", "골든 생성",
        inputs: [new PortDescriptor("regions", "정상 die들", PortKind.ImageList)],
        outputs: [new PortDescriptor("result", "골든 이미지")],
        @params:
        [
            new ParamDescriptor("strategy", typeof(string), Default: "Median",
                Choices: ["Mean", "Median", "Min", "Max"],
                Description: "겹치는 방법. Median이 오염된 이웃에 강하다"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new CombineNode(
            config.Id,
            regionsKey: Wire.In(config, "regions"),
            outKey: Wire.Out(config, "result"),
            strategy: Names.Combine(config, ParamValues.GetString(config.Params, "strategy", "Median")));
}

public sealed class NormalizeNodeFactory : INodeFactory
{
    public string TypeName => "Normalize";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Normalize", "전처리",
        inputs: [new PortDescriptor("src", "골든 이미지")],
        outputs: [new PortDescriptor("result", "정규화된 골든")],
        @params:
        [
            new ParamDescriptor("method", typeof(string), Default: "GaussianBlur",
                Choices: ["None", "GaussianBlur", "MedianBlur"],
                Description: "골든을 다듬는 방법. 값의 기준을 바꾸는 변환은 쓰지 않는다"),
            new ParamDescriptor("kernelSize", typeof(int), Default: 5, Min: 1, Max: 31,
                Constraint: ParamConstraint.OddOnly,
                Description: "커널 크기(홀수). MedianBlur는 GrayF32에서 5까지만"),
            new ParamDescriptor("sigma", typeof(double), Default: 1.2, Min: 0.0),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new NormalizeNode(
            config.Id,
            srcKey: Wire.In(config, "src"),
            outKey: Wire.Out(config, "result"),
            method: Names.Normalize(config, ParamValues.GetString(config.Params, "method", "GaussianBlur")),
            kernelSize: ParamValues.GetInt(config.Params, "kernelSize", 5),
            sigma: ParamValues.GetDouble(config.Params, "sigma", 1.2));
}

public sealed class AlignNodeFactory : INodeFactory
{
    public string TypeName => "Align";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Align", "정합",
        inputs:
        [
            new PortDescriptor("src", "움직일 이미지 — 정규화된 골든"),
            new PortDescriptor("reference", "기준 이미지 — 검사할 die"),
        ],
        outputs: [new PortDescriptor("result", "되민 골든")],
        @params:
        [
            new ParamDescriptor("method", typeof(string), Default: "Translation",
                Choices: ["None", "Translation"],
                Description: "되미는 방법. 이웃 die끼리는 평행이동만 일어난다"),
            new ParamDescriptor("minResponse", typeof(double), Default: 0.3, Min: 0.0, Max: 1.0,
                Description: "위상상관 확신도 하한. 이보다 낮으면 안 움직인다"),
            new ParamDescriptor("maxShiftPx", typeof(double), Default: 20.0, Min: 0.0,
                Description: "믿을 수 있는 최대 이동 거리(px). 넘으면 안 움직인다"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new AlignNode(
            config.Id,
            srcKey: Wire.In(config, "src"),
            referenceKey: Wire.In(config, "reference"),
            outKey: Wire.Out(config, "result"),
            method: Names.Align(config, ParamValues.GetString(config.Params, "method", "Translation")),
            minResponse: ParamValues.GetDouble(config.Params, "minResponse", Aligners.DefaultMinResponse),
            maxShiftPx: ParamValues.GetDouble(config.Params, "maxShiftPx", Aligners.DefaultMaxShiftPx));
}

public sealed class DifferenceNodeFactory : INodeFactory
{
    public string TypeName => "Difference";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Difference", "비교",
        inputs:
        [
            new PortDescriptor("test", "검사할 die"),
            new PortDescriptor("golden", "되민 골든"),
        ],
        outputs: [new PortDescriptor("result", "차이 이미지")],
        @params:
        [
            new ParamDescriptor("windowSize", typeof(int), Default: 3, Min: 1, Max: 31,
                Constraint: ParamConstraint.OddOnly,
                Description: "골든의 근방을 몇 칸으로 볼지(홀수)"),
            new ParamDescriptor("diffPolicy", typeof(string), Default: "Absolute",
                Choices: ["Absolute", "BrightOnly", "DarkOnly", "Signed"],
                Description: "어느 쪽 차이를 남길지"),
            new ParamDescriptor("neighborMode", typeof(string), Default: "None",
                Choices: ["None", "Min", "Max", "MinMax", "Mean"],
                Description: "골든의 어디와 비교할지. MinMax면 근방 범위 밖일 때만 결함"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new DifferenceNode(
            config.Id,
            testKey: Wire.In(config, "test"),
            goldenKey: Wire.In(config, "golden"),
            outKey: Wire.Out(config, "result"),
            options: new DifferenceOptions
            {
                WindowSize = ParamValues.GetInt(config.Params, "windowSize", 3),
                DiffPolicy = Enum.Parse<DiffPolicy>(
                    ParamValues.GetString(config.Params, "diffPolicy", "Absolute"), ignoreCase: true),
                NeighborMode = Enum.Parse<NeighborMode>(
                    ParamValues.GetString(config.Params, "neighborMode", "None"), ignoreCase: true),
            });
}

/// <summary>
/// 검사 영역 한정 — 교수님 Phase 4 의 <c>MaskApply</c>. 범주도 새로 생긴다("영역").
///
/// ★ <c>fill</c> 만 우리가 더했다. 기본 0 이라 안 주면 교수님 동작과 똑같다.
/// </summary>
public sealed class MaskApplyNodeFactory : INodeFactory
{
    public string TypeName => "MaskApply";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("MaskApply", "영역",
        inputs:
        [
            new PortDescriptor("src", "가릴 이미지(GrayF32)"),
            new PortDescriptor("mask", "볼 곳 — 0 이 아니면 켜짐(Gray8)"),
        ],
        outputs: [new PortDescriptor("result", "가린 이미지")],
        @params:
        [
            new ParamDescriptor("invert", typeof(bool), Default: false,
                Description: "true 면 반대로 — 마스크가 켜진 곳을 지우고 꺼진 곳만 남긴다"),
            new ParamDescriptor("fill", typeof(double), Default: 0.0, Min: 0.0, Max: 1.0,
                Description: "지운 곳을 채울 밝기. 0 = 교수님 동작. 어두운 결함을 찾는 레시피 앞이면 1.0"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new MaskApplyNode(
            config.Id,
            srcKey: Wire.In(config, "src"),
            maskKey: Wire.In(config, "mask"),
            outKey: Wire.Out(config, "result"),
            options: new MaskApplyOptions
            {
                Invert = ParamValues.GetBool(config.Params, "invert", false),
                Fill = ParamValues.GetDouble(config.Params, "fill", 0.0),
            });
}

/// <summary>
/// 표식 찾기 블록 설명 — 웨이퍼 레시피의 첫 블록(교수님 Phase 8 · 이름 · 파라미터 이름이 같다).
///
/// ★ <c>nmsRadiusPx</c> 기본은 0 = <b>조각 짧은 변의 절반</b>(자동)이다. 교수님 코드 기본값은 8 인데,
///   교수님 문서(특징 탐색 §3)가 "템플릿 크기의 절반 이상 — 274px 템플릿이면 150~200" 이라고 적었다.
///   8 이면 큰 조각에서 한 표식 둘레의 봉우리가 여러 개로 갈라져 짝짓기가 옆 봉우리를 집을 수 있다.
/// ★ <c>subPixel</c> 은 우리 것 — 반 칸이 각도 오차가 된다(4주차 실측). 기본은 켠다.
/// </summary>
public sealed class TemplateMatchAllNodeFactory : INodeFactory
{
    public string TypeName => "TemplateMatchAll";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("TemplateMatchAll", "정렬",
        inputs:
        [
            new PortDescriptor("src", "찾을 그림 — 실행기가 표식마다 잘라 준 창(GrayF32)"),
            new PortDescriptor("template", "표식 조각(GrayF32)"),
        ],
        outputs: [new PortDescriptor("matches", "찾은 자리 목록 — 점수 높은 순", PortKind.MatchList)],
        @params:
        [
            new ParamDescriptor("method", typeof(string), Default: "CCoeffNormed",
                Choices: ["CCoeffNormed", "CCorrNormed", "SqDiffNormed"],
                Description: "닮은 정도를 재는 법. CCoeffNormed 가 밝기 차이에 제일 둔하다"),
            new ParamDescriptor("minScore", typeof(double), Default: 0.8, Min: -1.0, Max: 1.0,
                Description: "이보다 안 닮았으면 못 찾은 것으로 친다(실측 — 진짜 0.93~1.00 · 무늬 오답 0.70 언저리)"),
            new ParamDescriptor("maxMatches", typeof(int), Default: 32, Min: 1, Description: "몇 개까지 찾을까"),
            new ParamDescriptor("nmsRadiusPx", typeof(int), Default: 0, Min: 0,
                Description: "한 자리를 고른 뒤 눌러 둘 반경(px). 0 = 조각 짧은 변의 절반"),
            new ParamDescriptor("subPixel", typeof(bool), Default: true,
                Description: "소수점 자리까지 잡을까 — 두 점으로 각도를 풀 때 반 칸이 각도 오차가 된다"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new TemplateMatchAllNode(
            config.Id,
            srcKey: Wire.In(config, "src"),
            templateKey: Wire.In(config, "template"),
            outKey: Wire.Out(config, "matches"),
            options: new MatchOptions
            {
                Method = ParamValues.GetString(config.Params, "method", "CCoeffNormed") switch
                {
                    "CCoeffNormed" => MatchMethod.CCoeffNormed,
                    "CCorrNormed" => MatchMethod.CCorrNormed,
                    "SqDiffNormed" => MatchMethod.SqDiffNormed,
                    string other => throw new ArgumentException(
                        $"node '{config.Id}'(TemplateMatchAll): 알 수 없는 method '{other}'"),
                },
                MinScore = ParamValues.GetDouble(config.Params, "minScore", 0.8),
                MaxMatches = ParamValues.GetInt(config.Params, "maxMatches", 32),
                SuppressRadius = ParamValues.GetInt(config.Params, "nmsRadiusPx", 0),
                SubPixel = ParamValues.GetBool(config.Params, "subPixel", true),
            });
}

public sealed class FilterNodeFactory : INodeFactory, ICustomParamsValidator
{
    public string TypeName => "Filter";

    // ★ Params가 비어 있다 — filters[]는 <b>중첩 목록</b>이라 ParamDescriptor로 못 적는다.
    //   그래서 이 노드만 ICustomParamsValidator로 검증을 직접 맡는다.
    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Filter", "전처리",
        inputs: [new PortDescriptor("src", "다듬을 이미지")],
        outputs: [new PortDescriptor("result", "필터를 거친 이미지")],
        @params: []);

    public IPipelineNode Create(NodeConfig config)
    {
        List<IImageFilter> filters = [];

        foreach (JsonElement step in ParamValues.GetArray(config.Params, "filters"))
        {
            string type = ParamValues.GetString(step, "type", string.Empty);

            FilterStep spec = FilterStepCatalog.Find(type)
                ?? throw new ArgumentException($"node '{config.Id}'({config.Type}): 알 수 없는 필터 '{type}'");

            filters.Add(spec.Build(step));
        }

        return new FilterNode(config.Id, filters, Wire.In(config, "src"), Wire.Out(config, "result"));
    }

    // ── filters[]는 서술자로 못 적으니 여기서 검증한다.
    //    단, 규칙을 새로 짜지 않는다 — 항목별 ParamDescriptor를 카탈로그에서 가져와 ParamCheck에 넘긴다.
    public IEnumerable<string> ValidateParams(NodeConfig config)
    {
        int index = 0;

        foreach (JsonElement step in ParamValues.GetArray(config.Params, "filters"))
        {
            string where = $"filters[{index}]";
            string type = ParamValues.GetString(step, "type", string.Empty);

            FilterStep? spec = FilterStepCatalog.Find(type);

            if (spec is null)
            {
                yield return $"{where}: 알 수 없는 필터 '{type}' " +
                             $"(쓸 수 있는 것: {string.Join(", ", FilterStepCatalog.Catalog.Select(c => c.Type))})";
            }
            else
            {
                foreach (ParamDescriptor param in spec.Params)
                {
                    foreach (string message in ParamCheck.Validate(param, step))
                    {
                        yield return $"{where}: {message}";
                    }
                }
            }

            index++;
        }
    }
}

public sealed class BinarizeNodeFactory : INodeFactory
{
    public string TypeName => "Binarize";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Binarize", "판정",
        inputs: [new PortDescriptor("src", "필터를 거친 차이 이미지")],
        outputs: [new PortDescriptor("result", "결함 마스크 — Gray8(0 또는 255)")],
        @params:
        [
            new ParamDescriptor("method", typeof(string), Default: "Threshold",
                Choices: ["Threshold", "Otsu", "Range", "Sigma", "Adaptive", "Hysteresis"]),
            new ParamDescriptor("value", typeof(double), Default: 0.5, Min: 0.0, Max: 1.0,
                Description: "Threshold 임계값(작업 포맷 스케일)"),
            new ParamDescriptor("low", typeof(double), Default: 0.0, Min: 0.0, Max: 1.0,
                Description: "Range 하한(포함, 작업 포맷 스케일)"),
            new ParamDescriptor("high", typeof(double), Default: 1.0, Min: 0.0, Max: 1.0,
                Description: "Range 상한(포함, 작업 포맷 스케일)"),
            new ParamDescriptor("k", typeof(double), Default: 3.0, Min: 0.0,
                Description: "Sigma 임계 = 평균 + k·표준편차"),
            new ParamDescriptor("blockSize", typeof(int), Default: 15, Min: 3, Max: 99,
                Constraint: ParamConstraint.OddOnly, Description: "Adaptive 국부 창(홀수)"),
            new ParamDescriptor("C", typeof(double), Default: 0.05, Min: 0.0,
                Description: "Adaptive 편차 임계(작업 포맷 오프셋)"),
            new ParamDescriptor("direction", typeof(string), Default: "Bright",
                Choices: ["Bright", "Dark", "Both"], Description: "Adaptive 검출 방향"),
        ]);

    public IPipelineNode Create(NodeConfig config)
    {
        string method = ParamValues.GetString(config.Params, "method", "Threshold");

        IBinarizer binarizer = method switch
        {
            "Threshold" => new ThresholdBinarizer(ParamValues.GetDouble(config.Params, "value", 0.5)),
            "Otsu" => new OtsuBinarizer(),
            "Range" => new RangeBinarizer(
                ParamValues.GetDouble(config.Params, "low", 0.0),
                ParamValues.GetDouble(config.Params, "high", 1.0)),
            "Sigma" => new SigmaThresholdBinarizer(ParamValues.GetDouble(config.Params, "k", 3.0)),
            "Adaptive" => new AdaptiveBinarizer(
                ParamValues.GetInt(config.Params, "blockSize", 15),
                ParamValues.GetDouble(config.Params, "C", 0.05),
                Enum.Parse<AdaptiveBinarizer.Mode>(
                    ParamValues.GetString(config.Params, "direction", "Bright"), ignoreCase: true)),

            // 이중 임계: low/high를 Range와 나눠 쓴다(여기선 약·강 임계). 의미상 low ≤ high.
            "Hysteresis" => new HysteresisBinarizer(
                ParamValues.GetDouble(config.Params, "low", 0.3),
                ParamValues.GetDouble(config.Params, "high", 0.7)),

            _ => throw new ArgumentException($"node '{config.Id}'({config.Type}): 알 수 없는 method '{method}'"),
        };

        return new BinarizeNode(config.Id, binarizer, Wire.In(config, "src"), Wire.Out(config, "result"));
    }
}

public sealed class LabelNodeFactory : INodeFactory
{
    public string TypeName => "Label";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("Label", "출력",
        inputs:
        [
            new PortDescriptor("mask", "결함 마스크 — Gray8"),
            new PortDescriptor("intensity", "밝기를 잴 원본. 안 이어도 된다", Optional: true),
        ],
        outputs: [new PortDescriptor("result", "결함 목록", PortKind.DefectList)],
        @params:
        [
            new ParamDescriptor("connectivity", typeof(int), Default: 8,
                Description: "4 또는 8. 대각선으로 닿은 픽셀을 한 덩어리로 볼지"),
            new ParamDescriptor("minArea", typeof(int), Default: 1, Min: 0,
                Description: "이보다 작은 덩어리는 버린다"),
            new ParamDescriptor("maxArea", typeof(int), Default: int.MaxValue, Min: 0,
                Description: "이보다 큰 덩어리는 버린다"),
            new ParamDescriptor("computeShape", typeof(bool), Default: false,
                Description: "원형도 등 형상 값 계산(비용 있음)"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new LabelNode(
            config.Id,
            maskKey: Wire.In(config, "mask"),
            intensityKey: Wire.OptIn(config, "intensity"),
            outKey: Wire.Out(config, "result"),
            options: new LabelOptions
            {
                Connectivity = ParamValues.GetInt(config.Params, "connectivity", 8),
                MinArea = ParamValues.GetInt(config.Params, "minArea", 1),
                MaxArea = ParamValues.GetInt(config.Params, "maxArea", int.MaxValue),
                ComputeShapeFeatures = ParamValues.GetBool(config.Params, "computeShape", false),
            });
}

public sealed class DefectFilterNodeFactory : INodeFactory
{
    public string TypeName => "DefectFilter";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("DefectFilter", "출력",
        inputs: [new PortDescriptor("defects", "결함 목록", PortKind.DefectList)],
        outputs: [new PortDescriptor("result", "걸러낸 결함 목록", PortKind.DefectList)],
        @params:
        [
            new ParamDescriptor("minAspect", typeof(double), Default: 1.0, Min: 1.0,
                Description: "최소 종횡비(긴변/짧은변) — 스크래치는 크다"),
            new ParamDescriptor("maxAspect", typeof(double), Default: 1000.0, Min: 1.0,
                Description: "최대 종횡비"),
            new ParamDescriptor("minExtent", typeof(double), Default: 0.0, Min: 0.0, Max: 1.0,
                Description: "최소 채움비(면적/bbox)"),
            new ParamDescriptor("maxExtent", typeof(double), Default: 1.0, Min: 0.0, Max: 1.0,
                Description: "최대 채움비 — 선형 결함은 작다"),
            new ParamDescriptor("minCircularity", typeof(double), Default: 0.0, Min: 0.0, Max: 1.0,
                Description: "최소 원형도(Label의 computeShape 필요. 안 재 뒀으면 무시)"),
            new ParamDescriptor("maxCircularity", typeof(double), Default: 1.0, Min: 0.0, Max: 1.0,
                Description: "최대 원형도"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new DefectFilterNode(
            config.Id,
            new ShapeDefectFilter(
                ParamValues.GetDouble(config.Params, "minAspect", 1.0),
                ParamValues.GetDouble(config.Params, "maxAspect", 1000.0),
                ParamValues.GetDouble(config.Params, "minExtent", 0.0),
                ParamValues.GetDouble(config.Params, "maxExtent", 1.0),
                ParamValues.GetDouble(config.Params, "minCircularity", 0.0),
                ParamValues.GetDouble(config.Params, "maxCircularity", 1.0)),
            Wire.In(config, "defects"),
            Wire.Out(config, "result"));
}

public sealed class DefectClassifyNodeFactory : INodeFactory
{
    public string TypeName => "DefectClassify";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("DefectClassify", "출력",
        inputs: [new PortDescriptor("defects", "결함 목록", PortKind.DefectList)],
        outputs: [new PortDescriptor("result", "유형이 붙은 결함 목록", PortKind.DefectList)],
        @params:
        [
            new ParamDescriptor("circularityThreshold", typeof(double), Default: 0.6, Min: 0.0, Max: 1.0,
                Description: "이 미만이면 Scratch(선형). 원형도를 안 재 뒀으면 둥근 것으로 본다"),
            new ParamDescriptor("areaThreshold", typeof(double), Default: 100.0, Min: 0.0,
                Description: "둥근 결함이 이 면적 이상이면 Blob, 미만이면 Particle"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new DefectClassifyNode(
            config.Id,
            new RuleBasedDefectClassifier(
                ParamValues.GetDouble(config.Params, "circularityThreshold", 0.6),
                ParamValues.GetDouble(config.Params, "areaThreshold", 100.0)),
            Wire.In(config, "defects"),
            Wire.Out(config, "result"));
}

public sealed class DefectLimitNodeFactory : INodeFactory
{
    public string TypeName => "DefectLimit";

    public INodeDescriptor Descriptor { get; } = new NodeDescriptor("DefectLimit", "출력",
        inputs: [new PortDescriptor("defects", "결함 목록", PortKind.DefectList)],
        outputs: [new PortDescriptor("result", "심각한 것부터 N개", PortKind.DefectList)],
        @params:
        [
            new ParamDescriptor("minArea", typeof(int), Default: 0, Min: 0,
                Description: "면적 하한(px²). 모은 뒤 다시 거르는 자리다"),
            new ParamDescriptor("maxArea", typeof(int), Default: 100000000, Min: 0,
                Description: "면적 상한(px²)"),
            new ParamDescriptor("maxCount", typeof(int), Default: 100, Min: 1,
                Description: "남길 최대 개수 — 면적 내림차순(같으면 Id 오름차순) 상위만"),
        ]);

    public IPipelineNode Create(NodeConfig config)
        => new DefectLimitNode(
            config.Id,
            new TopLimitDefectFilter(
                ParamValues.GetInt(config.Params, "minArea", 0),
                ParamValues.GetInt(config.Params, "maxArea", 100000000),
                ParamValues.GetInt(config.Params, "maxCount", 100)),
            Wire.In(config, "defects"),
            Wire.Out(config, "result"));
}
