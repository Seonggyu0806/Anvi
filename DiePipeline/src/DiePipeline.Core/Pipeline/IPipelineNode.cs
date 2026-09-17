namespace DiePipeline.Core.Pipeline;

/// <summary>
/// 파이프라인을 구성하는 처리 단위(=블록).
/// 입력 키를 읽어 출력 키에 쓴다 — 그게 전부다.
///
/// ★ 이 인터페이스에 <b>없는 것들</b>을 보라:
///   - 다음 노드가 누구인지 모른다 (그래서 순서를 JSON이 정할 수 있다)
///   - 반환값이 없다 (결과는 칠판에 놓는다)
///   - 파라미터가 없다 (생성자에서 이미 받아 필드에 갖고 있다)
/// </summary>
public interface IPipelineNode
{
    /// <summary>레시피 안에서의 고유 식별자. 오류 메시지·시간 측정에 쓴다.</summary>
    string Id { get; }

    /// <summary>레지스트리 등록명(예: "Blur"). JSON의 "type"과 같은 문자열.</summary>
    string TypeName { get; }

    void Execute(IPipelineContext context);
}