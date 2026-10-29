using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Perialls;

public partial class App : Application
{
    public override void Initialize()
    {
        Lang.Load(UserSettings.Current.Language);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            // O OpenRGB que nós abrimos fecha junto com o Perialls (nada fica rodando depois).
            desktop.Exit += (_, _) => OpenRgb.StopIfStarted();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
