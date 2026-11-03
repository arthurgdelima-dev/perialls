using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Perialls;

// Perfis (a parte de aplicar). O app NUNCA aplica um perfil sozinho: só quando você clica nele.
// Cada configuração vai pelo mesmo caminho do clique manual (OpenRGB, drivers embutidos e driver JSON),
// com as mesmas travas e a mesma gravação na memória do aparelho.
public partial class MainWindow
{
    // Um perfil sendo aplicado: os cliques em outro perfil esperam este terminar.
    private bool _applyingProfile;

    // O perfil que está sendo renomeado ou esperando a confirmação para excluir (só um por vez).
    private Profile? _renamingProfile;
    private Profile? _deletingProfile;

    private static ProfileStore Store => ProfileStore.Current;

    // Redesenha a lista de perfis (o mesmo jeito dos cartões: monta a lista de novo).
    private void ShowProfiles()
    {
        ProfileList.ItemsSource = Store.Profiles
            .Select(p => new ProfileView(p, p.Name == Store.LastApplied, p == _renamingProfile, p == _deletingProfile))
            .ToList();
        ProfilesEmpty.IsVisible = Store.Profiles.Count == 0;
        ToolTip.SetTip(ProfilesButton, Store.LastApplied is { } last && Store.Profiles.Any(p => p.Name == last)
            ? Lang.T("sidebar.profilesLast", last)
            : Lang.T("sidebar.profiles"));
    }

    private static Profile? ProfileOf(object? sender) => ((sender as Control)?.DataContext as ProfileView)?.Profile;

    // Salvar como perfil: tira uma foto do que cada aparelho está usando agora.
    private void SaveProfile_Click(object? sender, RoutedEventArgs e) => SaveNewProfile();

