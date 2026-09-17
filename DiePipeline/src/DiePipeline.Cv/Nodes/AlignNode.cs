using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>골든 되밀기. 브리핑 흐름의 3단계.</summary>
public sealed class AlignNode : IPipelineNode
{
    private readonly string _srcKey;
    private readonly string _referenceKey;
    private readonly string _outKey;
    private readonly AlignMethod _method;
    private readonly double _minResponse;
    private readonly double _maxShiftPx;

    public AlignNode(
        string id,
        string srcKey,
        string referenceKey,
        string outKey,
        AlignMethod method,
        double minResponse,
        double maxShiftPx)
    {
        Id = id;
        _srcKey = srcKey;
        _referenceKey = referenceKey;
        _outKey = outKey;
        _method = method;
        _minResponse = minResponse;
        _maxShiftPx = maxShiftPx;
    }

    public string Id { get; }

    public string TypeName => "Align";

    public void Execute(IPipelineContext context)
    {
        Mat src = context.Get<Mat>(_srcKey);
        Mat reference = context.Get<Mat>(_referenceKey);

        Mat aligned = Aligners.Align(src, reference, _method, _minResponse, _maxShiftPx);

        context.Set(_outKey, aligned);
    }
}