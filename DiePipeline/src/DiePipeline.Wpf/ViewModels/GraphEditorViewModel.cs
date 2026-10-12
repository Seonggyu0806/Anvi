using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
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
/// ★ 캔버스는 Nodify(교수님과 같다) — 포트 점에서 점으로 끌어 잇고, 확대 · 축소 · 이동한다.
/// </summary>
public sealed partial class GraphEditorViewModel : ObservableObject
{
    // ★ x를 240부터 시작한다 — 왼쪽 열은 컨텍스트 입력 블록 자리다.
    //   40부터 깔면 컨텍스트 블록과 겹쳐서 둘 다 안 읽힌다.
    public const double CanvasLeft = 240;

    private readonly GraphEditorSettings _settings;
    private readonly GraphHistory _history = new();
    private readonly RelayCommand _undo;
    private readonly RelayCommand _redo;

    private EditorGraph _graph = new();

    private NodeViewModel? _selectedNode;
    private bool _isValid;
    private string _statusText = "준비됨";
    private string _paletteSearch = string.Empty;

    public GraphEditorViewModel(GraphEditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        // ★ 팔레트를 손으로 안 적는다. 명부에서 그대로 펼친다. 카테고리로 묶고, 검색칸으로 거른다.
        PaletteItems =
        [
            .. PaletteModel.Filtered(settings.Registry, settings.PaletteFilter)
                .SelectMany(c => c.Nodes.Select(d => new PaletteItemViewModel(c.Category, d.TypeName, AddNode)))
        ];

        CollectionViewSource source = new() { Source = PaletteItems };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PaletteItemViewModel.Category)));
        Palette = source.View;
        Palette.Filter = item => item is PaletteItemViewModel p && Matches(p, _paletteSearch);

        PendingConnection = new PendingConnectionViewModel(Connect);

        _undo = new RelayCommand(Undo, () => _history.CanUndo);
        _redo = new RelayCommand(Redo, () => _history.CanRedo);

        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        DisconnectCommand = new RelayCommand<ConnectionViewModel>(Disconnect);
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        NewStandardCommand = new RelayCommand(() => LoadGraph(settings.StarterGraph(), fresh: true));
        FitCommand = new RelayCommand(() => FitRequested?.Invoke());

        LoadGraph(settings.StarterGraph(), fresh: true);
    }

    /// <summary>
    /// 그래프가 바뀔 때마다 — 블록 추가·삭제·연결·파라미터 한 글자·체크박스. 검증을 마친 <b>뒤</b>에 부른다.
    /// ★ die 탭은 여기서 미리보기를 다시 돌린다. 편집기는 무엇을 돌릴지 모른다.
    /// </summary>
    public event Action? GraphChanged;

    /// <summary>
    /// 캔버스에 그래프 전체가 보이게 맞춰 달라 — 확대 · 이동은 화면(Nodify)의 일이라 화면이 받아서 한다.
    /// ★ 그래프를 새로 열었을 때와 '전체 보기' 버튼에서.
    /// </summary>
    public event Action? FitRequested;

    public NodeRegistry Registry => _settings.Registry;

    /// <summary>팔레트 전체 — 거르기 전.</summary>
    public IReadOnlyList<PaletteItemViewModel> PaletteItems { get; }

    /// <summary>화면이 보는 팔레트 — 카테고리로 묶고 검색칸으로 거른 것.</summary>
    public ICollectionView Palette { get; }

    /// <summary>팔레트 검색 — 블록 이름이나 카테고리에 이 글자가 들어 있으면 보인다. 비우면 전부.</summary>
    public string PaletteSearch
    {
        get => _paletteSearch;
        set
        {
            if (SetField(ref _paletteSearch, value ?? string.Empty))
            {
                Palette.Refresh();
            }
        }
    }

    /// <summary>끌고 있는 선 — 포트 점에서 점으로.</summary>
    public PendingConnectionViewModel PendingConnection { get; }

    /// <summary>캔버스에서 고른 선들 — Delete 로 지운다. Nodify 가 채운다.</summary>
    public ObservableCollection<object> SelectedConnections { get; } = [];

    // ★ ObservableCollection — 항목을 더하고 빼면 화면이 알아서 따라온다.
    //   그냥 List 를 쓰면 추가해도 화면이 그대로다.
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<string> ValidationMessages { get; } = [];

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    /// <summary>선 하나 끊기 — 선을 더블클릭(또는 Alt+클릭)하면 편집기(RemoveConnectionCommand)가 그 선을 넘긴다.</summary>
    public ICommand DisconnectCommand { get; }

    public ICommand UndoCommand => _undo;

    public ICommand RedoCommand => _redo;

    public ICommand FitCommand { get; }

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

        // ★ 오류가 가리키는 블록을 캔버스에서 빨갛게 — 검증 문장은 "node 'id'(종류): …" 로 시작한다.
        HashSet<string> broken = new(
            errors.Select(e => NodeIdPattern().Match(e)).Where(m => m.Success).Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        foreach (NodeViewModel node in Nodes)
        {
            node.HasError = !node.IsContext && broken.Contains(node.Id);
        }

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
        _history.Push(_graph);

        EditorNode model = new()
        {
            Id = GraphOps.UniqueId(_graph, type),
            Type = type,
            // ★ 컨텍스트 블록은 빼고 센다 — 안 빼면 새 블록이 자꾸 오른쪽으로 밀린다.
            //   간격은 자동 배치와 같다 — Nodify 블록이 겹치지 않을 만큼.
            X = CanvasLeft + (_graph.Nodes.Count % 4 * LayoutEngine.ColumnGap),
            Y = 60 + (_graph.Nodes.Count / 4 * LayoutEngine.RowGap),
        };

        _graph.Nodes.Add(model);

        NodeViewModel node = new(model, _settings.Registry.Describe(type), OnGraphChanged);
        Nodes.Add(node);
        SelectedNode = node;

        OnGraphChanged();
    }

    /// <summary>
    /// 점에서 점으로 끌어 놓았을 때. 어느 쪽에서 끌기 시작했든 <b>출력 → 입력</b>으로 바로잡는다.
    /// ★ 출력끼리 · 입력끼리 · 같은 블록 안은 무시한다. 규칙("입력 포트엔 선이 하나")은 GraphOps 안에 있다 — 테스트가 지킨다.
    /// </summary>
    private void Connect(ConnectorViewModel start, ConnectorViewModel end)
    {
        (ConnectorViewModel? source, ConnectorViewModel? target) = (start.IsInput, end.IsInput) switch
        {
            (false, true) => (start, end),
            (true, false) => (end, start),
            _ => (null, null),
        };

        if (source is null || target is null || ReferenceEquals(source.Node, target.Node) || target.Node.IsContext)
        {
            return;
        }

        _history.Push(_graph);

        // ★ 컨텍스트에서 오는 선은 노드 id 가 없고, 포트 이름이 곧 이름표다(예: "roi").
        GraphOps.Connect(
            _graph,
            source.Node.IsContext ? null : source.Node.Id,
            source.Title,
            target.Node.Id,
            target.Title);

        RebuildConnections();
        OnGraphChanged();
    }

    private void Disconnect(ConnectionViewModel connection)
    {
        _history.Push(_graph);
        _graph.Connections.Remove(connection.Model);

        RebuildConnections();
        OnGraphChanged();
    }

    private void Undo()
    {
        LoadGraph(_history.Undo(_graph), fresh: false);
        StatusText = "되돌렸습니다";
    }

    private void Redo()
    {
        LoadGraph(_history.Redo(_graph), fresh: false);
        StatusText = "다시 했습니다";
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

        _history.Push(_graph);
        EditorNode copy = GraphOps.Duplicate(_graph, SelectedNode.Id);

        NodeViewModel node = new(copy, _settings.Registry.Describe(copy.Type), OnGraphChanged);
        Nodes.Add(node);
        SelectedNode = node;

        OnGraphChanged();

        // ★ 복제본은 아무 데도 안 이어져 있다. 검증칸이 바로 "연결되지 않았습니다"를 띄우는데,
        //   그게 정상이라는 걸 상태줄로 한 번 더 말해 준다.
        StatusText = $"'{copy.Id}' 로 복제했습니다 — 선은 직접 이어야 합니다";
    }

    private void DeleteSelected()
    {
        // ★ 선을 골라 두었으면 선을 지운다 — 블록은 그대로.
        ConnectionViewModel[] lines = [.. SelectedConnections.OfType<ConnectionViewModel>()];

        if (lines.Length > 0)
        {
            _history.Push(_graph);

            foreach (ConnectionViewModel line in lines)
            {
                _graph.Connections.Remove(line.Model);
            }

            SelectedConnections.Clear();
            RebuildConnections();
            OnGraphChanged();
            StatusText = $"선 {lines.Length}개를 지웠습니다";
            return;
        }

        // ★ 컨텍스트 입력은 지울 수 없다 — 레시피의 노드가 아니라 실행기가 넣어 주는 것이다.
        if (SelectedNode is null || SelectedNode.IsContext)
        {
            StatusText = SelectedNode is null ? "지울 블록을 고르세요" : "컨텍스트 입력은 지울 수 없습니다";
            return;
        }

        // 블록 + 닿은 선을 같이 지우는 건 GraphOps 몫이다.
        _history.Push(_graph);
        GraphOps.Remove(_graph, SelectedNode.Id);

        Nodes.Remove(SelectedNode);
        SelectedNode = null;

        RebuildConnections();
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

        // 좌표 파일이 같이 있으면 그대로, 없으면 흐름 순서대로 늘어놓는다(안 그러면 전부 0,0 에 겹친다).
        if (File.Exists(layoutPath))
        {
            LayoutSidecar.Parse(File.ReadAllText(layoutPath)).ApplyTo(graph);
        }
        else
        {
            LayoutEngine.Layered(graph, CanvasLeft);
        }

        LoadGraph(graph, fresh: true);
        StatusText = $"불러왔습니다: {Path.GetFileName(dialog.FileName)}";
    }

    // ───────────────────────── 내부 ─────────────────────────

    /// <param name="fresh">새 그래프를 열었나(처음 · 표준 · 열기). 그러면 되돌리기 기록을 비우고 캔버스를 그래프에 맞춘다.
    /// 되돌리기 · 다시로 바꾼 것이면 기록도 화면 위치도 그대로 둔다.</param>
    private void LoadGraph(EditorGraph graph, bool fresh)
    {
        _graph = graph;
        SelectedNode = null;
        SelectedConnections.Clear();

        if (fresh)
        {
            _history.Clear();
        }

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
        OnGraphChanged();

        if (fresh)
        {
            FitRequested?.Invoke();
        }
    }

    /// <summary>그래프의 선을 점과 점 사이 선으로 다시 놓는다. 이어진 점은 채워 그린다.</summary>
    private void RebuildConnections()
    {
        Connections.Clear();

        foreach (ConnectorViewModel point in Nodes.SelectMany(n => n.Input.Concat(n.Output)))
        {
            point.IsConnected = false;
        }

        foreach (EditorConnection model in _graph.Connections)
        {
            // ★ 컨텍스트에서 오는 선(SourceNodeId == null)은 <b>그 컨텍스트 블록</b>의 점에 붙인다.
            //   SourcePort 가 곧 키라서(= "roi") 그 이름의 블록을 찾으면 된다.
            ConnectorViewModel? source = (model.SourceNodeId is null
                    ? Nodes.FirstOrDefault(n => n.IsContext && n.ContextKey == model.SourcePort)
                    : Nodes.FirstOrDefault(n => !n.IsContext && n.Id == model.SourceNodeId))
                ?.Output.FirstOrDefault(c => c.Title == model.SourcePort);

            ConnectorViewModel? target = Nodes.FirstOrDefault(n => !n.IsContext && n.Id == model.TargetNodeId)
                ?.Input.FirstOrDefault(c => c.Title == model.TargetPort);

            // 점이 없는 선(입력 블록이 없는 이름표 · 디스크립터에 없는 포트)은 그리지 않는다 — 검증칸이 알린다.
            if (source is not null && target is not null)
            {
                source.IsConnected = true;
                target.IsConnected = true;
                Connections.Add(new ConnectionViewModel(model, source, target));
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

        // 되돌리기 · 다시 버튼이 눌릴 수 있는지가 바뀌었을 수 있다.
        _undo.RaiseCanExecuteChanged();
        _redo.RaiseCanExecuteChanged();

        GraphChanged?.Invoke();
    }

    private static bool Matches(PaletteItemViewModel item, string search)
        => string.IsNullOrWhiteSpace(search)
           || item.TypeName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
           || item.Category.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>검증 문장 앞머리 "node 'id'(종류)" 에서 id.</summary>
    [GeneratedRegex(@"^node '([^']+)'")]
    private static partial Regex NodeIdPattern();

    private static string LayoutPath(string recipePath)
        => Path.ChangeExtension(recipePath, null) + ".layout.json";
}