    private void NewProfileName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SaveNewProfile();
    }

    private void SaveNewProfile()
    {
        if (Store.Profiles.Count >= ProfileStore.MaxProfiles)
        {
            StatusText.Text = Lang.T("profiles.tooMany", ProfileStore.MaxProfiles);
            return;
        }

        var typed = (NewProfileName.Text ?? "").Trim();
        var name = Store.UniqueName(typed != "" ? typed : Lang.T("profiles.defaultName", Store.Profiles.Count + 1));
        var profile = Profile.Capture(name, Store.NextColor(), _devices);
        if (profile.Devices.Count == 0 && profile.SyncColor == null)
        {
            StatusText.Text = Lang.T("profiles.nothingToSave");
            return;
        }

        Store.Profiles.Add(profile);
        NewProfileName.Text = "";
        SaveProfiles(Lang.T("profiles.saved", name, profile.Devices.Count));
    }

    // Grava o profiles.json e mostra a mensagem (ou o erro, se não deu para gravar).
    private void SaveProfiles(string message)
    {
        StatusText.Text = Store.Save() ? message : Lang.T("profiles.saveFailed");
        ShowProfiles();
    }

    private static readonly FilePickerFileType JsonFiles = new("JSON") { Patterns = ["*.json"] };

    // Exportar: um arquivo .json com o perfil, para trocar com amigos ou levar para outro PC.
    private async void ProfileExport_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
            return;
        ProfilesButton.Flyout?.Hide();
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Lang.T("profiles.exportTip"),
                SuggestedFileName = $"perialls-{profile.Name}.json",
                DefaultExtension = "json",
                FileTypeChoices = [JsonFiles],
            });
            if (file == null)
                return;

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(ProfileLogic.Export(profile));
            StatusText.Text = Lang.T("profiles.exported", profile.Name, file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("profiles.exportFailed", ex.Message);
        }
    }

    // Importar: confere o arquivo inteiro antes de usar e deixa de fora os aparelhos que não estão
    // conectados aqui. Nada é aplicado: o perfil só entra na lista.
    private async void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        ProfilesButton.Flyout?.Hide();
        if (Store.Profiles.Count >= ProfileStore.MaxProfiles)
        {
            StatusText.Text = Lang.T("profiles.tooMany", ProfileStore.MaxProfiles);
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Lang.T("profiles.importTip"),
                AllowMultiple = false,
                FileTypeFilter = [JsonFiles],
            });
            if (files.Count == 0)
                return;

            // Lê no máximo o limite + 1 byte: passou disso, o arquivo é grande demais.
            byte[] data;
            await using (var stream = await files[0].OpenReadAsync())
            {
                var buffer = new byte[Profile.MaxFileBytes + 1];
                int total = 0, read;
                while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total))) > 0)
                    total += read;
                data = buffer[..total];
            }
            StatusText.Text = ImportProfile(data);
            ShowProfiles();
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("profiles.importFailed", ex.Message);
        }
    }

    // Devolve a mensagem do rodapé: importado, ou o motivo de não ter importado.
    private string ImportProfile(byte[] data)
    {
        var result = ProfileLogic.Import(data, _devices);
        if (result.Profile is not { } profile)
            return result.Error!;

        profile.Name = Store.UniqueName(profile.Name);
        Store.Profiles.Add(profile);
        if (!Store.Save())
            return Lang.T("profiles.saveFailed");
        return Lang.T("profiles.imported", profile.Name, profile.Devices.Count)
            + (result.Ignored.Count > 0 ? " " + Lang.T("profiles.importIgnored", string.Join(", ", result.Ignored)) : "");
    }

    private async void ProfileApply_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
            return;
        ProfilesButton.Flyout?.Hide(); // para ver o rodapé
        await ApplyProfileAsync(profile);
    }

    // A bolinha troca para a próxima cor da paleta.
    private void ProfileColor_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
            return;
        var index = Array.FindIndex(Profile.Colors, c => string.Equals(c, profile.Color, StringComparison.OrdinalIgnoreCase));
        profile.Color = Profile.Colors[(index + 1) % Profile.Colors.Length];
        SaveProfiles(StatusText.Text ?? "");
    }

    // Atualizar: troca o que o perfil guarda pelas configurações de agora (mantém nome e cor).
    private void ProfileUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
            return;
        var fresh = Profile.Capture(profile.Name, profile.Color, _devices);
        if (fresh.Devices.Count == 0 && fresh.SyncColor == null)
        {
            StatusText.Text = Lang.T("profiles.nothingToSave");
            return;
        }
        profile.Devices = fresh.Devices;
        profile.SyncColor = fresh.SyncColor;
        SaveProfiles(Lang.T("profiles.updated", profile.Name, profile.Devices.Count));
    }

    private void ProfileRename_Click(object? sender, RoutedEventArgs e)
    {
        _renamingProfile = ProfileOf(sender);
        _deletingProfile = null;
        ShowProfiles();
    }

    private void ProfileRenameBox_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox { DataContext: ProfileView { IsRenaming: true } } box)
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Loaded);
    }

    // Enter salva, Esc cancela. Nome vazio não muda nada; nome repetido ganha um número.
    private void ProfileRenameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProfileView { Profile: var profile } } box || e.Key is not (Key.Enter or Key.Escape))
            return;
        e.Handled = true;
        _renamingProfile = null;

        var name = (box.Text ?? "").Trim();
        if (e.Key == Key.Escape || name == "" || name == profile.Name)
        {
            ShowProfiles();
            return;
        }

        name = Store.UniqueName(name[..Math.Min(name.Length, Profile.MaxNameLength)], profile);
        if (Store.LastApplied == profile.Name)
            Store.LastApplied = name;
        var old = profile.Name;
        profile.Name = name;
        SaveProfiles(Lang.T("profiles.renamed", old, name));
    }

    private void ProfileRenameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ProfileView { IsRenaming: true } })
        {
            _renamingProfile = null;
            ShowProfiles();
        }
    }

    // Excluir pede confirmação na própria linha.
    private void ProfileDelete_Click(object? sender, RoutedEventArgs e)
    {
        _deletingProfile = ProfileOf(sender);
        _renamingProfile = null;
        ShowProfiles();
    }

    private void ProfileDeleteNo_Click(object? sender, RoutedEventArgs e)
    {
        _deletingProfile = null;
        ShowProfiles();
    }

    private void ProfileDeleteYes_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
            return;
        _deletingProfile = null;
        Store.Profiles.Remove(profile);
        if (Store.LastApplied == profile.Name)
            Store.LastApplied = null;
        SaveProfiles(Lang.T("profiles.deleted", profile.Name));
    }

    private async Task ApplyProfileAsync(Profile profile)
    {
        if (_applyingProfile)
            return;
        _applyingProfile = true;
        StatusText.Text = Lang.T("profiles.applying", profile.Name);
        try
        {
            // O OpenRGB embutido só liga se alguma cor do perfil precisar dele (como no clique de cor).
            // Ligar busca os aparelhos de novo, então o perfil é ligado aos aparelhos só depois.
            string? rgbError = null;
            var syncColor = profile.SyncColor;
            if (syncColor != null ? OpenRgb.FindExe() != null : ProfileLogic.Match(profile, _devices).Any(x => x.Entry.Color != null && x.Device.NeedsOpenRgb))
            {
                rgbError = await StartRgbAsync();
                StatusText.Text = Lang.T("profiles.applying", profile.Name);
            }

            var plan = ProfileLogic.Match(profile, _devices);
            var missing = profile.Devices.Where(e => plan.All(x => x.Entry != e)).Select(e => e.Name ?? e.Id).ToList();

            // Sincronizar RGB: a cor vai também para os aparelhos com RGB que não estão no perfil
            // (memória, placa de vídeo...), como o botão de sincronizar faz.
            if (syncColor != null)
                plan.AddRange(_devices.Where(d => d.HasRgb && plan.All(x => x.Device != d))
                    .Select(d => (new ProfileDevice { Id = d.Key }, d)));

            // Aparelhos diferentes recebem ao mesmo tempo. Os que dividem o mesmo receptor Logitech vão
            // um de cada vez (é o mesmo canal HID). Num aparelho, as configurações vão em ordem.
            var groups = plan.GroupBy(x => x.Device.LogitechProductId != 0 ? $"logitech:{x.Device.LogitechProductId}" : x.Device.Key);
            var results = await Task.WhenAll(groups.Select(async group =>
            {
                var list = new List<DeviceResult>();
                foreach (var (entry, device) in group)
                    list.Add(await ApplyToDeviceAsync(entry, device, syncColor, rgbError));
                return list;
            }));

            ProfileStore.Current.LastApplied = profile.Name;
            ProfileStore.Current.Save();
            ShowDevices();
            ShowProfiles();
            StatusText.Text = ProfileSummary(profile, results.SelectMany(r => r).ToList(), missing);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("profiles.applyFailed", profile.Name, ex.Message);
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    // O que deu certo e o que falhou num aparelho.
    private sealed record DeviceResult(DeviceInfo Device, List<string> Applied, List<string> Failed);

    private static async Task<DeviceResult> ApplyToDeviceAsync(ProfileDevice entry, DeviceInfo device, string? syncColor, string? rgbError)
    {
        var applied = new List<string>();
        var failed = new List<string>();

        async Task Try(string label, Func<Task> action)
        {
            try
            {
                await action();
                applied.Add(label);
            }
            catch (DriverException ex)
            {
                failed.Add($"{label}: {Lang.T("error.nothingSent", ex.Message)}");
            }
            catch (Exception ex)
            {
                failed.Add($"{label}: {ex.Message}");
            }
        }

        var color = entry.Color ?? (device.HasRgb ? syncColor : null);
        var values = entry.Values ?? [];

        // Teclado Akko: efeito, brilho, velocidade e cor vão juntos, num pacote só.
        if (device.KeyboardLighting is { } current && (color != null || values.Count > 0))
        {
            await Try(Lang.T("profiles.lighting"), async () =>
                await SetKeyboardLightingAsync(device, ProfileLogic.KeyboardLighting(current, values, color)));
            if (color != null && applied.Count > 0)
            {
                device.SavedColor = color.ToUpperInvariant();
                UserSettings.Current.Remember(device, s => s.Color = device.SavedColor);
            }
        }
        else if (color != null)
        {
            var (r, g, b) = ProfileLogic.ParseHex(color);
            // Sem o OpenRGB, a cor de um aparelho que depende dele falha com o motivo de não ter ligado.
            var error = device.NeedsOpenRgb && rgbError != null ? Lang.T("status.rgbFailed", rgbError)
                : await SetColorAsync(device, r, g, b);
            if (error == null)
                applied.Add(Lang.T("profiles.color", color.ToUpperInvariant()));
            else
                failed.Add($"{Lang.T("control.color")}: {error}");
        }

        // Driver JSON: só opções que o próprio driver oferece, como num clique.
        if (device.Driver is { } driver && device.KeyboardLighting == null)
            foreach (var (control, sets) in ProfileLogic.DriverCommands(driver, values))
                await Try(control.Label, () => SendDriverAsync(device, control, sets));

        if (entry.Dpi is int dpi)
        {
            if (device.HasDpi)
                await Try($"DPI {dpi}", () => SetDpiAsync(device, dpi));
            else
                failed.Add($"DPI: {Lang.T("error.notAvailableNow")}");
        }

        if (entry.ReportRateHz is int hz)
        {
            if (device.HasReportRate)
                await Try($"{hz} Hz", () => SetReportRateAsync(device, hz));
            else
                failed.Add($"{hz} Hz: {Lang.T("error.notAvailableNow")}");
        }

        return new DeviceResult(device, applied, failed);
    }

    // Rodapé: o que foi aplicado em cada aparelho, o que falhou e quem não estava conectado.
    private static string ProfileSummary(Profile profile, List<DeviceResult> results, List<string> missing)
    {
        var ok = results.Where(r => r.Applied.Count > 0).ToList();
        var lines = new List<string>
        {
            Lang.T("profiles.applied", profile.Name, ok.Count)
            + (ok.Count > 0 ? " " + string.Join(" · ", ok.Select(r => $"{r.Device.Name} ({string.Join(", ", r.Applied)})")) : ""),
        };

        var failed = results.Where(r => r.Failed.Count > 0).ToList();
        if (failed.Count > 0)
            lines.Add(Lang.T("profiles.failed", string.Join(" · ", failed.Select(r => $"{r.Device.Name} ({string.Join("; ", r.Failed)})"))));
        if (missing.Count > 0)
            lines.Add(Lang.T("profiles.missing", string.Join(", ", missing)));
        return string.Join("\n", lines);
    }
}

// Uma linha da lista de perfis.
public sealed record ProfileView(Profile Profile, bool IsLast, bool IsRenaming, bool IsConfirmingDelete)
{
    public bool IsNormal => !IsRenaming && !IsConfirmingDelete;
    public IBrush Brush { get; } = SolidColorBrush.Parse(Profile.Color);
    public string ApplyTip => Lang.T(IsLast ? "profiles.applyTipLast" : "profiles.applyTip", Profile.Name);
    public string DeleteQuestion => Lang.T("profiles.deleteQuestion", Profile.Name);
}
