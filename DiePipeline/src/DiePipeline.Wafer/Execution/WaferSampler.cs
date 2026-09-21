using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>결함 목록에서 무엇만 남길까.</summary>
public enum SamplingMode
{
    /// <summary>정해지지 않음. 받으면 거부한다.</summary>
    Unknown = 0,

    /// <summary>전부 남긴다 — 샘플링을 안 하는 것과 같다.</summary>
    All = 1,

    /// <summary>면적 큰 순서로 <c>Count</c> 개.</summary>
    BigSize = 2,

    /// <summary>면적이 <c>MinArea</c>~<c>MaxArea</c> 인 것만.</summary>
    Range = 3,

    /// <summary>무작위 <c>Count</c> 개. <b>씨가 필수다.</b></summary>
    Random = 4,
}

/// <summary>
/// 샘플링 설정.
///
/// ★ 숫자 네댓 개를 따로 넘기지 않고 묶는다.
///   <c>Sample(list, 2, 100, 1, 0, 500)</c> 은 순서를 바꿔 넣어도 컴파일이 된다.
///   묶어 두면 이름이 붙어 섞일 수가 없다 — <see cref="Core.Imaging.Roi"/> 를 묶은 것과 같은 이유다.
/// </summary>
public sealed record SamplingOptions
{
    public SamplingMode Mode { get; init; } = SamplingMode.All;

    /// <summary><see cref="SamplingMode.BigSize"/>·<see cref="SamplingMode.Random"/> 에서 몇 개를 남길까.</summary>
    public int Count { get; init; } = 200;

    /// <summary>
    /// 무작위의 씨.
    ///
    /// ★ 기본값을 <b>고정</b>해 둔다. 씨가 없으면 같은 웨이퍼가 매번 다른 목록을 내고,
    ///   "어제와 다른데?" 가 프로그램 탓인지 주사위 탓인지 알 수 없게 된다.
    /// </summary>
    public int Seed { get; init; } = 1;

    public int MinArea { get; init; }

    public int MaxArea { get; init; } = int.MaxValue;
}

/// <summary>
/// 웨이퍼 결함 목록에서 <b>일부만 남긴다.</b>
///
/// <para>★ 검사를 덜 하는 게 아니다 — <b>다 검사하고 결과만 추린다.</b>
/// 실제 웨이퍼에서는 결함이 수천 개 나올 수 있는데, 그걸 전부 화면에 띄우거나
/// 파일로 남기면 하류가 감당을 못 한다.</para>
///
/// <para>★★ <b>전부 결정론이다</b> — 같은 입력이면 반드시 같은 출력이 나온다.
/// 무작위는 씨를 고정하고, 면적 정렬은 동률을 <b>행·열·번호</b>까지 끝까지 가른다.
/// 하나라도 흔들리면 "어제와 결과가 다르다" 가 프로그램 탓인지 운 탓인지 알 수 없어지고,
/// 회귀 시험이 통째로 무너진다.</para>
/// </summary>
public static class WaferSampler
{
    public static WaferDefectList Sample(WaferDefectList input, SamplingOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Mode == SamplingMode.Unknown)
        {
            throw new ArgumentException("샘플링 방식을 안 정했습니다", nameof(options));
        }

        if (options.MinArea < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "면적 하한은 0 이상이어야 합니다");
        }

        if (options.MinArea > options.MaxArea)
        {
            throw new ArgumentException(
                $"면적 범위가 뒤집혔습니다 ({options.MinArea}~{options.MaxArea}) — 아무것도 안 남습니다",
                nameof(options));
        }

        // ★ 0개를 남기는 건 거의 언제나 실수다. 조용히 빈 목록을 주면
        //   "결함이 없다" 와 구분이 안 된다 — 이 프로젝트에서 제일 위험한 모양이다.
        if (options.Mode is SamplingMode.BigSize or SamplingMode.Random && options.Count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), $"남길 개수는 1 이상이어야 합니다 (현재 {options.Count})");
        }

        IReadOnlyList<WaferDefect> items = input.Items;

        List<WaferDefect> kept = options.Mode switch
        {
            SamplingMode.All => [.. items],
            SamplingMode.Range => [.. items.Where(d =>
                d.Defect.Area >= options.MinArea && d.Defect.Area <= options.MaxArea)],
            SamplingMode.BigSize => BigSize(items, options.Count),
            SamplingMode.Random => RandomSubset(items, options.Count, options.Seed),
            _ => throw new ArgumentOutOfRangeException(nameof(options), $"모르는 방식입니다: {options.Mode}"),
        };

        return kept.Count == 0 ? WaferDefectList.Empty : new WaferDefectList { Items = kept };
    }

    /// <summary>
    /// 면적 큰 순서로 <paramref name="count"/> 개.
    ///
    /// ★ 동률을 <b>행 → 열 → 번호</b>까지 가른다. 면적만으로 정렬하면
    ///   같은 면적끼리의 순서가 실행마다 달라질 수 있다 — 그러면 "상위 5개" 가 매번 바뀐다.
    /// </summary>
    private static List<WaferDefect> BigSize(IReadOnlyList<WaferDefect> items, int count)
        => [.. items
            .OrderByDescending(d => d.Defect.Area)
            .ThenBy(d => d.DieRow)
            .ThenBy(d => d.DieCol)
            .ThenBy(d => d.Defect.Id)
            .Take(count)];

    /// <summary>
    /// 씨를 고정한 무작위 <paramref name="count"/> 개.
    ///
    /// <para>★ 고른 뒤 <b>원래 순서로 되돌린다.</b> 뽑힌 순서대로 내보내면
    /// 같은 결함 묶음인데도 줄 순서가 뒤죽박죽이라 눈으로 비교할 수가 없다.</para>
    ///
    /// <para>★ 왜 "하나씩 뽑기" 가 아니라 <b>섞은 뒤 앞에서 자르기</b> 인가 —
    /// 하나씩 뽑으면 이미 뽑은 것이 또 나올 수 있어 다시 뽑아야 하고,
    /// 몇 번 다시 뽑을지가 운에 달린다. 섞으면 <b>몇 개를 뽑든 계산량이 같다.</b></para>
    /// </summary>
    private static List<WaferDefect> RandomSubset(IReadOnlyList<WaferDefect> items, int count, int seed)
    {
        if (items.Count <= count)
        {
            return [.. items];
        }

        int[] order = [.. Enumerable.Range(0, items.Count)];
        Random random = new(seed);

        // 피셔-예이츠 — 뒤에서부터 "남은 것 중 하나" 와 맞바꾼다.
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return [.. order.Take(count).Order().Select(i => items[i])];
    }
}