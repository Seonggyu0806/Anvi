using System.Globalization;
using System.Text.Json;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

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

    private bool _boolValue;
    private string _stringValue = string.Empty;

    public PropertyFieldViewModel(EditorNode node, PropertyField field, Action onChanged)
    {
        _node = node;
        _field = field;
        _onChanged = onChanged;

        LoadFromModel();
    }

    public string Name => _field.Name;

    public string? Description => _field.Description;

    public FieldControl Control => _field.Control;

    public IReadOnlyList<string>? Choices => _field.Choices;

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
                WriteString(value);
                _onChanged();
            }
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