using Avalonia;

namespace Perialls;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Termina (ou desfaz) uma atualização, se houver uma em andamento. Ver Update/AppFolder.cs.
        AppFolder.Current?.OnStartup(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // O Avalonia desenha a mesma tela no Windows e no Linux.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
