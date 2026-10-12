using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Pipeline;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Processing;
using OpenCvSharp;

namespace DiePipeline.Wafer.Execution;

/// <summary>웨이퍼 레시피를 토막 낼 수 없을 때 — 블록을 잘못 이었다. 검사를 시작하기 전에 던진다.</summary>
public sealed class WaferRecipeException : Exception
{
    public WaferRecipeException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// 웨이퍼 레시피(<c>*.wafer.json</c>)를 돌린다 — 교수님 <c>WaferRunner.Run(recipe…)</c> 과 같은 자리.
///
/// <para>★ 엔진은 블록을 한 번씩만 돈다(4주차). 그런데 웨이퍼에는 <b>여러 번 돌아야 하는 곳이 둘</b> 있다 —
/// 표식 찾기는 창마다, die 검사는 die 마다. 그래서 실행기가 레시피를 <b>토막 내서</b> 토막마다 맞는 횟수로
/// 엔진에 돌리고, 결과를 다음 토막에 넘긴다.</para>
///
/// <code>
/// [TemplateMatchAll] → [SolveAlignment] → [InspectDies] → [TransformAbs] → [Sampling]
///  ① 찾기 — 창마다      ② 풀기 — 1번      ③ die 마다         ④ 후처리 — 1번
/// </code>
///
/// <para>★ 교수님과 다른 곳은 ①② 다. 교수님은 정렬 토막을 사진 전체에서 한 번 돌린다(창은 "후속").
/// 우리는 창마다 찾아야 해서 정렬 토막을 '표식 목록' 칸에서 한 번 더 자른다.</para>
///
/// <para>★ ③ 부터는 명령행 · 화면과 <b>같은 속</b>(<see cref="WaferInspection"/>)을 쓴다 — 정렬을 못 믿을 때
/// 멈추는 가드 · 격자 · 경고 · die 반복이 한 곳에만 있다. 입구만 다르다.</para>
/// </summary>
public static class WaferRecipeRunner
{
    /// <summary>
    /// 레시피 파일로 돌린다 — 안에 적힌 die 레시피 · 표식 조각 경로는 <b>이 파일이 있는 폴더 기준</b>이다(교수님 CLI 와 같다).
    /// ★ 경로 푸는 규칙은 <see cref="PathIn"/> 한 곳 — 명령행 · 화면 · 시험이 같은 규칙을 쓴다.
    /// </summary>
    public static WaferInspectionResult RunFile(
        string recipePath, IImageSource source, NodeRegistry registry, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipePath);

        return RunIn(RecipeLoader.Load(recipePath), FolderOf(recipePath), source, registry, ct);
    }

    /// <summary>
    /// 메모리에 있는 레시피로 돌린다 — 안의 경로는 <paramref name="folder"/> 기준. 웨이퍼 에디터가 쓴다.
    /// ★ 화면의 그래프는 아직 파일로 안 썼을 수 있다. 그래도 경로는 <b>그 레시피를 연 · 저장한 폴더</b> 기준이라
    ///   파일로 돌릴 때(<see cref="RunFile"/>)와 같은 파일을 읽는다.
    /// </summary>
    public static WaferInspectionResult RunIn(
        Recipe recipe, string folder, IImageSource source, NodeRegistry registry, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return Run(
            recipe, source, registry,
            path => RecipeLoader.Load(PathIn(folder, path)),
            path => ImageFile.LoadGrayF32(PathIn(folder, path)),
            ct);
    }

    /// <summary>
    /// 레시피 안에 적힌 경로 → 실제 경로. 상대 경로면 <b>레시피 파일이 있는 폴더</b> 기준, 절대 경로면 그대로.
    /// ★ 견본은 레포 안이라 상대 경로로(<c>../recipes/die.diff.json</c>), 실데이터 레시피는 레포 밖이라 절대 경로도 된다.
    /// </summary>
    public static string PathFrom(string recipePath, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipePath);

