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
    /// <param name="inspect">
    /// 반납 직전에 칠판을 <b>한 번 들여다볼</b> 기회. 화면의 '중간 결과 보기'가 쓴다.
    ///
    /// ★ 왜 여기여야 하나: 중간 이미지는 아래 <c>ReleaseAll</c>에서 전부 죽는다.
    ///   <c>Run</c>이 돌아온 뒤에 꺼내려 하면 이미 없다.
    ///   그래서 <b>보려는 쪽이 안으로 들어오게</b> 한다 — 밖으로 내보내 주는 게 아니라.
    ///
    /// ★ 이 틈이 없으면 보려는 쪽이 이 루프를 복사해 간다. 그러면 취소 검사 같은 것이
    ///   한쪽에만 생겨 <b>둘이 조용히 달라진다.</b> 베껴 가지 말라고 열어 둔 구멍이다.
    ///
    /// 예외·취소로 빠져나갈 때는 부르지 않는다 — 반쯤 찬 칠판을 결과처럼 보여 주면 안 된다.
    /// </param>
    public PipelineResult Run(
        IReadOnlyList<IPipelineNode> nodes, PipelineContext context, Action<IPipelineContext>? inspect = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            foreach (IPipelineNode node in nodes)
            {
                // 취소는 노드 경계에서만 본다 — 노드 하나는 원자적으로 끝나게 둔다.
                context.Cancellation.ThrowIfCancellationRequested();

                node.Execute(context);
            }

            inspect?.Invoke(context);

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