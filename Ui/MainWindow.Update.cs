using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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

    // Só roda quando a pessoa clica. Baixa, confere o hash e (parte 3) troca os arquivos.
    private bool _updating;

    private async void UpdateNow_Click(object? sender, RoutedEventArgs e)
    {
        if (_update == null || _updating || AppFolder.Current is not { } folder)
            return;

        _updating = true;
        UpdateNowButton.IsEnabled = false;
        try
        {
            // Pasta sem permissão de escrita (ex.: Arquivos de Programas): avisa e abre a página da release.
            if (!await Task.Run(folder.TryPrepare))
            {
                ShowUpdateStatus(Lang.T("update.noPermission"));
                await OpenLinkAsync(_update.PageUrl);
                return;
            }

            UpdateProgress.Value = 0;
            UpdateProgress.IsVisible = true;
            ShowUpdateStatus(Lang.T("update.downloading", 0));
            var shown = 0;
            var downloading = true;
            var archive = await Updater.DownloadAsync(_update, folder, p =>
            {
                var percent = (int)(p * 100);
                if (percent == shown)
                    return;
                shown = percent;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!downloading)
                        return;
                    UpdateProgress.Value = p;
                    ShowUpdateStatus(Lang.T("update.downloading", percent));
                });
            }, CancellationToken.None);
            downloading = false;
            UpdateProgress.IsVisible = false;
            ShowUpdateStatus(Lang.T("update.verified"));
        }
        catch (Exception ex)
        {
            folder.DeleteWork();
            UpdateProgress.IsVisible = false;
            ShowUpdateStatus(ex is UpdateException ? ex.Message : Lang.T("update.failed", ex.Message));
        }
        finally
        {
            _updating = false;
            UpdateNowButton.IsEnabled = true;
        }
    }

    // A mensagem aparece no painel e também no rodapé (o painel pode estar fechado).
    private void ShowUpdateStatus(string text)
    {
        UpdateStatus.Text = text;
        UpdateStatus.IsVisible = true;
        StatusText.Text = text;
    }
}
