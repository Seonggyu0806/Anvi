using System.Collections.ObjectModel;
using System.Windows;
using DiePipeline.Core.Registry;
using DiePipeline.Editor;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 캔버스의 블록 하나. <b>두 종류</b>가 있다.
///
/// <list type="bullet">
///   <item><b>진짜 노드</b> — 레시피에 들어가는 것. <see cref="Model"/>과 <see cref="Descriptor"/>가 있다.</item>
///   <item><b>컨텍스트 입력</b> — <c>roi</c>·<c>chipRegions</c>. 실행기가 칠판에 미리 올려 주는 것이라
///     레시피의 노드가 아니다. 그래서 모델도 디스크립터도 없고, <b>출력 하나</b>만 있다.</item>
/// </list>
///
/// ★ 왜 컨텍스트도 블록으로 그리나: 안 그리면 선이 <b>화면 왼쪽 끝에서 그냥 튀어나온다.</b>
///   어디서 오는 건지 알 수가 없어서, 쓰는 사람이 엉뚱한 데 잇는다.
///   (교수님도 4.6에서 같은 이유로 컨텍스트 소스 노드를 넣었다.)
///
/// ★ 포트도 속성폼도 <b>디스크립터에서 나온다.</b> 노드 타입별 분기가 한 줄도 없다 —
///   여기에 <c>if (Type == "Binarize")</c>가 생기는 순간 이 층의 값어치가 사라진다.
///
/// ★ 포트는 <b>점</b>(<see cref="ConnectorViewModel"/>)이다 — 왼쪽 입력 · 오른쪽 출력. 점에서 점으로 끌어 잇는다(Nodify).
/// </summary>
public sealed class NodeViewModel : ObservableObject
{
    private readonly Action? _onChanged;

    private double _x;
    private double _y;
    private bool _hasError;

    /// <summary>진짜 파이프라인 블록.</summary>
    /// <param name="services">속성 칸이 묻는 것(기준 폴더 · 파일 고르기). 없으면 찾아보기가 아무 일도 안 한다.</param>
    public NodeViewModel(EditorNode model, INodeDescriptor descriptor, Action onChanged, FieldServices? services = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(descriptor);

        Model = model;
        Descriptor = descriptor;
        _onChanged = onChanged;
        _x = model.X;
        _y = model.Y;

        foreach (PortDescriptor port in descriptor.Inputs)
        {
            Input.Add(new ConnectorViewModel(this, port.Name, isInput: true));
        }

        foreach (PortDescriptor port in descriptor.Outputs)
        {
            Output.Add(new ConnectorViewModel(this, port.Name, isInput: false));
        }

        FieldServices fieldServices = services ?? FieldServices.None;

        Fields = [.. descriptor.Params.Select(p => new PropertyFieldViewModel(model, PropertyFormModel.FromParam(p), onChanged, fieldServices))];

        // ★ 서술자로 못 적는 목록 · 묶음 값(표식 자리 · 표식 조각 · 원판 · 수동 지정) — 표(ListParams)에 있는 것만.
        Lists = [.. ListParams.For(model.Type).Select(p => new ListParamViewModel(model, p, onChanged, fieldServices))];
    }

    /// <summary>컨텍스트 입력 블록 — 실행기가 넣어 주는 것이라 레시피에는 없다.</summary>
    public NodeViewModel(string contextKey, double x, double y)
    {
        IsContext = true;
        ContextKey = contextKey;
        _x = x;
        _y = y;

        // ★ 컨텍스트의 출력 이름은 키 그 자체다 — 그래서 선이 그 이름으로 이어진다.
        Output.Add(new ConnectorViewModel(this, contextKey, isInput: false));
        Fields = [];
        Lists = [];
    }

    /// <summary>컨텍스트 입력이면 <c>null</c>.</summary>
    public EditorNode? Model { get; }

    /// <summary>컨텍스트 입력이면 <c>null</c>.</summary>
    public INodeDescriptor? Descriptor { get; }

    public bool IsContext { get; }

    /// <summary>컨텍스트 입력일 때만 값이 있다(예: "roi").</summary>
    public string? ContextKey { get; }

    public string Id => IsContext ? ContextKey! : Model!.Id;

    /// <summary>블록 머리글. 컨텍스트는 타입이 없으니 "입력"이라고 적는다.</summary>
    public string Type => IsContext ? "입력" : Model!.Type;

    public string Category => Descriptor?.Category ?? "입력";

    /// <summary>블록 머리 — 컨텍스트는 키, 진짜 노드는 "종류 (id)".</summary>
    public string DisplayName => IsContext ? ContextKey! : $"{Model!.Type} ({Model.Id})";

    /// <summary>왼쪽 포트 점들(입력). 컨텍스트는 없다.</summary>
    public ObservableCollection<ConnectorViewModel> Input { get; } = [];

    /// <summary>오른쪽 포트 점들(출력).</summary>
    public ObservableCollection<ConnectorViewModel> Output { get; } = [];

    public IReadOnlyList<PropertyFieldViewModel> Fields { get; }

    /// <summary>목록 · 묶음 값 칸들 — 낱개 값(<see cref="Fields"/>) 아래에 그린다. 대부분의 블록은 비었다.</summary>
    public IReadOnlyList<ListParamViewModel> Lists { get; }

    /// <summary>검증 오류가 이 블록을 가리키면 true — 캔버스에서 빨간 테두리로 보인다.</summary>
    public bool HasError
    {
        get => _hasError;
        set => SetField(ref _hasError, value);
    }

    /// <summary>
    /// 캔버스 위치 — Nodify 가 블록을 끌면 여기에 쓴다. <see cref="X"/> · <see cref="Y"/> 와 같은 값이다.
    /// </summary>
    public Point Location
    {
        get => new(_x, _y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    // ★ 좌표는 이제 이 객체가 들고, 진짜 노드일 때만 모델에도 적는다.
    //   컨텍스트 블록은 모델이 없어서 어딘가에는 들고 있어야 한다.
    public double X
    {
        get => _x;
        set
        {
            if (SetField(ref _x, value))
            {
                if (Model is not null)
                {
                    Model.X = value;
                }

                OnPropertyChanged(nameof(Location));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetField(ref _y, value))
            {
                if (Model is not null)
                {
                    Model.Y = value;
                }

                OnPropertyChanged(nameof(Location));
            }
        }
    }

    /// <summary>
    /// 노드 끄기 체크박스. ★ 여기가 코어에 뚫어 둔 <c>NodeConfig.Enabled</c>로 이어진다.
    /// 끄면 <c>_onChanged()</c>가 재검증을 부른다 — 못 끄는 노드면 즉시 빨간 줄이 뜬다.
    ///
    /// ★ 컨텍스트 입력은 끌 수 없다 — 실행기가 넣어 주는 것이라 끌 대상이 아니다.
    /// </summary>
    public bool Enabled
    {
        get => !IsContext && Model!.Enabled;
        set
        {
            if (IsContext || Model!.Enabled == value)
            {
                return;
            }

            Model.Enabled = value;
            OnPropertyChanged();
            _onChanged?.Invoke();
        }
    }
}
