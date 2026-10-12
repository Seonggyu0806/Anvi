using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using DiePipeline.Core.Configuration;
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

/// <summary>
/// 웨이퍼 에디터 탭 — 웨이퍼 레시피(<c>*.wafer.json</c>)를 블록으로 짜고(<see cref="Editor"/>), 그 자리에서 돌린다(실행 칸).
///
/// ★ 편집은 die 탭과 <b>같은 부품</b>이다. 다른 것은 설정(명부 · 팔레트 · 입구 블록 · 시작 파일 · 경로 옮기기)과 실행 칸뿐.
/// ★ 실행은 명령행 <c>wafer --recipe</c> 와 <b>같은 실행기</b>(<see cref="WaferRecipeRunner"/>)다 —
///   같은 레시피 · 같은 사진이면 같은 답이 나온다(시험: 편집기를_거친_견본은_파일과_같은_답).
/// ★ 원본 사진 위에 결과를 겹쳐 그리는 것은 여기서 안 한다(C — 오버레이). 여기는 칸 지도와 결함 표까지.
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

    private WaferScene? _scene;
    private CancellationTokenSource? _cancel;
    private bool _isBusy;
    private bool _isStale;
    private string _imageText = "사진 없음 — 합성 견본을 그리거나 사진을 여세요";
    private string _summaryText = "아직 안 돌렸습니다";
    private string _alignmentText = string.Empty;
    private string? _noteText;
    private int _mapCols = 1;

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
        });

        // ★ 레시피를 고치면 결과가 낡는다 — 웨이퍼는 오래 걸릴 수 있어 저절로 다시 돌리지는 않고, 낡았다고만 알린다.
        Editor.GraphChanged += () => IsStale = MapCells.Count > 0 || Defects.Count > 0 || _noteText is not null;

        _drawCommand = new RelayCommand(DrawSample, () => !IsBusy);
        _openImageCommand = new RelayCommand(OpenImage, () => !IsBusy);
        _runCommand = new RelayCommand(Run, () => !IsBusy && _scene is not null);
        _cancelCommand = new RelayCommand(() => _cancel?.Cancel(), () => IsBusy);
    }

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
        _scene?.Dispose();
    }

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

        using CancellationTokenSource cancel = new();
        _cancel = cancel;

        try
        {
            WaferInspectionResult result = await Task.Run(
                () => WaferRecipeRunner.RunIn(recipe, folder, scene.Source, _registry, cancel.Token), cancel.Token);

            Show(result);
            Editor.StatusText = "실행 끝";
        }
        catch (OperationCanceledException)
        {
            SummaryText = "멈췄습니다 — 결과는 버렸습니다(반쯤 본 웨이퍼를 결과처럼 보이지 않게)";
        }
        catch (Exception error) when (error is WaferRecipeException or RecipeLoadException or RecipeValidationException
                                          or ArgumentException or InvalidOperationException or IOException)
        {
            // ★ 예외 이름을 띄우지 않는다 — 블록을 잘못 이었거나 경로가 틀린 것을 사람 말로.
            SummaryText = "돌리지 못했습니다";
            Editor.ShowRunError(error.Message);
            Editor.StatusText = "실행 실패";
        }
        finally
        {
            _cancel = null;
            IsBusy = false;
        }
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

        foreach (WaferMapCell cell in WaferMap.Of(result))
        {
            MapCells.Add(cell);
        }

        foreach (WaferDefect defect in result.Defects!.Items)
        {
            Defects.Add(defect);
        }

        int e1 = result.Grid.Dies.Count(d => d.Zone == WaferZone.E1);
        int e0 = result.Grid.Dies.Count(d => d.Zone == WaferZone.E0);
        int outside = result.Grid.Dies.Count(d => d.Zone == WaferZone.Outside);

        SummaryText = $"die {result.Run!.DieCount}개(E1 {e1} · E0 {e0}" + (outside > 0 ? $" · 원판 밖 {outside}" : string.Empty) + ") · "
                      + $"결함 {result.Found!.Count}개(표에는 샘플링 후 {result.Defects.Count}개) · {result.InspectMs:F0} ms";
    }

    private void ClearResults()
    {
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