        return PathIn(FolderOf(recipePath), reference);
    }

    /// <summary>레시피 안에 적힌 경로 → 실제 경로. 상대 경로면 <paramref name="folder"/> 기준, 절대 경로면 그대로.</summary>
    public static string PathIn(string folder, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        return Path.GetFullPath(Path.Combine(folder, reference));
    }

    private static string FolderOf(string recipePath) => Path.GetDirectoryName(Path.GetFullPath(recipePath))!;

    /// <param name="loadRecipe">InspectDies 에 적힌 die 레시피 경로 → 레시피. 상대 경로를 푸는 것은 부르는 쪽이다.</param>
    /// <param name="loadTemplate">
    /// InspectDies 에 적힌 표식 조각 경로 → 조각(GrayF32). ★ 받은 <see cref="Mat"/> 은 <b>실행기가 놓는다.</b>
    /// </param>
    public static WaferInspectionResult Run(
        Recipe recipe, IImageSource source, NodeRegistry registry,
        Func<string, Recipe> loadRecipe, Func<string, Mat> loadTemplate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loadRecipe);
        ArgumentNullException.ThrowIfNull(loadTemplate);

        // ★ 돌리기 전에 다 본다 — 배선 검사(검증기) → 토막 내기(실행기의 규칙). die 를 다 돈 뒤에
        //   "끝 블록이 둘이라 결과를 못 고른다" 를 알면 그만큼이 헛일이다.
        RecipeValidator.EnsureValid(recipe, registry);
        Plan plan = Split(recipe, registry);
        InspectDiesSettings settings = InspectDiesNodeFactory.Read(plan.Inspect);

        // ★ 조각은 하나만 — 교수님도 features[0] 만 쓴다. 조용히 첫 번째만 쓰면 둘 다 쓰인다고 믿게 된다.
        if (plan.Aligned && settings.Features.Count != 1)
        {
            throw new WaferRecipeException(
                $"'{plan.Inspect.Id}'(InspectDies) 의 features 에 조각이 {settings.Features.Count}개입니다 — 지금은 1개만 씁니다");
        }

        WaferInspectionRequest request = new()
        {
            E1 = loadRecipe(settings.E1Recipe),
            E0 = settings.E0Recipe is null ? null : loadRecipe(settings.E0Recipe),
            Spec = settings.Spec,
            Strategy = settings.Neighbors,
            GoldenWanted = settings.GoldenWanted,

            // 추리기는 뒤 토막(Sampling 블록)의 일이다 — 속에서는 덜어내지 않는다.
        };

        using Mat? template = plan.Aligned ? loadTemplate(settings.Features[0]) : null;

        WaferInspection.Aligner? aligner = template is null
            ? null
            : new WaferInspection.Aligner(
                settings.SearchPadding, () => Align(recipe, plan, source, template, settings.SearchPadding, registry, ct));

        WaferInspectionResult result = WaferInspection.Run(request, source, registry, aligner, ct);

        // 멈췄으면 뒤 토막도 없다. 뒤 토막이 비었으면 속이 한 좌표 변환 그대로다(교수님 하위호환과 같다).
        return result.Stopped || plan.Downstream.Count == 0 ? result : Finish(recipe, plan, result, registry, ct);
    }

    /// <summary>레시피를 토막 낸 모양.</summary>
    /// <param name="Inspect">경계 블록(InspectDies).</param>
    /// <param name="AlignmentKey">InspectDies 의 '놓인 자세' 입력 이름표. 정렬을 안 이었으면 null.</param>
    /// <param name="Find">① 찾기 토막 — '표식 목록' 을 만드는 블록까지.</param>
    /// <param name="MatchKey">①과 ②를 잇는 '표식 목록' 이름표.</param>
    /// <param name="Solve">② 풀기 토막 — 나머지 정렬 블록.</param>
    /// <param name="SolveBlock">② 안의 자세 계산 블록 — 창을 자를 자리(설계 자리 · 대략 원점)를 여기서 읽는다.</param>
    /// <param name="Downstream">④ 후처리 토막.</param>
    /// <param name="Terminal">④의 결과 이름표 — 웨이퍼 결함 목록을 내되 아무도 안 읽는 것. ④가 비었으면 null.</param>
    private sealed record Plan(
        NodeConfig Inspect,
        string? AlignmentKey,
        IReadOnlyList<NodeConfig> Find,
        string? MatchKey,
        IReadOnlyList<NodeConfig> Solve,
        NodeConfig? SolveBlock,
        IReadOnlyList<NodeConfig> Downstream,
        string? Terminal)
    {
        public bool Aligned => AlignmentKey is not null;
    }

    /// <summary>
    /// InspectDies 에서 자른다 — 앞 = InspectDies 로 <b>흘러드는</b> 블록, 뒤 = InspectDies 에서 <b>흘러나가는</b> 블록.
    /// 앞은 '표식 목록' 칸에서 한 번 더 자른다(①②). 레시피에 적힌 순서는 토막 안에서도 그대로 지킨다.
    /// </summary>
    private static Plan Split(Recipe recipe, NodeRegistry registry)
    {
        List<NodeConfig> nodes = recipe.Pipeline.Nodes;
        NodeConfig[] boundary = [.. nodes.Where(n => n.Enabled && n.Type == "InspectDies")];

        if (boundary.Length != 1)
        {
            throw new WaferRecipeException(
                $"웨이퍼 레시피에는 InspectDies 블록이 정확히 1개 필요합니다(지금 {boundary.Length}개)");
        }

        NodeConfig inspect = boundary[0];
        HashSet<string> up = Upstream(nodes, inspect.Inputs.Values);
        HashSet<string> down = Downstream(nodes, inspect.Outputs.Values);

        // ★ 앞에도 뒤에도 안 이어진 블록 — 조용히 건너뛰면 "넣었는데 왜 안 듣지?" 가 된다.
        string[] stray = [.. nodes.Where(n => n != inspect && !up.Contains(n.Id) && !down.Contains(n.Id)).Select(n => n.Id)];

        if (stray.Length > 0)
        {
            throw new WaferRecipeException(
                $"InspectDies 앞(정렬)에도 뒤(후처리)에도 이어지지 않은 블록이 있습니다: {string.Join(", ", stray)} — 잇거나 지우세요");
        }

        List<NodeConfig> downstream = [.. nodes.Where(n => down.Contains(n.Id))];
        string? terminal = downstream.Count == 0 ? null : Terminal(nodes, downstream, registry);

        if (!inspect.Inputs.TryGetValue("alignment", out string? alignmentKey))
        {
            return new Plan(inspect, null, [], null, [], null, downstream, terminal);
        }

        List<NodeConfig> upstream = [.. nodes.Where(n => up.Contains(n.Id))];

        // ①②를 가르는 칸 — 정렬 토막 안에서 '표식 목록' 을 내는 출력. 정확히 하나여야 어디서 자를지 정해진다.
        //   ★ 지금 블록으로는 이 검사와 아래 SolveAlignment 검사에 닿기 전에 검증기가 먼저 막는다(표식 목록 ·
        //     놓인 자세를 내는 블록이 하나씩뿐이다). 블록이 늘 때 — 표식 목록을 합치는 블록 같은 — 를 위한 방어다.
        string[] matchKeys =
        [
            .. upstream
                .SelectMany(n => registry.Describe(n.Type).Outputs
                    .Where(p => p.Kind == PortKind.MatchList && n.Outputs.ContainsKey(p.Name))
                    .Select(p => n.Outputs[p.Name]))
                .Distinct(StringComparer.Ordinal),
        ];

        if (matchKeys.Length != 1)
        {
            throw new WaferRecipeException(
                $"정렬 토막에 표식 목록(MatchList)을 내는 블록이 정확히 1개 필요합니다(지금 {matchKeys.Length}개) — "
                + "TemplateMatchAll 을 SolveAlignment 앞에 두세요");
        }

        HashSet<string> findIds = Upstream(nodes, matchKeys);
        List<NodeConfig> find = [.. upstream.Where(n => findIds.Contains(n.Id))];
        List<NodeConfig> solve = [.. upstream.Where(n => !findIds.Contains(n.Id))];
        NodeConfig[] solveBlocks = [.. solve.Where(n => n.Enabled && n.Type == "SolveAlignment")];

        if (solveBlocks.Length != 1)
        {
            throw new WaferRecipeException(
                $"정렬 토막에 SolveAlignment 블록이 정확히 1개 필요합니다(지금 {solveBlocks.Length}개) — "
                + "표식을 찾을 창의 자리(expected · firstDieX/Y)를 거기서 읽습니다");
        }

        return new Plan(inspect, alignmentKey, find, matchKeys[0], solve, solveBlocks[0], downstream, terminal);
    }

    /// <summary>주어진 이름표들을 <b>만드는</b> 블록과 그 블록의 재료를 만드는 블록 … 을 거슬러 모은다.</summary>
    private static HashSet<string> Upstream(IReadOnlyList<NodeConfig> nodes, IEnumerable<string> keys)
    {
        Dictionary<string, NodeConfig> producerOf = new(StringComparer.Ordinal);

        foreach (NodeConfig node in nodes)
        {
            foreach (string key in node.Outputs.Values)
            {
                producerOf[key] = node;
            }
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        Queue<string> queue = new(keys);

        while (queue.Count > 0)
        {
            if (producerOf.TryGetValue(queue.Dequeue(), out NodeConfig? producer) && ids.Add(producer.Id))
            {
                foreach (string key in producer.Inputs.Values)
                {
                    queue.Enqueue(key);
                }
            }
        }

        return ids;
    }

    /// <summary>주어진 이름표들을 <b>읽는</b> 블록과 그 블록의 결과를 읽는 블록 … 을 따라 모은다.</summary>
    private static HashSet<string> Downstream(IReadOnlyList<NodeConfig> nodes, IEnumerable<string> keys)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        Queue<string> queue = new(keys);

        while (queue.Count > 0)
        {
            string key = queue.Dequeue();

            foreach (NodeConfig reader in nodes.Where(n => n.Inputs.Values.Contains(key, StringComparer.Ordinal)))
            {
                if (ids.Add(reader.Id))
                {
                    foreach (string next in reader.Outputs.Values)
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// 뒤 토막의 결과 — 웨이퍼 결함 목록을 내되 <b>아무 블록도 안 읽는</b> 이름표. 정확히 하나여야 한다(교수님 TerminalKey 와 같다).
    /// </summary>
    private static string Terminal(IReadOnlyList<NodeConfig> nodes, IReadOnlyList<NodeConfig> downstream, NodeRegistry registry)
    {
        HashSet<string> read = new(nodes.SelectMany(n => n.Inputs.Values), StringComparer.Ordinal);

        string[] terminals =
        [
            .. downstream
                .Where(n => n.Enabled)
                .SelectMany(n => registry.Describe(n.Type).Outputs
                    .Where(p => p.Kind == PortKind.WaferDefectList && n.Outputs.ContainsKey(p.Name))
                    .Select(p => n.Outputs[p.Name]))
                .Where(key => !read.Contains(key)),
        ];

        return terminals.Length == 1
            ? terminals[0]
            : throw new WaferRecipeException(
                $"뒤 토막의 끝(웨이퍼 결함 목록을 내고 아무도 안 읽는 칸)이 {terminals.Length}개입니다 — 정확히 1개여야 결과를 고릅니다"
                + (terminals.Length == 0 ? string.Empty : $": {string.Join(", ", terminals)}"));
    }

    /// <summary>
    /// ①② 정렬 — 창마다 찾기 토막을 돌려 사진 좌표로 모으고, 모은 목록으로 풀기 토막을 한 번 돌린다.
    /// ★ 창을 자르고 모으는 일은 명령행 · 화면과 같은 함수(<see cref="WaferInspection.FindMarks"/>)가 한다.
    /// </summary>
    private static (MatchList Marks, AlignmentResult Solved) Align(
        Recipe recipe, Plan plan, IImageSource source, Mat template, int? padding, NodeRegistry registry, CancellationToken ct)
    {
        IReadOnlyList<PointF> expected = SolveAlignmentNodeFactory.Expected(plan.SolveBlock!);
        PointF prior = SolveAlignmentNodeFactory.Prior(plan.SolveBlock!);

        // 블록은 한 번만 만든다 — 파라미터만 들고 결과는 칠판에 놓으니 창마다 그대로 다시 쓴다.
        IReadOnlyList<IPipelineNode> find = registry.CreateAll(Part(recipe, plan.Find, "find"));
        IReadOnlyList<IPipelineNode> solve = registry.CreateAll(Part(recipe, plan.Solve, "solve"));

        MatchList marks = WaferInspection.FindMarks(
            source, template.Cols, template.Rows, AlignmentSolver.Guess(expected, prior), padding,
            window => Pull<MatchList>(
                find, plan.MatchKey!, ct, (ReservedContext.AlignSrc, window), (ReservedContext.AlignTemplate, template)));

        AlignmentResult solved = Pull<AlignmentResult>(solve, plan.AlignmentKey!, ct, (plan.MatchKey!, marks));

        return (marks, solved);
    }

    /// <summary>④ 후처리 — InspectDies 가 내놓는 셋(결함 · 격자 · 놓인 자세)을 칠판에 올리고 뒤 토막을 한 번 돌린다.</summary>
    private static WaferInspectionResult Finish(
        Recipe recipe, Plan plan, WaferInspectionResult result, NodeRegistry registry, CancellationToken ct)
    {
        List<(string Key, object Value)> given = [];

        Wired("defects", DefectCollector.Collect(result.Run!.Dies));
        Wired("grid", result.Grid!);

        // 정렬을 안 이었으면 '반듯하게 놓임' — 웨이퍼 좌표 블록이 항등으로 옮긴다.
        Wired("alignment", result.Solved ?? AlignmentResult.Unaligned);

        WaferDefectList defects = Pull<WaferDefectList>(
            registry.CreateAll(Part(recipe, plan.Downstream, "process")), plan.Terminal!, ct, [.. given]);

        // Found(추리기 전 · 판정용)는 속이 낸 그대로, Defects(보여 줄 것)는 뒤 토막의 결과.
        return result with { Defects = defects };

        void Wired(string port, object value)
        {
            if (plan.Inspect.Outputs.TryGetValue(port, out string? key))
            {
                given.Add((key, value));
            }
        }
    }

    /// <summary>토막 하나를 레시피 모양으로 — 블록을 만드는 규칙(꺼진 블록의 별칭)을 그대로 쓰려고.</summary>
    private static Recipe Part(Recipe recipe, IReadOnlyList<NodeConfig> nodes, string suffix)
        => new()
        {
            RecipeId = $"{recipe.RecipeId}.{suffix}",
            ContextInputs = recipe.ContextInputs,
            Pipeline = new PipelineSection { Nodes = [.. nodes] },
        };

    /// <summary>
    /// 토막 하나를 엔진으로 돌리고, <b>칠판이 비워지기 전에</b> 결과 하나를 꺼낸다.
    /// ★ 넣는 재료는 <c>SetInput</c> — 칠판이 놓지 않는다. 창 · 조각은 부르는 쪽이 놓는다.
    /// </summary>
    private static T Pull<T>(
        IReadOnlyList<IPipelineNode> nodes, string key, CancellationToken ct, params (string Key, object Value)[] inputs)
        where T : class
    {
        PipelineContext context = new(ct);

        foreach ((string name, object value) in inputs)
        {
            context.SetInput(name, value);
        }

        T? taken = null;
        new PipelineEngine().Run(nodes, context, board => taken = board.Get<T>(key));

        return taken!;
    }
}