using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Lumora.Launcher.App.ViewModels;

namespace Lumora.Launcher.App.Views;

public partial class ConsoleView : UserControl
{
    private ConsoleViewModel? _vm;

    public ConsoleView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.LineAdded -= OnLineAdded;
        _vm = DataContext as ConsoleViewModel;
        if (_vm is not null)
            _vm.LineAdded += OnLineAdded;
    }

    private void OnLineAdded()
    {
        if (_vm is { AutoScroll: true, Lines.Count: > 0 } vm)
            Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(vm.Lines[^1]), DispatcherPriority.Background);
    }
}
