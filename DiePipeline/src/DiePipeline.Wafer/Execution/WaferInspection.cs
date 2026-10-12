using System.Diagnostics;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Algorithms;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Domain;
using OpenCvSharp;

namespace DiePipeline.Wafer.Execution;

/// <summary>표식으로 웨이퍼가 어떻게 놓였는지 풀라는 주문.</summary>
public sealed record MarkAlignment
{
    /// <summary>
    /// 찾을 표식 조각(GrayF32). ★ <b>부르는 쪽이 갖고 있다가 놓는다</b> — 여기서 Dispose 하지 않는다.
    /// 같은 표식으로 웨이퍼 여러 장을 돌릴 수 있게.
    /// </summary>
    public required Mat Template { get; init; }

    /// <summary>
    /// 표식들이 <b>설계상</b> 있어야 할 자리 — die(0,0) 을 (0,0) 으로 본 좌표. 2개 이상.
    /// ★ 사진 좌표가 아니다. 사진 좌표는 웨이퍼가 어떻게 놓였느냐에 따라 매번 달라진다.
    /// </summary>
    public required IReadOnlyList<PointF> Expected { get; init; }

    public AlignmentOptions Options { get; init; } = new();

    /// <summary>
    /// 표식을 찾을 창의 여유(px). <c>null</c> 이면 <b>사진 전체</b>를 뒤진다 — 작은 사진은 이게 제일 단순하다.
    ///
    /// ★ 값을 주면 표식마다 <b>있을 법한 자리 ± 여유</b>만 읽는다. 있을 법한 자리 = 대략 원점
    ///   (<see cref="WaferSpec"/> 의 원점 — 교수님 <c>firstDieX/Y</c>) + <see cref="Expected"/>.
    ///   원판 원본은 통째로 읽으면 작업 포맷으로 수 GB 라 5주차에 정렬을 못 썼다.
    /// ★ 창이 넓다고 die 번호가 안전해지지는 않는다. 표식이 die 마다 반복되면(십자) 대략 원점이
    ///   <b>피치의 절반</b>보다 틀릴 때 옆 표식에 붙어 die 번호가 통째로 밀린다 — 교수님 문서의 같은 한계.
    /// </summary>
    public int? SearchPadding { get; init; }
}

/// <summary>웨이퍼 한 장을 검사하라는 주문 — 명령행 옵션이든 화면 입력이든 결국 이 모양이 된다.</summary>
public sealed record WaferInspectionRequest
{
    /// <summary>E1(안쪽) die 레시피. 필수.</summary>
    public required Recipe E1 { get; init; }

    /// <summary>E0(가장자리) die 레시피. 없으면 E0 die 가 있을 때 그 die 는 실패로 남는다.</summary>
    public Recipe? E0 { get; init; }

    public required WaferSpec Spec { get; init; }

    /// <summary><c>null</c> 이면 정렬을 안 한다 — 반듯하게 놓였다고 보고 <see cref="WaferSpec"/>의 원점을 쓴다.</summary>
    public MarkAlignment? Alignment { get; init; }

    public NeighborStrategy Strategy { get; init; } = NeighborStrategy.SameRow1;

    public int GoldenWanted { get; init; } = 3;

    /// <summary>기본은 <see cref="SamplingMode.All"/> — 안 시키면 아무것도 안 덜어낸다.</summary>
    public SamplingOptions Sampling { get; init; } = new();
}

/// <summary>검사를 시작하지 않고 멈춘 까닭.</summary>
public enum WaferStop
{
    None = 0,

    /// <summary>정렬 결과를 믿을 수 없다(점수·각도·배율·어긋남 중 하나가 걸렸다).</summary>
    AlignmentRejected = 1,

    /// <summary>정렬은 풀렸지만 기울기가 커서 die 를 펴서 읽어야 하는데, 그 기능(warp read)이 없다.</summary>
    RotationNeedsWarp = 2,
}

