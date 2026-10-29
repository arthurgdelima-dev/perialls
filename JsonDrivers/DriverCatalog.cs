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

    // Drivers baixados e, na subpasta "local", os que você está testando (sem passar pelo índice).
    // Cópia do índice e da lista do OpenRGB, para funcionar sem internet.
    // Um índice de teste (PERIALLS_DRIVERS) usa pastas separadas, para não misturar com o de verdade.
    public static string DriversFolder => Path.Combine(DataFolder, IsTestSource ? "drivers-teste" : "drivers");
    public static string LocalDriversFolder => Path.Combine(DriversFolder, "local");
    private static string CacheFolder => Path.Combine(DataFolder, IsTestSource ? "indice-teste" : "indice");
    private static string InstalledFile => Path.Combine(DriversFolder, "_instalados.json");

    // Limites de tamanho: nada aqui deveria passar disso.
    private const int MaxIndexBytes = 1024 * 1024;
    private const int MaxListBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static DriverIndex Index { get; private set; } = new();
    private static HashSet<string> _openRgbIds = [];

    // Drivers instalados: a versão e o hash de cada um (conferidos no download) e os já validados.
    private static Dictionary<string, InstalledDriver> _installed = [];
    private static readonly Dictionary<string, Driver> Loaded = [];
    private static readonly List<Driver> LocalDrivers = [];

    // Aparelhos com driver embutido no código do app. O embutido tem prioridade: drivers JSON para
    // estes VID/PID são ignorados e não geram ícone. (Akko/MonsGeek: precisa perguntar o modelo ao
    // teclado, porque vários modelos usam o mesmo PID, e isso um JSON não faz.)
    public static readonly HashSet<string> BuiltInDevices = [MonsGeekKeyboard.HardwareId];

    // Valores atuais dos controles de cada aparelho (brilho, efeito...), guardados enquanto o app está aberto.
    private static readonly Dictionary<string, Dictionary<string, int>> Values = [];

    // Drivers que estão no disco mas não podem ser usados (alterados ou inválidos), para avisar no rodapé.
    public static List<string> Problems { get; } = [];

    // Os nomes de arquivo só podem ter letras minúsculas, números e "-": nada de pastas ("../").
    private static readonly Regex SafeFileName = new(@"^[a-z0-9-]{1,40}\.json$");
    private static readonly Regex DeviceId = new(@"^VID_[0-9A-F]{4}&PID_[0-9A-F]{4}$");
    private static readonly Regex Sha256 = new(@"^[0-9a-fA-F]{64}$");

    private static string Source =>
        Environment.GetEnvironmentVariable("PERIALLS_DRIVERS") is { Length: > 0 } s ? s : DefaultSource;
    private static bool IsTestSource => Source != DefaultSource;

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
                warning = Lang.T("drivers.indexInvalid", error);
        }
        index ??= ReadFile(Path.Combine(CacheFolder, "index.json"), MaxIndexBytes) is { } cached
            ? ParseIndex(cached, out _)
            : null;
        Index = index ?? new DriverIndex();

        _openRgbIds = await LoadOpenRgbListAsync();
        LoadInstalled();
        return warning;
    }

    // Lê os drivers já baixados. Cada um é conferido de novo: o hash precisa ser o mesmo do download.
    private static void LoadInstalled()
    {
        Problems.Clear();
        Loaded.Clear();
        LocalDrivers.Clear();

        try
        {
            _installed = ReadFile(InstalledFile, MaxIndexBytes) is { } json
                ? JsonSerializer.Deserialize<Dictionary<string, InstalledDriver>>(json, Json) ?? []
                : [];
        }
        catch (JsonException)
        {
            _installed = [];
        }

        foreach (var (file, record) in _installed)
        {
            if (!SafeFileName.IsMatch(file) || ReadFile(Path.Combine(DriversFolder, file), Driver.MaxFileBytes) is not { } data)
                continue; // apagado: o ícone volta a oferecer o download
            if (!HashMatches(data, record.Sha256))
            {
                Problems.Add(Lang.T("drivers.changedOnDisk", record.Name));
                continue;
            }
            if (Driver.Load(data, isLocal: false, out var error) is { } driver)
                Loaded[file] = driver;
            else
                Problems.Add(Lang.T("drivers.installedInvalid", record.Name, error));
        }

        // Drivers em teste: valem as mesmas regras, só não há hash para conferir.
        try
        {
            foreach (var path in Directory.Exists(LocalDriversFolder) ? Directory.GetFiles(LocalDriversFolder, "*.json") : [])
            {
                var name = Path.GetFileName(path);
                if (ReadFile(path, Driver.MaxFileBytes) is not { } data)
                    Problems.Add(Lang.T("drivers.localTooBig", name));
                else if (Driver.Load(data, isLocal: true, out var error) is { } driver)
                    LocalDrivers.Add(driver);
                else
                    Problems.Add(Lang.T("drivers.localInvalid", name, error));
            }
        }
        catch
        {
            // Pasta inacessível: seguimos sem os drivers locais.
        }
    }

    // Baixa um driver do índice, confere o hash e as regras, e só então salva.
    public static async Task<(bool Ok, string Message)> InstallAsync(DriverIndexEntry entry)
    {
        var data = await TryFetchAsync(entry.File, Driver.MaxFileBytes);
        if (data == null)
            return (false, Lang.T("drivers.downloadFailed", entry.Name));
        if (!HashMatches(data, entry.Sha256))
            return (false, Lang.T("drivers.hashMismatch", entry.Name));

        var driver = Driver.Load(data, isLocal: false, out var error);
        if (driver == null)
            return (false, Lang.T("drivers.invalid", entry.Name, error));
        if (!entry.Devices.All(driver.HardwareIds.Contains))
            return (false, Lang.T("drivers.devicesMismatch", entry.Name));

        try
        {
            Directory.CreateDirectory(DriversFolder);
            await File.WriteAllBytesAsync(Path.Combine(DriversFolder, entry.File), data);
            _installed[entry.File] = new InstalledDriver
            {
                Name = entry.Name,
                Version = entry.Version,
                Sha256 = entry.Sha256.ToLowerInvariant(),
            };
            await File.WriteAllTextAsync(InstalledFile, JsonSerializer.Serialize(_installed, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            return (false, Lang.T("drivers.saveFailed", entry.Name, ex.Message));
        }

        Loaded[entry.File] = driver;
        return (true, Lang.T("drivers.installed", entry.Name, entry.Version));
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
    // Também liga o driver instalado ao aparelho, para os controles aparecerem no cartão.
    public static void Describe(DeviceInfo device)
    {
        // Aparelho com driver embutido: os drivers JSON dele são ignorados.
        var hasBuiltIn = device.HardwareIds.Any(BuiltInDevices.Contains);
        var entry = hasBuiltIn ? null : Index.Drivers.FirstOrDefault(e => e.Devices.Any(device.HardwareIds.Contains));
        device.DriverEntry = entry;
        device.Driver = null;

        // Um driver em teste (pasta "local") tem prioridade sobre o do índice.
        var local = hasBuiltIn ? null : LocalDrivers.FirstOrDefault(d => d.HardwareIds.Overlaps(device.HardwareIds));
        var installedFile = hasBuiltIn ? null
            : entry?.File ?? Loaded.FirstOrDefault(kv => kv.Value.HardwareIds.Overlaps(device.HardwareIds)).Key;
        var driver = local ?? (installedFile != null ? Loaded.GetValueOrDefault(installedFile) : null);
        if (driver != null)
        {
            device.Driver = driver;
            var key = device.Key + "|" + driver.Name;
            if (!Values.TryGetValue(key, out var values))
            {
                // O aparelho não informa o estado: começa com a última escolha salva (só para mostrar).
                Values[key] = values = new Dictionary<string, int>(driver.InitialValues);
                foreach (var (name, value) in UserSettings.Current.Find(device)?.Values ?? [])
                {
                    if (values.ContainsKey(name) && value is >= 0 and <= 255)
                        values[name] = value;
                }
            }
            device.DriverValues = values;
        }

        var record = entry != null ? _installed.GetValueOrDefault(entry.File) : null;
        if (local != null)
        {
            device.DriverAction = DriverAction.Installed;
            device.DriverTip = Lang.T("tip.localDriver", local.Name);
        }
        else if (entry != null && driver != null && record?.Version == entry.Version)
        {
            device.DriverAction = DriverAction.Installed;
            device.DriverTip = Lang.T("tip.installed", entry.Name, entry.Version);
        }
        else if (entry != null && driver != null)
        {
            device.DriverAction = DriverAction.Update;
            device.DriverTip = Lang.T("tip.update", entry.Name, record?.Version, entry.Version);
        }
        else if (entry != null)
        {
            device.DriverAction = DriverAction.Download;
            device.DriverTip = Lang.T("tip.download", entry.Name, entry.Version);
        }
        else if (driver != null)
        {
            // Instalado, mas saiu do índice: continua funcionando.
            device.DriverAction = DriverAction.Installed;
            device.DriverTip = Lang.T("tip.installedNoVersion", driver.Name);
        }
        else if (device.HasRgb || device.HasDpi || device.HasKeyboardLighting)
        {
            // Já tem funções pelo próprio app (Logitech, Akko, OpenRGB aberto): nada a oferecer.
            device.DriverAction = DriverAction.None;
        }
        else if (device.HardwareIds.Any(_openRgbIds.Contains))
        {
            device.DriverAction = DriverAction.OpenRgb;
            device.DriverTip = Lang.T("tip.openRgb");
        }
        else if (device.HardwareIds.Count > 0)
        {
            device.DriverAction = DriverAction.RequestSupport;
            device.DriverTip = Lang.T("tip.support");
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
            ["title"] = Lang.T("support.title", device.OriginalName),
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
            error = Lang.T("index.brokenJson", ex.Message);
            return null;
        }

        if (index == null || index.Format != 1)
        {
            error = Lang.T("index.unknownFormat");
            return null;
        }
        foreach (var e in index.Drivers)
        {
            if (!SafeFileName.IsMatch(e.File) || !Sha256.IsMatch(e.Sha256) || e.Version.Length is 0 or > 20
                || e.Name.Length is 0 or > 60 || e.Devices.Count == 0 || !e.Devices.All(id => DeviceId.IsMatch(id)))
            {
                error = Lang.T("index.badEntry", e.File);
                return null;
            }
        }
        if (index.OpenRgb is { } list && (!SafeFileName.IsMatch(list.File) || !Sha256.IsMatch(list.Sha256)))
        {
            error = Lang.T("index.badOpenRgb");
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

// O que foi baixado: a versão e o hash conferidos na hora do download.
public sealed class InstalledDriver
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
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
