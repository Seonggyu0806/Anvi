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

        // 영역 — 검사할 곳만 남긴다(교수님 Phase 4 · 검사영역 한정).
        registry.Register(new MaskApplyNodeFactory());

        registry.Register(new BinarizeNodeFactory());
        registry.Register(new LabelNodeFactory());

        // 꼬리 3종 — 찾은 결함을 다듬는다.
        registry.Register(new DefectFilterNodeFactory());
        registry.Register(new DefectClassifyNodeFactory());
        registry.Register(new DefectLimitNodeFactory());

        // 정렬 — 웨이퍼 레시피의 표식 찾기(교수님과 같이 공통 명부에 둔다).
        //   die 탭 팔레트는 웨이퍼 종류(표식 목록 · 정렬 · 격자 · 웨이퍼 결함)를 주고받는 블록을 걸러 낸다.
        registry.Register(new TemplateMatchAllNodeFactory());
    }
}
