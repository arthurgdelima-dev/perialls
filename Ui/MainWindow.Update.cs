using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Perialls;

// O botão "Atualizar para vX" da barra lateral (a lógica fica em Update/Updater.cs).
public partial class MainWindow
{
    private UpdateInfo? _update;

    // Uma consulta ao abrir, em segundo plano. Sem versão nova (ou sem internet), o botão nem aparece.
    private async Task CheckUpdateAsync()
    {
        _update = await Updater.CheckOnceAsync();
        if (_update == null)
            return;

        var title = Lang.T("update.button", Updater.Display(_update.Version));
        UpdateTitle.Text = title;
        UpdateNotes.Text = string.IsNullOrEmpty(_update.Notes) ? Lang.T("update.noNotes") : _update.Notes;
        ToolTip.SetTip(UpdateButton, title);
        UpdateButton.IsVisible = true;
    }

    private async void UpdatePage_Click(object? sender, RoutedEventArgs e) =>
        await OpenLinkAsync(_update?.PageUrl ?? Updater.ReleasesPage);

    private void UpdateNow_Click(object? sender, RoutedEventArgs e)
    {
        // Parte 2: baixar e conferir.
    }
}
