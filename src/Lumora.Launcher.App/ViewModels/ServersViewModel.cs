using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;

namespace Lumora.Launcher.App.ViewModels;

public partial class ServersViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;

    public ServersViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        _ = RefreshAsync();
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        LoadError = null;
        try
        {
            var list = await _services.Api.GetServersAsync(_shell.Session);
            // Keep cards that are installing/running so their progress is not lost.
            var busy = Servers.Where(s => s.IsBusy).ToDictionary(s => s.Dto.Build.Id);
            Servers.Clear();
            foreach (var dto in list)
                Servers.Add(busy.TryGetValue(dto.Build.Id, out var card) ? card : new ServerCardViewModel(_services, _shell, dto, UpdateSummary));
            foreach (var card in Servers)
                _ = card.LoadDetailsAsync();
            UpdateSummary();
        }
        catch (Lumora.Launcher.Core.Services.SessionExpiredException)
        {
            _services.Toasts.Error("Сессия истекла — войдите заново");
            _shell.LogoutCommand.Execute(null);
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
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void UpdateSummary()
    {
        var players = Servers.Sum(s => s.Players);
        Summary = $"{Text.Plural(Servers.Count, "сервер", "сервера", "серверов")} · {Text.Plural(players, "игрок", "игрока", "игроков")} онлайн";
    }
}
