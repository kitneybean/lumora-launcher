using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Contracts;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.App.ViewModels;

public partial class OptionalModViewModel(string path, string title, bool enabled, System.Action<OptionalModViewModel> changed) : ObservableObject
{
    public string Path { get; } = path;
    public string Title { get; } = title;
    public string FileName => System.IO.Path.GetFileName(Path);

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = enabled;

    partial void OnIsEnabledChanged(bool value) => changed(this);
}

/// <summary>Settings of one build: overrides, optional mods, maintenance.</summary>
public partial class InstanceSettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly ServerCardViewModel _card;
    private readonly InstanceSettings _instance;

    public InstanceSettingsViewModel(AppServices services, ShellViewModel shell, ServerCardViewModel card)
    {
        _services = services;
        _shell = shell;
        _card = card;
        _instance = services.Store.LoadInstance(services.Settings, card.Dto.Build.Id);
        _overrideGlobal = _instance.OverrideGlobal;

        var effective = SettingsResolver.Resolve(services.Settings, _instance);
        Editor = new GameSettingsEditorViewModel(services, _instance.Game, effective, card.Manifest?.RecommendedMemoryMb ?? 0, Save);

        foreach (var file in card.Manifest?.Files.Where(f => f.Mode != FileInstallMode.Required) ?? [])
        {
            var enabled = _instance.OptionalFiles.TryGetValue(file.Path, out var on) ? on : file.Mode == FileInstallMode.OptionalOn;
            OptionalMods.Add(new OptionalModViewModel(file.Path, file.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(file.Path), enabled, OnModChanged));
        }
    }

    public string Title => _card.Name;
    public string Subtitle => $"{_card.MinecraftVersion} · {_card.LoaderText}" + (_card.ModsText is { } m ? $" · {m}" : "");

    public GameSettingsEditorViewModel Editor { get; }

    public ObservableCollection<OptionalModViewModel> OptionalMods { get; } = [];
    public bool HasOptionalMods => OptionalMods.Count > 0;

    private bool _overrideGlobal;

    public bool OverrideGlobal
    {
        get => _overrideGlobal;
        set
        {
            if (SetProperty(ref _overrideGlobal, value))
            {
                _instance.OverrideGlobal = value;
                Save();
            }
        }
    }

    private void OnModChanged(OptionalModViewModel mod)
    {
        _instance.OptionalFiles[mod.Path] = mod.IsEnabled;
        Save();
        _services.Toasts.Show(mod.IsEnabled ? $"{mod.Title} будет включён при следующем запуске" : $"{mod.Title} будет выключен при следующем запуске");
    }

    private void Save() => _services.Store.SaveInstance(_services.Settings, _card.Dto.Build.Id, _instance);

    [RelayCommand]
    private void Back() => _shell.GoTo(_shell.Servers);

    [RelayCommand]
    private void OpenFolder() => _card.OpenFolderCommand.Execute(null);

    [RelayCommand]
    private void Verify()
    {
        _services.Game.MarkForReinstall(_card.Dto.Build.Id);
        _card.MarkForReinstall();
        _services.Toasts.Show("Нажмите «Установить» на карточке — все файлы будут проверены заново");
    }

    [RelayCommand]
    private void Reinstall()
    {
        _services.Game.MarkForReinstall(_card.Dto.Build.Id);
        _card.MarkForReinstall();
        _services.Toasts.Show($"Сборка «{Title}» будет установлена заново");
        Back();
    }
}
