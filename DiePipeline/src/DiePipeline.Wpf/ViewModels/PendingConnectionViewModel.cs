using System.Windows.Input;
using DiePipeline.Wpf.Mvvm;

namespace DiePipeline.Wpf.ViewModels;

/// <summary>
/// 끌고 있는 선(Nodify PendingConnection) — 포트 점에서 끌기 시작하면 그 점을 들고 있다가, 다른 점에 놓는 순간 잇는다.
/// ★ 잇는 규칙(입력 포트엔 선 하나 · 방향)은 여기가 아니라 받은 <c>connect</c> 가 정한다.
/// </summary>
public sealed class PendingConnectionViewModel : ObservableObject
{
    private ConnectorViewModel? _source;

    public PendingConnectionViewModel(Action<ConnectorViewModel, ConnectorViewModel> connect)
    {
        ArgumentNullException.ThrowIfNull(connect);

        StartCommand = new RelayCommand<ConnectorViewModel>(start => Source = start);
        FinishCommand = new RelayCommand<ConnectorViewModel>(end =>
        {
            if (Source is not null)
            {
                connect(Source, end);
            }

            Source = null;
        });
    }

    /// <summary>끌기 시작한 점. 놓으면 비운다.</summary>
    public ConnectorViewModel? Source
    {
        get => _source;
        private set => SetField(ref _source, value);
    }

    public ICommand StartCommand { get; }

    public ICommand FinishCommand { get; }
}