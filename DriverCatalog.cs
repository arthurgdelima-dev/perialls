using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perialls;

// O índice de drivers: diz quais aparelhos têm driver, em qual arquivo, a versão e o hash SHA-256.
// Fica no repositório (drivers/index.json) e o app baixa do GitHub ao abrir e ao clicar em Atualizar.
// Sem internet, usa a última cópia salva.
//
// Para testar um índice sem publicar no GitHub, aponte a variável PERIALLS_DRIVERS para uma pasta
// ou um endereço (ex.: PERIALLS_DRIVERS=tests/drivers-teste).
public static class DriverCatalog
{
    private const string DefaultSource = "https://raw.githubusercontent.com/arthurgdelima-dev/perialls/main/drivers/";
    public const string OpenRgbSite = "https://openrgb.org";
    public const string Repository = "https://github.com/arthurgdelima-dev/perialls";

    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perialls");

    // Drivers baixados (e, em "local", os que você está testando).
    public static readonly string DriversFolder = Path.Combine(DataFolder, "drivers");
    // Cópia do índice e da lista do OpenRGB, para funcionar sem internet.
    // Um índice de teste (PERIALLS_DRIVERS) fica numa cópia separada, para não misturar com o de verdade.
    private static string CacheFolder => Path.Combine(DataFolder, Source == DefaultSource ? "indice" : "indice-teste");

    // Limites de tamanho: nada aqui deveria passar disso.
    private const int MaxIndexBytes = 1024 * 1024;
    private const int MaxListBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static DriverIndex Index { get; private set; } = new();
    private static HashSet<string> _openRgbIds = [];

    // Os nomes de arquivo só podem ter letras minúsculas, números e "-": nada de pastas ("../").
    private static readonly Regex SafeFileName = new(@"^[a-z0-9-]{1,40}\.json$");
    private static readonly Regex DeviceId = new(@"^VID_[0-9A-F]{4}&PID_[0-9A-F]{4}$");
    private static readonly Regex Sha256 = new(@"^[0-9a-fA-F]{64}$");

    private static string Source =>
        Environment.GetEnvironmentVariable("PERIALLS_DRIVERS") is { Length: > 0 } s ? s : DefaultSource;

    // Baixa o índice (ou usa a cópia salva) e a lista de aparelhos do OpenRGB.
    // Retorna uma mensagem para o rodapé quando algo deu errado, ou null.
    public static async Task<string?> UpdateAsync()
    {
        string? warning = null;
        var fresh = await TryFetchAsync("index.json", MaxIndexBytes);
        DriverIndex? index = null;
        if (fresh != null)
        {
            index = ParseIndex(fresh, out var error);
            if (index != null)
                Save(Path.Combine(CacheFolder, "index.json"), fresh);
            else
                warning = "Índice de drivers inválido no servidor (" + error + "). Usando a cópia salva.";
        }
        index ??= ReadFile(Path.Combine(CacheFolder, "index.json"), MaxIndexBytes) is { } cached
            ? ParseIndex(cached, out _)
            : null;
        Index = index ?? new DriverIndex();

        _openRgbIds = await LoadOpenRgbListAsync();
        return warning;
    }

