using DiePipeline.Core.Pipeline;
using DiePipeline.Cv.Algorithms;
using OpenCvSharp;

namespace DiePipeline.Cv.Nodes;

/// <summary>
/// 필터를 <b>순서대로</b> 건다. 브리핑 흐름의 5단계.
///
/// ★ 방법 하나가 아니라 목록인 이유 — 전처리는 대개 겹쳐 쓴다.
///   "중앙값으로 점 잡티를 지우고 → 열림으로 가는 줄무늬를 끊고" 처럼.
/// </summary>
public sealed class FilterNode : IPipelineNode
{
    private readonly IReadOnlyList<IImageFilter> _filters;
    private readonly string _srcKey;
    private readonly string _outKey;

    public FilterNode(string id, IReadOnlyList<IImageFilter> filters, string srcKey, string outKey)
    {
        Id = id;
        _filters = filters;
        _srcKey = srcKey;
        _outKey = outKey;
    }

    public string Id { get; }

    public string TypeName => "Filter";

    public void Execute(IPipelineContext context)
    {
        Mat source = context.Get<Mat>(_srcKey);

        // ★ 빈 목록이면 통과 — 그래도 복사본을 낸다.
        //   원본을 그대로 올리면 칠판이 같은 Mat을 두 이름표로 들고 있다가 두 번 놓아버린다.
        if (_filters.Count == 0)
        {
            context.Set(_outKey, source.Clone());
            return;
        }

        Mat current = source;

        try
        {
            foreach (IImageFilter filter in _filters)
            {
                Mat next = filter.Apply(current);

                // ★ 중간 결과는 여기서 놓는다. 칠판에 안 올라가니 아무도 안 치워 준다.
                //   첫 바퀴의 current는 칠판의 원본이라 건드리면 안 된다.
                if (!ReferenceEquals(current, source))
                {
                    current.Dispose();
                }

                current = next;
            }
        }
        catch
        {
            if (!ReferenceEquals(current, source))
            {
                current.Dispose();
            }

            throw;
        }

        context.Set(_outKey, current);
    }
}