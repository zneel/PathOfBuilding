using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pob.App;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
