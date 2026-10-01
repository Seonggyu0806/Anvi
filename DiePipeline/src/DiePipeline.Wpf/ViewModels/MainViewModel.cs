using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;
using Microsoft.Win32;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 블록 에디터의 본체. 팔레트·속성폼·검증·불러오기/내보내기를 전부
/// <b>디스크립터와 명부 위에</b> 올린다 — 노드 타입별 UI 코드가 0줄이다.
///
/// ★ 화면(XAML)은 이 클래스의 속성에 <b>붙기만</b> 한다. 판단은 전부 여기서 한다.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] ContextInputs = [ReservedContext.Roi, ReservedContext.ChipRegions];

    // ★ x를 220부터 시작한다 — 왼쪽 열은 컨텍스트 입력 블록 자리다.
    //   40부터 깔면 컨텍스트 블록과 겹쳐서 둘 다 안 읽힌다.
    private const double CanvasLeft = 220;

    private readonly NodeRegistry _registry = CvBackend.CreateRegistry();

    private EditorGraph _graph = new();

    private NodeViewModel? _selectedNode;
    private bool _isValid;
    private string _statusText = "준비됨";
    private string? _selectedSource;
    private string? _selectedTarget;
    private string _previewSummary = "아직 안 돌렸습니다";
    private BitmapSource? _inspected;
    private double _inspectedWidth = 1;
    private double _inspectedHeight = 1;

    // ★ 한 번이라도 미리보기를 눌렀는가. 누르기 전에는 멋대로 돌리지 않는다.
    private bool _previewOn;

    public MainViewModel()
    {
        // ★ 팔레트를 손으로 안 적는다. 명부에서 그대로 펼친다.
        Palette =
        [
            .. PaletteModel.From(_registry)
                .SelectMany(c => c.Nodes.Select(d => new PaletteItemViewModel(c.Category, d.TypeName, AddNode)))
        ];

        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ConnectCommand = new RelayCommand(Connect);
        RunPreviewCommand = new RelayCommand(RunPreview);
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        NewStandardCommand = new RelayCommand(() => LoadGraph(StandardGraph()));

        LoadGraph(StandardGraph());
    }

    public IReadOnlyList<PaletteItemViewModel> Palette { get; }

    // ★ ObservableCollection — 항목을 더하고 빼면 화면이 알아서 따라온다.
    //   그냥 List 를 쓰면 추가해도 화면이 그대로다.
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<string> ValidationMessages { get; } = [];

    public ObservableCollection<string> SourceOptions { get; } = [];

    public ObservableCollection<string> TargetOptions { get; } = [];

    /// <summary>단계별 중간 이미지. 실행 순서대로 늘어선다.</summary>
    public ObservableCollection<PreviewItem> PreviewItems { get; } = [];

    /// <summary>
    /// 찾은 결함. ★ <see cref="Defect"/>를 그대로 담는다 — 화면용 포장지를 따로 안 만든다.
    /// 네모를 그릴 좌표(<c>Bounds</c>)가 이미 들어 있어서 감쌀 이유가 없다.
    /// </summary>
    public ObservableCollection<Defect> Defects { get; } = [];

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ConnectCommand { get; }

    public ICommand RunPreviewCommand { get; }

    public ICommand DuplicateSelectedCommand { get; }

    public ICommand DeleteSelectedCommand { get; }

    public ICommand NewStandardCommand { get; }

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetField(ref _selectedNode, value);
    }

    public bool IsValid
    {
        get => _isValid;
        private set => SetField(ref _isValid, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string? SelectedSource
    {
        get => _selectedSource;
        set => SetField(ref _selectedSource, value);
    }

    public string? SelectedTarget
    {
        get => _selectedTarget;
        set => SetField(ref _selectedTarget, value);
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        private set => SetField(ref _previewSummary, value);
    }

    /// <summary>결함 네모를 얹을 바탕 그림(검사한 조각).</summary>
    public BitmapSource? Inspected
    {
        get => _inspected;
        private set => SetField(ref _inspected, value);
    }

    // ★ 바탕의 '원본 픽셀' 크기. 결함 좌표도 원본 픽셀이라, 화면이 이 크기의 판을 깔면
    //   네모를 좌표 그대로 얹을 수 있다 — 배율 계산은 Viewbox 가 통째로 해 준다.
    public double InspectedWidth
    {
        get => _inspectedWidth;
        private set => SetField(ref _inspectedWidth, value);
    }

    public double InspectedHeight
    {
        get => _inspectedHeight;
        private set => SetField(ref _inspectedHeight, value);
    }

    // ───────────────────────── 그래프 편집 ─────────────────────────

    private void AddNode(string type)
    {
        EditorNode model = new()
        {
            Id = GraphOps.UniqueId(_graph, type),
            Type = type,
            // ★ 컨텍스트 블록은 빼고 센다 — 안 빼면 새 블록이 자꾸 오른쪽으로 밀린다.
            X = CanvasLeft + (_graph.Nodes.Count % 4 * 180),
            Y = 40 + (_graph.Nodes.Count / 4 * 170),
        };

        _graph.Nodes.Add(model);

        NodeViewModel node = new(model, _registry.Describe(type), OnGraphChanged);
        Nodes.Add(node);
        SelectedNode = node;

        RefreshPortOptions();
        OnGraphChanged();
    }

    private void Connect()
    {
        if (SelectedSource is null || SelectedTarget is null)
        {
            return;
        }

        (string? sourceNode, string sourcePort) = ParseSource(SelectedSource);
        (string targetNode, string targetPort) = ParseEndpoint(SelectedTarget);

        // 규칙("입력 포트엔 선이 하나")은 GraphOps 안에 있다 — 테스트가 지킨다.
        GraphOps.Connect(_graph, sourceNode, sourcePort, targetNode, targetPort);

        RebuildConnections();
        OnGraphChanged();
    }

    /// <summary>
    /// 고른 블록을 복제한다. ★ 같은 설정으로 임계값만 다르게 두 벌 돌려 보고 싶을 때 쓴다 —
    /// 파라미터를 하나씩 다시 치는 것보다 훨씬 빠르다.
    /// </summary>
    private void DuplicateSelected()
    {
        if (SelectedNode is null || SelectedNode.IsContext)
        {
            StatusText = SelectedNode is null ? "복제할 블록을 고르세요" : "컨텍스트 입력은 복제할 수 없습니다";
            return;
        }

        EditorNode copy = GraphOps.Duplicate(_graph, SelectedNode.Id);

        NodeViewModel node = new(copy, _registry.Describe(copy.Type), OnGraphChanged);
        Nodes.Add(node);
        SelectedNode = node;

        RefreshPortOptions();
        OnGraphChanged();

        // ★ 복제본은 아무 데도 안 이어져 있다. 검증칸이 바로 "연결되지 않았습니다"를 띄우는데,
        //   그게 정상이라는 걸 상태줄로 한 번 더 말해 준다.
        StatusText = $"'{copy.Id}' 로 복제했습니다 — 선은 직접 이어야 합니다";
    }

    private void DeleteSelected()
    {
        // ★ 컨텍스트 입력은 지울 수 없다 — 레시피의 노드가 아니라 실행기가 넣어 주는 것이다.
        if (SelectedNode is null || SelectedNode.IsContext)
        {
            StatusText = SelectedNode is null ? "지울 블록을 고르세요" : "컨텍스트 입력은 지울 수 없습니다";
            return;
        }

        // 블록 + 닿은 선을 같이 지우는 건 GraphOps 몫이다.
        GraphOps.Remove(_graph, SelectedNode.Id);

        Nodes.Remove(SelectedNode);
        SelectedNode = null;

        RebuildConnections();
        RefreshPortOptions();
        OnGraphChanged();
    }

    // ───────────────────────── 불러오기 / 내보내기 ─────────────────────────

    private void Export()
    {
        Recipe recipe;

        try
        {
            // ★ 검증 강제. 막히면 파일 대화상자까지 가지도 않는다.
            recipe = RecipeMapper.Export(_graph, _registry);
        }
        catch (RecipeExportException error)
        {
            ShowIssues(error.Errors);
            StatusText = "내보내기 차단 — 검증 오류를 해결하세요";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "레시피 JSON (*.json)|*.json",
            FileName = recipe.RecipeId + ".json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // ★ 두 파일로 나눠 쓴다 — 레시피는 검사 내용, 곁다리는 블록 좌표.
        File.WriteAllText(dialog.FileName, RecipeLoader.Serialize(recipe));
        File.WriteAllText(LayoutPath(dialog.FileName), LayoutSidecar.From(_graph).Serialize());

        StatusText = $"내보냈습니다: {Path.GetFileName(dialog.FileName)} (+ .layout.json)";
    }

    private void Import()
    {
        OpenFileDialog dialog = new() { Filter = "레시피 JSON (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Recipe recipe;

        try
        {
            recipe = RecipeLoader.Load(dialog.FileName);
        }
        catch (RecipeLoadException error)
        {
            // ★ 예외 이름을 그대로 띄우지 않는다. 2주차에 정리해 둔 그것이다.
            ValidationMessages.Clear();
            ValidationMessages.Add(error.Message);
            IsValid = false;
            StatusText = "레시피를 읽지 못했습니다";
            return;
        }

        EditorGraph graph = RecipeMapper.FromRecipe(recipe);
        string layoutPath = LayoutPath(dialog.FileName);

        // 좌표 파일이 같이 있으면 그대로, 없으면 격자로 늘어놓는다(안 그러면 전부 0,0 에 겹친다).
        if (File.Exists(layoutPath))
        {
            LayoutSidecar.Parse(File.ReadAllText(layoutPath)).ApplyTo(graph);
        }
        else
        {
            GraphOps.AutoLayout(graph, CanvasLeft);
        }

        LoadGraph(graph);
        StatusText = $"불러왔습니다: {Path.GetFileName(dialog.FileName)}";
    }

    // ───────────────────────── 미리보기 ─────────────────────────

    /// <summary>
    /// 합성 이미지로 한 번 돌려 단계별 중간 이미지를 가져온다.
    ///
    /// ★ 내보내기와 <b>같은 길</b>로 레시피를 만든다. 미리보기만 따로 만들면
    ///   "화면에선 되는데 파일로 내면 안 되는" 상태가 생긴다.
    /// </summary>
    private void RunPreview()
    {
        Recipe recipe;

        try
        {
            recipe = RecipeMapper.Export(_graph, _registry);
        }
        catch (RecipeExportException error)
        {
            ShowIssues(error.Errors);
            StatusText = "미리보기 불가 — 검증 오류";
            return;
        }

        _previewOn = true;

        PreviewItems.Clear();
        Defects.Clear();

        try
        {
            // ★ 걸린 시간을 잰다. 슬라이더를 끌면 초당 수십 번 도는데,
            //   여기가 느리면 '부분 재실행'을 다시 넣어야 한다(week-5 §4①).
            //   필요한지는 추측하지 말고 이 숫자로 정한다.
            Stopwatch clock = Stopwatch.StartNew();
            PreviewResult result = PreviewService.Run(_registry, recipe);
            clock.Stop();

            foreach (PreviewItem item in result.Items)
            {
                PreviewItems.Add(item);
            }

            foreach (Defect defect in result.Defects)
            {
                Defects.Add(defect);
            }

            Inspected = result.Inspected;
            InspectedWidth = result.Width;
            InspectedHeight = result.Height;

            PreviewSummary = $"결함 {result.Defects.Count}개 · 중간 이미지 {result.Items.Count}장 · " +
                             $"{clock.Elapsed.TotalMilliseconds:F1} ms";
            StatusText = "미리보기 갱신됨";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                          or KeyNotFoundException or InvalidCastException)
        {
            // ★ 검증을 지나도 돌리다 터지는 것이 있다 — 포맷이 안 맞거나 파라미터 조합이 나쁠 때.
            //   예외 이름을 그대로 띄우지 않는다(2주차에 정리해 둔 그것).
            PreviewSummary = "돌리다 멈췄습니다";
            ValidationMessages.Clear();
            ValidationMessages.Add(error.Message);
            StatusText = "미리보기 실패";
        }
    }

    // ───────────────────────── 내부 ─────────────────────────

    private void LoadGraph(EditorGraph graph)
    {
        _graph = graph;
        SelectedNode = null;

        Nodes.Clear();

        // ★ 컨텍스트 입력을 먼저, 캔버스 왼쪽 열에 세로로 깐다.
        //   이게 없으면 선이 화면 끝에서 그냥 튀어나와 어디서 오는지 알 수 없다.
        for (int i = 0; i < ContextInputs.Length; i++)
        {
            Nodes.Add(new NodeViewModel(ContextInputs[i], x: 20, y: 40 + (i * 150)));
        }

        foreach (EditorNode model in graph.Nodes)
        {
            Nodes.Add(new NodeViewModel(model, _registry.Describe(model.Type), OnGraphChanged));
        }

        RebuildConnections();
        RefreshPortOptions();
        OnGraphChanged();
    }

    private void RebuildConnections()
    {
        // ★★ 버리기 전에 구독을 끊는다. 안 끊으면 죽은 선이 노드에 붙어 계속 산다.
        foreach (ConnectionViewModel connection in Connections)
        {
            connection.Dispose();
        }

        Connections.Clear();

        foreach (EditorConnection model in _graph.Connections)
        {
            // ★ 컨텍스트에서 오는 선(SourceNodeId == null)은 이제 <b>그 컨텍스트 블록</b>에 붙인다.
            //   SourcePort 가 곧 키라서(= "roi") 그 이름의 블록을 찾으면 된다.
            NodeViewModel? source = model.SourceNodeId is null
                ? Nodes.FirstOrDefault(n => n.IsContext && n.ContextKey == model.SourcePort)
                : Nodes.FirstOrDefault(n => !n.IsContext && n.Id == model.SourceNodeId);

            NodeViewModel? target = Nodes.FirstOrDefault(n => !n.IsContext && n.Id == model.TargetNodeId);

            if (target is not null)
            {
                Connections.Add(new ConnectionViewModel(model, source, target));
            }
        }
    }

    /// <summary>연결 콤보박스의 항목. ★ 이것도 디스크립터의 포트에서 나온다.</summary>
    private void RefreshPortOptions()
    {
        SourceOptions.Clear();

        foreach (string context in ContextInputs)
        {
            SourceOptions.Add("ctx:" + context);
        }

        // ★ 컨텍스트 블록은 건너뛴다 — 바로 위에서 "ctx:…" 로 이미 넣었다.
        //   안 건너뛰면 "ctx:roi" 와 "roi:roi" 가 둘 다 뜬다.
        foreach (NodeViewModel node in Nodes.Where(n => !n.IsContext))
        {
            foreach (string port in node.OutputPorts)
            {
                SourceOptions.Add($"{node.Id}:{port}");
            }
        }

        TargetOptions.Clear();

        foreach (NodeViewModel node in Nodes.Where(n => !n.IsContext))
        {
            foreach (string port in node.InputPorts)
            {
                TargetOptions.Add($"{node.Id}:{port}");
            }
        }
    }

    /// <summary>
    /// ★ 무엇을 하든 끝에 이걸 부른다 — 블록 추가·삭제·연결·파라미터 수정·체크박스.
    ///   <b>저장할 때가 아니라 고치는 순간</b> 빨간 줄이 떠야 한다.
    /// </summary>
    /// <summary>
    /// 그래프가 바뀔 때마다 부르는 것 — 블록 추가·삭제·연결·파라미터 한 글자·체크박스.
    ///
    /// ★★ 검증만 하는 게 아니라 <b>미리보기도 다시 돌린다.</b>
    ///   안 그러면 값을 고쳐도 썸네일이 그대로라, 쓰는 사람은 <b>"안 먹네"</b> 라고 읽는다.
    ///   튜닝 화면의 값어치가 통째로 여기에 달려 있다 — 바꾸자마자 결과가 따라와야 한다.
    ///
    /// ★ 한 번도 미리보기를 안 눌렀으면 안 돌린다. 열자마자 멋대로 도는 건 다른 문제다.
    /// ★ 검증이 깨진 상태에서도 안 돌린다. 어차피 내보내기가 막혀 돌릴 수 없다.
    /// </summary>
    private void OnGraphChanged()
    {
        Revalidate();

        if (_previewOn && IsValid)
        {
            RunPreview();
        }
    }

    private void Revalidate()
        => ShowIssues(RecipeValidator.Validate(RecipeMapper.ToRecipe(_graph, _registry), _registry));

    private void ShowIssues(IReadOnlyList<string> errors)
    {
        IsValid = errors.Count == 0;

        ValidationMessages.Clear();

        if (IsValid)
        {
            ValidationMessages.Add("검증 통과");
            return;
        }

        foreach (string error in errors)
        {
            ValidationMessages.Add(error);
        }
    }

    // ★ 토큰 해석만 여기 남는다 — "ctx:roi" 라는 글자 형식은 이 화면의 콤보박스 사정이지
    //   그래프의 규칙이 아니다. 아래층이 알 이유가 없다.
    //
    // "ctx:roi" → (null, "roi") · "diff:result" → ("diff", "result")
    private static (string? Node, string Port) ParseSource(string token)
        => token.StartsWith("ctx:", StringComparison.Ordinal)
            ? (null, token["ctx:".Length..])
            : ParseEndpoint(token);

    private static (string Node, string Port) ParseEndpoint(string token)
    {
        int separator = token.IndexOf(':', StringComparison.Ordinal);

        return separator < 0 ? (token, string.Empty) : (token[..separator], token[(separator + 1)..]);
    }

    private static string LayoutPath(string recipePath)
        => Path.ChangeExtension(recipePath, null) + ".layout.json";

    private static EditorGraph StandardGraph()
    {
        EditorGraph graph = SampleGraph.Standard();
        GraphOps.AutoLayout(graph, CanvasLeft);
        return graph;
    }
}