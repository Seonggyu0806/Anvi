using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Registry;
using DiePipeline.Cv.Imaging;
using DiePipeline.Editor;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wafer.Processing;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;
using Microsoft.Win32;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>웨이퍼 결과의 die 하나를 다이 에디터에서 보자는 부탁 — 그 die 의 레시피 파일 + 러너와 같은 ROI · 골든(+ 원판).</summary>
public sealed record DiePreviewRequest(string RecipePath, PreviewInput Input);

/// <summary>
/// 웨이퍼 에디터 탭 — 웨이퍼 레시피(<c>*.wafer.json</c>)를 블록으로 짜고(<see cref="Editor"/>), 그 자리에서 돌린다(실행 칸).
///
/// ★ 편집은 die 탭과 <b>같은 부품</b>이다. 다른 것은 설정(명부 · 팔레트 · 입구 블록 · 시작 파일 · 경로 옮기기)과 실행 칸뿐.
/// ★ 실행은 명령행 <c>wafer --recipe</c> 와 <b>같은 실행기</b>(<see cref="WaferRecipeRunner"/>)다 —
///   같은 레시피 · 같은 사진이면 같은 답이 나온다(시험: 편집기를_거친_견본은_파일과_같은_답).
/// ★ 원본 사진 위에 결과를 겹쳐 그리는 것은 여기서 안 한다(C — 오버레이). 여기는 칸 지도와 결함 표까지.
/// ★ 실행 중에는 격자가 놓이자마자 맵을 그리고 die 가 끝날 때마다 칸을 칠한다(동기 앱 · 교수님 WaferEvents 와 같은 자리).
/// ★ 맵 칸 · 결함을 고르면 그 die 를 다이 에디터에서 러너와 같은 ROI · 골든으로 본다(<see cref="ToDieEditor"/>) — 값을 고치면 바로 다시.
/// </summary>
public sealed class WaferEditorViewModel : ObservableObject, IDisposable
{
    // 실행기가 넣어 주는 입력 둘 — 웨이퍼 사진(창마다 잘라서)과 표식 조각(features 에서 읽어서).
    private static readonly string[] ContextInputs = [ReservedContext.AlignSrc, ReservedContext.AlignTemplate];

    private readonly NodeRegistry _registry = WaferBackend.CreateRegistry();
    private readonly RelayCommand _drawCommand;
    private readonly RelayCommand _openImageCommand;
    private readonly RelayCommand _runCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _previewDieCommand;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _watch = new();

    private WaferScene? _scene;
    private CancellationTokenSource? _cancel;
    private bool _isBusy;
    private bool _isStale;
    private string _imageText = "사진 없음 — 합성 견본을 그리거나 사진을 여세요";
    private string _summaryText = "아직 안 돌렸습니다";
    private string _alignmentText = string.Empty;
    private string? _noteText;
    private int _mapCols = 1;
    private WaferDefect? _selectedDefect;
    private double _overviewPixel = 1;
    private bool _hasResult;

    // ── 실행 중 표시
    private int _runId;
    private string _stage = string.Empty;
    private string _stageText = string.Empty;
    private int _diesDone;
    private int _dieTotal = 1;
    private Dictionary<(int Col, int Row), int> _cellIndex = [];

    // ── 마지막 결과 · 그때 쓴 die 레시피 파일(다이 미리보기가 같은 파일을 연다)
    private WaferInspectionResult? _result;
    private string? _runE1;
    private string? _runE0;
    private WaferMapCell? _selectedCell;
    private Func<DiePreviewRequest, string>? _toDieEditor;

