using System.Diagnostics.CodeAnalysis;

namespace DiePipeline.Core.Pipeline;

/// <summary>
/// 노드끼리 데이터를 '이름표'로 주고받는 칠판(blackboard).
///
/// ★ 트레이드오프를 정직하게: 키는 string, 값은 object다.
///   → 컴파일 타임 안전이 없다. Get&lt;Mat&gt;("blured") 오타를 컴파일러가 못 잡는다.
///   그 대가로 얻는 것: 순서·연결을 JSON이 정할 수 있다(다중 입력·분기도 자연스럽다).
///   잃은 안전은 레시피 검증기가 되사온다.
/// </summary>
public interface IPipelineContext
{
    /// <summary>
    /// 노드가 만든 산출물을 칠판에 올린다.
    /// ★ 소유권이 칠판으로 넘어간다 — IDisposable이면 실행 끝에 칠판이 놓아준다.
    /// </summary>
    void Set(string key, object value);

    /// <summary>없거나 타입이 다르면 예외. 노드는 자기 입력이 있다고 믿고 쓴다.</summary>
    T Get<T>(string key);

    /// <summary>선택적 입력용.</summary>
    bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value);

    CancellationToken Cancellation { get; }
}