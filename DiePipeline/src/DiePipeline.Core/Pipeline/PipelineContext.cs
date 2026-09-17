using System.Diagnostics.CodeAnalysis;

namespace DiePipeline.Core.Pipeline;

/// <summary>칠판의 기본 구현. 산출물 보관 + 소유권 관리.</summary>
public sealed class PipelineContext : IPipelineContext
{
    private readonly Dictionary<string, object> _artifacts = new(StringComparer.Ordinal);

    // 칠판이 놓아줄 책임이 있는 것들. 호출자가 넣은 입력은 여기 안 들어간다.
    private readonly List<IDisposable> _owned = [];

    public PipelineContext(CancellationToken cancellation = default) => Cancellation = cancellation;

    public CancellationToken Cancellation { get; }

    /// <summary>
    /// 호출자가 넣는 입력(예: "roi"). ★ 소유권은 호출자에게 남는다 — 여기서 Dispose하지 않는다.
    ///
    /// 왜 Set과 나눴나: 원본 이미지를 파이프라인이 놓아버리면,
    /// 호출자가 다음 die를 검사하려고 같은 이미지를 다시 쓸 때 이미 죽어 있다.
    /// </summary>
    public void SetInput(string key, object value) => _artifacts[key] = value;

    public void Set(string key, object value)
    {
        // ★ 중복 키를 조용히 덮어쓰지 않는다.
        //   레시피에서 두 노드가 같은 출력 키를 쓰면 하나가 소리 없이 사라지고,
        //   증상은 "결함이 왜 이렇게 적지?"로만 나타나 추적이 매우 어렵다. 입구에서 막는다.
        if (_artifacts.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"이름표 '{key}'에 이미 값이 있습니다 — 두 노드가 같은 출력 키를 쓰고 있습니다");
        }

        _artifacts[key] = value;

        // ★ Core는 Mat이 뭔지 모른다. 하지만 Mat은 IDisposable이다.
        //   그래서 '무엇인지 몰라도 놓아줄 수는 있다' — 이게 Core가 OpenCV 없이 메모리를 관리하는 방법.
        if (value is IDisposable disposable)
        {
            _owned.Add(disposable);
        }
    }

    public T Get<T>(string key)
    {
        if (!_artifacts.TryGetValue(key, out object? value))
        {
            // ★ 있는 키 목록을 같이 찍는다. 문자열 키의 대가가 오타인데,
            //   "'blured' 없음. 있는 것: [roi, blurred, mask]"면 1초 만에 잡힌다.
            throw new KeyNotFoundException(
                $"이름표 '{key}'가 칠판에 없습니다. 지금 있는 것: [{string.Join(", ", _artifacts.Keys)}]");
        }

        if (value is not T typed)
        {
            throw new InvalidCastException(
                $"이름표 '{key}'는 {value.GetType().Name}인데 {typeof(T).Name}으로 읽으려 했습니다");
        }

        return typed;
    }

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (_artifacts.TryGetValue(key, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 소유한 중간 버퍼를 일괄 반납한다.
    ///
    /// ★ 왜 노드마다 using을 쓰지 않나:
    ///   using을 쓰면 Blur가 만든 이미지가 Blur의 Execute를 나가는 순간 죽는다.
    ///   그런데 그건 Binarize의 입력이다. 즉 파이프라인에서는 노드 하나가 자기 산출물의 수명을 못 정한다.
    ///   → 수명은 '실행 1회' 단위다. 실행 1회 = 스코프 1개, 끝나면 일괄 반납.
    /// </summary>
    public void ReleaseAll()
    {
        // 역순 — 나중에 만든 것부터 놓는다(의존 순서의 반대).
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
    }
}