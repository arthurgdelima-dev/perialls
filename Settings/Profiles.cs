using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perialls;

// Perfis: um conjunto de configurações de vários aparelhos, aplicado com um clique.
// Ficam em %AppData%\Perialls\profiles.json (Linux: ~/.config/Perialls/profiles.json), ao lado do settings.json.
//
// O app NUNCA aplica um perfil sozinho: só quando você clica nele (veja MainWindow.Profiles.cs).
// Um perfil guarda só o que cada aparelho suporta: cor, DPI, Hz e os valores dos controles
// (iluminação do teclado Akko e variáveis do driver JSON).
public sealed class ProfileStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perialls", "profiles.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static ProfileStore Current { get; } = Load();

    public int Format { get; set; } = Profile.Format;

    // Nome do último perfil aplicado (fica destacado na lista).
    public string? LastApplied { get; set; }

    public List<Profile> Profiles { get; set; } = [];

    public const int MaxProfiles = 50;

    // Um nome que ainda não existe: "Jogo", "Jogo (2)", "Jogo (3)"...
    public string UniqueName(string name, Profile? except = null)
    {
        var taken = Profiles.Where(p => p != except).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;
        for (int i = 2; ; i++)
        {
            var suffix = $" ({i})";
            var candidate = name[..Math.Min(name.Length, Profile.MaxNameLength - suffix.Length)] + suffix;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    // A próxima cor da paleta que nenhum perfil usa (ou a seguinte, se todas estiverem em uso).
    public string NextColor() =>
        Profile.Colors.FirstOrDefault(c => !Profiles.Any(p => string.Equals(p.Color, c, StringComparison.OrdinalIgnoreCase)))
        ?? Profile.Colors[Profiles.Count % Profile.Colors.Length];

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Perfis inválidos (arquivo editado à mão, por exemplo) ficam de fora em vez de quebrar o app.
    private static ProfileStore Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ProfileStore();
            var store = JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(FilePath), Json) ?? new ProfileStore();
            store.Profiles = (store.Profiles ?? []).Where(p => p != null && p.Validate() == null).Take(MaxProfiles).ToList();
            return store;
        }
        catch
        {
            return new ProfileStore(); // arquivo quebrado: começa do zero
        }
    }
}

public sealed class Profile
{
    public const int Format = 1;
    public const int MaxNameLength = 40;
    private const int MaxDevices = 64;
    private const int MaxValues = 32;
    public const int MaxFileBytes = 64 * 1024;

    // Cores para identificar os perfis na lista. Clicar na bolinha troca para a próxima.
    public static readonly string[] Colors =
        ["#FF3B30", "#FF9500", "#FFD60A", "#34C759", "#00C7BE", "#0A84FF", "#BF5AF2", "#FF2D92"];

    public string Name { get; set; } = "";
    public string Color { get; set; } = Colors[0];

    // "Sincronizar RGB": quando todos os aparelhos com RGB estavam com a mesma cor, o perfil guarda
    // só essa cor e, ao aplicar, ela vai para todos (como o botão de sincronizar).
    public string? SyncColor { get; set; }

    public List<ProfileDevice> Devices { get; set; } = [];

    private static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$");
    private static readonly Regex DeviceId = new(@"^VID_[0-9A-F]{4}&PID_[0-9A-F]{4}$");
    private static readonly Regex VariableName = new(@"^[a-zA-Z][a-zA-Z0-9]{0,19}$");

