using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;

namespace Lumora.Launcher.App.ViewModels;

public partial class SkinViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;

    public SkinViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        shell.PropertyChanged += OnShellChanged;
    }

    public string PlayerName => _shell.PlayerName;
    public Bitmap? Skin => _shell.SkinBitmap;
    public bool HasSkin => Skin is not null;

    public bool IsSlim
    {
        get => _shell.SlimModel;
        set
        {
            if (_shell.SlimModel != value)
            {
                _shell.SlimModel = value;
                if (_shell.SkinPng is { } png)
                    _ = SendAsync(png, value, value ? "Теперь тонкая модель" : "Теперь классическая модель");
            }
        }
    }

    public bool IsClassic
    {
        get => !IsSlim;
        set => IsSlim = !value;
    }

    [ObservableProperty]
    public partial bool ShowBack { get; set; }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SkinBitmap))
        {
            OnPropertyChanged(nameof(Skin));
            OnPropertyChanged(nameof(HasSkin));
        }
        else if (e.PropertyName == nameof(ShellViewModel.SlimModel))
        {
            OnPropertyChanged(nameof(IsSlim));
            OnPropertyChanged(nameof(IsClassic));
        }
    }

    [RelayCommand]
    private void Flip() => ShowBack = !ShowBack;

    [RelayCommand]
    private async Task UploadAsync()
    {
        var path = await _services.Platform.PickFileAsync("Выберите скин (PNG 64×64)", FilePickerFileTypes.ImagePng);
        if (path is null)
            return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using (var probe = new Bitmap(new MemoryStream(bytes)))
            {
                var size = probe.PixelSize;
                if (size.Width != 64 || (size.Height != 64 && size.Height != 32))
                {
                    _services.Toasts.Error($"Скин должен быть 64×64 или 64×32, а этот {size.Width}×{size.Height}");
                    return;
                }
            }
            await SendAsync(bytes, IsSlim, "Скин загружен — его увидят в игре после перезахода");
        }
        catch (Exception)
        {
            _services.Toasts.Error("Не удалось прочитать файл. Нужен PNG-скин Minecraft.");
        }
    }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    private async Task SendAsync(byte[] png, bool slim, string success)
    {
        IsUploading = true;
        try
        {
            _shell.UpdateSession(await _services.Api.UploadSkinAsync(_shell.Session, png, slim));
            _shell.SetSkin(png, slim);
            _services.Toasts.Show(_services.IsDemo ? "Демо-режим: скин показан только в лаунчере" : success, ToastKind.Success);
        }
        catch (Exception e)
        {
            _services.Toasts.Error("Не удалось загрузить скин: " + e.Message);
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private void OpenOnSite() => _services.Platform.OpenUrl(_services.Settings.AuthServerUrl.TrimEnd('/') + "/Account/Manage");
}
