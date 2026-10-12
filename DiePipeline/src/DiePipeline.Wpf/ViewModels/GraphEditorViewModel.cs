using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Registry;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;
using Microsoft.Win32;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 블록 편집기가 탭마다 다른 것 — 이것만 다르고 나머지(블록 · 선 · 속성 · 검증 · 열기/내보내기)는 같다.
/// </summary>
public sealed record GraphEditorSettings
{
    /// <summary>블록 명부. die 탭은 Cv, 웨이퍼 탭은 Cv + 웨이퍼 블록.</summary>
    public required NodeRegistry Registry { get; init; }

    /// <summary>팔레트에 보일 블록. 명부는 같아도 탭마다 쓸 수 있는 블록이 다르다.</summary>
    public Func<INodeDescriptor, bool> PaletteFilter { get; init; } = _ => true;

    /// <summary>실행기가 넣어 주는 입력 — 캔버스 왼쪽 열에 '출력만 있는 블록' 으로 그린다.</summary>
    public required IReadOnlyList<string> ContextInputs { get; init; }

    /// <summary>시작 그래프 — 처음 열 때와 '표준 불러오기' 를 누를 때.</summary>
    public required Func<EditorGraph> StarterGraph { get; init; }

    /// <summary>열기 · 내보내기 대화상자의 파일 종류.</summary>
    public string FileFilter { get; init; } = "레시피 JSON (*.json)|*.json";

    /// <summary>내보낼 때 기본 파일 이름 뒤에 붙일 것 — recipeId 뒤에.</summary>
    public string FileSuffix { get; init; } = ".json";
}

/// <summary>
/// 블록 편집기의 판단 — 팔레트 · 블록 추가/잇기/복제/삭제 · 속성 · 검증 · 열기/내보내기.
/// 전부 <b>디스크립터와 명부 위에</b> 올린다 — 노드 타입별 UI 코드가 0줄이다.
///
/// ★ die 탭과 웨이퍼 탭이 <b>같은 이 클래스</b>를 쓴다. 다른 것은 <see cref="GraphEditorSettings"/> 뿐이다.
///   베껴서 두 벌을 두면 한쪽만 고쳐지는 날이 온다(교수님도 die 에디터와 같은 캔버스 · 팔레트 · 속성폼 · 검증을 재사용).
/// ★ 화면(<c>GraphEditorView</c>)은 이 클래스의 속성에 <b>붙기만</b> 한다. 판단은 전부 여기서 한다.
/// </summary>
public sealed class GraphEditorViewModel : ObservableObject
{
    // ★ x를 220부터 시작한다 — 왼쪽 열은 컨텍스트 입력 블록 자리다.
    //   40부터 깔면 컨텍스트 블록과 겹쳐서 둘 다 안 읽힌다.
    public const double CanvasLeft = 220;

    private readonly GraphEditorSettings _settings;

    private EditorGraph _graph = new();

    private NodeViewModel? _selectedNode;
    private bool _isValid;
    private string _statusText = "준비됨";
    private string? _selectedSource;
    private string? _selectedTarget;

    public GraphEditorViewModel(GraphEditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        // ★ 팔레트를 손으로 안 적는다. 명부에서 그대로 펼친다.
        Palette =
        [
            .. PaletteModel.Filtered(settings.Registry, settings.PaletteFilter)
                .SelectMany(c => c.Nodes.Select(d => new PaletteItemViewModel(c.Category, d.TypeName, AddNode)))
        ];

        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ConnectCommand = new RelayCommand(Connect);
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        NewStandardCommand = new RelayCommand(() => LoadGraph(settings.StarterGraph()));

        LoadGraph(settings.StarterGraph());
    }

    /// <summary>
    /// 그래프가 바뀔 때마다 — 블록 추가·삭제·연결·파라미터 한 글자·체크박스. 검증을 마친 <b>뒤</b>에 부른다.
    /// ★ die 탭은 여기서 미리보기를 다시 돌린다. 편집기는 무엇을 돌릴지 모른다.
    /// </summary>
    public event Action? GraphChanged;

