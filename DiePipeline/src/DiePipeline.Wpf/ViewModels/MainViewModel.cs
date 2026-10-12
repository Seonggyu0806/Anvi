using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiePipeline.Core.Configuration;
using DiePipeline.Core.Domain;
using DiePipeline.Core.Registry;
using DiePipeline.Cv;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;
using DiePipeline.Wpf.Preview;

namespace DiePipeline.Wpf.ViewModels;

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

    private string _previewSummary = "아직 안 돌렸습니다";
    private BitmapSource? _inspected;
    private double _inspectedWidth = 1;
    private double _inspectedHeight = 1;

    // ★ 한 번이라도 미리보기를 눌렀는가. 누르기 전에는 멋대로 돌리지 않는다.
    private bool _previewOn;

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

        RunPreviewCommand = new RelayCommand(RunPreview);

        // ★ 웨이퍼 탭은 die 탭의 '지금 그래프' 를 레시피로 쓴다 — 따로 들고 있으면 둘이 갈라진다.
        //   내보내기와 같은 길(검증 강제)을 탄다 — 검증에 막히면 웨이퍼 검사도 안 돌아간다.
        Wafer = new WaferViewModel(registry, Editor.ExportRecipe);

        WaferEditor = new WaferEditorViewModel();
    }

    /// <summary>블록 편집기 — 웨이퍼 편집 탭과 같은 부품.</summary>
    public GraphEditorViewModel Editor { get; }

    /// <summary>웨이퍼 탭. die 탭의 그래프를 그대로 레시피로 쓴다.</summary>
    public WaferViewModel Wafer { get; }

    /// <summary>웨이퍼 에디터 탭 — 웨이퍼 레시피(<c>*.wafer.json</c>)를 블록으로 짜고 돌린다. die 탭과 같은 편집기 부품.</summary>
    public WaferEditorViewModel WaferEditor { get; }

    /// <summary>단계별 중간 이미지. 실행 순서대로 늘어선다.</summary>
    public ObservableCollection<PreviewItem> PreviewItems { get; } = [];

    /// <summary>
    /// 찾은 결함. ★ <see cref="Defect"/>를 그대로 담는다 — 화면용 포장지를 따로 안 만든다.
    /// 네모를 그릴 좌표(<c>Bounds</c>)가 이미 들어 있어서 감쌀 이유가 없다.
    /// </summary>
    public ObservableCollection<Defect> Defects { get; } = [];

    public ICommand RunPreviewCommand { get; }

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
            recipe = Editor.ExportRecipe();
        }
        catch (RecipeExportException error)
        {
            Editor.ShowIssues(error.Errors);
            Editor.StatusText = "미리보기 불가 — 검증 오류";
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
            PreviewResult result = PreviewService.Run(Editor.Registry, recipe);
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
            Editor.StatusText = "미리보기 갱신됨";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                          or KeyNotFoundException or InvalidCastException)
        {
            // ★ 검증을 지나도 돌리다 터지는 것이 있다 — 포맷이 안 맞거나 파라미터 조합이 나쁠 때.
            PreviewSummary = "돌리다 멈췄습니다";
            Editor.ShowRunError(error.Message);
            Editor.StatusText = "미리보기 실패";
        }
    }

    private static EditorGraph StandardGraph()
    {
        EditorGraph graph = SampleGraph.Standard();
        LayoutEngine.Layered(graph, GraphEditorViewModel.CanvasLeft);
        return graph;
    }
}
