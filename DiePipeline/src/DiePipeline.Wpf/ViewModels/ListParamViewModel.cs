using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 속성 칸의 '행 목록' 부품 — 목록 · 묶음 값 하나(표식 자리 · 표식 조각 · 원판 · 수동 지정).
///
/// ★ 부품은 하나다. 어느 값에 어떤 칸이 몇 개인지는 <see cref="ListParams"/> 표가 정한다 —
///   네 값마다 화면을 따로 만들지 않는다(교수님은 표식 목록 · 필터 체인을 따로 만들었다).
/// ★ 칸을 고칠 때마다 블록 값을 다시 쓰고 검증을 부른다 — 낱개 값 칸과 같은 길이다.
///   숫자 칸을 못 읽으면 블록 값을 안 건드린다(치는 중인 글자를 0 으로 쓰지 않는다).
/// ★ 되돌리기(Ctrl+Z) 대상이 아니다 — 낱개 값 칸과 같다. 되돌리기는 블록 · 선의 추가 · 삭제만 기록한다.
/// </summary>
public sealed class ListParamViewModel : ObservableObject
{
    private readonly EditorNode _node;
    private readonly ListParam _param;
    private readonly Action _onChanged;
    private readonly FieldServices _services;

    public ListParamViewModel(EditorNode node, ListParam param, Action onChanged, FieldServices services)
    {
        _node = node;
        _param = param;
        _onChanged = onChanged;
        _services = services;

        JsonElement? value = node.Params.TryGetValue(param.Param, out JsonElement v) ? v : null;

        foreach (string[] cells in ListParams.Read(param, value))
        {
            Rows.Add(new ListRowViewModel(this, cells));
        }

        AddCommand = new RelayCommand(() =>
        {
            Rows.Add(new ListRowViewModel(this, ListParams.NewRow(_param)));
            Write();
        });
    }

    /// <summary>값 이름(JSON) — 예: <c>features</c>.</summary>
    public string Name => _param.Param;

    public string Title => _param.Title;

    public string Description => _param.Description;

    /// <summary>칸 머리 글자 — 행들 위에 한 줄로.</summary>
    public IReadOnlyList<string> Headers => [.. _param.Columns.Select(c => c.Header)];

    /// <summary>행을 더하고 빼는 목록인가. 아니면 묶음 하나(원판) — '씀' 체크로 켜고 끈다.</summary>
    public bool IsList => _param.Layout != ListLayout.Single;

    public bool IsOptional => _param.Layout == ListLayout.Single;

    /// <summary>순서가 뜻이 있는 목록인가(표식 조각) — ▲▼ 를 보인다.</summary>
    public bool CanReorder => _param.Reorderable;

    public ObservableCollection<ListRowViewModel> Rows { get; } = [];

    public ICommand AddCommand { get; }

    /// <summary>
    /// 묶음 하나짜리(원판)를 쓰나. 켜면 기본 글자로 한 행, 끄면 행을 지우고 레시피에서 뺀다.
    /// </summary>
    public bool Enabled
    {
        get => Rows.Count > 0;
        set
        {
            if (value == Enabled)
            {
                return;
            }

            if (value)
            {
                Rows.Add(new ListRowViewModel(this, ListParams.NewRow(_param)));
            }
            else
            {
                Rows.Clear();
            }

            OnPropertyChanged();
            Write();
        }
    }

    internal ListParam Param => _param;

    internal FieldServices Services => _services;

    internal void Remove(ListRowViewModel row)
    {
        Rows.Remove(row);
        OnPropertyChanged(nameof(Enabled));
        Write();
    }

    internal void Move(ListRowViewModel row, int step)
    {
        int from = Rows.IndexOf(row);
        int to = from + step;

        if (from < 0 || to < 0 || to >= Rows.Count)
        {
            return;
        }

        Rows.Move(from, to);
        Write();
    }

    /// <summary>행들 → 블록 값. 못 읽는 숫자 칸이 있으면 아무것도 안 쓴다.</summary>
    internal void Write()
    {
        if (!ListParams.TryWrite(_param, [.. Rows.Select(r => r.Texts)], out JsonElement? value))
        {
            return;
        }

        if (value is { } json)
        {
            _node.Params[_param.Param] = json;
        }
        else
        {
            _node.Params.Remove(_param.Param);
        }

        _onChanged();
    }
}

/// <summary>행 하나 — 칸들 + ✕ · ▲▼.</summary>
public sealed class ListRowViewModel
{
    public ListRowViewModel(ListParamViewModel owner, IReadOnlyList<string> texts)
    {
        Cells = [.. owner.Param.Columns.Select((c, i) => new ListCellViewModel(owner, c, i < texts.Count ? texts[i] : c.Default))];

        RemoveCommand = new RelayCommand(() => owner.Remove(this));
        MoveUpCommand = new RelayCommand(() => owner.Move(this, -1));
        MoveDownCommand = new RelayCommand(() => owner.Move(this, +1));
    }

    public IReadOnlyList<ListCellViewModel> Cells { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    internal IReadOnlyList<string> Texts => [.. Cells.Select(c => c.Text)];
}

/// <summary>칸 하나 — 글자 입력 · 고르기 · 경로(+ 찾아보기).</summary>
public sealed class ListCellViewModel : ObservableObject
{
    private readonly ListParamViewModel _owner;
    private readonly ListColumn _column;
    private string _text;

    public ListCellViewModel(ListParamViewModel owner, ListColumn column, string text)
    {
        _owner = owner;
        _column = column;
        _text = text;

        BrowseCommand = new RelayCommand(Browse);
    }

    public string Header => _column.Header;

    public FieldControl Control => _column.Control;

    public IReadOnlyList<string>? Choices => _column.Choices;

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value ?? string.Empty))
            {
                _owner.Write();
            }
        }
    }

    public ICommand BrowseCommand { get; }

    private void Browse()
    {
        if (_owner.Services.PickFile() is { } file)
        {
            Text = EditorPaths.Relative(_owner.Services.Folder(), file);
        }
    }
}