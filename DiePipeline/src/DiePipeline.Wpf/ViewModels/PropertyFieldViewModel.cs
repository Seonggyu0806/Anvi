using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 속성 칸이 편집기에 묻는 것 — 경로를 풀 기준 폴더와 파일 고르기(찾아보기).
/// ★ 파일 고르기는 대화상자라, 화면 없이 검사할 때는 편집기에서 바꿔 끼운다.
/// </summary>
public sealed record FieldServices(Func<string?> Folder, Func<string?> PickFile)
{
    /// <summary>기준 폴더도 파일 고르기도 없음 — 찾아보기를 눌러도 아무 일 없다.</summary>
    public static FieldServices None { get; } = new(() => null, () => null);
}

/// <summary>
/// 속성폼의 칸 하나. <see cref="EditorNode.Params"/>(JsonElement)와 양방향으로 오간다.
///
/// ★ 어떤 컨트롤로 그릴지는 <see cref="PropertyField.Control"/>이 정한다.
///   화면은 <b>종류별 틀 5개</b>만 갖고, 노드 타입은 하나도 모른다.
///
/// ★ 값을 화면용으로 들고 있는 이유: 사용자가 "0." 까지만 쳤을 때가 있다.
///   그 순간 모델에 쓰면 못 쓰는 값이 들어간다. <b>화면 글자와 모델 값을 따로</b> 둔다.
/// </summary>
public sealed class PropertyFieldViewModel : ObservableObject
{
    private readonly EditorNode _node;
    private readonly PropertyField _field;
    private readonly Action _onChanged;
    private readonly FieldServices _services;

    private bool _boolValue;
    private string _stringValue = string.Empty;

    public PropertyFieldViewModel(EditorNode node, PropertyField field, Action onChanged, FieldServices? services = null)
    {
        _node = node;
        _field = field;
        _onChanged = onChanged;
        _services = services ?? FieldServices.None;

        BrowseCommand = new RelayCommand(Browse);

        LoadFromModel();
    }

    public string Name => _field.Name;

    public string? Description => _field.Description;

    public FieldControl Control => _field.Control;

    public IReadOnlyList<string>? Choices => _field.Choices;

    /// <summary>경로 칸의 '찾아보기' — 고른 파일을 레시피 폴더 기준 상대 경로로 적는다.</summary>
    public ICommand BrowseCommand { get; }

    // ── 슬라이더용. 붙일지·몇 칸씩인지는 아래층(PropertyField)이 정한다 ──

    public bool HasRange => _field.HasRange;

    public double Min => _field.Min ?? 0;

    public double Max => _field.Max ?? 0;

    public double Step => _field.Step;

    /// <summary>
    /// 슬라이더가 주고받는 숫자. ★ 값은 <b>글자 쪽(StringValue) 하나에만</b> 산다 —
    /// 이건 그걸 숫자로 비춰 보는 창이다. 둘이 따로 값을 들면 언젠가 어긋난다.
    /// </summary>
    public double NumberValue
    {
        get => double.TryParse(_stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : Min;
        set
        {
            // ★ 정수 칸은 반올림, 실수 칸은 소수 셋째 자리까지.
            //   슬라이더는 0.23478912 같은 값을 주는데, 그대로 두면 레시피 파일이 그 숫자로 더러워진다.
            double rounded = Control == FieldControl.Integer ? Math.Round(value) : Math.Round(value, 3);

            StringValue = rounded.ToString(CultureInfo.InvariantCulture);
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (SetField(ref _boolValue, value))
            {
                _node.Params[Name] = JsonSerializer.SerializeToElement(value);
                _onChanged();
            }
        }
    }

    /// <summary>정수·실수·콤보·자유입력이 전부 이 하나를 쓴다. 화면에서는 어차피 다 글자다.</summary>
    public string StringValue
    {
        get => _stringValue;
        set
        {
            if (SetField(ref _stringValue, value))
            {
                // ★ 칸에 직접 쳤을 때 슬라이더 손잡이도 따라오게 알린다.
                OnPropertyChanged(nameof(NumberValue));

                WriteString(value);
                _onChanged();
            }
        }
    }

    private void Browse()
    {
        if (_services.PickFile() is { } file)
        {
            StringValue = EditorPaths.Relative(_services.Folder(), file);
        }
    }

    private void WriteString(string value)
    {
        switch (_field.Control)
        {
            // ★ InvariantCulture 로 고정한다. 나라 설정에 따라 소수점이 쉼표인 데가 있는데,
            //   그러면 "0,2" 가 레시피 파일에 들어가서 다른 컴퓨터에서 안 읽힌다.
            case FieldControl.Integer
                when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt):
                _node.Params[Name] = JsonSerializer.SerializeToElement(parsedInt);
                break;

            case FieldControl.Number
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble):
                _node.Params[Name] = JsonSerializer.SerializeToElement(parsedDouble);
                break;

            // ★★ 숫자 칸인데 못 읽으면 <b>모델을 안 건드린다.</b>
            //   "0." 까지 친 순간이나 지우는 중인 빈 칸이 여기로 온다.
            //   여기서 0을 써 버리면 사용자가 타이핑하는 동안 임계값이 0으로 튄다.
            case FieldControl.Integer or FieldControl.Number:
                break;

            default:
                _node.Params[Name] = JsonSerializer.SerializeToElement(value);
                break;
        }
    }

    private void LoadFromModel()
    {
        if (_node.Params.TryGetValue(Name, out JsonElement element))
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.True or JsonValueKind.False:
                    _boolValue = element.GetBoolean();
                    _stringValue = _boolValue.ToString();
                    return;

                case JsonValueKind.Number:
                    _stringValue = element.GetDouble().ToString(CultureInfo.InvariantCulture);
                    return;

                case JsonValueKind.String:
                    _stringValue = element.GetString() ?? string.Empty;
                    return;
            }
        }

        // ★ 레시피에 안 적혀 있으면 디스크립터의 기본값을 보여 준다.
        //   빈 칸으로 두면 "이게 뭘 쓰고 있는 건지" 를 사용자가 알 수 없다.
        object? fallback = _field.Default;

        switch (fallback)
        {
            case bool flag:
                _boolValue = flag;
                _stringValue = flag.ToString();
                break;

            case null:
                _stringValue = string.Empty;
                break;

            case IFormattable formattable:
                _stringValue = formattable.ToString(null, CultureInfo.InvariantCulture);
                break;

            default:
                _stringValue = fallback.ToString() ?? string.Empty;
                break;
        }
    }
}