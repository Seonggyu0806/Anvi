using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>볼 곳만 남기고 지운다 — 검사 영역 한정(교수님 Phase 4 · <c>MaskApply</c>).</summary>
public sealed class MaskApplyNode : IPipelineNode
{
    private readonly string _srcKey;
    private readonly string _maskKey;
    private readonly string _outKey;
    private readonly MaskApplyOptions _options;

    public MaskApplyNode(string id, string srcKey, string maskKey, string outKey, MaskApplyOptions options)
    {
        Id = id;
        _srcKey = srcKey;
        _maskKey = maskKey;
        _outKey = outKey;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "MaskApply";

    public void Execute(IPipelineContext context)
    {
        Mat src = context.Get<Mat>(_srcKey);
        Mat mask = context.Get<Mat>(_maskKey);

        Mat masked = MaskAppliers.Apply(src, mask, _options);

        context.Set(_outKey, masked);
    }
}