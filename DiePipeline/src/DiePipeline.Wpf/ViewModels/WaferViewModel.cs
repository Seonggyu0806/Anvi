using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Registry;
using DiePipeline.Editor;
using DiePipeline.Cv.Imaging;
using DiePipeline.Wafer.Config;
using DiePipeline.Wafer.Execution;
using DiePipeline.Wpf.Imaging;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;
using Microsoft.Win32;
using OpenCvSharp;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 웨이퍼 탭. 웨이퍼 한 장을 올리고 → <b>die 탭의 레시피로</b> 검사하고 → 맵으로 보여 준다.
///
/// ★ 검사 흐름은 직접 짜지 않는다. 명령행과 같은 <see cref="WaferInspection"/>을 부른다 —
///   그래야 "정렬을 못 믿으면 멈춘다" 가 화면에서도 똑같이 걸린다.
///
/// ★ die 레시피는 die 탭의 <b>지금 그래프</b>를 내보낸 것이다(교수님 Phase 8 —
///   "die 에디터에서 만든 레시피가 곧 웨이퍼의 입력"). 따로 들고 있으면 둘이 갈라진다.
/// </summary>
public sealed class WaferViewModel : ObservableObject, IDisposable
{
    private readonly NodeRegistry _registry;
    private readonly Func<Recipe> _dieRecipe;

    private readonly RelayCommand _drawCommand;
    private readonly RelayCommand _openCommand;
    private readonly RelayCommand _inspectCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _openE0Command;
    private readonly RelayCommand _clearE0Command;
    private readonly RelayCommand _openMarkCommand;
    private readonly RelayCommand _clearMarkCommand;

    private WaferScene? _scene;
    private Mat? _markTemplate;
    private string _markText = NoMark;
    private string _markExpect = string.Empty;
    private int _markPadding;
    private string _zoneText = string.Empty;
    private CancellationTokenSource? _cancel;

    private int _gridCols = 4;
    private int _gridRows = 3;
    private int _gridPitch = 100;
    private int _gridOriginX;
    private int _gridOriginY;
    private double _circleX;
    private double _circleY;
    private double _circleRadius;
    private double _edgeExclusion;

    private Recipe? _e0Recipe;
    private string _e0Text = NoE0;

    private bool _isBusy;
    private string _sceneTitle = "아직 아무것도 안 올렸습니다";
    private BitmapSource? _overview;
    private double _sceneWidth = 1;
    private double _sceneHeight = 1;
    private DieInspection? _selectedDie;
    private Defect? _selectedDefect;
    private BitmapSource? _defectPatch;
    private BoundingBox _defectBox;
    private double _patchWidth = 1;
    private double _patchHeight = 1;
    private string _defectText = string.Empty;
    private string _status = "합성 웨이퍼를 그리거나 이미지를 여세요";
    private string? _stopReason;
    private string? _failureText;
    private string _alignmentText = string.Empty;
    private string _needText = string.Empty;
    private string? _needWarning;
    private string _recipeText = string.Empty;
    private string? _coverageWarning;
    private string? _edgeWarning;
    private string _summaryText = string.Empty;

    /// <param name="dieRecipe">die 탭의 지금 레시피. 검증에 막히면 <see cref="RecipeExportException"/>.</param>
    public WaferViewModel(NodeRegistry registry, Func<Recipe> dieRecipe)
    {
        _registry = registry;
        _dieRecipe = dieRecipe;

        // ★ 검사 중엔 웨이퍼를 못 바꾼다. 바꾸면 돌고 있는 검사가 읽던 파일을 놓아 버린다.
        _drawCommand = new RelayCommand(DrawSynthetic, () => !IsBusy);
        _openCommand = new RelayCommand(OpenFile, () => !IsBusy);
        _inspectCommand = new RelayCommand(Inspect, () => !IsBusy && _scene is not null);
        _cancelCommand = new RelayCommand(() => _cancel?.Cancel(), () => IsBusy);
        _openE0Command = new RelayCommand(OpenE0, () => !IsBusy);
        _clearE0Command = new RelayCommand(() => SetE0(null, null), () => !IsBusy && _e0Recipe is not null);

        // ★ 검사 중엔 표식을 못 바꾼다 — 돌고 있는 정렬이 그 조각을 빌려 쓰고 있다.
        _openMarkCommand = new RelayCommand(OpenMark, () => !IsBusy);
        _clearMarkCommand = new RelayCommand(() => SetMark(null, null), () => !IsBusy && _markTemplate is not null);
    }

