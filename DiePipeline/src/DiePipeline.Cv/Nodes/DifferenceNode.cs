using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>검사 이미지에서 골든 빼기. 브리핑 흐름의 4단계.</summary>
public sealed class DifferenceNode : IPipelineNode
{
    private readonly string _testKey;
    private readonly string _goldenKey;
    private readonly string _outKey;
    private readonly DifferenceOptions _options;

    public DifferenceNode(string id, string testKey, string goldenKey, string outKey, DifferenceOptions options)
    {
        Id = id;
        _testKey = testKey;
        _goldenKey = goldenKey;
        _outKey = outKey;
        _options = options;
    }

    public string Id { get; }

    public string TypeName => "Difference";

    public void Execute(IPipelineContext context)
    {
        Mat test = context.Get<Mat>(_testKey);
        Mat golden = context.Get<Mat>(_goldenKey);

        Mat diff = Differencers.Difference(test, golden, _options);

        context.Set(_outKey, diff);
    }
}