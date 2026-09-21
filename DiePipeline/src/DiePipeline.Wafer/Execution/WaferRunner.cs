using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>die 하나를 검사한 결과.</summary>
public sealed record DieInspection
{
    public required DieOrigin Die { get; init; }

    /// <summary>실제로 잘라 본 자리. 이미지 밖에 걸치면 <b>잘려서</b> 설계 크기보다 작을 수 있다.</summary>
    public required Roi Roi { get; init; }

    public required DefectList Defects { get; init; }

    /// <summary>골든을 몇 장으로 만들었나. E0 는 0 이다.</summary>
    public required int GoldenCount { get; init; }

    /// <summary>검사가 터졌으면 그 까닭. <c>null</c> 이면 정상이다.</summary>
    public string? Failure { get; init; }

    public bool Failed => Failure is not null;

    /// <summary>웨이퍼 맵에 찍을 한 줄 요약.</summary>
    public DieResult Summary => new()
    {
        Col = Die.Col,
        Row = Die.Row,
        Zone = Die.Zone,
        DefectCount = Defects.Count,
    };

    public override string ToString()
        => $"die(c{Die.Col},r{Die.Row}) {Die.Zone} 골든{GoldenCount}장 결함{Defects.Count}개"
           + (Failed ? $"  {Failure}" : string.Empty);
}

/// <summary>웨이퍼 한 장을 돌린 결과.</summary>
public sealed record WaferRunResult
{
    public required IReadOnlyList<DieInspection> Dies { get; init; }

    public int DieCount => Dies.Count;

    public int DefectCount => Dies.Sum(d => d.Defects.Count);

    /// <summary>검사가 터진 die 들. <b>비어 있어야 정상이다.</b></summary>
    public IEnumerable<DieInspection> Failures => Dies.Where(d => d.Failed);
}

/// <summary>러너에게 주는 설정.</summary>
public sealed record WaferRunOptions
{
    /// <summary>이웃을 어떻게 고를까. <b>합치는 법과 짝이다</b> — 레시피와 같이 정해야 한다.</summary>
    public NeighborStrategy Strategy { get; init; } = NeighborStrategy.SameRow1;

    /// <summary><see cref="NeighborStrategy.Nearest"/> 에서만 쓴다. 골든을 몇 장으로.</summary>
    public int GoldenWanted { get; init; } = 3;

    public required int ImageWidth { get; init; }

    public required int ImageHeight { get; init; }
    /// <summary>
    /// 이 레시피가 요구하는 최소 골든 장수. <b>0 이면 안 따진다.</b>
    ///
    /// ★ <see cref="GoldenContract.Of"/> 가 레시피에서 읽어낸 값을 넣는다.
    ///   러너는 레시피를 모르므로(검사 함수만 받는다) 숫자로만 받는다.
    /// </summary>
    public int MinimumGoldens { get; init; }
}

