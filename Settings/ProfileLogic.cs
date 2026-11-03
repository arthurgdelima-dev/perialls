using System.Text.Json;

namespace Perialls;

// As decisões dos perfis que não dependem da tela nem dos aparelhos: a qual aparelho conectado cada
// entrada do perfil vai, o que mandar para o teclado Akko e para o driver JSON, e a conferência dos
// arquivos exportados e importados. Fica separado da janela para os testes (tests/Perialls.Tests)
// conseguirem conferir tudo sem abrir o app e sem enviar nada.
public static class ProfileLogic
{
    // Liga cada aparelho do perfil a um aparelho conectado. A série, quando os dois sabem, desempata
    // dois aparelhos iguais; cada aparelho conectado recebe no máximo uma entrada do perfil.
    // Entradas sem aparelho conectado ficam de fora (são puladas ao aplicar).
    public static List<(ProfileDevice Entry, DeviceInfo Device)> Match(Profile profile, List<DeviceInfo> devices)
    {
        var free = devices.ToList();
        var plan = new List<(ProfileDevice, DeviceInfo)>();
        foreach (var entry in profile.Devices)
        {
            var device = free.FirstOrDefault(d => entry.Matches(d) && entry.Serial != null
                    && string.Equals(entry.Serial, d.Serial, StringComparison.OrdinalIgnoreCase))
                ?? free.FirstOrDefault(entry.Matches);
            if (device == null)
                continue;
            free.Remove(device);
            plan.Add((entry, device));
        }
        return plan;
    }

    // A iluminação do teclado Akko pedida pelo perfil, a partir da atual. Valores fora do que o teclado
    // aceita ficam como estão. Mudar de efeito usa a opção padrão dele, como na lista do cartão.
    public static MonsGeekKeyboard.Lighting KeyboardLighting(MonsGeekKeyboard.Lighting current, IReadOnlyDictionary<string, int> values, string? color)
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

    // Os comandos do driver JSON que o perfil pede: só opções que o próprio driver oferece e valores
    // dentro de min/max, como num clique. A cor fica de fora (vai pelo caminho da cor).
    public static List<(DriverControl Control, Dictionary<string, int> Sets)> DriverCommands(Driver driver, IReadOnlyDictionary<string, int> values)
    {
        var commands = new List<(DriverControl, Dictionary<string, int>)>();
        foreach (var control in driver.Controls.Where(c => c.Kind is DriverControlKind.Range or DriverControlKind.Options))
        {
            var sets = control.Kind == DriverControlKind.Range
                ? values.TryGetValue(control.Key, out var v) && v >= control.Min && v <= control.Max
                    ? new Dictionary<string, int> { [control.Key] = v } : null
                : control.Options.FirstOrDefault(o => o.Sets.All(kv => values.TryGetValue(kv.Key, out var x) && x == kv.Value))?.Sets;
            if (sets != null)
                commands.Add((control, new Dictionary<string, int>(sets)));
        }
        return commands;
    }

    // "#FF8800" → (255, 136, 0). As cores do perfil já foram validadas (#RRGGBB).
    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    // O arquivo exportado: o perfil sem os números de série (no outro PC os aparelhos são outros).
    public static byte[] Export(Profile profile)
    {
        var copy = new Profile
        {
            Name = profile.Name,
            Color = profile.Color,
            SyncColor = profile.SyncColor,
            Devices = profile.Devices.Select(d => new ProfileDevice
            {
                Id = d.Id, Name = d.Name, Color = d.Color, Dpi = d.Dpi, ReportRateHz = d.ReportRateHz,
                Values = d.Values == null ? null : new Dictionary<string, int>(d.Values),
            }).ToList(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(new ProfileFile { Profile = copy }, ProfileStore.Json);
    }

    // Resultado de importar: o perfil pronto para entrar na lista, ou o motivo (Error) de não importar.
    // Ignored: os aparelhos do arquivo que não estão conectados aqui.
    public sealed record ImportResult(Profile? Profile, List<string> Ignored, string? Error);

    // Confere o arquivo inteiro antes de usar e deixa de fora os aparelhos que não estão conectados.
    // Não mexe na lista de perfis nem aplica nada.
    public static ImportResult Import(byte[] data, List<DeviceInfo> devices)
    {
        static ImportResult Fail(string reason) => new(null, [], Lang.T("profiles.importFailed", reason));

        if (data.Length > Profile.MaxFileBytes)
            return Fail(Lang.T("driver.tooBig"));

        ProfileFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ProfileFile>(data, ProfileStore.Json);
        }
        catch (JsonException ex)
        {
            return Fail(Lang.T("driver.brokenJson", ex.Message));
        }
        if (file?.Profile is not { } profile || file.App != "Perialls" || file.Format is < 1 or > Profile.Format)
            return Fail(Lang.T("profiles.notProfile"));
        if (profile.Validate() is { } problem)
            return Fail(problem);

        // Só os aparelhos que estão conectados aqui (pelo código USB; a série é de outro PC).
        foreach (var entry in profile.Devices)
            entry.Serial = null;
        var kept = Match(profile, devices).Select(x => x.Entry).ToList();
        var ignored = profile.Devices.Except(kept).Select(d => d.Name ?? d.Id).ToList();
        profile.Devices = kept;
        if (kept.Count == 0 && profile.SyncColor == null)
            return new ImportResult(null, ignored, Lang.T("profiles.importNone"));
        return new ImportResult(profile, ignored, null);
    }
}
