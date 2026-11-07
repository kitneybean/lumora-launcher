using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Lumora.Launcher.App.Views;

public partial class ServersView : UserControl
{
    private const double MinCardWidth = 380;

    public ServersView()
    {
        InitializeComponent();
        Cards.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                UpdateColumns();
        };
        Cards.ContainerPrepared += (_, _) => UpdateColumns();
    }

    /// <summary>Responsive grid: as many columns as fit at least <see cref="MinCardWidth"/> each.</summary>
    private void UpdateColumns()
    {
        if (Cards.ItemsPanelRoot is not UniformGrid grid)
            return;
        var width = Cards.Bounds.Width;
        var columns = width <= 0 ? 2 : (int)((width + grid.ColumnSpacing) / (MinCardWidth + grid.ColumnSpacing));
        columns = System.Math.Max(1, columns);
        if (grid.Columns != columns)
            grid.Columns = columns;
    }
}