    private const string NoE0 = "없음 — 원판이면 걸친 칸 검사에 필요합니다";

    private const string NoMark = "없음 — 파일은 정렬 안 함(원점 칸을 그대로). 합성은 그림의 표식을 쓴다";

    // ───────────────────────── 입력 — 합성 ─────────────────────────

    public int SynCols { get; set; } = 4;

    public int SynRows { get; set; } = 3;

    public int SynPitch { get; set; } = 100;

    public int SynDefects { get; set; } = 3;

    /// <summary>
    /// 기울기(도). ★ 0.05 를 넘으면 warp read 가드로 멈추고, 2 를 넘으면 정렬이 거부된다 —
    /// 화면에서 그 둘을 직접 보려고 둔 칸이다.
    /// </summary>
    public double SynAngle { get; set; }

    public int SynSeed { get; set; } = 1;

    /// <summary>원판으로 그린다 — 명령행 <c>demo --round</c> 와 같은 그림. 그리면 원 칸이 정답으로 채워진다.</summary>
    public bool SynRound { get; set; }

    // ───────────────────────── 입력 — 격자 ─────────────────────────

    public int GridCols
    {
        get => _gridCols;
        set => SetField(ref _gridCols, value);
    }

    public int GridRows
    {
        get => _gridRows;
        set => SetField(ref _gridRows, value);
    }

    public int GridPitch
    {
        get => _gridPitch;
        set => SetField(ref _gridPitch, value);
    }

    /// <summary>die(0,0) 의 사진 속 자리. ★ 파일일 때만 쓴다 — 합성은 표식 정렬이 찾아낸다.</summary>
    public int GridOriginX
    {
        get => _gridOriginX;
        set => SetField(ref _gridOriginX, value);
    }

    public int GridOriginY
    {
        get => _gridOriginY;
        set => SetField(ref _gridOriginY, value);
    }

    /// <summary>
    /// 원판 — <b>사진 좌표</b>의 중심과 반지름. <b>반지름 0 이면 원판을 안 쓴다</b>(사각형 웨이퍼).
    /// ★ 합성 원판은 그리면 정답으로 채워진다. 파일이면 사람이 넣는다 — 교수님은 장비가 주는 값으로 봤다.
    /// </summary>
    public double GridCircleX
    {
        get => _circleX;
        set => SetField(ref _circleX, value);
    }

    public double GridCircleY
    {
        get => _circleY;
        set => SetField(ref _circleY, value);
    }

    public double GridCircleRadius
    {
        get => _circleRadius;
        set => SetField(ref _circleRadius, value);
    }

    /// <summary>가장자리 여유(px) — 원판 가장자리에서 이만큼 안쪽까지는 안 본다. 원판을 줬을 때만 쓴다.</summary>
    public double GridEdgeExclusion
    {
        get => _edgeExclusion;
        set => SetField(ref _edgeExclusion, value);
    }

    /// <summary>E0(가장자리) 레시피 — 파일에서 연다. die 탭 그래프는 E1 로만 쓴다.</summary>
    public string E0Text
    {
        get => _e0Text;
        private set => SetField(ref _e0Text, value);
    }