    // A lista do OpenRGB só é baixada de novo quando o hash no índice muda.
    private static async Task<HashSet<string>> LoadOpenRgbListAsync()
    {
        if (Index.OpenRgb is not { } list)
            return [];

        var path = Path.Combine(CacheFolder, "openrgb.json");
        var data = ReadFile(path, MaxListBytes);
        if (data == null || !HashMatches(data, list.Sha256))
        {
            data = await TryFetchAsync(list.File, MaxListBytes);
            if (data == null || !HashMatches(data, list.Sha256))
                return [];
            Save(path, data);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<OpenRgbList>(data, Json);
            return parsed?.Devices.Where(id => DeviceId.IsMatch(id)).ToHashSet() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // O que o ícone do cartão deve mostrar para este aparelho.
    public static void Describe(DeviceInfo device)
    {
        var entry = Index.Drivers.FirstOrDefault(e => e.Devices.Any(device.HardwareIds.Contains));
        device.DriverEntry = entry;

        if (entry != null)
        {
            device.DriverAction = DriverAction.Download;
            device.DriverTip = $"Baixar driver {entry.Name} (versão {entry.Version}): ativa funções extras deste aparelho";
        }
        else if (device.HasRgb || device.HasDpi || device.HasKeyboardLighting)
        {
            // Já tem funções pelo próprio app (Logitech, Akko, OpenRGB aberto): nada a oferecer.
            device.DriverAction = DriverAction.None;
        }
        else if (device.HardwareIds.Any(_openRgbIds.Contains))
        {
            device.DriverAction = DriverAction.OpenRgb;
            device.DriverTip = "O OpenRGB controla o RGB deste aparelho. Clique para abrir o site dele";
        }
        else if (device.HardwareIds.Count > 0)
        {
            device.DriverAction = DriverAction.RequestSupport;
            device.DriverTip = "Pedir suporte: abre um pedido no GitHub com os dados deste aparelho";
        }
        else
        {
            // Sem código USB (ex.: placa de som da placa-mãe): não há como pedir um driver.
            device.DriverAction = DriverAction.None;
        }
    }

    // Endereço de um pedido de suporte no GitHub, já preenchido com os dados do aparelho.
    public static string SupportRequestUrl(DeviceInfo device)
    {
        var system = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Outro";
        var fields = new Dictionary<string, string>
        {
            ["template"] = "pedir-suporte.yml",
            ["title"] = $"Suporte para {device.OriginalName}",
            ["aparelho"] = device.OriginalName,
            ["tipo"] = device.Kind,
            ["vidpid"] = string.Join(", ", device.HardwareIds),
            ["sistema"] = system,
        };
        return $"{Repository}/issues/new?" + string.Join("&",
            fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"));
    }

    private static DriverIndex? ParseIndex(byte[] data, out string error)
    {
        DriverIndex? index;
        try
        {
            index = JsonSerializer.Deserialize<DriverIndex>(data, Json);
        }
        catch (JsonException ex)
        {
            error = "JSON quebrado: " + ex.Message;
            return null;
        }

        if (index == null || index.Format != 1)
        {
            error = "formato desconhecido";
            return null;
        }
        foreach (var e in index.Drivers)
        {
            if (!SafeFileName.IsMatch(e.File) || !Sha256.IsMatch(e.Sha256) || e.Version.Length is 0 or > 20
                || e.Name.Length is 0 or > 60 || e.Devices.Count == 0 || !e.Devices.All(id => DeviceId.IsMatch(id)))
            {
                error = $"entrada \"{e.File}\" inválida";
                return null;
            }
        }
        if (index.OpenRgb is { } list && (!SafeFileName.IsMatch(list.File) || !Sha256.IsMatch(list.Sha256)))
        {
            error = "lista do OpenRGB inválida";
            return null;
        }

        error = "";
        return index;
    }

    // Baixa um arquivo da fonte de drivers (GitHub, outro endereço ou uma pasta). Null se não deu.
    private static async Task<byte[]?> TryFetchAsync(string file, int maxBytes)
    {
        try
        {
            var source = Source;
            if (!source.StartsWith("https://") && !source.StartsWith("http://"))
                return ReadFile(Path.Combine(source, file), maxBytes);

            using var response = await Http.GetAsync(source.TrimEnd('/') + "/" + file, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
                return null;
            var data = await response.Content.ReadAsByteArrayAsync();
            return data.Length <= maxBytes ? data : null;
        }
        catch
        {
            return null; // sem internet, GitHub fora do ar etc.
        }
    }

    public static bool HashMatches(byte[] data, string sha256) =>
        Convert.ToHexString(SHA256.HashData(data)).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    private static byte[]? ReadFile(string path, int maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= maxBytes ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
        catch
        {
            // Sem a cópia, só não funciona sem internet da próxima vez.
        }
    }
}

// O que o ícone do canto do cartão faz.
public enum DriverAction { None, Download, Update, Installed, OpenRgb, RequestSupport }

public sealed class DriverIndex
{
    public int Format { get; set; }
    public List<DriverIndexEntry> Drivers { get; set; } = [];
    public IndexedFile? OpenRgb { get; set; }
}

// Uma linha do índice: um driver, os aparelhos dele, a versão e o hash do arquivo.
public sealed class DriverIndexEntry
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<string> Devices { get; set; } = [];
}

public sealed class IndexedFile
{
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class OpenRgbList
{
    public List<string> Devices { get; set; } = [];
}
