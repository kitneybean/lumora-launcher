using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumora.Launcher.App.Services;

/// <summary>Live appearance options that several views bind to.</summary>
public partial class Appearance : ObservableObject
{
    [ObservableProperty]
    public partial bool ShowBackground { get; set; } = true;

    [ObservableProperty]
    public partial bool AnimateBackground { get; set; } = true;
}
