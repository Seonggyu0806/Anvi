using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>
/// 연결요소 라벨링 — 붙어 있는 흰 픽셀 덩어리를 결함 하나로 묶는다. 브리핑 흐름의 7단계.
///
/// ★ 파이프라인에서 <b>이미지가 아닌 것</b>을 칠판에 올리는 첫 노드다.
/// </summary>
public sealed class LabelNode : IPipelineNode
{
    private readonly string _maskKey;
    private readonly string? _intensityKey;
    private readonly string _outKey;
    private readonly LabelOptions _options;

    public LabelNode(string id, string maskKey, string? intensityKey, string outKey, LabelOptions options)
    {
        Id = id;
        _maskKey = maskKey;
        _intensityKey = intensityKey;
        _outKey = outKey;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "Label";

    public void Execute(IPipelineContext context)
    {
        Mat mask = context.Get<Mat>(_maskKey);

        // ★ intensity는 <b>선택</b>이다. 안 이어져 있으면 밝기 통계 없이 좌표만 낸다.
        Mat? intensity = _intensityKey is null ? null : context.Get<Mat>(_intensityKey);

        DefectList defects = Labelers.Label(mask, intensity, _options);

        context.Set(_outKey, defects);
    }
}