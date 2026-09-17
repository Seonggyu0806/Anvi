using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>골든 다듬기. 브리핑 흐름의 2단계.</summary>
public sealed class NormalizeNode : IPipelineNode
{
    private readonly string _srcKey;
    private readonly string _outKey;
    private readonly NormalizeMethod _method;
    private readonly int _kernelSize;
    private readonly double _sigma;

    public NormalizeNode(
        string id, string srcKey, string outKey, NormalizeMethod method, int kernelSize, double sigma)
    {
        Id = id;
        _srcKey = srcKey;
        _outKey = outKey;
        _method = method;
        _kernelSize = kernelSize;
        _sigma = sigma;
    }

    public string Id { get; }

    public string TypeName => "Normalize";

    public void Execute(IPipelineContext context)
    {
        Mat golden = context.Get<Mat>(_srcKey);

        Mat normalized = Normalizers.Normalize(golden, _method, _kernelSize, _sigma);

        context.Set(_outKey, normalized);
    }
}