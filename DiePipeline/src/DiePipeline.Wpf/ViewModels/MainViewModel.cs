using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Imaging;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Cv.Imaging;
using DiePipeline.Editor;
using DiePipeline.Viewer;
using DiePipeline.Wafer.Domain;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 작업공간 — 창 맨 위 바의 칸. 교수님 앱과 같은 묶음(다이 · 웨이퍼 · 원본)으로 늘어선다.
/// </summary>
public enum Workspace
{
    /// <summary>다이 · 블록 에디터 — die 레시피 편집 + 미리보기.</summary>
    DieEditor,

    /// <summary>다이 · 오버레이 — 미리보기 결과를 크게: 배경(단계 그림) + 결함 네모 + 결함 목록.</summary>
    DieOverlay,

    /// <summary>웨이퍼 · 블록 에디터 — 웨이퍼 레시피 편집 + 실행.</summary>
    WaferEditor,

    /// <summary>웨이퍼 · 오버레이 — 웨이퍼 에디터의 마지막 실행을 개요 그림 위에: die 테두리 + 결함 네모 + 결함 목록.</summary>
    WaferOverlay,

    /// <summary>웨이퍼 · 옵션 검사 — 레시피 파일 없이 입력칸으로 검사(이전 '웨이퍼' 탭).</summary>
    WaferOptions,

    /// <summary>원본 · 이미지 뷰어 — 원본 한 장을 축소본 + 선명 조각으로 확대 · 이동하며 본다.</summary>
    Viewer,
}