    public WaferEditorViewModel()
    {
        Editor = new GraphEditorViewModel(new GraphEditorSettings
        {
            Registry = _registry,

            // 웨이퍼 종류(표식 목록 · 자세 · 격자 · 웨이퍼 결함)를 주고받는 블록만 — die 탭과 반대다.
            PaletteFilter = d => PaletteModel.TouchesPortKind(d, PaletteModel.WaferKinds),
            ContextInputs = ContextInputs,

            // ★ 처음 열면 합성 견본 레시피 — 파일로 열어야 안의 경로(die 레시피 · 표식 조각)를 풀 폴더가 생긴다.
            //   견본을 못 찾으면(레포 밖에서 실행) 빈 그래프.
            StarterFile = SamplePath("wafer/demo.wafer.json"),
            StarterGraph = () => new EditorGraph { RecipeId = "untitled.wafer" },
            RebasePaths = WaferRecipePaths.Rebase,
            FileFilter = "웨이퍼 레시피 (*.wafer.json)|*.wafer.json|레시피 JSON (*.json)|*.json",

            // ★ 입구 블록은 레시피의 블록이 아니라 값이 없다 — 무엇이 들어오는지만 알려 준다(교수님 화면과 같다).
            ContextHints = new Dictionary<string, string>
            {
                [ReservedContext.AlignSrc] = "웨이퍼 사진 — 아래 실행 칸에서 고릅니다(합성 견본 그리기 · 사진 열기). 실행기가 표식을 찾을 창마다 잘라 넣습니다.",
                [ReservedContext.AlignTemplate] = "표식 조각 — InspectDies 블록의 '표식 조각' 에서 읽어 넣습니다. 아래에서 고치면 그 블록이 바뀝니다.",
            },
            ContextLists = new Dictionary<string, (string NodeType, string Param)>
            {
                [ReservedContext.AlignTemplate] = ("InspectDies", "features"),
            },
        });

        // ★ 레시피를 고치면 결과가 낡는다 — 웨이퍼는 오래 걸릴 수 있어 저절로 다시 돌리지는 않고, 낡았다고만 알린다.
        Editor.GraphChanged += () => IsStale = MapCells.Count > 0 || Defects.Count > 0 || _noteText is not null;

        _drawCommand = new RelayCommand(DrawSample, () => !IsBusy);
        _openImageCommand = new RelayCommand(OpenImage, () => !IsBusy);
        _runCommand = new RelayCommand(Run, () => !IsBusy && _scene is not null);
        _cancelCommand = new RelayCommand(() => _cancel?.Cancel(), () => IsBusy);
        _previewDieCommand = new RelayCommand(PreviewDie, () => !IsBusy && _toDieEditor is not null && SelectedRunDie() is { OffWafer: false });
        _clock.Tick += (_, _) => RaiseStage();
    }

    /// <summary>'die → 다이 미리보기' — 고른 die 를 다이 블록 에디터에서 러너와 같은 ROI · 골든으로.</summary>
    public ICommand PreviewDieCommand => _previewDieCommand;

