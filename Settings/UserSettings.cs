using System.IO;
using System.Text.Json;

namespace Perialls;

// Configurações do app num arquivo só: %AppData%\Perialls\settings.json (Linux: ~/.config/Perialls/settings.json).
//
// Guarda a última escolha que você fez em cada aparelho (pelo código USB). Serve SÓ para mostrar na
// tela o que você escolheu da última vez quando o aparelho não informa o próprio estado.
// O app nunca reaplica isso sozinho: ao abrir, ele só lê os aparelhos.
public sealed class UserSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perialls", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static UserSettings Current { get; } = Load();

    // Idioma da tela ("pt-BR" ou "en"). Vazio = ainda não escolhido (usa o do sistema).
    public string? Language { get; set; }

    public Dictionary<string, DeviceSettings> Devices { get; set; } = [];

    // A última escolha num aparelho. Chave: o código USB (ex.: "VID_046D&PID_4093").
    public DeviceSettings For(DeviceInfo device)
    {
        if (!Devices.TryGetValue(device.Key, out var settings))
            Devices[device.Key] = settings = new DeviceSettings();
        return settings;
    }

    public DeviceSettings? Find(DeviceInfo device) => Devices.GetValueOrDefault(device.Key);

    // Anota uma mudança que você fez e salva o arquivo na hora.
    public void Remember(DeviceInfo device, Action<DeviceSettings> change)
    {
        change(For(device));
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Sem salvar, só não lembra da escolha na próxima vez.
        }
    }

    private static UserSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Json) ?? new UserSettings()
                : new UserSettings();
        }
        catch
        {
            return new UserSettings(); // arquivo quebrado: começa do zero
        }
    }
}

// O que lembramos de um aparelho.
public sealed class DeviceSettings
{
    public string? Color { get; set; }       // "#FF8800"
    public int? Dpi { get; set; }
    public int? ReportRateHz { get; set; }

    // Valores dos controles: variáveis do driver JSON ({brightness}, {effect}...) e iluminação do teclado Akko.
    public Dictionary<string, int> Values { get; set; } = [];
}
