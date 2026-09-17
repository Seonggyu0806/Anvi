using System.Text.Json;
using DiePipeline.Core.Configuration;
using DiePipeline.Cv.Algorithms;

namespace DiePipeline.Cv.Stages;

/// <summary>한 필터 스텝 타입 — 타입별 파라미터 메타(검증용) + 필터를 만드는 법.</summary>
internal sealed record FilterStep(
    string Type,
    IReadOnlyList<ParamDescriptor> Params,
    Func<JsonElement, IImageFilter> Build);

/// <summary>바깥(검증·UI)에 보여줄 스텝 메타. 만드는 법은 빼고 이름과 파라미터만.</summary>
public sealed record FilterStepInfo(string Type, IReadOnlyList<ParamDescriptor> Params);

/// <summary>
/// Filter <c>filters[]</c> 스텝 타입의 <b>단일 진실원</b>.
///
/// ★ 새 필터 타입을 넣는다 = <b>여기 한 항목</b>. 만들기·검증·UI가 전부 이 목록만 본다.
///   목록이 두 군데로 갈라지면 "검증은 통과했는데 만들 때 터지는" 일이 생긴다.
/// </summary>
public static class FilterStepCatalog
{
    internal static readonly IReadOnlyList<FilterStep> Steps =
    [
        new("Median",
            [Kernel(3, 5, "중앙값 커널(3 또는 5 — 32F 제약)")],
            s => new MedianFilter(K(s))),

        new("Gaussian",
            [
                Kernel(1, 31, "가우시안 커널(홀수)"),
                new ParamDescriptor("sigma", typeof(double), Default: 0.0, Min: 0.0),
            ],
            s => new GaussianFilter(K(s), ParamValues.GetDouble(s, "sigma", 0))),

        new("Morphology",
            [
                new ParamDescriptor("op", typeof(string), Default: "Open",
                    Choices: ["Open", "Close", "Erode", "Dilate", "TopHat", "BlackHat"],
                    Description: "모폴로지 연산"),
                Kernel(1, 31, "구조요소 커널(홀수)"),
            ],
            s => new MorphologyFilter(
                Enum.Parse<MorphologyFilter.Op>(ParamValues.GetString(s, "op", "Open"), ignoreCase: true), K(s))),

        new("Sobel",
            [Kernel(1, 31, "에지 커널(홀수) — ⚠ 출력 [0,1] 초과 가능")],
            s => new SobelFilter(K(s))),

        new("Laplacian",
            [Kernel(1, 31, "라플라시안 커널(홀수) — ⚠ 출력 [0,1] 초과 가능")],
            s => new LaplacianFilter(K(s))),

        new("Bilateral",
            [
                new ParamDescriptor("d", typeof(int), Default: 5, Min: 1, Description: "지름(px)"),
                new ParamDescriptor("sigmaColor", typeof(double), Default: 0.1, Min: 0.0,
                    Description: "밝기 시그마([0,1] 스케일)"),
                new ParamDescriptor("sigmaSpace", typeof(double), Default: 5.0, Min: 0.0,
                    Description: "공간 시그마(px)"),
            ],
            s => new BilateralFilter(
                ParamValues.GetInt(s, "d", 5),
                ParamValues.GetDouble(s, "sigmaColor", 0.1),
                ParamValues.GetDouble(s, "sigmaSpace", 5.0))),
    ];

    /// <summary>바깥에 보여줄 목록. 나중에 WPF의 필터 편집기가 이걸로 폼을 그린다.</summary>
    public static IReadOnlyList<FilterStepInfo> Catalog { get; } =
        Steps.Select(s => new FilterStepInfo(s.Type, s.Params)).ToArray();

    internal static FilterStep? Find(string type) => Steps.FirstOrDefault(s => s.Type == type);

    private static int K(JsonElement step) => ParamValues.GetInt(step, "kernel", 3);

    private static ParamDescriptor Kernel(int min, int max, string description)
        => new("kernel", typeof(int), Default: 3, Min: min, Max: max,
            Constraint: ParamConstraint.OddOnly, Description: description);
}