    /// <summary>
    /// 정렬 표식 조각 — 파일에서 연다. 명령행 <c>--mark</c> 와 같다. 합성 웨이퍼는 그림의 표식을 쓴다.
    /// </summary>
    public string MarkText
    {
        get => _markText;
        private set => SetField(ref _markText, value);
    }

    /// <summary>
    /// 표식의 설계 자리 — <c>"x,y; x,y"</c>, die(0,0) 을 (0,0) 으로 본 좌표. 명령행 <c>--expect</c> 여러 개와 같다.
    /// ★ 글자 해석은 <see cref="WaferInputText"/> — 명령행과 같은 규칙.
    /// </summary>
    public string MarkExpect
    {
        get => _markExpect;
        set => SetField(ref _markExpect, value);
    }

    /// <summary>
    /// 표식을 찾을 범위(px) — 대략 자리 ± 이만큼만 읽는다. 0 = 사진 전체. 명령행 <c>--mark-padding</c> 과 같다.
    /// ★ 원판 원본은 통째로 못 읽으니 꼭 준다.
    /// </summary>
    public int MarkPadding
    {
        get => _markPadding;
        set => SetField(ref _markPadding, value);
    }

    /// <summary>
    /// 사람이 정하는 die — <c>"2,3=off; 3,3=E0"</c>. 명령행 <c>--zone</c> 여러 개와 같다.
    /// ★ 노치·플랫처럼 원 판정이 틀리는 die 에 쓴다. off 는 die 전체를 검사에서 뺀다.
    /// </summary>
    public string ZoneText
    {
        get => _zoneText;
        set => SetField(ref _zoneText, value);
    }

    /// <summary>
    /// 이웃 고르는 법. false = 같은 행 좌우 1칸(최대 2장), true = 가까운 순서(최대 3장).
    /// ★ 레시피가 요구하는 골든 장수와 <b>짝</b>이 맞아야 한다 — 안 맞으면 die 가 "검사 실패" 로 남는다.
    /// </summary>
    public bool UseNearest { get; set; }

    // ───────────────────────── 명령 ─────────────────────────

    public ICommand DrawCommand => _drawCommand;

    public ICommand OpenCommand => _openCommand;

    public ICommand InspectCommand => _inspectCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand OpenE0Command => _openE0Command;

    public ICommand ClearE0Command => _clearE0Command;

    public ICommand OpenMarkCommand => _openMarkCommand;

    public ICommand ClearMarkCommand => _clearMarkCommand;

    // ───────────────────────── 결과 ─────────────────────────

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

    public string SceneTitle
    {
        get => _sceneTitle;
        private set => SetField(ref _sceneTitle, value);
    }

    public BitmapSource? Overview
    {
        get => _overview;
        private set => SetField(ref _overview, value);
    }

    /// <summary>
    /// ★ 원본 픽셀 크기. 화면은 이 크기의 판을 깔고 die·결함 좌표를 <b>그대로</b> 얹는다 —
    /// 늘이고 줄이는 건 Viewbox 가 통째로 한다(die 탭 결함 오버레이와 같은 수법).
    /// </summary>
    public double SceneWidth
    {
        get => _sceneWidth;
        private set => SetField(ref _sceneWidth, value);
    }

    public double SceneHeight
    {
        get => _sceneHeight;
        private set => SetField(ref _sceneHeight, value);
    }

    /// <summary>
    /// die 테두리 굵기(원본 픽셀 단위). ★ 그림이 크면 굵게 — 2만 픽셀짜리 사진에 2픽셀 선은 안 보인다.
    /// </summary>
    public double StrokePx => Math.Max(1.5, Math.Max(SceneWidth, SceneHeight) / 300);

    /// <summary>
    /// 웨이퍼 맵의 칸들. ★ <see cref="DieInspection"/>을 그대로 담는다 —
    /// 칸 자리(<c>Roi</c>)·상태(<c>Summary.State</c>)·결함이 다 들어 있어서 감쌀 이유가 없다.
    /// </summary>
    public ObservableCollection<DieInspection> Dies { get; } = [];

