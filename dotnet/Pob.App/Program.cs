using Avalonia;

namespace Pob.App;

internal static class Program
{
    // Avalonia configuration; must not be async and must not reference any
    // SynchronizationContext-bound state before AppMain is called.
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Used by the Avalonia visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
