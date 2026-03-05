using System.Windows;
using System.Windows.Controls;

namespace MeshhessenClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Show tooltips faster (system default is ~400ms; 150ms feels much more responsive)
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(UIElement),
            new FrameworkPropertyMetadata(150));
        // Keep tooltips visible longer (default ~5s is too short for multi-line tips)
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(UIElement),
            new FrameworkPropertyMetadata(20000));
    }
}
