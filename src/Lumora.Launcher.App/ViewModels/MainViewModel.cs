using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumora.Launcher.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Lumora Launcher — UI в разработке";
}