    public DieInspection? SelectedDie
    {
        get => _selectedDie;
        set
        {
            // 다른 die 를 고르면 앞 die 의 결함 그림은 지운다 — 남아 있으면 엉뚱한 die 의 결함으로 읽힌다.
            if (SetField(ref _selectedDie, value))
            {
                SelectedDefect = null;
            }
        }
    }

    /// <summary>선택한 die 의 결함 표에서 고른 결함. 고르면 그 둘레를 원본 해상도로 읽는다.</summary>
    public Defect? SelectedDefect
    {
        get => _selectedDefect;
        set
        {
            if (SetField(ref _selectedDefect, value))
            {
                ShowDefect();
            }
        }
    }

    /// <summary>
    /// 고른 결함 둘레 — <b>원본 해상도</b>. ★ 맵은 축소본이라 원판 원본이면 결함이 1~2 px 점으로 보인다.
    /// </summary>
    public BitmapSource? DefectPatch
    {
        get => _defectPatch;
        private set => SetField(ref _defectPatch, value);
    }

    /// <summary>그림 안의 결함 네모(그림 픽셀 기준). 그림 크기의 판 위에 그대로 얹는다.</summary>
    public BoundingBox DefectBox
    {
        get => _defectBox;
        private set => SetField(ref _defectBox, value);
    }

    public double PatchWidth
    {
        get => _patchWidth;
        private set => SetField(ref _patchWidth, value);
    }

    public double PatchHeight
    {
        get => _patchHeight;
        private set => SetField(ref _patchHeight, value);
    }

    /// <summary>결함 네모 굵기(그림 픽셀). 창이 크면 굵게 — 화면에 줄여 보이므로.</summary>
    public double PatchStroke => Math.Max(1, Math.Max(PatchWidth, PatchHeight) / 128);

