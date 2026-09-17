using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>결함이냐 아니냐 가르기. 브리핑 흐름의 6단계.</summary>
public sealed class BinarizeNode : IPipelineNode
{
    private readonly IBinarizer _binarizer;
    private readonly string _srcKey;
    private readonly string _outKey;

    public BinarizeNode(string id, IBinarizer binarizer, string srcKey, string outKey)
    {
        Id = id;
        _binarizer = binarizer;
        _srcKey = srcKey;
        _outKey = outKey;
    }

    public string Id { get; }

    public string TypeName => "Binarize";

    public void Execute(IPipelineContext context)
    {
        Mat src = context.Get<Mat>(_srcKey);

        Mat mask = _binarizer.Binarize(src);

        context.Set(_outKey, mask);
    }
}