/// <summary>
/// 웨이퍼 한 장의 검사 결과 — <b>멈췄으면 왜 멈췄는지까지</b>.
///
/// ★ 출력은 하지 않는다. 명령행은 글자로, 화면은 맵으로 — 같은 결과를 각자 보여 준다.
/// </summary>
public sealed record WaferInspectionResult
{
    /// <summary>레시피가 골든을 몇 장 전제하는가 — 레시피에서 읽어낸 값.</summary>
    public required GoldenContract.Requirement Need { get; init; }

    /// <summary>사진에서 찾은 표식 후보. 정렬을 안 했으면 null.</summary>
    public MatchList? Marks { get; init; }

    /// <summary>정렬을 푼 결과(받아들였든 거부했든). 정렬을 안 했으면 null.</summary>
    public AlignmentResult? Solved { get; init; }

    /// <summary>실제로 쓴 정렬. 정렬을 안 했으면 항등.</summary>
    public required WaferAlignment Alignment { get; init; }

    public WaferStop Stop { get; init; }

    /// <summary>멈춘 까닭 — <b>사람이 읽는 한 줄</b>. 명령행과 화면이 같은 문장을 띄운다.</summary>
    public string? StopReason { get; init; }

    public bool Stopped => Stop != WaferStop.None;

    // ── 아래는 멈추지 않았을 때만 있다 ──

    public DieGrid? Grid { get; init; }

    public WaferRunResult? Run { get; init; }

    /// <summary>샘플링 <b>전</b> — 실제로 찾아낸 전부. 판정(합격·불합격)은 이걸로 한다.</summary>
    public WaferDefectList? Found { get; init; }

    /// <summary>샘플링 <b>후</b> — 보여 줄 것.</summary>
    public WaferDefectList? Defects { get; init; }

    /// <summary>die 검사에 걸린 시간(정렬·모으기 빼고).</summary>
    public double InspectMs { get; init; }

    /// <summary>
    /// 격자가 사진의 일부만 덮을 때 — 사람이 읽는 한 줄. 다 덮으면 <c>null</c>.
    /// ★ 검사는 멈추지 않는다(일부러 일부만 볼 수도 있다). 대신 결과보다 먼저 보여야 한다.
    /// </summary>
    public string? CoverageWarning { get; init; }

    /// <summary>
    /// 원판인데 E0 레시피가 <c>waferMask</c> 를 안 받을 때 — 사람이 읽는 한 줄. 아니면 <c>null</c>.
    /// ★ 검사는 멈추지 않는다. 걸친 die 의 원판 밖 배경이 결함으로 잡힐 수 있다는 <b>짝 경고</b>다.
    /// </summary>
    public string? EdgeWarning { get; init; }
}

/// <summary>
/// 웨이퍼 한 장을 처음부터 끝까지 — 정렬 → 격자 → die 마다 레시피 → 모으기 → 좌표 → 샘플링.
///
/// <para>★ 왜 여기 있나: 이 흐름이 명령행(<c>WaferCommand</c>) 안에 갇혀 있었다. 화면이 두 번째로
/// 쓰려고 하자 "베끼거나 꺼내거나" 가 됐다. 베끼면 <b>정렬을 못 믿을 때 멈추는 가드</b>가
/// 한쪽에만 남는 날이 온다 — 그러면 화면에서 "안 봤는데 깨끗해 보이는" 웨이퍼가 생긴다.
/// 교수님도 같은 자리에서 Wafer 가 OpenCv 를 참조하게 바꿨다(Phase 8 · 8.4b).</para>
///
/// <para>★ <see cref="WaferRunner"/>는 그대로 <b>검사 함수를 받는다.</b> 이 클래스가 그 함수를
/// 진짜 주방(사진 읽기 + 레시피 실행)으로 만들어 넣는 자리다. 그래서 격자·이웃·실패 기록은
/// 여전히 사진 없이 테스트된다 — 4주차에 얻은 것은 잃지 않는다.</para>
/// </summary>
public static class WaferInspection
{
    /// <summary>명령행 · 화면 입구 — 표식 조각 하나로 찾고, 대략 원점은 격자 원점(<see cref="WaferSpec.OriginX"/>)이다.</summary>
    public static WaferInspectionResult Run(
        WaferInspectionRequest request, IImageSource source, NodeRegistry registry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);

