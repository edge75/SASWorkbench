using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SasWorkbench.Gui.ViewModels;
using SasWorkbench.Gui.Views;

namespace SasWorkbench.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new WorkbenchViewModel();
            desktop.MainWindow = new MainWindow { DataContext = model };
            desktop.ShutdownRequested += (_, _) => model.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
