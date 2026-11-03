namespace Perialls;

// Perfis (a parte de aplicar). O app NUNCA aplica um perfil sozinho: só quando você clica nele.
// Cada configuração vai pelo mesmo caminho do clique manual (OpenRGB, drivers embutidos e driver JSON),
// com as mesmas travas e a mesma gravação na memória do aparelho.
public partial class MainWindow
{
    // Um perfil sendo aplicado: os cliques em outro perfil esperam este terminar.
    private bool _applyingProfile;

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
            if (syncColor != null ? OpenRgb.FindExe() != null : Match(profile, _devices).Any(x => x.Entry.Color != null && x.Device.NeedsOpenRgb))
            {
                rgbError = await StartRgbAsync();
                StatusText.Text = Lang.T("profiles.applying", profile.Name);
            }

            var plan = Match(profile, _devices);
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

    // Liga cada aparelho do perfil a um aparelho conectado. A série, quando os dois sabem, desempata
    // dois aparelhos iguais; cada aparelho conectado recebe no máximo uma entrada do perfil.
    private static List<(ProfileDevice Entry, DeviceInfo Device)> Match(Profile profile, List<DeviceInfo> devices)
    {
        var free = devices.ToList();
        var plan = new List<(ProfileDevice, DeviceInfo)>();
        foreach (var entry in profile.Devices)
        {
            var device = free.FirstOrDefault(d => entry.Matches(d) && entry.Serial != null
                    && string.Equals(entry.Serial, d.Serial, StringComparison.OrdinalIgnoreCase))
                ?? free.FirstOrDefault(entry.Matches);
            if (device == null)
                continue; // não está conectado: é pulado
            free.Remove(device);
            plan.Add((entry, device));
        }
        return plan;
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
                await SetKeyboardLightingAsync(device, KeyboardLighting(current, values, color)));
            if (color != null && applied.Count > 0)
            {
                device.SavedColor = color.ToUpperInvariant();
                UserSettings.Current.Remember(device, s => s.Color = device.SavedColor);
            }
        }
        else if (color != null)
        {
            var (r, g, b) = ParseHex(color);
            // Sem o OpenRGB, a cor de um aparelho que depende dele falha com o motivo de não ter ligado.
            var error = device.NeedsOpenRgb && rgbError != null ? Lang.T("status.rgbFailed", rgbError)
                : await SetColorAsync(device, r, g, b);
            if (error == null)
                applied.Add(Lang.T("profiles.color", color.ToUpperInvariant()));
            else
                failed.Add($"{Lang.T("control.color")}: {error}");
        }

        // Driver JSON: só opções que o próprio driver oferece, como num clique.
        if (device.Driver is { } driver && device.KeyboardLighting == null && values.Count > 0)
        {
            foreach (var control in driver.Controls.Where(c => c.Kind != DriverControlKind.Color))
            {
                var sets = control.Kind == DriverControlKind.Range
                    ? values.TryGetValue(control.Key, out var v) && v >= control.Min && v <= control.Max
                        ? new Dictionary<string, int> { [control.Key] = v } : null
                    : control.Options.FirstOrDefault(o => o.Sets.All(kv => values.TryGetValue(kv.Key, out var x) && x == kv.Value))?.Sets;
                if (sets != null)
                    await Try(control.Label, () => SendDriverAsync(device, control, sets));
            }
        }

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

    // A iluminação do perfil a partir da atual. Valores fora do que o teclado aceita ficam como estão.
    // Mudar de efeito usa a opção padrão dele, como na lista do cartão.
    private static MonsGeekKeyboard.Lighting KeyboardLighting(MonsGeekKeyboard.Lighting current, Dictionary<string, int> values, string? color)
    {
        var lighting = current;
        if (values.TryGetValue("effect", out var id) && MonsGeekKeyboard.Effects.FirstOrDefault(e => e.Id == id) is { } effect
            && effect.Id != current.Effect)
            lighting = lighting with { Effect = effect.Id, Option = effect.DefaultOption };
        if (values.TryGetValue("brightness", out var brightness) && brightness is >= 0 and <= 4)
            lighting = lighting with { Brightness = (byte)brightness };
        if (values.TryGetValue("speed", out var speed) && speed is >= 0 and <= 4)
            lighting = lighting with { Speed = (byte)speed };

        if (color != null)
        {
            // Preto apaga (brilho 0), como na bolinha preta do cartão.
            var (r, g, b) = ParseHex(color);
            lighting = (r, g, b) == (0, 0, 0)
                ? lighting with { Brightness = 0 }
                : lighting with
                {
                    R = r, G = g, B = b,
                    Multicolor = false,
                    Brightness = lighting.Brightness == 0 ? (byte)4 : lighting.Brightness,
                };
        }
        return lighting;
    }

    // As cores do perfil já foram validadas (#RRGGBB).
    private static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
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
