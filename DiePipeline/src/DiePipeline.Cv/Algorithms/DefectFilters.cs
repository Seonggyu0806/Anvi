using DiePipeline.Core.Domain;

namespace DiePipeline.Cv.Algorithms;

/// <summary>
/// 찾은 결함 목록을 <b>다듬는다</b>. 파이프라인의 꼬리.
///
/// ★ 여기서부터는 <b>이미지를 안 본다.</b> Label이 뽑아 놓은 숫자만 가지고 거른다.
///   그래서 빠르고, 레시피에서 값만 바꿔가며 여러 번 시험해 보기 좋다.
/// </summary>
public interface IDefectFilter
{
    DefectList Filter(DefectList input);
}

/// <summary>
/// 결함을 <b>모양</b>으로 거른다 — 종횡비(긴변/짧은변)와 채움비(면적/bbox)로
/// 스크래치(가늘고 긴 것) ↔ 입자(둥근 것)를 갈라낸다.
///
/// ★ bbox와 면적만 쓴다. 윤곽이나 모멘트를 안 구하므로 <b>가볍고 결정적</b>이다.
///   원형도는 Label이 계산해 뒀을 때만 쓴다(null이면 그 조건은 건너뛴다).
/// </summary>
public sealed class ShapeDefectFilter : IDefectFilter
{
    private readonly double _minAspect;
    private readonly double _maxAspect;
    private readonly double _minExtent;
    private readonly double _maxExtent;
    private readonly double _minCircularity;
    private readonly double _maxCircularity;

    public ShapeDefectFilter(
        double minAspect,
        double maxAspect,
        double minExtent,
        double maxExtent,
        double minCircularity = 0.0,
        double maxCircularity = 1.0)
    {
        _minAspect = minAspect;
        _maxAspect = maxAspect;
        _minExtent = minExtent;
        _maxExtent = maxExtent;
        _minCircularity = minCircularity;
        _maxCircularity = maxCircularity;
    }

    public DefectList Filter(DefectList input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<Defect> kept = [];

        foreach (Defect defect in input.Items)
        {
            int width = defect.Bounds.Width;
            int height = defect.Bounds.Height;

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            // 종횡비 — 1이면 정사각(둥근 것), 크면 가늘고 길다. 언제나 1 이상이다.
            double aspect = (double)Math.Max(width, height) / Math.Min(width, height);

            // 채움비 — bbox를 얼마나 채웠나. 0~1. 선형 결함은 작고, 꽉 찬 덩어리는 크다.
            double extent = (double)defect.Area / (width * height);

            if (aspect < _minAspect || aspect > _maxAspect || extent < _minExtent || extent > _maxExtent)
            {
                continue;
            }

            // ★ 원형도가 없으면(Label의 computeShape를 안 켰으면) 이 조건은 없는 셈 친다.
            //   "모르는 것"을 "불합격"으로 처리하면 조용히 전부 사라진다.
            if (defect.Circularity is { } circularity &&
                (circularity < _minCircularity || circularity > _maxCircularity))
            {
                continue;
            }

            kept.Add(defect);
        }

        // ★ 번호를 다시 안 매긴다 — 거르기 전 목록과 <b>같은 결함인지</b> 추적할 수 있어야 한다.
        return input with { Items = kept };
    }
}

/// <summary>
/// 결함 <b>크기·개수</b>를 제한한다 — 면적으로 거른 뒤
/// <b>면적 내림차순</b>(같으면 Id 오름차순)으로 상위 몇 개만 남긴다.
///
/// ★ Label의 minArea와 자리가 다르다. 거기는 <b>덩어리를 만들 때</b>,
///   여기는 <b>다 만들고 모은 뒤</b>다. 웨이퍼 층에서 die 수십 개의 결과를 합치면
///   결함이 수백 개가 되는데, 그때 "심각한 것부터 N개"로 줄이는 자리가 여기다.
/// </summary>
public sealed class TopLimitDefectFilter : IDefectFilter
{
    private readonly int _minArea;
    private readonly int _maxArea;
    private readonly int _maxCount;

    public TopLimitDefectFilter(int minArea, int maxArea, int maxCount)
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _maxCount = maxCount;
    }

    public DefectList Filter(DefectList input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<Defect> kept = input.Items
            .Where(d => d.Area >= _minArea && d.Area <= _maxArea)
            .OrderByDescending(d => d.Area)

            // ★ 면적이 같을 때 Id로 한 번 더 정렬한다. 안 그러면 순서가 들쭉날쭉해져
            //   "같은 입력인데 결과 파일이 다른" 일이 생긴다.
            .ThenBy(d => d.Id)
            .Take(_maxCount)
            .ToList();

        return input with { Items = kept };
    }
}
