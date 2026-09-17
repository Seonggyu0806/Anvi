namespace DiePipeline.Core.Pipeline;

/// <summary>
/// 실행기. 노드를 적힌 순서대로 돌린다 — 그게 전부다.
///
/// ★ 이 클래스가 모르는 것: OpenCV, 블러, 이진화, JSON, WPF, 결함이 무엇인지.
///   앞으로 노드를 몇 개를 더 만들어도 이 파일은 바뀌지 않는다.
///   그것이 "새 알고리즘 = 클래스 1개 + 등록 1줄, 코어 불변"의 실체다.
/// </summary>
public sealed class PipelineEngine
{
    public PipelineResult Run(IReadOnlyList<IPipelineNode> nodes, PipelineContext context)
    {
        try
        {
            foreach (IPipelineNode node in nodes)
            {
                // 취소는 노드 경계에서만 본다 — 노드 하나는 원자적으로 끝나게 둔다.
                context.Cancellation.ThrowIfCancellationRequested();

                node.Execute(context);
            }

            // ★ C# 문법 요점: return의 '값'이 먼저 평가되고, 그다음 finally가 실행된다.
            //   그래서 여기서 결과를 뽑은 뒤에 아래 ReleaseAll이 돈다 — 순서가 정확히 맞는다.
            return PipelineResult.From(context);
        }
        finally
        {
            // 예외·취소로 빠져나가도 반드시 반납된다. 누수 0.
            context.ReleaseAll();
        }
    }
}