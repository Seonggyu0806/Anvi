using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;

namespace DiePipeline.Cv.Nodes;

/// <summary>
/// 결함 목록을 받아 결함 목록을 내는 노드들의 공통 뼈대.
///
/// ★ 꼬리 3종은 하는 일만 다르고 배선이 똑같다 — 읽고, 시키고, 올린다.
///   그 반복을 여기 한 번만 적는다.
/// </summary>
public abstract class DefectListNode : IPipelineNode
{
    private readonly string _inKey;
    private readonly string _outKey;

    protected DefectListNode(string id, string inKey, string outKey)
    {
        Id = id;
        _inKey = inKey;
        _outKey = outKey;
    }

    public string Id { get; }

    public abstract string TypeName { get; }

    public void Execute(IPipelineContext context)
    {
        DefectList defects = context.Get<DefectList>(_inKey);

        context.Set(_outKey, Transform(defects));
    }

    /// <summary>목록을 어떻게 바꿀지는 자식이 정한다.</summary>
    protected abstract DefectList Transform(DefectList defects);
}

/// <summary>모양으로 거르기. 브리핑 꼬리 ①.</summary>
public sealed class DefectFilterNode : DefectListNode
{
    private readonly IDefectFilter _filter;

    public DefectFilterNode(string id, IDefectFilter filter, string inKey, string outKey)
        : base(id, inKey, outKey)
        => _filter = filter;

    public override string TypeName => "DefectFilter";

    protected override DefectList Transform(DefectList defects) => _filter.Filter(defects);
}

/// <summary>유형 이름 붙이기. 브리핑 꼬리 ②.</summary>
public sealed class DefectClassifyNode : DefectListNode
{
    private readonly IDefectClassifier _classifier;

    public DefectClassifyNode(string id, IDefectClassifier classifier, string inKey, string outKey)
        : base(id, inKey, outKey)
        => _classifier = classifier;

    public override string TypeName => "DefectClassify";

    protected override DefectList Transform(DefectList defects) => _classifier.Classify(defects);
}

/// <summary>크기·개수 제한. 브리핑 꼬리 ③.</summary>
public sealed class DefectLimitNode : DefectListNode
{
    private readonly IDefectFilter _filter;

    public DefectLimitNode(string id, IDefectFilter filter, string inKey, string outKey)
        : base(id, inKey, outKey)
        => _filter = filter;

    public override string TypeName => "DefectLimit";

    protected override DefectList Transform(DefectList defects) => _filter.Filter(defects);
}