    /// <summary>다이 에디터로 넘기는 곳 — 창이 이어 준다. 돌려준 글자를 상태 줄에 띄운다.</summary>
    public Func<DiePreviewRequest, string>? ToDieEditor
    {
        get => _toDieEditor;
        set
        {
            _toDieEditor = value;
            _previewDieCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>실행 단계 — 준비 · 정렬 중 → die n / N → 완료(경과 초). 실행 전엔 빈 글자.</summary>
    public string StageText
    {
        get => _stageText;
        private set => SetField(ref _stageText, value);
    }

    /// <summary>진행 막대 — 끝낸 칸 수 / 격자 칸 수.</summary>
    public int DiesDone
    {
        get => _diesDone;
        private set => SetField(ref _diesDone, value);
    }

    public int DieTotal
    {
        get => _dieTotal;
        private set => SetField(ref _dieTotal, Math.Max(1, value));
    }

    /// <summary>
    /// 고른 맵 칸 — 다이 미리보기의 대상. 결함을 고르면 그 결함의 die 칸이 같이 골라진다.
    /// </summary>
    public WaferMapCell? SelectedCell
    {
        get => _selectedCell;
        set
        {
            if (SetField(ref _selectedCell, value))
            {
                OnPropertyChanged(nameof(PreviewDieText));
                _previewDieCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>다이 미리보기 단추 글자 — 고른 die 를 적는다.</summary>
    public string PreviewDieText
        => SelectedRunDie() is { OffWafer: false } die
            ? $"die (c{die.Die.Col},r{die.Die.Row}) {die.Die.Zone} → 다이 미리보기"
            : "die 를 고르면 다이 미리보기";

    /// <summary>블록 편집기 — die 탭과 같은 부품.</summary>
    public GraphEditorViewModel Editor { get; }

    public ICommand DrawSampleCommand => _drawCommand;

    public ICommand OpenImageCommand => _openImageCommand;

    public ICommand RunCommand => _runCommand;

    public ICommand CancelCommand => _cancelCommand;

    /// <summary>칸 지도 — 행 우선. 화면이 <see cref="MapCols"/> 개씩 끊어 놓는다.</summary>
    public ObservableCollection<WaferMapCell> MapCells { get; } = [];

    /// <summary>결함 표 — 샘플링 <b>후</b>(보여 줄 것). 맵의 칸 숫자는 샘플링 전이다.</summary>
    public ObservableCollection<WaferDefect> Defects { get; } = [];

    // ───────────────────────── 웨이퍼 오버레이 ─────────────────────────

    /// <summary>지금 사진 — 원본 뷰어가 결함 자리로 갈 때 연다(파일이면 같은 경로, 합성이면 복사본). 없으면 null.</summary>
    public WaferScene? Scene => _scene;

    /// <summary>오버레이 바탕 — 지금 사진의 개요 그림(원본은 통째로 못 올린다). 사진이 없으면 null.</summary>
    public System.Windows.Media.Imaging.BitmapSource? OverviewImage => _scene?.Overview;

    /// <summary>원본 픽셀 크기 — 오버레이 판의 크기. die 테두리 · 결함 네모가 이 자로 적혀 있다.</summary>
    public double ImageWidth => _scene?.Width ?? 1;

    public double ImageHeight => _scene?.Height ?? 1;

    /// <summary>선 두께 · 글자 크기(원본 픽셀) — 개요 그림 1~2 px 로 보이게.</summary>
    public double OverlayLine => 1.5 * _overviewPixel;

    public double OverlayLabelSize => 12 * _overviewPixel;

    /// <summary>마지막 실행 결과가 있나 — 없으면 오버레이 칸에 '먼저 실행하세요' 를 띄운다.</summary>
    public bool HasResult
    {
        get => _hasResult;
        private set => SetField(ref _hasResult, value);
    }

    /// <summary>die 테두리 — 색은 맵 칸과 같은 상태.</summary>
    public ObservableCollection<OverlayDie> OverlayDies { get; } = [];

    /// <summary>결함 네모 — 결함 표(추린 뒤)와 하나씩 짝.</summary>
    public ObservableCollection<OverlayBoxViewModel> DefectBoxes { get; } = [];

    /// <summary>고른 결함 — 실행 칸의 결함 표 · 오버레이의 결함 표 · 네모가 같이 본다.</summary>
    public WaferDefect? SelectedDefect
    {
        get => _selectedDefect;
        set
        {
            if (SetField(ref _selectedDefect, value))
            {
                foreach (OverlayBoxViewModel box in DefectBoxes)
                {
                    box.IsSelected = ReferenceEquals(box.Item, value);
                }

                // 결함의 die 칸도 같이 고른다 — 다이 미리보기가 그 die 를 본다.
                if (value is not null)
                {
                    SelectedCell = MapCells.FirstOrDefault(c => c.Col == value.DieCol && c.Row == value.DieRow) ?? SelectedCell;
                }
            }
        }
    }

    public int MapCols
    {
        get => _mapCols;
        private set => SetField(ref _mapCols, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>결과를 낸 뒤 레시피가 바뀌었나 — 그러면 지금 보이는 결과는 옛 레시피의 것이다.</summary>
    public bool IsStale
    {
        get => _isStale;
        private set => SetField(ref _isStale, value);
    }

    public string ImageText
    {
        get => _imageText;
        private set => SetField(ref _imageText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public string AlignmentText
    {
        get => _alignmentText;
        private set => SetField(ref _alignmentText, value);
    }

    /// <summary>멈춘 까닭 · 경고 — 결과보다 먼저 읽어야 하는 것. 없으면 <c>null</c>(화면에서 칸이 사라진다).</summary>
    public string? NoteText
    {
        get => _noteText;
        private set => SetField(ref _noteText, value);
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _clock.Stop();
        _scene?.Dispose();
    }

    // ───────────────────────── 원본 뷰어에서 ─────────────────────────

    /// <summary>지금 레시피의 die(0,0) 대략 원점(SolveAlignment 의 firstDieX/Y). 그 블록이 정확히 하나가 아니면 null.</summary>
    public PointF? AlignmentOrigin()
        => SolveBlock() is { } node
            ? new PointF(Field(node, "firstDieX").NumberValue, Field(node, "firstDieY").NumberValue)
            : null;

    /// <summary>
    /// 원본 뷰어에서 그린 정렬 특징을 SolveAlignment 블록에 쓴다 — 표식 설계 자리(<c>expected</c>) + die(0,0) 원점(<c>firstDieX/Y</c>).
    /// ★ 속성 칸에서 고치는 것과 같은 길(검증 · '결과가 낡았다' 알림)을 탄다. 돌려준 글자는 뷰어가 띄운다.
    /// ★ 표식 조각 그림(features)은 여기서 안 만든다 — 파일 저장이라 다음 단계(레포 밖 저장)에서 한다.
    /// </summary>
    public string ApplyAlignment(IReadOnlyList<PointF> expected, PointF origin)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (SolveBlock() is not { } node)
        {
            int count = Editor.Nodes.Count(n => !n.IsContext && n.Type == "SolveAlignment");
            return $"적용 못 함 — 웨이퍼 레시피에 SolveAlignment 블록이 하나여야 합니다(지금 {count}개)";
        }

        Field(node, "firstDieX").StringValue = Text(origin.X);
        Field(node, "firstDieY").StringValue = Text(origin.Y);
        node.Lists.First(l => l.Name == "expected").ReplaceRows(expected.Select(p => (IReadOnlyList<string>)[Text(p.X), Text(p.Y)]));

        Editor.SelectedNode = node;
        Editor.StatusText = "원본 뷰어에서 표식 설계 자리를 받았습니다";

        return $"웨이퍼 에디터 {node.Id} 에 표식 자리 {expected.Count}개 · die(0,0) 원점 ({Text(origin.X)}, {Text(origin.Y)}) 를 썼습니다";
    }

    /// <summary>
    /// 원본 뷰어의 '특징 등록' — 표식 조각 파일을 InspectDies 의 <c>features</c> 로(하나로 바꾼다 — 실행기는 1장만 쓴다),
    /// 설계 자리 · 원점은 <see cref="ApplyAlignment"/> 와 같은 길로 쓴다.
    /// ★ 경로는 레시피 파일 기준 상대로 적는다(레시피를 아직 저장 안 했으면 절대 경로).
    /// ★ 두 블록이 다 있어야 쓴다 — 하나만 바뀐 반쪽 레시피를 만들지 않는다.
    /// </summary>
    public string RegisterFeature(string templateFile, IReadOnlyList<PointF> expected, PointF origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateFile);

        NodeViewModel[] inspects = [.. Editor.Nodes.Where(n => !n.IsContext && n.Type == "InspectDies")];

        if (inspects.Length != 1)
        {
            return $"레시피에는 못 씀 — InspectDies 블록이 하나여야 합니다(지금 {inspects.Length}개)";
        }

        if (SolveBlock() is null)
        {
            return ApplyAlignment(expected, origin);
        }

        inspects[0].Lists.First(l => l.Name == "features").ReplaceRows([[EditorPaths.Relative(Editor.Folder, templateFile)]]);

        return $"{inspects[0].Id} 에 조각 경로 · " + ApplyAlignment(expected, origin);
    }

    private NodeViewModel? SolveBlock()
    {
        NodeViewModel[] blocks = [.. Editor.Nodes.Where(n => !n.IsContext && n.Type == "SolveAlignment")];

        return blocks.Length == 1 ? blocks[0] : null;
    }

    private static PropertyFieldViewModel Field(NodeViewModel node, string name) => node.Fields.First(f => f.Name == name);

    private static string Text(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    // ───────────────────────── 사진 ─────────────────────────

    /// <summary>합성 견본 — 명령행 <c>demo</c> 기본 그림과 같다. 견본 레시피(<c>demo.wafer.json</c>)가 이 그림에 맞춰져 있다.</summary>
    private void DrawSample()
    {
        Replace(WaferScene.Synthetic(new SyntheticWaferSpec()));
        ImageText = $"{_scene!.Title} — 견본 레시피와 짝";
    }

    private void OpenImage()
    {
        OpenFileDialog dialog = new()
        {
            Filter = "이미지 (*.bmp;*.png;*.tif;*.raw)|*.bmp;*.png;*.tif;*.tiff;*.raw|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Replace(WaferScene.Open(dialog.FileName));
            ImageText = _scene!.Title;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
        {
            ImageText = $"사진을 열지 못했습니다 — {error.Message}";
        }
    }

    /// <summary>새 사진으로 바꾼다. ★ 옛 사진은 놓는다 — 파일이면 손잡이를 붙잡고 있다. 옛 결과도 지운다(다른 사진의 것이다).</summary>
    private void Replace(WaferScene scene)
    {
        _scene?.Dispose();
        _scene = scene;

        // 개요 그림 1 px 이 원본 몇 px 인가 — 네모 최소 크기 · 선 두께가 여기서 나온다.
        _overviewPixel = scene.Width / (double)Math.Max(1, scene.Overview.PixelWidth);
        OnPropertyChanged(nameof(OverviewImage));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        OnPropertyChanged(nameof(OverlayLine));
        OnPropertyChanged(nameof(OverlayLabelSize));

        ClearResults();
        SummaryText = "아직 안 돌렸습니다";
        RaiseCommandStates();
    }

    // ───────────────────────── 실행 ─────────────────────────

    private async void Run()
    {
        if (_scene is not { } scene)
        {
            return;
        }

        Recipe recipe;

        try
        {
            // ★ 내보내기와 같은 길(검증 강제) — 검증에 막힌 레시피는 돌리지 않는다.
            recipe = Editor.ExportRecipe();
        }
        catch (RecipeExportException error)
        {
            Editor.ShowIssues(error.Errors);
            Editor.StatusText = "실행 불가 — 검증 오류";
            return;
        }

        if (Editor.Folder is not { } folder)
        {
            Editor.ShowRunError("레시피를 한 번 저장하세요 — 안에 적힌 경로(die 레시피 · 표식 조각)를 풀 기준 폴더가 없습니다");
            return;
        }

        ClearResults();
        SummaryText = "검사 중…";
        IsBusy = true;
        RememberDieRecipes(recipe, folder);

        // ★ 알림은 화면 스레드로 넘어온다(Progress 가 만든 자리의 문맥으로 보낸다). 이번 실행 번호가 아니면 버린다 — 늦게 온 옛 알림.
        int runId = ++_runId;
        Progress<WaferProgress> progress = new(p => OnProgress(runId, p));
        DiesDone = 0;
        DieTotal = 1;
        SetStage(recipe.ContextInputs.Contains(ReservedContext.AlignSrc) ? "정렬 중" : "준비 중");
        _watch.Restart();
        _clock.Start();

        using CancellationTokenSource cancel = new();
        _cancel = cancel;

        try
        {
            WaferInspectionResult result = await Task.Run(
                () => WaferRecipeRunner.RunIn(recipe, folder, scene.Source, _registry, cancel.Token, progress), cancel.Token);

            Show(result);
            SetStage(result.Stopped ? "멈춤 — 검사를 시작하지 않았습니다" : $"완료 · die {DiesDone} / {DieTotal}");
            Editor.StatusText = "실행 끝";
        }
        catch (OperationCanceledException)
        {
            MapCells.Clear();
            SummaryText = "멈췄습니다 — 결과는 버렸습니다(반쯤 본 웨이퍼를 결과처럼 보이지 않게)";
            SetStage("취소됨");
        }
        catch (Exception error) when (error is WaferRecipeException or RecipeLoadException or RecipeValidationException
                                          or ArgumentException or InvalidOperationException or IOException)
        {
            // ★ 예외 이름을 띄우지 않는다 — 블록을 잘못 이었거나 경로가 틀린 것을 사람 말로.
            MapCells.Clear();
            SummaryText = "돌리지 못했습니다";
            Editor.ShowRunError(error.Message);
            Editor.StatusText = "실행 실패";
            SetStage("중단");
        }
        finally
        {
            _clock.Stop();
            _watch.Stop();
            RaiseStage();
            _cancel = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// 실행 중 알림 — 격자가 놓이면 빈 맵(전부 '아직 안 봄'), die 가 끝나면 그 칸만 결과로 바꾼다.
    /// ★ 끝난 뒤의 맵은 결과에서 다시 그린다(<see cref="Show"/>) — 같은 칸이 나온다(시험: 실행_중_맵_칸).
    /// </summary>
    private void OnProgress(int runId, WaferProgress progress)
    {
        if (runId != _runId || !IsBusy)
        {
            return;
        }

        switch (progress)
        {
            case WaferGridReady ready:
                MapCols = ready.Grid.Cols;
                MapCells.Clear();

                foreach (WaferMapCell cell in WaferMap.Pending(ready.Grid))
                {
                    MapCells.Add(cell);
                }

                _cellIndex = MapCells.Select((c, i) => (c, i)).ToDictionary(x => (x.c.Col, x.c.Row), x => x.i);
                DieTotal = ready.Grid.Count;
                DiesDone = 0;
                SetStage($"die 0 / {DieTotal}");
                break;

            case WaferDieDone done:
                if (_cellIndex.TryGetValue((done.Die.Die.Col, done.Die.Die.Row), out int index) && index < MapCells.Count)
                {
                    MapCells[index] = WaferMap.Cell(done.Die);
                }

                DiesDone = done.Done;
                SetStage($"die {done.Done} / {done.Total}");
                break;
        }
    }

    private void SetStage(string stage)
    {
        _stage = stage;
        RaiseStage();
    }

    private void RaiseStage()
        => StageText = _stage.Length == 0 ? string.Empty : $"{_stage} · {_watch.Elapsed.TotalSeconds:0}초";

    /// <summary>이번 실행이 읽는 die 레시피 파일 — 다이 미리보기가 같은 파일을 연다(레시피를 나중에 고쳐도 결과와 짝이 맞게).</summary>
    private void RememberDieRecipes(Recipe recipe, string folder)
    {
        _runE1 = null;
        _runE0 = null;

        if (recipe.Pipeline.Nodes.FirstOrDefault(n => n.Type == "InspectDies") is not { } inspect)
        {
            return;
        }

        try
        {
            InspectDiesSettings settings = InspectDiesNodeFactory.Read(inspect);
            _runE1 = WaferRecipeRunner.PathIn(folder, settings.E1Recipe);
            _runE0 = settings.E0Recipe is { } e0 ? WaferRecipeRunner.PathIn(folder, e0) : null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or WaferRecipeException)
        {
            // 읽지 못하면 실행기가 같은 까닭으로 멈춘다 — 여기서는 다이 미리보기만 못 한다.
        }
    }

    /// <summary>고른 칸의 die 검사 결과 — 결과가 없거나 칸을 안 골랐으면 null.</summary>
    private DieInspection? SelectedRunDie()
        => _selectedCell is { } cell
            ? _result?.Run?.Dies.FirstOrDefault(d => d.Die.Col == cell.Col && d.Die.Row == cell.Row)
            : null;

    /// <summary>
    /// 고른 die 를 다이 에디터로 — 그 die 의 존에 맞는 레시피 파일(E1 · E0) + 러너가 실제로 쓴 ROI · 골든 + 원판.
    /// ★ 손으로 그린 영역이 아니라 러너의 자리라서, 같은 레시피면 웨이퍼 결과와 같은 결함이 나온다.
    /// </summary>
    private void PreviewDie()
    {
        if (_toDieEditor is null || _scene is not { } scene || _result is not { } result || SelectedRunDie() is not { OffWafer: false } die)
        {
            return;
        }

        string? recipe = die.Die.Zone == WaferZone.E1 ? _runE1 : _runE0 ?? _runE1;

        if (recipe is null || !File.Exists(recipe))
        {
            Editor.StatusText = $"다이 미리보기 못 함 — die 레시피 파일이 없습니다: {recipe}";
            return;
        }

        string title = $"웨이퍼 die (c{die.Die.Col},r{die.Die.Row}) {die.Die.Zone}";
        Editor.StatusText = _toDieEditor(new DiePreviewRequest(recipe, new PreviewInput(scene.Source, title, die.Roi, die.GoldenRois, result.Circle)));
    }

    private void Show(WaferInspectionResult result)
    {
        AlignmentText = result.Solved is { } solved
            ? $"die(0,0) ({solved.Alignment.Origin.X:F1}, {solved.Alignment.Origin.Y:F1}) · 각도 {solved.Alignment.RotationDeg:F3}° · "
              + $"배율 {solved.Alignment.Scale:F4} · 짝 {solved.PairCount}개 · 어긋남 {solved.ResidualPx:F2}px"
            : "정렬 안 함 — 격자 원점을 그대로 썼습니다";

        DieInspection[] failures = [.. result.Run?.Failures ?? []];

        string?[] notes =
        [
            result.StopReason,
            result.CoverageWarning,
            result.EdgeWarning,
            result.Need.Warning,
            failures.Length > 0 ? $"⚠ 검사가 터진 die {failures.Length}개 — 결함 0개로 보이지만 안 본 것입니다(맵에 '!')" : null,
        ];

        NoteText = notes.Any(n => n is not null) ? string.Join("\n", notes.Where(n => n is not null)) : null;

        // ★★ 멈췄으면 맵을 안 그린다. 빈 맵은 "깨끗한 웨이퍼" 로 읽힌다.
        if (result.Stopped)
        {
            SummaryText = "검사를 시작하지 않았습니다";
            return;
        }

        MapCols = result.Grid!.Cols;
        _result = result;
        OnPropertyChanged(nameof(PreviewDieText));
        _previewDieCommand.RaiseCanExecuteChanged();

        // 실행 중에 칠한 칸을 결과로 다시 — 같은 칸이다(모으기 · 좌표 변환 뒤의 수로 확정).
        MapCells.Clear();

        foreach (WaferMapCell cell in WaferMap.Of(result))
        {
            MapCells.Add(cell);
        }

        foreach (WaferDefect defect in result.Defects!.Items)
        {
            Defects.Add(defect);
        }

        WaferOverlayLayout layout = WaferOverlay.Of(result, _overviewPixel);

        foreach (OverlayDie die in layout.Dies)
        {
            OverlayDies.Add(die);
        }

        foreach (OverlayDefect box in layout.Defects)
        {
            WaferDefect d = box.Defect;
            DefectBoxes.Add(new OverlayBoxViewModel(
                d, box.X, box.Y, box.Width, box.Height,
                d.Defect.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"die(c{d.DieCol},r{d.DieRow}) {d.Zone} #{d.Defect.Id} · 웨이퍼 ({d.AbsX:F1}, {d.AbsY:F1}) · {d.Defect.Bounds.Width}×{d.Defect.Bounds.Height}",
                item => SelectedDefect = (WaferDefect)item));
        }

        HasResult = true;

        int e1 = result.Grid.Dies.Count(d => d.Zone == WaferZone.E1);
        int e0 = result.Grid.Dies.Count(d => d.Zone == WaferZone.E0);
        int outside = result.Grid.Dies.Count(d => d.Zone == WaferZone.Outside);

        SummaryText = $"die {result.Run!.DieCount}개(E1 {e1} · E0 {e0}" + (outside > 0 ? $" · 원판 밖 {outside}" : string.Empty) + ") · "
                      + $"결함 {result.Found!.Count}개(표에는 샘플링 후 {result.Defects.Count}개) · {result.InspectMs:F0} ms";
    }

    private void ClearResults()
    {
        _result = null;
        SelectedCell = null;
        OnPropertyChanged(nameof(PreviewDieText));
        _previewDieCommand.RaiseCanExecuteChanged();
        StageText = string.Empty;
        _stage = string.Empty;
        SelectedDefect = null;
        DefectBoxes.Clear();
        OverlayDies.Clear();
        HasResult = false;
        MapCells.Clear();
        Defects.Clear();
        AlignmentText = string.Empty;
        NoteText = null;
        IsStale = false;
    }

    private void RaiseCommandStates()
    {
        _drawCommand.RaiseCanExecuteChanged();
        _openImageCommand.RaiseCanExecuteChanged();
        _runCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _previewDieCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 레포의 <c>samples/</c> 아래 파일 — 실행 폴더에서 위로 올라가며 <c>DiePipeline.slnx</c> 가 있는 곳을 찾는다(시험과 같은 방법).
    /// 못 찾으면 <c>null</c>.
    /// </summary>
    private static string? SamplePath(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiePipeline.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", relative);
            }
        }

        return null;
    }
}