    // Tira uma "foto" do que cada aparelho está usando agora. Só entra o que o aparelho suporta.
    public static Profile Capture(string name, string color, IEnumerable<DeviceInfo> devices)
    {
        var profile = new Profile { Name = name, Color = color };
        var list = devices.ToList();

        // Todos os aparelhos com RGB na mesma cor (2 ou mais): é o "Sincronizar RGB" em uso.
        var colors = list.Where(d => d.HasRgb).Select(d => d.CurrentColor?.ToUpperInvariant()).ToList();
        if (colors.Count >= 2 && colors[0] != null && colors.All(c => c == colors[0]))
            profile.SyncColor = colors[0];

        foreach (var device in list)
        {
            var entry = new ProfileDevice
            {
                Id = device.Key,
                Serial = device.Serial,
                Name = device.Name,
                Color = profile.SyncColor == null && device.HasRgb ? device.CurrentColor?.ToUpperInvariant() : null,
                Dpi = device.HasDpi ? device.Dpi : null,
                ReportRateHz = device.HasReportRate ? device.ReportRateHz : null,
            };

            if (device.KeyboardLighting is { } lighting)
            {
                entry.Values = new()
                {
                    ["effect"] = lighting.Effect,
                    ["brightness"] = lighting.Brightness,
                    ["speed"] = lighting.Speed,
                };
            }
            else
            {
                // Driver JSON: as variáveis da opção escolhida em cada controle (menos a cor, que vai em Color).
                var values = new Dictionary<string, int>();
                foreach (var view in device.DriverControlViews)
                    foreach (var (key, value) in view.Selected?.Sets ?? new Dictionary<string, int>())
                        values[key] = value;
                if (values.Count > 0)
                    entry.Values = values;
            }

            if (entry.HasSettings)
                profile.Devices.Add(entry);
        }
        return profile;
    }

    // Confere tudo antes de usar (arquivo importado ou editado à mão). Devolve o problema, ou null se estiver certo.
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength || Name.Any(char.IsControl))
            return Lang.T("profile.badName", MaxNameLength);
        if (Color == null || !Hex.IsMatch(Color))
            return Lang.T("profile.badField", "color");
        if (SyncColor != null && !Hex.IsMatch(SyncColor))
            return Lang.T("profile.badField", "syncColor");
        if (Devices == null || Devices.Count > MaxDevices)
            return Lang.T("profile.badDevices", MaxDevices);

        foreach (var d in Devices)
        {
            if (d == null)
                return Lang.T("profile.badDevices", MaxDevices);
            // Aparelho USB: "VID_xxxx&PID_yyyy". Sem código USB (ex.: memória RAM no OpenRGB): o nome.
            if (d.Id == null || !(DeviceId.IsMatch(d.Id) || (d.Id.Length is > 0 and <= 100 && !d.Id.Any(char.IsControl))))
                return Lang.T("profile.badField", "id");
            if (d.Serial != null && (d.Serial.Length is 0 or > 64 || d.Serial.Any(char.IsControl)))
                return Lang.T("profile.badField", "serial");
            if (d.Name != null && (d.Name.Length > 100 || d.Name.Any(char.IsControl)))
                return Lang.T("profile.badField", "name");
            if (d.Color != null && !Hex.IsMatch(d.Color))
                return Lang.T("profile.badField", "color");
            if (d.Dpi is < 50 or > 50000)
                return Lang.T("profile.badField", "dpi");
            if (d.ReportRateHz is < 1 or > 8000)
                return Lang.T("profile.badField", "reportRateHz");
            if (d.Values != null && (d.Values.Count > MaxValues
                || d.Values.Any(kv => !VariableName.IsMatch(kv.Key) || kv.Value is < 0 or > 255)))
                return Lang.T("profile.badField", "values");
        }
        return null;
    }
}

// O que o perfil guarda de um aparelho. Campos vazios ficam como estão no aparelho.
public sealed class ProfileDevice
{
    public string Id { get; set; } = "";       // "VID_046D&PID_C547" (ou o nome, sem código USB)
    public string? Serial { get; set; }        // separa dois aparelhos iguais
    public string? Name { get; set; }          // só para mostrar no rodapé quando ele não está conectado
    public string? Color { get; set; }         // "#FF8800"
    public int? Dpi { get; set; }
    public int? ReportRateHz { get; set; }
    public Dictionary<string, int>? Values { get; set; } // iluminação do Akko ou variáveis do driver JSON

    public bool HasSettings => Color != null || Dpi != null || ReportRateHz != null || Values is { Count: > 0 };

    // É este aparelho? Mesmo código USB e, se os dois souberem a série, a mesma série.
    public bool Matches(DeviceInfo device) =>
        (device.HardwareIds.Contains(Id) || device.Key == Id)
        && (Serial == null || device.Serial == null || string.Equals(Serial, device.Serial, StringComparison.OrdinalIgnoreCase));
}

// Arquivo de um perfil exportado (para trocar com amigos ou levar para outro PC).
public sealed class ProfileFile
{
    public string App { get; set; } = "Perialls";
    public int Format { get; set; } = Profile.Format;
    public Profile? Profile { get; set; }
}