    /// <summary>"원본 (x, y) · 결함 w×h · 창 w×h" — 사진 좌표로 어디인지.</summary>
    public string DefectText
    {
        get => _defectText;
        private set => SetField(ref _defectText, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>검사를 <b>시작하지 않은</b> 까닭. 명령행이 stderr 에 찍는 것과 같은 문장이다.</summary>
    public string? StopReason
    {
        get => _stopReason;
        private set => SetField(ref _stopReason, value);
    }

    /// <summary>
    /// ★★ 터진 die 가 있을 때의 경고. 결과 숫자보다 <b>먼저</b> 보여야 한다 —
    /// "결함 0개" 로 보이는데 사실은 안 본 것, 이 프로젝트에서 제일 위험한 상태다.
    /// </summary>
    public string? FailureText
    {
        get => _failureText;
        private set => SetField(ref _failureText, value);
    }

    public string AlignmentText
    {
        get => _alignmentText;
        private set => SetField(ref _alignmentText, value);
    }

    public string NeedText
    {
        get => _needText;
        private set => SetField(ref _needText, value);
    }

    public string? NeedWarning
    {
        get => _needWarning;
        private set => SetField(ref _needWarning, value);
    }

    /// <summary>
    /// 이번 검사에 <b>실제로 쓴</b> die 레시피 — 이름과 설명.
    ///
    /// ★ 5주차에 두 번 헷갈렸다. 실데이터가 0개로 나온 것도, 합성이 3개 대신 4개로 나온 것도
    ///   원인은 같았다 — die 탭의 <b>미리보기용 시드</b>로 웨이퍼를 검사했다. 그런데 화면 어디에도
    ///   어떤 레시피로 돌았는지가 없어서 원인을 찾는 데 오래 걸렸다.
    ///   die 탭을 보고 짐작한 이름이 아니라 <b>검사에 넘긴 그 레시피</b>에서 읽는다.
    /// </summary>
    public string RecipeText
    {
        get => _recipeText;
        private set => SetField(ref _recipeText, value);
    }

    /// <summary>격자가 사진 일부만 덮을 때의 경고. 판정은 웨이퍼 층이 한다 — 명령행과 같은 문장.</summary>
    public string? CoverageWarning
    {
        get => _coverageWarning;
        private set => SetField(ref _coverageWarning, value);
    }

    /// <summary>원판인데 E0 레시피가 원판 밖을 안 가릴 때의 경고. 판정은 웨이퍼 층이 한다.</summary>
    public string? EdgeWarning
    {
        get => _edgeWarning;
        private set => SetField(ref _edgeWarning, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _scene?.Dispose();
        _scene = null;
        _markTemplate?.Dispose();
        _markTemplate = null;
    }

    // ───────────────────────── 표식 ─────────────────────────

    private void OpenMark()
    {
        OpenFileDialog dialog = new() { Filter = "표식 조각 (*.png;*.bmp;*.tif)|*.png;*.bmp;*.tif;*.tiff|모든 파일 (*.*)|*.*" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetMark(ImageFile.LoadGrayF32(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException
                                          or OpenCVException)
        {
            Status = $"표식을 열지 못했습니다 — {error.Message}";
        }
    }

    /// <summary>표식 조각을 바꾼다. ★ 옛 조각은 우리가 읽었으니 우리가 놓는다.</summary>
    private void SetMark(Mat? template, string? name)
    {
        _markTemplate?.Dispose();
        _markTemplate = template;
        MarkText = template is null ? NoMark : $"{name} ({template.Cols}×{template.Rows})";
        RaiseCommandStates();
    }

    /// <summary>
    /// 이번 검사의 정렬 주문. 합성이면 그림의 표식, 파일이면 연 표식 조각 + 적은 자리. 없으면 정렬 안 함.
    /// </summary>
    /// <exception cref="FormatException">표식 위치 글자가 틀렸거나 2개보다 적을 때.</exception>
    private MarkAlignment? Alignment(WaferScene scene)
    {
        int? padding = MarkPadding > 0 ? MarkPadding : null;

        if (scene.Alignment is { } drawn)
        {
            return drawn with { SearchPadding = padding };
        }

        if (_markTemplate is null)
        {
            return null;
        }

        IReadOnlyList<PointF> expected = WaferInputText.Points(MarkExpect);

        // ★ 하나로는 밀린 양만 알고 돌아간 각도를 모른다 — 명령행과 같은 규칙.
        if (expected.Count < 2)
        {
            throw new FormatException(
                $"표식 위치가 {expected.Count}개입니다 — 2개 이상 적으세요 (예: -40,-40; 410,310)");
        }

        return new MarkAlignment { Template = _markTemplate, Expected = expected, SearchPadding = padding };
    }

    // ───────────────────────── E0 레시피 ─────────────────────────

    /// <summary>
    /// E0 레시피를 파일에서 연다.
    ///
    /// ★ die 탭 그래프는 E1(die-to-die)로 쓴다. E0 는 골든 없는 <b>다른 레시피</b>라 따로 받는다.
    /// ★ 원판이면 걸친 칸이 원판 밖 배경까지 찍혀 있다 — <c>samples/recipes/die.dark.masked.json</c> 처럼
    ///   원판 밖을 가리는 레시피를 쓴다. 안 가리면 웨이퍼 층이 경고(EdgeWarning)를 낸다.
    /// </summary>
    private void OpenE0()
    {
        OpenFileDialog dialog = new() { Filter = "레시피 JSON (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Recipe recipe = RecipeLoader.Load(dialog.FileName);

            // 여는 순간 검사한다 — 검사 버튼을 누른 뒤에야 "레시피가 틀렸다" 를 알면 늦다.
            RecipeValidator.EnsureValid(recipe, _registry);

            SetE0(recipe, Path.GetFileName(dialog.FileName));
        }
        catch (Exception error) when (error is RecipeLoadException or RecipeValidationException)
        {
            Status = $"E0 레시피를 쓸 수 없습니다 — {error.Message}";
        }
    }

    private void SetE0(Recipe? recipe, string? fileName)
    {
        _e0Recipe = recipe;
        E0Text = recipe is null ? NoE0 : $"{fileName}  ({recipe.RecipeId})";

        // 다른 레시피로 낸 결과를 남겨 두지 않는다 — 화면의 결과와 레시피가 어긋나 보인다.
        ClearResults();
        RaiseCommandStates();
    }

    // ───────────────────────── 올리기 ─────────────────────────

    private void DrawSynthetic()
    {
        WaferScene scene;

        try
        {
            scene = WaferScene.Synthetic(new SyntheticWaferSpec
            {
                Cols = SynCols,
                Rows = SynRows,
                Pitch = SynPitch,
                Defects = SynDefects,
                AngleDeg = SynAngle,
                Seed = SynSeed,
                Round = SynRound,
            });
        }
        catch (ArgumentException error)
        {
            // 격자·피치·각도가 말이 안 될 때 — SyntheticWafer 가 사람 말로 적어 준다.
            Status = error.Message;
            return;
        }

        // 격자 칸을 합성 설정에 맞춘다. 원점은 0 — 표식 정렬이 die(0,0) 자리를 찾아낸다.
        GridCols = SynCols;
        GridRows = SynRows;
        GridPitch = SynPitch;
        GridOriginX = 0;
        GridOriginY = 0;

        // 원 칸도 정답으로 채운다 — 원판이 아니면 0(안 씀).
        GridCircleX = scene.Circle?.CenterX ?? 0;
        GridCircleY = scene.Circle?.CenterY ?? 0;
        GridCircleRadius = scene.Circle?.Radius ?? 0;

        Replace(scene);
    }

    private void OpenFile()
    {
        OpenFileDialog dialog = new() { Filter = "이미지 (*.bmp;*.png;*.tif;*.raw)|*.bmp;*.png;*.tif;*.tiff;*.raw|모든 파일 (*.*)|*.*" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Replace(WaferScene.Open(dialog.FileName));

            // ★ 합성에서 채운 원은 이 사진과 상관없다 — 남겨 두면 엉뚱한 원으로 칸을 나눈다.
            GridCircleX = 0;
            GridCircleY = 0;
            GridCircleRadius = 0;

            Status = "격자 열·행·피치와 die(0,0) 자리를 넣고 검사하세요 — 원판이면 원 중심·반지름과 E0 레시피도";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
                                          or NotSupportedException)
        {
            Status = $"열지 못했습니다 — {error.Message}";
        }
    }

    private void Replace(WaferScene scene)
    {
        // ★ 옛 웨이퍼를 놓는다. 파일이면 손잡이가 쌓인다.
        _scene?.Dispose();
        _scene = scene;

        SceneTitle = scene.Title;
        Overview = scene.Overview;
        SceneWidth = scene.Width;
        SceneHeight = scene.Height;
        OnPropertyChanged(nameof(StrokePx));

        ClearResults();
        Status = scene.Template is null ? Status : "올렸습니다 — ▶ 검사를 누르세요";
        RaiseCommandStates();
    }

    // ───────────────────────── 검사 ─────────────────────────

    /// <summary>
    /// ★ 백그라운드에서 돈다. 웨이퍼 한 장은 die 한 장의 N 배라, UI 스레드에서 돌리면 그동안 화면이 굳는다.
    ///   <c>async void</c> 는 버튼 처리에서만 쓴다 — 예외를 안에서 전부 받는다.
    /// </summary>
    private async void Inspect()
    {
        if (_scene is not { } scene)
        {
            return;
        }

        Recipe recipe;

        try
        {
            // die 탭에서 지금 보이는 그래프를 내보낸다 — 검증에 막히면 여기서 멈춘다.
            recipe = _dieRecipe();
        }
        catch (RecipeExportException)
        {
            ClearResults();
            StopReason = "die 탭의 레시피에 검증 오류가 있어 검사할 수 없습니다 — die 탭의 검증칸을 보세요";
            return;
        }

        MarkAlignment? alignment;
        IReadOnlyList<ZoneOverride> zones;

        try
        {
            alignment = Alignment(scene);
            zones = WaferInputText.Zones(ZoneText);
        }
        catch (FormatException error)
        {
            ClearResults();
            StopReason = error.Message;
            Status = "입력을 읽지 못했습니다";
            return;
        }

        WaferInspectionRequest request = new()
        {
            E1 = recipe,
            E0 = _e0Recipe,
            Spec = new WaferSpec
            {
                Cols = GridCols,
                Rows = GridRows,
                PitchX = GridPitch,
                PitchY = GridPitch,

                // ★ 정렬하면 원점은 '정렬 전 대략 자리'(교수님 firstDieX/Y) — 표식을 그 둘레에서 찾고 정렬이 고친다.
                //   이동이 두 번 들어가지 않게 웨이퍼 층이 격자에서는 뺀다(명령행과 같은 규칙).
                OriginX = GridOriginX,
                OriginY = GridOriginY,

                // 반지름 0 = 원판 안 씀. 원은 사진 좌표라 정렬과 상관없이 그대로 넘긴다.
                Circle = GridCircleRadius > 0 ? new WaferCircle(GridCircleX, GridCircleY, GridCircleRadius) : null,

                // 원판이 없는데 여유만 있으면 웨이퍼 층이 까닭을 말하며 거부한다 — 화면도 그 문장을 띄운다.
                EdgeExclusion = GridEdgeExclusion,

                ZoneOverrides = zones,
            },
            Alignment = alignment,
            Strategy = UseNearest ? NeighborStrategy.Nearest : NeighborStrategy.SameRow1,
            GoldenWanted = 3,
        };

        ClearResults();
        RecipeText = Describe(recipe) + (_e0Recipe is null ? string.Empty : $"\nE0 레시피  {_e0Recipe.RecipeId}")
                     + (zones.Count == 0
                         ? string.Empty
                         : $"\n수동 지정  {string.Join(" · ", zones.Select(WaferInputText.Describe))} — 자동 판정을 사람이 덮어씀");
        Status = "검사 중…";
        IsBusy = true;

        using CancellationTokenSource cancel = new();
        _cancel = cancel;

        try
        {
            WaferInspectionResult result =
                await Task.Run(() => WaferInspection.Run(request, scene.Source, _registry, cancel.Token), cancel.Token);

            Show(result, scene);
        }
        catch (OperationCanceledException)
        {
            Status = "멈췄습니다 — 결과는 버렸습니다(반쯤 본 웨이퍼를 결과처럼 보이지 않게)";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                          or RecipeValidationException)
        {
            // ★ 예외 이름을 띄우지 않는다. 격자가 사진 밖으로 나가는 것 같은 사정을 사람 말로.
            StopReason = error.Message;
            Status = "검사하지 못했습니다";
        }
        finally
        {
            _cancel = null;
            IsBusy = false;
        }
    }

    private void Show(WaferInspectionResult result, WaferScene scene)
    {
        NeedText = $"골든 최소 {result.Need.Minimum}장 — {result.Need.Reason}";
        NeedWarning = result.Need.Warning;
        CoverageWarning = result.CoverageWarning;
        EdgeWarning = result.EdgeWarning;

        AlignmentText = result.Solved is { } solved
            ? $"각도 {solved.Alignment.RotationDeg:F3}° · 배율 {solved.Alignment.Scale:F4} · "
              + $"die(0,0) ({solved.Alignment.Origin.X:F1}, {solved.Alignment.Origin.Y:F1})\n"
              + $"짝 {solved.PairCount}개 · 어긋남 {solved.ResidualPx:F2}px · 점수 {solved.Alignment.MeanScore:F3}"
            : "정렬 안 함 — 격자 원점을 그대로 썼습니다";

        // ★★ 멈췄으면 맵을 안 그린다. 빈 맵은 "깨끗한 웨이퍼" 로 읽힌다.
        if (result.Stopped)
        {
            StopReason = result.StopReason;
            Status = "검사를 시작하지 않았습니다";
            return;
        }

        foreach (DieInspection die in result.Run!.Dies)
        {
            Dies.Add(die);
        }

        DieInspection[] failures = [.. result.Run.Failures];

        if (failures.Length > 0)
        {
            FailureText = $"⚠ 검사가 터진 die {failures.Length}개 — 결함 0개로 보이지만 안 본 것입니다\n"
                          + $"   예: die(c{failures[0].Die.Col},r{failures[0].Die.Row}) {failures[0].Failure}";
        }

        string truth = scene.Truth.Count > 0 ? $" (심은 정답 {scene.Truth.Count}개)" : string.Empty;

        SummaryText = $"die {result.Run.DieCount}개 · 결함 {result.Found!.Count}개{truth} · "
                      + $"{result.InspectMs:F0} ms";

        Status = failures.Length > 0 ? "검사는 끝났지만 못 본 die 가 있습니다" : "검사 끝";
    }

    /// <summary>
    /// 고른 결함 둘레를 원본에서 읽어 그림으로 만든다. 창 정하기는 <see cref="DefectWindow"/>(웨이퍼 층, 시험됨)가 한다.
    /// </summary>
    private void ShowDefect()
    {
        if (_selectedDefect is not { } defect || _selectedDie is not { } die || _scene is not { } scene)
        {
            DefectPatch = null;
            DefectText = string.Empty;
            return;
        }

        try
        {
            DefectView view = DefectWindow.Around(die.Roi, defect.Bounds, scene.Width, scene.Height);

            using Mat patch = scene.Source.ReadRegion(view.Window);

            PatchWidth = view.Window.Width;
            PatchHeight = view.Window.Height;
            OnPropertyChanged(nameof(PatchStroke));
            DefectBox = view.Box;
            DefectPatch = BitmapConvert.ToBitmap(patch);
            DefectText = $"원본 ({view.Window.X + view.Box.X}, {view.Window.Y + view.Box.Y}) · "
                         + $"결함 {defect.Bounds.Width}×{defect.Bounds.Height} · 창 {view.Window.Width}×{view.Window.Height}";
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
        {
            DefectPatch = null;
            DefectText = $"원본을 읽지 못했습니다 — {error.Message}";
        }
    }

    private void ClearResults()
    {
        Dies.Clear();
        SelectedDie = null;
        StopReason = null;
        FailureText = null;
        AlignmentText = string.Empty;
        NeedText = string.Empty;
        NeedWarning = null;
        RecipeText = string.Empty;
        CoverageWarning = null;
        EdgeWarning = null;
        SummaryText = string.Empty;
    }

    /// <summary>레시피 한 줄 — 설명이 있으면 같이. 시드는 설명에 "실데이터 값 아님" 이 적혀 있다.</summary>
    private static string Describe(Recipe recipe)
        => string.IsNullOrWhiteSpace(recipe.Description)
            ? $"die 레시피  {recipe.RecipeId}"
            : $"die 레시피  {recipe.RecipeId} — {recipe.Description}";

    private void RaiseCommandStates()
    {
        _drawCommand.RaiseCanExecuteChanged();
        _openCommand.RaiseCanExecuteChanged();
        _inspectCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _openE0Command.RaiseCanExecuteChanged();
        _clearE0Command.RaiseCanExecuteChanged();
        _openMarkCommand.RaiseCanExecuteChanged();
        _clearMarkCommand.RaiseCanExecuteChanged();
    }
}