/// <summary>
/// die 탭 — 블록 편집기(<see cref="GraphEditorViewModel"/>) + 미리보기.
///
/// ★ 편집(팔레트 · 블록 · 선 · 속성 · 검증 · 열기/내보내기)은 <see cref="Editor"/> 가 한다 — 웨이퍼 편집 탭과 같은 부품이다.
///   이 클래스에 남은 것은 <b>die 탭에만 있는 것</b> — 합성 이미지로 돌리는 미리보기뿐이다.
/// ★ 화면(XAML)은 이 클래스의 속성에 <b>붙기만</b> 한다. 판단은 전부 여기서 한다.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    // ★ waferMask — 원판에 걸친 die(E0) 레시피가 받는 원판 마스크. 없으면 die.dark.masked 를 열었을 때
    //   MaskApply 로 가는 선이 이어질 블록이 없다. 안 쓰는 레시피는 선을 안 이으니 내보내기에 안 들어간다.
    private static readonly string[] ContextInputs =
        [ReservedContext.Roi, ReservedContext.ChipRegions, ReservedContext.WaferMask];

    private Workspace _selectedWorkspace = Workspace.DieEditor;
    private string _previewSummary = "아직 안 돌렸습니다";
    private PreviewItem? _overlayBackground;

    // ★ 마지막으로 고른 배경의 이름. 미리보기를 다시 돌리면 배경 목록을 비웠다 채우는데, 비우는 순간 콤보가
    //   고른 값을 null 로 되돌려 보낸다 — 그래서 고른 값과 따로 이름을 들고 있다가 다시 찾는다.
    private string? _overlayBackgroundKey;
    private Defect? _selectedDefect;
    private bool _hasPreview;
    private BitmapSource? _inspected;
    private double _inspectedWidth = 1;
    private double _inspectedHeight = 1;

    // ★ 한 번이라도 미리보기를 눌렀는가. 누르기 전에는 멋대로 돌리지 않는다.
    private bool _previewOn;

    // ★ 원본 뷰어에서 넘겨받은 미리보기 입력. null 이면 합성 견본으로 돈다.
    private PreviewInput? _previewInput;

    // 원본 뷰어에 지금 올라 있는 웨이퍼 사진(결함 자리로 갈 때 다시 열지 않으려고). 뷰어가 다른 원본을 열면 비운다.
    private WaferScene? _viewerScene;

    // 마지막으로 뷰어에 옮겨 준 웨이퍼 레시피의 원점 — 레시피가 그대로면 뷰어에서 고치던 값을 덮지 않는다.
    private PointF? _syncedOrigin;

    // ★ 미리보기 입력이 웨이퍼 에디터의 사진에서 왔나 — 그 사진이 바뀌면(놓으면) 합성으로 되돌린다. 뷰어 원본이 바뀌어도 상관없다.
    private bool _previewFromWafer;
    private readonly RelayCommand _useSyntheticCommand;
    private readonly RelayCommand _showDefectInViewerCommand;
    private readonly RelayCommand _exportReportCommand;
    private string _reportText = string.Empty;

    public MainViewModel()
    {
        NodeRegistry registry = CvBackend.CreateRegistry();

        Editor = new GraphEditorViewModel(new GraphEditorSettings
        {
            Registry = registry,

            // 웨이퍼 종류를 주고받는 블록(표식 찾기 등)은 die 레시피에 못 쓰니 숨긴다 — 이름이 아니라 포트 종류로.
            PaletteFilter = d => !PaletteModel.TouchesPortKind(d, PaletteModel.WaferKinds),
            ContextInputs = ContextInputs,
            StarterGraph = StandardGraph,
        });

        // ★★ 그래프가 바뀌면 검증만 하는 게 아니라 <b>미리보기도 다시 돌린다.</b>
        //   안 그러면 값을 고쳐도 썸네일이 그대로라, 쓰는 사람은 <b>"안 먹네"</b> 라고 읽는다.
        //   튜닝 화면의 값어치가 통째로 여기에 달려 있다 — 바꾸자마자 결과가 따라와야 한다.
        // ★ 한 번도 미리보기를 안 눌렀으면 안 돌린다. 열자마자 멋대로 도는 건 다른 문제다.
        // ★ 검증이 깨진 상태에서도 안 돌린다. 어차피 내보내기가 막혀 돌릴 수 없다.
        Editor.GraphChanged += () =>
        {
            if (_previewOn && Editor.IsValid)
            {
                RunPreview();
            }
        };

        RunPreviewCommand = new RelayCommand(() => RunPreview());

        // 미리보기 썸네일 더블클릭 → 그 단계 그림을 배경으로 다이 오버레이(교수님 앱과 같다).
        OpenOverlayCommand = new RelayCommand<PreviewItem>(item =>
        {
            SelectedOverlayBackground = OverlayBackgrounds.FirstOrDefault(b => b.Key == item.Key) ?? SelectedOverlayBackground;
            SelectedWorkspace = Workspace.DieOverlay;
        });

        // ★ 웨이퍼 탭은 die 탭의 '지금 그래프' 를 레시피로 쓴다 — 따로 들고 있으면 둘이 갈라진다.
        //   내보내기와 같은 길(검증 강제)을 탄다 — 검증에 막히면 웨이퍼 검사도 안 돌아간다.
        Wafer = new WaferViewModel(registry, Editor.ExportRecipe);

        WaferEditor = new WaferEditorViewModel();

        Viewer = new ImageViewerViewModel();

        // ★ 원본 뷰어 ↔ 두 에디터. 뷰어는 에디터를 모르고, 창이 잇는다(교수님 앱과 같다).
        Viewer.ApplyToDie = ApplyViewerRegions;
        Viewer.ApplyToWafer = WaferEditor.ApplyAlignment;
        Viewer.RegisterToWafer = WaferEditor.RegisterFeature;
        Viewer.SourceChanged += OnViewerSourceChanged;

        _useSyntheticCommand = new RelayCommand(UseSynthetic, () => _previewInput is not null);
        _showDefectInViewerCommand = new RelayCommand(
            ShowDefectInViewer, () => WaferEditor.SelectedDefect is { AbsX: not null, AbsY: not null } && WaferEditor.Scene is not null);

        WaferEditor.PropertyChanged += OnWaferEditorChanged;
        WaferEditor.ToDieEditor = PreviewWaferDie;

        _exportReportCommand = new RelayCommand(ExportReport, () => Defects.Count > 0);
        Defects.CollectionChanged += (_, _) => _exportReportCommand.RaiseCanExecuteChanged();
        PickReportPath = () =>
        {
            Microsoft.Win32.SaveFileDialog dialog = new() { Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json", FileName = "defects" };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        };
    }

    /// <summary>지금 보이는 작업공간 — 맨 위 바의 단추가 바꾼다.</summary>
    public Workspace SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetField(ref _selectedWorkspace, value) && value == Workspace.Viewer)
            {
                SyncOrigin();
            }
        }
    }

    /// <summary>블록 편집기 — 웨이퍼 편집 탭과 같은 부품.</summary>
    public GraphEditorViewModel Editor { get; }

    /// <summary>웨이퍼 탭. die 탭의 그래프를 그대로 레시피로 쓴다.</summary>
    public WaferViewModel Wafer { get; }

    /// <summary>웨이퍼 에디터 탭 — 웨이퍼 레시피(<c>*.wafer.json</c>)를 블록으로 짜고 돌린다. die 탭과 같은 편집기 부품.</summary>
    public WaferEditorViewModel WaferEditor { get; }

    /// <summary>원본 이미지 뷰어.</summary>
    public ImageViewerViewModel Viewer { get; }

    /// <summary>단계별 중간 이미지. 실행 순서대로 늘어선다.</summary>
    public ObservableCollection<PreviewItem> PreviewItems { get; } = [];

    /// <summary>
    /// 찾은 결함. ★ <see cref="Defect"/>를 그대로 담는다 — 화면용 포장지를 따로 안 만든다.
    /// 네모를 그릴 좌표(<c>Bounds</c>)가 이미 들어 있어서 감쌀 이유가 없다.
    /// </summary>
    public ObservableCollection<Defect> Defects { get; } = [];

    public ICommand RunPreviewCommand { get; }

    /// <summary>미리보기 입력 — 합성 견본 또는 원본 뷰어에서 넘겨받은 ROI · 정상 die.</summary>
    public string PreviewSourceText
        => _previewInput is { } input
            ? $"원본 {input.Title} · ROI ({input.Roi.X}, {input.Roi.Y}) {input.Roi.Width}×{input.Roi.Height} · 정상 die {input.Chips.Count}"
            : "합성 견본";

    /// <summary>미리보기가 원본으로 도나 — '합성으로 되돌리기' 단추가 보인다.</summary>
    public bool IsOriginalPreview => _previewInput is not null;

    /// <summary>'합성으로 되돌리기' — 원본 입력을 버리고 합성 견본으로 다시 돌린다.</summary>
    public ICommand UseSyntheticCommand => _useSyntheticCommand;

    /// <summary>'원본에서 보기' — 고른 웨이퍼 결함 자리로 원본 뷰어를 옮긴다(웨이퍼 오버레이의 결함 줄 더블클릭과 같다).</summary>
    public ICommand ShowDefectInViewerCommand => _showDefectInViewerCommand;

    /// <summary>'결함 보고서…' — 지금 미리보기 결함을 CSV · JSON 으로(확장자로 고른다). 레포 안이면 거부.</summary>
    public ICommand ExportReportCommand => _exportReportCommand;

    /// <summary>보고서를 쓸 파일 고르기 — 고르지 않으면 null. 시험에서 바꿔 끼운다.</summary>
    public Func<string?> PickReportPath { get; set; }

    /// <summary>보고서 알림 — 다이 오버레이의 결함 목록 위.</summary>
    public string ReportText
    {
        get => _reportText;
        private set => SetField(ref _reportText, value);
    }

    // ───────────────────────── 다이 오버레이 ─────────────────────────

    /// <summary>미리보기를 한 번이라도 돌렸나 — 아니면 오버레이 칸에 '먼저 미리보기를 돌리세요' 를 띄운다.</summary>
    public bool HasPreview
    {
        get => _hasPreview;
        private set => SetField(ref _hasPreview, value);
    }

    /// <summary>오버레이 배경 후보 — 검사 조각 + 미리보기의 단계 그림들. 모두 같은 크기(원본 픽셀)라 네모가 그대로 얹힌다.</summary>
    public ObservableCollection<PreviewItem> OverlayBackgrounds { get; } = [];

    public PreviewItem? SelectedOverlayBackground
    {
        get => _overlayBackground;
        set
        {
            if (SetField(ref _overlayBackground, value) && value is not null)
            {
                _overlayBackgroundKey = value.Key;
            }
        }
    }

    /// <summary>결함 네모 — 미리보기의 결함과 하나씩 짝.</summary>
    public ObservableCollection<OverlayBoxViewModel> DefectBoxes { get; } = [];

    /// <summary>고른 결함 — 결함 목록과 네모가 같이 본다. 네모를 누르면 여기가 바뀌고, 여기가 바뀌면 네모가 강조된다.</summary>
    public Defect? SelectedDefect
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
            }
        }
    }

    /// <summary>네모 선 두께 · 번호 글자 크기(원본 픽셀) — 그림이 화면에 맞게 늘어나도 적당한 굵기로 보이게 그림 크기에 맞춘다.</summary>
    public double OverlayLine => Math.Max(InspectedWidth, InspectedHeight) / 400;

    public double OverlayLabelSize => Math.Max(InspectedWidth, InspectedHeight) / 30;

    /// <summary>미리보기 썸네일 더블클릭 — 그 단계 그림을 배경으로 오버레이 칸을 연다.</summary>
    public ICommand OpenOverlayCommand { get; }

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

    // ───────────────────────── 미리보기 ─────────────────────────

    /// <summary>
    /// 합성 이미지(또는 원본 뷰어에서 넘겨받은 ROI · 정상 die)로 한 번 돌려 단계별 중간 이미지를 가져온다. 돌았으면 true.
    ///
    /// ★ 내보내기와 <b>같은 길</b>로 레시피를 만든다. 미리보기만 따로 만들면
    ///   "화면에선 되는데 파일로 내면 안 되는" 상태가 생긴다.
    /// </summary>
    private bool RunPreview()
    {
        Recipe recipe;

        try
        {
            recipe = Editor.ExportRecipe();
        }
        catch (RecipeExportException error)
        {
            Editor.ShowIssues(error.Errors);
            Editor.StatusText = "미리보기 불가 — 검증 오류";
            return false;
        }

        _previewOn = true;

        PreviewItems.Clear();
        Defects.Clear();
        ClearOverlay();

        try
        {
            // ★ 걸린 시간을 잰다. 슬라이더를 끌면 초당 수십 번 도는데,
            //   여기가 느리면 '부분 재실행'을 다시 넣어야 한다(week-5 §4①).
            //   필요한지는 추측하지 말고 이 숫자로 정한다.
            Stopwatch clock = Stopwatch.StartNew();
            PreviewResult result = _previewInput is { } input
                ? PreviewService.Run(Editor.Registry, recipe, input)
                : PreviewService.Run(Editor.Registry, recipe);
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

            FillOverlay(result);

            PreviewSummary = $"결함 {result.Defects.Count}개 · 중간 이미지 {result.Items.Count}장 · " +
                             $"{clock.Elapsed.TotalMilliseconds:F1} ms";
            Editor.StatusText = "미리보기 갱신됨";
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                          or KeyNotFoundException or InvalidCastException)
        {
            // ★ 검증을 지나도 돌리다 터지는 것이 있다 — 포맷이 안 맞거나 파라미터 조합이 나쁠 때.
            PreviewSummary = "돌리다 멈췄습니다";
            Editor.ShowRunError(error.Message);
            Editor.StatusText = "미리보기 실패";
            return false;
        }
    }

    /// <summary>
    /// 지금 미리보기 결함을 보고서로 쓴다(교수님 <c>DefectReport</c> 형식).
    /// ★ 환산 값은 입력이 <b>원본</b>일 때만 뷰어의 µm/px 를 쓴다 — 합성 견본에 실제 µm 를 붙이면 지어낸 숫자가 된다.
    /// ★ 실데이터에서 나온 숫자라 레포 안이면 쓰지 않는다.
    /// </summary>
    private void ExportReport()
    {
        if (Defects.Count == 0 || PickReportPath() is not { } file)
        {
            return;
        }

        if (RepositoryGuard.RepositoryOf(file) is { } repository)
        {
            ReportText = $"저장 안 함 — 레포 안입니다({repository}). 보고서는 레포 밖 폴더에 저장하세요";
            return;
        }

        Calibration calibration = _previewInput is not null ? Viewer.Calibration : Calibration.Uncalibrated;
        string source = _previewInput is { } input ? $"원본 {input.Title}" : "합성 견본";
        Roi? roi = _previewInput?.Roi;
        bool json = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase);

        try
        {
            File.WriteAllText(file, json
                ? DefectReport.ToJson([.. Defects], calibration, source, roi)
                : DefectReport.ToCsv([.. Defects], calibration));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportText = "저장 못 함 — " + error.Message;
            return;
        }

        ReportText = $"보고서 저장 — {Path.GetFileName(file)} (결함 {Defects.Count}개 · "
                     + (calibration.IsCalibrated ? $"{calibration.MicronsPerPixel} µm/px" : "미보정 — µm = px") + ")";
    }

    // ───────────────────────── 원본 뷰어 ↔ 에디터 ─────────────────────────

    /// <summary>원본 뷰어의 '다이 에디터에 적용' — 미리보기를 원본의 ROI · 정상 die 로 돌린다. 돌려준 글자는 뷰어가 띄운다.</summary>
    private string ApplyViewerRegions(PreviewInput input)
    {
        SetPreviewInput(input);

        return RunPreview()
            ? $"다이 에디터 미리보기를 원본으로 돌렸습니다 — {PreviewSummary}"
            : "다이 에디터 미리보기를 못 돌렸습니다 — 다이 · 블록 에디터의 검증 · 실행 오류를 보세요";
    }

    private void UseSynthetic()
    {
        SetPreviewInput(null);

        if (_previewOn)
        {
            RunPreview();
        }
    }

    /// <summary>
    /// 뷰어가 다른 원본을 열었다. ★ 원본으로 돌던 미리보기는 옛 원본의 영역이라 합성으로 되돌린다
    ///   (옛 원본은 뷰어가 이미 놓았다 — 그대로 두면 놓은 것을 읽는다).
    /// </summary>
    private void OnViewerSourceChanged()
    {
        _viewerScene = null;

        if (_previewInput is not null && !_previewFromWafer)
        {
            UseSynthetic();
        }
    }

    /// <summary>
    /// 웨이퍼 에디터의 'die → 다이 미리보기' — 그 die 의 레시피 파일을 다이 에디터에 열고(이미 열려 있으면 고치던 값 그대로),
    /// 러너와 같은 ROI · 골든으로 미리보기를 돌린 뒤 다이 블록 에디터로 넘어간다. 그 뒤로는 값을 고칠 때마다 이 die 가 다시 검사된다.
    /// </summary>
    private string PreviewWaferDie(DiePreviewRequest request)
    {
        SetPreviewInput(request.Input, fromWafer: true);

        if (Editor.FilePath is not { } open || !SamePath(open, request.RecipePath))
        {
            if (!Editor.Open(request.RecipePath))
            {
                return $"다이 미리보기 못 함 — 레시피를 열지 못했습니다: {request.RecipePath}";
            }
        }

        bool ran = RunPreview();
        SelectedWorkspace = Workspace.DieEditor;

        return ran
            ? $"{request.Input.Title} → 다이 블록 에디터({Path.GetFileName(request.RecipePath)} · 골든 {request.Input.Chips.Count}장) — {PreviewSummary}"
            : $"{request.Input.Title} → 다이 블록 에디터 — 미리보기를 못 돌렸습니다(검증 · 실행 오류를 보세요)";
    }

    private void SetPreviewInput(PreviewInput? input, bool fromWafer = false)
    {
        _previewInput = input;
        _previewFromWafer = input is not null && fromWafer;
        OnPropertyChanged(nameof(PreviewSourceText));
        OnPropertyChanged(nameof(IsOriginalPreview));
        _useSyntheticCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 뷰어에 들어올 때 — 웨이퍼 레시피의 die(0,0) 원점을 뷰어 칸에 옮긴다.
    /// ★ 레시피 값이 지난번과 같으면 덮지 않는다 — 뷰어에서 고치던 값을 지키려고.
    /// </summary>
    private void SyncOrigin()
    {
        if (WaferEditor.AlignmentOrigin() is { } origin && origin != _syncedOrigin)
        {
            Viewer.OriginX = origin.X;
            Viewer.OriginY = origin.Y;
            _syncedOrigin = origin;
        }
    }

    /// <summary>
    /// 고른 웨이퍼 결함 자리로 원본 뷰어를 옮긴다(교수님 <c>ShowWaferDefectCore</c>).
    /// 그 사진이 뷰어에 없으면 연다(파일이면 같은 경로, 합성이면 복사본) → 결함 전부 빨강 · 고른 것 노랑 → 그 자리를 가운데에.
    /// </summary>
    private void ShowDefectInViewer()
    {
        if (WaferEditor.Scene is not { } scene || WaferEditor.SelectedDefect is not { AbsX: { } x, AbsY: { } y } chosen)
        {
            return;
        }

        if (!ViewerShows(scene))
        {
            if (scene.Path is { } path)
            {
                Viewer.Open(path);
            }
            else
            {
                Viewer.Load(CopyOf(scene.Source), scene.Title + " (웨이퍼 에디터의 견본)");
            }

            // 못 열었으면 뷰어 아래 줄에 까닭이 떠 있다 — 칸만 넘긴다.
            if (scene.Path is { } wanted && (Viewer.SourcePath is not { } opened || !SamePath(opened, wanted)))
            {
                SelectedWorkspace = Workspace.Viewer;
                return;
            }

            _viewerScene = scene;
        }

        Viewer.ShowMarkers(WaferEditor.Defects.Where(d => d.AbsX is not null && d.AbsY is not null).Select(MarkerOf));
        Viewer.Highlight(chosen);
        SelectedWorkspace = Workspace.Viewer;
        Viewer.CenterOn(x, y);
    }

    private bool ViewerShows(WaferScene scene)
        => ReferenceEquals(_viewerScene, scene)
           || (scene.Path is { } path && Viewer.SourcePath is { } opened && SamePath(path, opened));

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>합성 사진의 복사본 — 뷰어가 놓을 것이라 웨이퍼 에디터의 사진과 따로 산다.</summary>
    private static MatImageSource CopyOf(IImageSource source)
        => new(source.ReadRegion(new Roi(0, 0, source.Width, source.Height)));

    /// <summary>결함 표시 하나 — 결함 가운데(웨이퍼 절대 좌표) + die 안 크기, 작으면 12 px.</summary>
    private OverlayBoxViewModel MarkerOf(WaferDefect d)
    {
        MarkerBox box = RegionTool.Marker(d.AbsX!.Value, d.AbsY!.Value, d.Defect.Bounds.Width, d.Defect.Bounds.Height);

        return new OverlayBoxViewModel(
            d, box.X, box.Y, box.Width, box.Height,
            d.Defect.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"die(c{d.DieCol},r{d.DieRow}) {d.Zone} #{d.Defect.Id}",
            item => WaferEditor.SelectedDefect = (WaferDefect)item);
    }

    /// <summary>웨이퍼 결함을 고르면 뷰어의 노랑 표시도 따라가고, 결과가 지워지면 뷰어의 표시도 지운다(낡은 결과다).</summary>
    private void OnWaferEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WaferEditorViewModel.SelectedDefect):
                Viewer.Highlight(WaferEditor.SelectedDefect);
                _showDefectInViewerCommand.RaiseCanExecuteChanged();
                break;

            case nameof(WaferEditorViewModel.HasResult) when !WaferEditor.HasResult:
                Viewer.ClearMarkers();
                break;

            // ★ 웨이퍼 사진이 바뀌었다(옛 사진은 놓였다) — 그 사진으로 돌던 다이 미리보기는 합성으로 되돌린다.
            case nameof(WaferEditorViewModel.OverviewImage) when _previewFromWafer:
                UseSynthetic();
                break;
        }
    }

    /// <summary>
    /// 오버레이를 새 미리보기 결과로 채운다. ★ 고른 배경은 이름으로 다시 찾는다 — 값을 고칠 때마다 미리보기가 다시 돌아도
    ///   보던 단계 그림이 그대로 보이게.
    /// </summary>
    private void FillOverlay(PreviewResult result)
    {
        string? keep = _overlayBackgroundKey;

        OverlayBackgrounds.Add(new PreviewItem("검사 조각", result.Inspected));

        foreach (PreviewItem item in result.Items)
        {
            OverlayBackgrounds.Add(item);
        }

        SelectedOverlayBackground = OverlayBackgrounds.FirstOrDefault(b => b.Key == keep) ?? OverlayBackgrounds[0];

        foreach (Defect defect in result.Defects)
        {
            BoundingBox b = defect.Bounds;
            DefectBoxes.Add(new OverlayBoxViewModel(
                defect, b.X, b.Y, b.Width, b.Height,
                defect.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"#{defect.Id} · ({b.X}, {b.Y}) {b.Width}×{b.Height} · 면적 {defect.Area}",
                item => SelectedDefect = (Defect)item));
        }

        OnPropertyChanged(nameof(OverlayLine));
        OnPropertyChanged(nameof(OverlayLabelSize));
        HasPreview = true;
    }

    private void ClearOverlay()
    {
        SelectedDefect = null;
        DefectBoxes.Clear();
        OverlayBackgrounds.Clear();
        HasPreview = false;
    }

    private static EditorGraph StandardGraph()
    {
        EditorGraph graph = SampleGraph.Standard();
        LayoutEngine.Layered(graph, GraphEditorViewModel.CanvasLeft);
        return graph;
    }
}