/// <summary>
/// 웨이퍼 한 장을 돌린다 — <b>die 목록을 만들고 하나씩 검사에 넘긴다.</b>
///
/// <para>★ 왜 die 반복이 엔진 <b>밖</b>에 있나 — JSON 레시피는 "노드를 몇 개 어떤 순서로" 를 적는 파일인데,
/// die 개수는 <b>웨이퍼를 열어 봐야 안다.</b> 미리 못 적는다.
/// 레시피에 "양파를 볶는다 → 고기를 넣는다" 는 적을 수 있어도
/// "손님 수만큼 반복한다" 는 못 적는 것과 같다. 그건 홀 매니저가 한다.</para>
///
/// <para>★ 그래서 이 러너는 <b>이미지를 안 만진다.</b> 자리(<see cref="Roi"/>)만 계산해서
/// 바깥에서 받은 <b>검사 함수</b>에 넘기고, 돌아온 결함 목록을 모은다.
/// 덕분에 이 층은 OpenCV 를 모르고, <b>사진 한 장 없이 웨이퍼 전체 흐름을 테스트</b>할 수 있다.</para>
/// </summary>
public static class WaferRunner
{
    /// <param name="inspectE1">검사 ROI + 골든 ROI 들 → 결함. die-to-die 다.</param>
    /// <param name="inspectE0">검사 ROI → 결함. 골든이 없다. E0 die 가 있으면 반드시 줘야 한다.</param>
    public static WaferRunResult Run(
        DieGrid grid,
        Func<Roi, IReadOnlyList<Roi>, DefectList> inspectE1,
        Func<Roi, DefectList>? inspectE0,
        WaferRunOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(inspectE1);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ImageWidth < 1 || options.ImageHeight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), $"이미지 크기가 이상합니다 ({options.ImageWidth}×{options.ImageHeight})");
        }

        if (options.Strategy == NeighborStrategy.Unknown)
        {
            throw new ArgumentException(
                "이웃 고르기 방법을 안 정했습니다 — 합치는 법과 짝이므로 레시피와 같이 정해야 합니다",
                nameof(options));
        }

        List<DieInspection> results = new(grid.Count);

        // row-major 순서 그대로. 순서가 흔들리면 같은 웨이퍼가 다른 답을 낸다.
        foreach (DieOrigin die in grid.InRowMajorOrder())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(InspectOne(grid, die, inspectE1, inspectE0, options));
        }

        return new WaferRunResult { Dies = results };
    }

    private static DieInspection InspectOne(
        DieGrid grid,
        DieOrigin die,
        Func<Roi, IReadOnlyList<Roi>, DefectList> inspectE1,
        Func<Roi, DefectList>? inspectE0,
        WaferRunOptions options)
    {
        // ── 여기까지는 우리 계산이다. 틀리면 설정이 잘못된 것이므로 멈춘다(fail-loud).
        Roi roi = ClampToImage(grid.RoiOf(die), options.ImageWidth, options.ImageHeight);

        if (roi.Width < 1 || roi.Height < 1)
        {
            // 격자나 정렬이 크게 틀어졌다는 뜻이다. 그 die 만 기록하고 넘어간다.
            return new DieInspection
            {
                Die = die,
                Roi = roi,
                Defects = DefectList.Empty,
                GoldenCount = 0,
                Failure = "die 가 이미지 밖에 있습니다 — 격자나 정렬을 확인하세요",
            };
        }

        IReadOnlyList<Roi> goldenRois = [];

        if (die.Zone == WaferZone.E1)
        {
            IReadOnlyList<DieOrigin> neighbours =
                GoldenSelector.Pick(grid, die, options.Strategy, options.GoldenWanted);

            goldenRois = GoldenSelector.RoisOf(neighbours, roi, options.ImageWidth, options.ImageHeight);

            // ★ 이웃 고르는 법과 합치는 법은 짝이다 — 레시피가 3장을 전제하는데 2장만 주면
            //   결과가 안 터지고 그냥 틀린다. 이웃의 결함이 되비쳐 결함 수가 조용히 부푼다.
            //   그래서 검사하기 전에 멈춘다. 그 die 만 실패로 기록하고 웨이퍼는 계속 돈다.
            if (goldenRois.Count < options.MinimumGoldens)
            {
                return new DieInspection
                {
                    Die = die,
                    Roi = roi,
                    Defects = DefectList.Empty,
                    GoldenCount = goldenRois.Count,
                    Failure = $"골든이 {goldenRois.Count}장뿐입니다 — "
                              + $"이 레시피는 {options.MinimumGoldens}장이 필요합니다 "
                              + "(이웃 고르는 법을 바꾸거나, 부호로 막는 레시피를 쓰세요)",
                };
            }
        }
        else if (inspectE0 is null)
        {
            // ★ 설정 오류다. E0 die 가 있는데 그것을 검사할 방법을 안 줬다.
            //   조용히 건너뛰면 "결함 0개"로 보이지만 사실은 안 본 것이다.
            throw new InvalidOperationException(
                $"die(c{die.Col},r{die.Row})는 {die.Zone} 인데 E0 검사 함수가 없습니다 — "
                + "골든 없는 레시피(die.dark)를 같이 넘기세요");
        }

        // ── 여기서부터는 바깥에서 받은 함수다. 터져도 웨이퍼 전체를 죽이지 않는다.
        try
        {
            DefectList defects = die.Zone == WaferZone.E1
                ? inspectE1(roi, goldenRois)
                : inspectE0!(roi);

            return new DieInspection
            {
                Die = die,
                Roi = roi,
                Defects = defects ?? DefectList.Empty,
                GoldenCount = goldenRois.Count,
            };
        }
        catch (OperationCanceledException)
        {
            throw;   // 취소는 삼키면 안 된다
        }
        catch (Exception ex)
        {
            return new DieInspection
            {
                Die = die,
                Roi = roi,
                Defects = DefectList.Empty,
                GoldenCount = goldenRois.Count,
                Failure = ex.Message,
            };
        }
    }

    /// <summary>
    /// 검사 ROI 를 이미지와 <b>교집합</b>한다 — 밖으로 나간 만큼 잘라 낸다.
    ///
    /// <para>★ 골든 ROI 와 반대다. 검사 ROI 는 "이 die 가 실제로 차지한 자리" 라서
    /// 이미지 밖은 픽셀이 없으니 <b>줄이는 게 맞다.</b>
    /// 골든은 그 줄어든 크기에 <b>맞춰서</b> 만들어지므로 둘의 크기는 늘 같다.</para>
    /// </summary>
    private static Roi ClampToImage(Roi roi, int imageWidth, int imageHeight)
    {
        int x = Math.Max(0, roi.X);
        int y = Math.Max(0, roi.Y);
        int right = Math.Min(imageWidth, roi.X + roi.Width);
        int bottom = Math.Min(imageHeight, roi.Y + roi.Height);

        return new Roi(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}