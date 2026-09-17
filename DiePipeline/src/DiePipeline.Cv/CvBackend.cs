using DiePipeline.Core.Registry;

namespace DiePipeline.Cv;

public static class CvBackend
{
    public static NodeRegistry CreateRegistry()
    {
        NodeRegistry registry = new();
        Register(registry);
        return registry;
    }

    public static void Register(NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new CombineNodeFactory());
        registry.Register(new NormalizeNodeFactory());
        registry.Register(new AlignNodeFactory());
        registry.Register(new DifferenceNodeFactory());
        registry.Register(new FilterNodeFactory());
        registry.Register(new BinarizeNodeFactory());
        registry.Register(new LabelNodeFactory());

        // 꼬리 3종 — 찾은 결함을 다듬는다.
        registry.Register(new DefectFilterNodeFactory());
        registry.Register(new DefectClassifyNodeFactory());
        registry.Register(new DefectLimitNodeFactory());
    }
}
