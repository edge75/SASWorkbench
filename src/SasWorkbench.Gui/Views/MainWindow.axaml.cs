using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SasWorkbench.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