    public NodeRegistry Registry => _settings.Registry;

    public IReadOnlyList<PaletteItemViewModel> Palette { get; }

    // ★ ObservableCollection — 항목을 더하고 빼면 화면이 알아서 따라온다.
    //   그냥 List 를 쓰면 추가해도 화면이 그대로다.
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<string> ValidationMessages { get; } = [];

    public ObservableCollection<string> SourceOptions { get; } = [];

    public ObservableCollection<string> TargetOptions { get; } = [];

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ConnectCommand { get; }

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

    /// <summary>상태줄 한 줄. 탭(미리보기 · 실행)도 여기에 쓴다 — 상태줄은 하나다.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
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

    /// <summary>
    /// 지금 그래프를 레시피로 — <b>검증 강제</b>(<see cref="RecipeMapper.Export"/>). 막히면 <see cref="RecipeExportException"/>.
    /// ★ 내보내기 · 미리보기 · 웨이퍼 검사가 모두 이 길을 탄다 — 따로 만들면 "화면에선 되는데 파일로 내면 안 되는" 상태가 생긴다.
    /// </summary>
    public Recipe ExportRecipe() => RecipeMapper.Export(_graph, _settings.Registry);

    /// <summary>검증 오류를 검증칸에 띄운다. 비었으면 '검증 통과'.</summary>
    public void ShowIssues(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

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

    /// <summary>
    /// 돌리다 멈춘 까닭을 검증칸에 띄운다 — 검증은 지났는데 실행에서 터진 것(포맷 · 파라미터 조합).
    /// ★ 예외 이름을 그대로 띄우지 않는다(2주차에 정리해 둔 그것). 검증 통과 여부(<see cref="IsValid"/>)는 그대로 둔다.
    /// </summary>
    public void ShowRunError(string message)
    {
        ValidationMessages.Clear();
        ValidationMessages.Add(message);
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

        NodeViewModel node = new(model, _settings.Registry.Describe(type), OnGraphChanged);
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

        NodeViewModel node = new(copy, _settings.Registry.Describe(copy.Type), OnGraphChanged);
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
            recipe = ExportRecipe();
        }
        catch (RecipeExportException error)
        {
            ShowIssues(error.Errors);
            StatusText = "내보내기 차단 — 검증 오류를 해결하세요";
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = _settings.FileFilter,
            FileName = recipe.RecipeId + _settings.FileSuffix,
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
        OpenFileDialog dialog = new() { Filter = _settings.FileFilter };

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

    // ───────────────────────── 내부 ─────────────────────────

    private void LoadGraph(EditorGraph graph)
    {
        _graph = graph;
        SelectedNode = null;

        Nodes.Clear();

        // ★ 컨텍스트 입력을 먼저, 캔버스 왼쪽 열에 세로로 깐다.
        //   이게 없으면 선이 화면 끝에서 그냥 튀어나와 어디서 오는지 알 수 없다.
        for (int i = 0; i < _settings.ContextInputs.Count; i++)
        {
            Nodes.Add(new NodeViewModel(_settings.ContextInputs[i], x: 20, y: 40 + (i * 150)));
        }

        foreach (EditorNode model in graph.Nodes)
        {
            Nodes.Add(new NodeViewModel(model, _settings.Registry.Describe(model.Type), OnGraphChanged));
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
            // ★ 컨텍스트에서 오는 선(SourceNodeId == null)은 <b>그 컨텍스트 블록</b>에 붙인다.
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

        foreach (string context in _settings.ContextInputs)
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
    /// 그래프가 바뀔 때마다 부르는 것 — 블록 추가·삭제·연결·파라미터 한 글자·체크박스.
    /// ★ <b>저장할 때가 아니라 고치는 순간</b> 빨간 줄이 떠야 한다. 그다음 탭에 알린다(<see cref="GraphChanged"/>).
    /// </summary>
    private void OnGraphChanged()
    {
        ShowIssues(RecipeValidator.Validate(RecipeMapper.ToRecipe(_graph, _settings.Registry), _settings.Registry));
        GraphChanged?.Invoke();
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
}