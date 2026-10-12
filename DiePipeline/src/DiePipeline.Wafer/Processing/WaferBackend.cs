using DiePipeline.Core.Registry;
using DiePipeline.Cv;

namespace DiePipeline.Wafer.Processing;

/// <summary>
/// 웨이퍼 레시피의 명부 — Cv 블록 전부 + 웨이퍼 블록 넷(교수님 <c>WaferBackend</c> 와 같은 모양).
///
/// ★ die 레시피는 지금처럼 <see cref="CvBackend"/> 명부만 쓴다. 웨이퍼 블록은 die 탭 팔레트에 섞이지 않는다.
/// ★ 표식 찾기(<c>TemplateMatchAll</c>)는 Cv 블록이라 Cv 명부에 있다 — die 탭 팔레트는 웨이퍼 종류를 주고받는 블록을 걸러 낸다.
/// </summary>
public static class WaferBackend
{
    public static NodeRegistry CreateRegistry()
    {
        NodeRegistry registry = new();
        CvBackend.Register(registry);
        Register(registry);
        return registry;
    }

    public static void Register(NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new SolveAlignmentNodeFactory());
        registry.Register(new InspectDiesNodeFactory());   // 경계 블록 — 실행기가 여기서 die 반복을 한다
        registry.Register(new TransformAbsNodeFactory());
        registry.Register(new SamplingNodeFactory());
    }
}