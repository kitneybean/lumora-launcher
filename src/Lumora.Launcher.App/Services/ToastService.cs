using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace Lumora.Launcher.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed partial class ToastViewModel(ToastService owner, string message, ToastKind kind, string? actionText, Action? action)
    : ObservableObject
{
    public string Message { get; } = message;
    public ToastKind Kind { get; } = kind;
    public string? ActionText { get; } = actionText;
    public bool HasAction => ActionText is not null;

    public MaterialIconKind Icon => Kind switch
    {
        ToastKind.Success => MaterialIconKind.CheckCircleOutline,
        ToastKind.Warning => MaterialIconKind.AlertOutline,
        ToastKind.Error => MaterialIconKind.AlertCircleOutline,
        _ => MaterialIconKind.InformationOutline,
    };

    public bool IsSuccess => Kind == ToastKind.Success;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsError => Kind == ToastKind.Error;
    public bool IsInfo => Kind == ToastKind.Info;

    [RelayCommand]
    private void RunAction()
    {
        action?.Invoke();
        owner.Dismiss(this);
    }

    [RelayCommand]
    private void Close() => owner.Dismiss(this);
}

/// <summary>Short notifications in the bottom-right corner.</summary>
public sealed class ToastService
{
    private const int MaxVisible = 4;

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Info, string? actionText = null, Action? action = null)
    {
        var toast = new ToastViewModel(this, message, kind, actionText, action);
        Items.Add(toast);
        while (Items.Count > MaxVisible)
            Items.RemoveAt(0);
        _ = AutoDismissAsync(toast, kind == ToastKind.Error ? 8000 : 4500);
    }

    public void Success(string message) => Show(message, ToastKind.Success);
    public void Error(string message, string? actionText = null, Action? action = null) => Show(message, ToastKind.Error, actionText, action);

    public void Dismiss(ToastViewModel toast) => Items.Remove(toast);

    private async Task AutoDismissAsync(ToastViewModel toast, int ms)
    {
        await Task.Delay(ms);
        Items.Remove(toast);
    }
}
