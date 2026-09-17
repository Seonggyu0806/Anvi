using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>정상 die들 → 골든. 브리핑 흐름의 1단계.</summary>
public sealed class CombineNode : IPipelineNode
{
    private readonly string _regionsKey;
    private readonly string _outKey;
    private readonly CombineStrategy _strategy;

    public CombineNode(string id, string regionsKey, string outKey, CombineStrategy strategy)
    {
        Id = id;
        _regionsKey = regionsKey;
        _outKey = outKey;
        _strategy = strategy;
    }

    public string Id { get; }

    public string TypeName => "Combine";

    public void Execute(IPipelineContext context)
    {
        IReadOnlyList<Mat> regions = context.Get<IReadOnlyList<Mat>>(_regionsKey);

        // ★ using을 붙이지 않는다. 이 골든은 다음 노드(Normalize)의 입력이다.
        //   using이면 Execute를 나가는 순간 죽어서 다음 노드가 해제된 메모리를 읽는다.
        //   Set으로 넘기는 순간 소유권이 칠판으로 이전되고, 실행이 끝날 때 일괄 반납된다.
        Mat golden = CombineStrategies.Combine(regions, _strategy);

        context.Set(_outKey, golden);
    }
}