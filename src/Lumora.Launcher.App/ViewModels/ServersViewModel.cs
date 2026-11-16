using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;

namespace Lumora.Launcher.App.ViewModels;

public partial class ServersViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly SemaphoreSlim _detailsThrottle = new(4);
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _detailsCts;
    private Task _detailsTask = Task.CompletedTask;
    private bool _isActive;

    public ServersViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
    }

    public ObservableCollection<ServerCardViewModel> Servers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? LoadError { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    public bool IsEmpty => !IsLoading && LoadError is null && Servers.Count == 0;

    public void Activate()
    {
        if (_isActive)
            return;
        _isActive = true;
        RefreshCommand.Execute(null);
    }

    public void Deactivate()
    {
        _isActive = false;
        _refreshCts?.Cancel();
        _detailsCts?.Cancel();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
            return;
        var refreshCts = new CancellationTokenSource();
        _refreshCts = refreshCts;
        IsLoading = true;
        LoadError = null;
        try
        {
            var list = await _services.Api.GetServersAsync(_shell.Session, refreshCts.Token);
            // Keep cards that are installing/running so their progress is not lost.
            var busy = Servers.Where(s => s.IsBusy).ToDictionary(s => s.Dto.Build.Id);
            Servers.Clear();
            foreach (var dto in list)
                Servers.Add(busy.TryGetValue(dto.Build.Id, out var card) ? card : new ServerCardViewModel(_services, _shell, dto, UpdateSummary));
            UpdateSummary();
            await StartDetailsAsync(Servers.ToArray());
        }
        catch (Lumora.Launcher.Core.Services.SessionExpiredException)
        {
            _services.Toasts.Error("Сессия истекла — войдите заново");
            _shell.LogoutCommand.Execute(null);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            // Leaving the server page cancels the in-flight list request.
        }
        catch (System.Net.Http.HttpRequestException)
        {
            LoadError = $"Сервер {_services.Settings.AuthServerUrl} недоступен. Проверьте интернет или адрес в настройках.";
        }
        catch (Exception e)
        {
            LoadError = "Не удалось загрузить список серверов: " + e.Message;
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, refreshCts))
                _refreshCts = null;
            refreshCts.Dispose();
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task StartDetailsAsync(IReadOnlyList<ServerCardViewModel> cards)
    {
        await CancelDetailsAsync();
        if (!_isActive || cards.Count == 0)
            return;

        var cts = new CancellationTokenSource();
        _detailsCts = cts;
        _detailsTask = LoadDetailsAsync(cards, cts.Token);
    }

    private async Task CancelDetailsAsync()
    {
        var cts = _detailsCts;
        if (cts is null)
            return;

        _detailsCts = null;
        cts.Cancel();
        try
        {
            await _detailsTask;
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task LoadDetailsAsync(IReadOnlyList<ServerCardViewModel> cards, CancellationToken ct)
    {
        async Task LoadCardAsync(ServerCardViewModel card)
        {
            try
            {
                await _detailsThrottle.WaitAsync(ct);
                try
                {
                    await card.LoadDetailsAsync(ct);
                }
                finally
                {
                    _detailsThrottle.Release();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Leaving the server page or refreshing cancels all outstanding details requests.
            }
            catch (Exception)
            {
                // A single card must not fault the rest of the server list refresh.
            }
        }

        await Task.WhenAll(cards.Select(LoadCardAsync));
    }

    private void UpdateSummary()
    {
        var players = Servers.Sum(s => s.Players);
        Summary = $"{Text.Plural(Servers.Count, "сервер", "сервера", "серверов")} · {Text.Plural(players, "игрок", "игрока", "игроков")} онлайн";
    }
}
