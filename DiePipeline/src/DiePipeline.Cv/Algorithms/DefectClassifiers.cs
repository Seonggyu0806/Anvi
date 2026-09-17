using DiePipeline.Core.Domain;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 결함마다 <b>유형 이름</b>을 붙인다. 개수는 그대로 두고 <see cref="Defect.ClassLabel"/>만 채운다.
///
/// ★ 거르는 게 아니라 <b>이름표를 다는</b> 것이다. 사람이 결과를 볼 때
///   "긁힘 3개, 입자 12개"로 읽히면 원인을 훨씬 빨리 짚는다.
/// </summary>
public interface IDefectClassifier
{
    DefectList Classify(DefectList input);
}

/// <summary>
/// 모양(원형도)과 크기(면적)로 유형을 정하는 <b>규칙 기반</b> 분류기.
///
/// <code>
///   원형도 &lt; 기준            → "Scratch"    가늘고 긴 것 = 긁힘
///   그 외 면적 ≥ 기준         → "Blob"       크고 둥근 것
///   그 외                     → "Particle"   작고 둥근 것 = 입자
/// </code>
///
/// ★ 학습이 아니라 <b>규칙</b>이다. 같은 입력이면 항상 같은 답이 나오고,
///   왜 그렇게 분류됐는지 숫자로 설명할 수 있다. 검사 장비에서는 이게 중요하다.
/// </summary>
public sealed class RuleBasedDefectClassifier : IDefectClassifier
{
    private readonly double _circularityThreshold;
    private readonly double _areaThreshold;

    public RuleBasedDefectClassifier(double circularityThreshold, double areaThreshold)
    {
        _circularityThreshold = circularityThreshold;
        _areaThreshold = areaThreshold;
    }

    public DefectList Classify(DefectList input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<Defect> classified = new(input.Items.Count);

        foreach (Defect defect in input.Items)
        {
            // ★ 원형도를 안 재 뒀으면(Label의 computeShape가 꺼져 있으면) 둥근 것으로 본다.
            //   "모른다"를 "긁힘"으로 몰면, 켜는 걸 깜빡했을 때 전부 Scratch가 된다.
            double circularity = defect.Circularity ?? 1.0;

            string label = circularity < _circularityThreshold
                ? "Scratch"
                : defect.Area >= _areaThreshold
                    ? "Blob"
                    : "Particle";

            // record라 'with'로 한 칸만 바꾼 새 Defect를 만든다. 원래 것은 안 건드린다.
            classified.Add(defect with { ClassLabel = label });
        }

        return input with { Items = classified };
    }
}