        Aligner? aligner = request.Alignment is { } wanted
            ? new Aligner(wanted.SearchPadding, () => AlignByTemplate(source, wanted, new PointF(request.Spec.OriginX, request.Spec.OriginY)))
            : null;

        return Run(request, source, registry, aligner, ct);
    }

    /// <summary>
    /// 정렬하는 법 — <b>입구마다 다르고 그 뒤는 같다.</b> 명령행 · 화면은 조각 하나로 찾고(<see cref="AlignByTemplate"/>),
    /// 웨이퍼 레시피는 블록 토막을 엔진으로 돌린다(<see cref="WaferRecipeRunner"/>).
    /// </summary>
    /// <param name="SearchPadding">창의 여유 — 거부 문장에 "창에서만 찾았다" 를 붙이려고 같이 든다.</param>
    /// <param name="Solve">표식 후보와 풀린 자세를 낸다.</param>
    internal sealed record Aligner(int? SearchPadding, Func<(MatchList Marks, AlignmentResult Solved)> Solve);

    /// <summary>
    /// ★ 두 입구가 같이 쓰는 속 — 레시피 검사 → 정렬 → 가드 → 격자 · 경고 → die 반복 → 모으기 · 좌표 → 샘플링.
    ///   입구가 둘이어도 <b>정렬을 못 믿을 때 멈추는 가드는 여기 한 곳</b>에만 있다.
    /// </summary>
    /// <param name="aligner"><c>null</c> 이면 정렬을 안 한다 — 반듯하게 놓였다고 보고 격자 원점을 쓴다.</param>
    internal static WaferInspectionResult Run(
        WaferInspectionRequest request, IImageSource source, NodeRegistry registry, Aligner? aligner, CancellationToken ct)
    {
        // ★ 돌리기 전에 통째로 검사한다. die 30개를 돈 뒤에 "레시피가 틀렸다" 를 알면 30번 헛돈 것이다.
        RecipeValidator.EnsureValid(request.E1, registry);

        if (request.E0 is not null)
        {
            RecipeValidator.EnsureValid(request.E0, registry);
        }

        // ★ 레시피는 die 마다 다시 만들지 않고 한 번만 만든다.
        //   노드는 파라미터만 들고 결과는 칠판에 놓는다 — 상태가 없으니 N die 에 그대로 재사용된다.
        IReadOnlyList<IPipelineNode> e1Nodes = registry.CreateAll(request.E1);
        IReadOnlyList<IPipelineNode>? e0Nodes = request.E0 is null ? null : registry.CreateAll(request.E0);

        // ★ 레시피가 골든 몇 장을 전제하는지 읽어낸다 — 이웃 고르는 법과 합치는 법은 짝이다.
        GoldenContract.Requirement need = GoldenContract.Of(request.E1);

        // ★ 정렬이 먼저다. 웨이퍼가 어떻게 놓였는지 모르면 격자를 어디에 놓을지 알 수 없다.
        WaferAlignment alignment = WaferAlignment.Identity;
        MatchList? marks = null;
        AlignmentResult? solved = null;

        if (aligner is not null)
        {
            (marks, solved) = aligner.Solve();

            // ★★ 못 믿겠으면 <b>검사를 시작조차 하지 않는다.</b>
            //   정렬이 틀리면 웨이퍼 전체가 밀린 자리를 검사하고, 결과는 "오검 천 개" 로만 보인다.
            if (!solved.Accepted)
            {
                string window = aligner.SearchPadding is { } padding
                    ? $" (표식마다 있을 법한 자리 ±{padding}px 창에서만 찾았습니다 — 대략 원점이나 창의 여유를 확인하세요)"
                    : string.Empty;

                return new WaferInspectionResult
                {
                    Need = need,
                    Marks = marks,
                    Solved = solved,
                    Alignment = solved.Alignment,
                    Stop = WaferStop.AlignmentRejected,
                    StopReason = $"정렬을 믿을 수 없어 검사를 하지 않았습니다 — {solved.Rejection}{window}",
                };
            }

            alignment = solved.Alignment;

            // ★ die 를 펴서 읽는 warp read 가 아직 없다. 여기서 미리 막지 않으면
            //   die 를 전부 검사한 뒤 마지막 좌표 단계에서 터진다 — 그만큼이 헛일이다.
            if (Math.Abs(alignment.RotationDeg) > AbsoluteTransform.MaxRotationDegWithoutWarp)
            {
                return new WaferInspectionResult
                {
                    Need = need,
                    Marks = marks,
                    Solved = solved,
                    Alignment = alignment,
                    Stop = WaferStop.RotationNeedsWarp,
                    StopReason = $"정렬은 풀렸지만({alignment.RotationDeg:F3}°) 검사를 하지 않았습니다 — "
                                 + $"die 를 펴서 읽는 warp read 가 없어 {AbsoluteTransform.MaxRotationDegWithoutWarp}° 까지만 "
                                 + "좌표를 믿을 수 있습니다. 기울기가 더 작은 원본을 쓰거나 warp read 를 붙이세요",
                };
            }
        }

        // ★ 정렬했으면 대략 원점은 이미 정렬(die(0,0) 자리) 안에 들어갔다 — 격자에 또 더하면 두 번 밀린다.
        WaferSpec spec = aligner is null ? request.Spec : request.Spec with { OriginX = 0, OriginY = 0 };
        DieGrid grid = DieGridBuilder.Build(spec, alignment);
        string? coverage = Coverage(grid, source.Width, source.Height);

        // ★ 원판 마스크는 레시피가 달라고 할 때만 만든다 — 안 쓰는 재료를 die 마다 만들면 헛일이다.
        // 칸 판정(DieGridBuilder)과 같은 원 — 가장자리 여유만큼 줄인 것.
        WaferCircle? circle = request.Spec.InspectedCircle;
        bool e1Mask = request.E1.ContextInputs.Contains(ReservedContext.WaferMask);
        bool e0Mask = request.E0 is not null && request.E0.ContextInputs.Contains(ReservedContext.WaferMask);

        // ★ 짝 경고 — 원판에 걸친 die(E0)가 있는데 E0 레시피가 가리지 않는다.
        //   그 die 는 원판 밖 배경까지 찍혀 있다. 배경이 어두우면 어두운 결함 임계에 통째로 걸린다.
        string? edge = circle is not null && request.E0 is not null && !e0Mask
                       && grid.Dies.Any(d => d.Zone == WaferZone.E0)
            ? "원판(circle)을 줬는데 E0 레시피가 waferMask 를 안 받습니다 — 원판에 걸친 die 는 원판 밖 배경까지 "
              + "찍혀 있어, 가리지 않으면 배경이 결함으로 잡힐 수 있습니다. "
              + "E0 레시피 앞에 MaskApply(mask = waferMask, fill 1.0) 를 두세요"
            : null;

        WaferRunOptions runOptions = new()
        {
            Strategy = request.Strategy,
            GoldenWanted = request.GoldenWanted,
            ImageWidth = source.Width,
            ImageHeight = source.Height,
            MinimumGoldens = need.Minimum,
        };

        Stopwatch clock = Stopwatch.StartNew();

        WaferRunResult run = WaferRunner.Run(
            grid,
            (roi, goldenRois) => Inspect(source, e1Nodes, roi, goldenRois, e1Mask ? circle : null, e1Mask),
            e0Nodes is null ? null : roi => Inspect(source, e0Nodes, roi, [], e0Mask ? circle : null, e0Mask),
            runOptions,
            ct);

        clock.Stop();

        // 모으기 → 좌표 변환. 정렬을 안 했으면 항등이다.
        WaferDefectList found = AbsoluteTransform.Transform(DefectCollector.Collect(run.Dies), grid, alignment);

        // 샘플링은 맨 끝이다 — 검사를 덜 하는 게 아니라 다 하고 결과만 추린다.
        WaferDefectList defects = WaferSampler.Sample(found, request.Sampling);

        return new WaferInspectionResult
        {
            Need = need,
            Marks = marks,
            Solved = solved,
            Alignment = alignment,
            Grid = grid,
            Run = run,
            Found = found,
            Defects = defects,
            InspectMs = clock.Elapsed.TotalMilliseconds,
            CoverageWarning = coverage,
            EdgeWarning = edge,
        };
    }

    /// <summary>
    /// 격자 밖 어느 한쪽에 <b>die 가 한 칸 이상 더 들어갈 자리</b>가 남으면 경고 문장, 아니면 <c>null</c>.
    ///
    /// <para>★ 5주차에 화면에서 실데이터를 열고 격자를 합성용 값 그대로 둔 채 검사했다.
    /// 사진 왼쪽 위 귀퉁이만 보고 결과가 나왔다 — 안 터지고, 틀렸다고도 안 하는 고장이다.</para>
    ///
    /// <para>★ 기준을 '넓이 몇 %' 로 안 잡은 까닭 — 합성 웨이퍼는 테두리에 여백(피치의 절반쯤)이 있어
    /// 격자가 맞는데도 비율이 낮게 나온다. '한 칸이 더 들어가느냐' 는 <b>여백과 빠뜨린 줄을 가른다.</b>
    /// 행 하나만 덜 넣어도 걸린다.</para>
    /// </summary>
    private static string? Coverage(DieGrid grid, int imageWidth, int imageHeight)
    {
        double minX = grid.Dies.Min(d => d.Origin.X);
        double minY = grid.Dies.Min(d => d.Origin.Y);
        double maxX = grid.Dies.Max(d => d.Origin.X) + grid.DieWidth;
        double maxY = grid.Dies.Max(d => d.Origin.Y) + grid.DieHeight;

        List<string> room = [];
        Room(room, "왼쪽", minX, grid.Pitch.X, "열");
        Room(room, "위", minY, grid.Pitch.Y, "행");
        Room(room, "오른쪽", imageWidth - maxX, grid.Pitch.X, "열");
        Room(room, "아래", imageHeight - maxY, grid.Pitch.Y, "행");

        if (room.Count == 0)
        {
            return null;
        }

        return $"격자가 사진의 일부만 덮습니다 — 격자 {grid.Cols}×{grid.Rows} · 피치 {grid.Pitch.X:F0} 이 덮는 범위는 "
               + $"{maxX - minX:F0}×{maxY - minY:F0} 인데 사진은 {imageWidth}×{imageHeight} 입니다. "
               + $"격자 밖 {string.Join(" · ", room)} 자리가 남습니다. "
               + "일부러 일부만 보는 게 아니면 열·행·피치를 확인하세요";

        static void Room(List<string> room, string side, double slack, double pitch, string unit)
        {
            int count = (int)Math.Floor(slack / pitch);

            if (count >= 1)
            {
                room.Add($"{side}에 {count}{unit}");
            }
        }
    }

    /// <summary>
    /// 명령행 · 화면의 정렬 — 조각 하나로 표식을 찾고, 대략 원점 둘레에서 자세를 푼다.
    /// 풀이는 웨이퍼 레시피의 자세 계산 블록과 같은 함수(<see cref="AlignmentSolver.SolveAround"/>)다.
    /// </summary>
    /// <param name="prior">
    /// 대략 원점 — 정렬 전에 die(0,0) 이 있을 법한 사진 자리(교수님 firstDieX/Y). 모르면 0.
    /// 표식이 있을 법한 자리 = 대략 원점 + 설계 자리. 창도 짝짓기도 이 자리를 기준으로 한다.
    /// </param>
    private static (MatchList Marks, AlignmentResult Solved) AlignByTemplate(
        IImageSource source, MarkAlignment wanted, PointF prior)
    {
        MatchOptions options = new()
        {
            MinScore = wanted.Options.MinScore,

            // 기대 위치보다 넉넉히 찾아 두고 짝짓기에서 고른다 — 여분이 나오는 건 정상이다.
            MaxMatches = Math.Max(4, wanted.Expected.Count * 2),
        };

        MatchList marks = FindMarks(
            source, wanted.Template.Cols, wanted.Template.Rows, AlignmentSolver.Guess(wanted.Expected, prior),
            wanted.SearchPadding, scene => TemplateMatchers.FindAll(scene, wanted.Template, options));

        return (marks, AlignmentSolver.SolveAround(marks, wanted.Expected, prior, wanted.Options));
    }

    /// <summary>
    /// 사진에서 표식 후보를 찾는다.
    ///
    /// <para>★ <paramref name="searchPadding"/> 이 없으면 <b>사진 전체</b>를 뒤진다.</para>
    ///
    /// <para>★ 있으면 표식마다 <b>있을 법한 자리 ± 여유</b> 창만 원해상도로 읽는다 — 교수님 Phase5 #5.10
    /// "러너 정렬 윈도잉"(블록이 아니라 러너 조립 변경 · 전체 검색은 남겨 둔다). 교수님 문서의 앞 단계
    /// (축소본으로 자리 가설 세우기)는 안 넣었다 — 있을 법한 자리를 대략 원점으로 이미 받기 때문이다.</para>
    ///
    /// <para>★ 창을 자르고 · 사진 좌표로 옮기고 · 겹친 것을 지우는 일은 여기 한 곳이다. 창 하나에서
    /// <b>무엇으로 찾는지</b>만 입구마다 다르다(<paramref name="find"/> — 조각 맞추기 함수 / 레시피의 찾기 토막).</para>
    /// </summary>
    /// <param name="templateWidth">표식 조각 폭 — 창 크기와 '같은 표식' 판정에 쓴다.</param>
    /// <param name="templateHeight">표식 조각 높이.</param>
    /// <param name="guess">표식마다 있을 법한 사진 자리(왼쪽 위) — 대략 원점 + 설계 자리.</param>
    /// <param name="searchPadding">창의 여유(px). <c>null</c> 이면 사진 전체 한 번.</param>
    /// <param name="find">그림 한 장(창 또는 사진 전체)에서 표식 후보를 찾는다 — 그 그림 기준 좌표로.</param>
    internal static MatchList FindMarks(
        IImageSource source, int templateWidth, int templateHeight, IReadOnlyList<PointF> guess, int? searchPadding,
        Func<Mat, MatchList> find)
    {
        if (searchPadding is not { } padding)
        {
            using Mat scene = source.ReadRegion(new Roi(0, 0, source.Width, source.Height));
            return find(scene);
        }

        if (padding < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchPadding), $"표식을 찾을 창의 여유는 0 이상이어야 합니다 (현재 {padding})");
        }

        int width = templateWidth;
        int height = templateHeight;

        // ★ 창끼리 겹치면 같은 표식이 두 번 잡힌다 — 조각 짧은 변의 절반 안이면 같은 표식으로 본다.
        double same = Math.Max(1, Math.Min(width, height) / 2.0);
        List<Match> found = [];

        foreach (PointF at in guess)
        {
            // 창 = 조각이 놓일 자리 + 사방 여유. 사진 밖은 잘라 낸다.
            int left = Math.Max(0, (int)Math.Floor(at.X) - padding);
            int top = Math.Max(0, (int)Math.Floor(at.Y) - padding);
            int right = Math.Min(source.Width, (int)Math.Ceiling(at.X) + width + padding);
            int bottom = Math.Min(source.Height, (int)Math.Ceiling(at.Y) + height + padding);

            // 창이 조각보다 작으면(자리가 사진 밖) 여기선 못 찾는다 — 짝이 모자라면 정렬이 거부한다.
            if (right - left < width || bottom - top < height)
            {
                continue;
            }

            using Mat window = source.ReadRegion(new Roi(left, top, right - left, bottom - top));

            foreach (Match match in find(window).Items)
            {
                // 창 안 좌표 → 사진 좌표
                PointF location = new(match.Location.X + left, match.Location.Y + top);

                if (!found.Any(f => Math.Abs(f.Location.X - location.X) < same
                                    && Math.Abs(f.Location.Y - location.Y) < same))
                {
                    found.Add(match with { Location = location });
                }
            }
        }

        // MatchList 규약 — 점수 높은 순.
        return new MatchList { Items = [.. found.OrderByDescending(m => m.Score)] };
    }

    /// <summary>
    /// die 하나 크기의 <b>원판 안</b> 마스크 — 안 255 · 밖 0. <b>픽셀 중심</b>이 원 안이면 안이다.
    ///
    /// <para>★ 원이 없으면 전부 255 — 사각형 웨이퍼에서도 같은 E0 레시피가 그대로 돈다(가릴 게 없을 뿐).</para>
    ///
    /// <para>★ 행마다 '원 안인 x 구간' 을 계산해 한 번에 칠한다. 픽셀마다 거리를 재면 큰 die 에서 수백만 번이다.</para>
    /// </summary>
    /// <param name="roi">die 자리 — <b>사진 좌표</b>. 원도 사진 좌표라 그대로 맞댄다.</param>
    public static Mat WaferMaskOf(Roi roi, WaferCircle? circle)
    {
        Mat mask = new(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(circle is null ? 255 : 0));

        if (circle is null)
        {
            return mask;
        }

        try
        {
            for (int y = 0; y < roi.Height; y++)
            {
                double dy = roi.Y + y + 0.5 - circle.CenterY;
                double reach2 = (circle.Radius * circle.Radius) - (dy * dy);

                if (reach2 < 0)
                {
                    continue;
                }

                // 픽셀 중심 roi.X + x + 0.5 가 [중심 − reach, 중심 + reach] 안인 x
                double reach = Math.Sqrt(reach2);
                int from = Math.Max(0, (int)Math.Ceiling(circle.CenterX - reach - 0.5 - roi.X));
                int to = Math.Min(roi.Width - 1, (int)Math.Floor(circle.CenterX + reach - 0.5 - roi.X));

                if (from > to)
                {
                    continue;
                }

                // 높이 1 짜리 꽉 찬 네모 = 그 행의 [from, to] 구간. 행마다 새 Mat 을 만들지 않는다.
                Cv2.Rectangle(mask, new Rect(from, y, to - from + 1, 1), Scalar.All(255), -1);
            }

            return mask;
        }
        catch
        {
            mask.Dispose();
            throw;
        }
    }

    /// <summary>
    /// die 한 칸을 실제로 읽어 레시피를 돌린다 — <b>러너에게 넘기는 "검사 함수"</b>다.
    ///
    /// <para>★ 우리가 읽은 <see cref="Mat"/>은 우리가 놓는다. <c>SetInput</c>은 소유권을 안 가져가고,
    /// <c>ReleaseAll</c>은 노드가 칠판에 올린 것만 놓는다. 그래서 같은 원본을 다음 die 에서 또 쓸 수 있다.</para>
    /// </summary>
    /// <param name="circle">원판 — <paramref name="wantsMask"/> 일 때만 쓴다. null 이면 전부 원판 안.</param>
    /// <param name="wantsMask">레시피가 <c>waferMask</c> 를 달라고 했나.</param>
    private static DefectList Inspect(
        IImageSource source, IReadOnlyList<IPipelineNode> nodes, Roi roi, IReadOnlyList<Roi> goldenRois,
        WaferCircle? circle, bool wantsMask)
    {
        // 이 칸만 뜯어 온다. 나머지 수백 MB 는 건드리지 않는다.
        using Mat test = source.ReadRegion(roi);
        using Mat? waferMask = wantsMask ? WaferMaskOf(roi, circle) : null;

        List<Mat> goldens = new(goldenRois.Count);

        try
        {
            foreach (Roi golden in goldenRois)
            {
                goldens.Add(source.ReadRegion(golden));
            }

            PipelineContext context = new();
            context.SetInput(ReservedContext.Roi, test);
            context.SetInput(ReservedContext.ChipRegions, (IReadOnlyList<Mat>)goldens);

            if (waferMask is not null)
            {
                context.SetInput(ReservedContext.WaferMask, waferMask);
            }

            // 엔진이 끝에서 칠판을 비운다(ReleaseAll) — 한 die = 한 번 정리.
            return new PipelineEngine().Run(nodes, context).Defects;
        }
        finally
        {
            foreach (Mat golden in goldens)
            {
                golden.Dispose();
            }
        }
    }
}