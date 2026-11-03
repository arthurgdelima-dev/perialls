using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Perialls;

// "Copiar diagnóstico": um texto para colar numa mensagem ou issue quando algo não funciona em outro PC.
// Traz o sistema, a versão, os aparelhos (VID/PID e o caminho usado para cada um), a placa-mãe,
// o estado do OpenRGB e os últimos erros mostrados no rodapé. Não traz número de série nem pastas
// com o nome do usuário.
public static class Diagnostics
{
    private const int MaxErrors = 30;
    private static readonly object Lock = new();
    private static readonly Queue<string> Errors = new();

    // Guarda um erro que apareceu no rodapé (com a hora). Só os últimos MaxErrors.
    public static void RecordError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        lock (Lock)
        {
            Errors.Enqueue($"{DateTime.Now:HH:mm:ss} {message.Replace('\n', ' ')}");
            while (Errors.Count > MaxErrors)
                Errors.Dequeue();
        }
    }

    public static IReadOnlyList<string> RecentErrors
    {
        get
        {
            lock (Lock)
                return Errors.ToList();
        }
    }

    // Programas que também mexem no RGB e podem disputar os aparelhos com o OpenRGB/Perialls.
    private static readonly string[] RgbPrograms =
    [
        "OpenRGB", "LightingService", "ArmouryCrate", "ArmouryCrate.Service", "ArmourySocketServer", "AuraService",
        "MSI.CentralServer", "MSI Center", "MysticLight", "LEDKeeper2", "SignalRgb", "SignalRgbLauncher",
        "iCUE", "RGBFusion", "GCC", "Polychrome", "LGHUB", "lghub_agent", "Razer Synapse 3", "Akko Cloud", "MonsGeek", "iot_driver",
    ];

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";

    // Monta o texto. "openRgb" vem de OpenRgb.Describe() (fala com a rede, então quem chama roda fora da tela).
    public static string Build(IReadOnlyList<DeviceInfo> devices, BoardInfo? board, string openRgb, bool rgbOn)
    {
        var text = new StringBuilder();
        text.AppendLine("=== Perialls: diagnóstico ===");
        text.AppendLine($"data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Perialls: {Version}");
        text.AppendLine($"sistema: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        text.AppendLine($"idioma: {Lang.Current}");
        if (OperatingSystem.IsWindows())
            text.AppendLine($"administrador: {(Environment.IsPrivilegedProcess ? "sim" : "não")} · PawnIO: {(Motherboard.PawnIoInstalled() ? "instalado" : "não instalado")}");
        else
            text.AppendLine($"i2c: {Motherboard.LinuxI2c()}");

        text.AppendLine();
        text.AppendLine("--- placa-mãe ---");
        if (board == null)
        {
            text.AppendLine("não foi possível ler");
        }
        else
        {
            text.AppendLine($"{board.Vendor} / {board.Model}");
            var card = devices.FirstOrDefault(d => d.IsMotherboardRgb);
            text.AppendLine("RGB: " + (card?.BoardRgbPath ?? "fabricante sem RGB conhecido"));
        }

        text.AppendLine();
        text.AppendLine($"--- aparelhos ({devices.Count}) ---");
        foreach (var d in devices)
        {
            var ids = d.HardwareIds.Count > 0 ? string.Join(", ", d.HardwareIds) : "sem VID/PID";
            var name = d.Name == d.OriginalName || d.OriginalName == "" ? d.Name : $"{d.Name} (original: {d.OriginalName})";
            text.AppendLine($"* {name} | {d.Kind} | {d.Status}{(d.IsInternal ? " | interno" : "")}{(d.IsHidden ? " | oculto" : "")}");
            text.AppendLine($"    {ids}");
            text.AppendLine($"    caminho: {PathUsed(d)}");
            if (d.HasRgb)
                text.AppendLine($"    cor: atual {d.CurrentColor ?? "?"} (informada {d.ReportedColor ?? "-"}, salva {d.SavedColor ?? "-"})");
        }

        // Só quando há um teclado Akko/MonsGeek conectado (a busca por ele só roda nesse caso).
        if (devices.Any(d => d.HardwareIds.Contains(MonsGeekKeyboard.HardwareId)))
        {
            text.AppendLine();
            text.AppendLine("--- teclado Akko/MonsGeek ---");
            text.AppendLine(MonsGeekKeyboard.LastProbe ?? "nenhuma busca");
        }

        text.AppendLine();
        text.AppendLine($"--- OpenRGB (RGB ligado: {(rgbOn ? "sim" : "não")}) ---");
        text.AppendLine(openRgb);

        text.AppendLine();
        text.AppendLine("--- programas de RGB abertos ---");
        var running = RunningRgbPrograms();
        text.AppendLine(running.Count > 0 ? string.Join(", ", running) : "nenhum");

        text.AppendLine();
        var errors = RecentErrors;
        text.AppendLine($"--- últimos erros ({errors.Count}) ---");
        foreach (var e in errors)
            text.AppendLine(e);
        if (errors.Count == 0)
            text.AppendLine("nenhum");

        return HideUserFolder(text.ToString());
    }

    // Por onde o Perialls fala com o aparelho (pode ser mais de um caminho).
    internal static string PathUsed(DeviceInfo d)
    {
        var paths = new List<string>();
        if (d.KeyboardLighting != null)
            paths.Add("embutido Akko/MonsGeek");
        if (d.Compx != null)
            paths.Add("embutido Compx");
        if (d.LogitechProductId != 0)
            paths.Add("embutido Logitech HID++");
        if (d.Driver is { } driver)
            paths.Add($"driver JSON \"{driver.Name}\"{(d.DriverEntry is { } e ? $" v{e.Version}" : "")}");
        else if (d.DriverEntry is { } entry)
            paths.Add($"driver JSON \"{entry.Name}\" v{entry.Version} disponível, não instalado");
        if (d.RgbLocation != null)
            paths.Add($"OpenRGB ({d.RgbLocation})");
        else if (d.OpenRgbSupported)
            paths.Add("OpenRGB (suportado, ainda não ligado ou não encontrado por ele)");
        if (d.BoardRgbPath != null && d.RgbLocation == null && !d.OpenRgbSupported)
            paths.Add(d.BoardRgbPath);
        return paths.Count > 0 ? string.Join(" + ", paths) : "nenhum (só leitura do sistema)";
    }

    private static List<string> RunningRgbPrograms()
    {
        var found = new List<string>();
        foreach (var name in RgbPrograms)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                    found.Add(name);
                foreach (var p in processes)
                    p.Dispose();
            }
            catch
            {
                // Sem permissão para listar: segue.
            }
        }
        return found;
    }

    // Troca a pasta do usuário por "~" (o nome de usuário não precisa ir junto).
    internal static string HideUserFolder(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 3 ? text.Replace(home, "~", StringComparison.OrdinalIgnoreCase) : text;
    }
}
