using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using OpenRGB.NET;

namespace Perialls;

// Conversa com o OpenRGB pela rede (o "SDK Server" dele, na porta 6742).
// O OpenRGB é um programa separado, instalado à parte: nós só mandamos comandos para ele.
// Cada ação abre uma conexão rápida e fecha logo depois. Nada fica conectado.
public static class OpenRgb
{
    private const string Host = "127.0.0.1";
    private const int Port = 6742;

    // Lista os aparelhos que o OpenRGB conhece, ou null se ele não estiver aberto com o servidor ligado.
    public static Device[]? TryGetDevices()
    {
        try
        {
            using var client = Connect();
            return client.GetAllControllerData();
        }
        catch
        {
            return null;
        }
    }

    // Pinta todos os LEDs do aparelho com uma cor só.
    public static void SetColor(string location, byte r, byte g, byte b)
    {
        using var client = Connect();
        var device = FindDevice(client, location);

        // Conectores ARGB (ex.: JRAINBOW) começam com 0 LEDs, porque o OpenRGB não tem como saber
        // quantas fans estão ligadas. Para cor única, usamos o máximo do conector: LEDs a mais
        // que não existem são ignorados pela placa, e nenhum LED real fica de fora.
        // O OpenRGB guarda esse tamanho; dá para ajustar depois em "Resize zone" se quiser.
        var emptyZones = device.Zones.Where(z => z.LedCount == 0 && z.LedsMax > 0).ToList();
        if (emptyZones.Count > 0)
        {
            foreach (var zone in emptyZones)
                client.ResizeZone(device.Index, zone.Index, (int)zone.LedsMax);
            device = FindDevice(client, location);
        }

        // Pedimos o modo "Static" (cor fixa); se não existir, "Direct" (LED por LED).
        // Alguns aparelhos ignoram o pedido e ficam em "Direct", e a cor é aplicada do mesmo jeito.
        var mode = device.Modes.FirstOrDefault(m => m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase))
            ?? device.Modes.FirstOrDefault(m => m.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase));

        var color = new Color(r, g, b);

        if (mode != null && mode.ColorMode == ColorMode.ModeSpecific)
        {
            // Alguns aparelhos guardam a cor dentro do próprio modo, não em cada LED.
            var colors = Enumerable.Repeat(color, Math.Max(1, mode.Colors.Length)).ToArray();
            client.UpdateMode(device.Index, mode.Index, colors: colors);
            return;
        }

        if (mode != null)
            client.UpdateMode(device.Index, mode.Index);

        client.UpdateLeds(device.Index, Enumerable.Repeat(color, device.Leds.Length).ToArray());
    }

    private static Device FindDevice(OpenRgbClient client, string location) =>
        client.GetAllControllerData().FirstOrDefault(d => d.Location == location)
        ?? throw new InvalidOperationException("O OpenRGB não encontrou mais esse aparelho.");

    // Descobre os códigos USB no endereço que o OpenRGB informa (ex.: "HID: \\?\HID#VID_1462&PID_7C56#...").
    public static string? HardwareId(Device device)
    {
        var match = Regex.Match(device.Location, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? $"VID_{match.Groups[1].Value}&PID_{match.Groups[2].Value}".ToUpperInvariant() : null;
    }

    // Procura o OpenRGB nos lugares onde o instalador costuma colocá-lo.
    // No Linux ele é instalado pelo gerenciador de pacotes, como "openrgb", em alguma pasta do PATH.
    public static string? FindExe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => Path.Combine(f, "openrgb"))
                .FirstOrDefault(File.Exists);
        }

        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        ];
        return folders
            .Select(f => Path.Combine(f, "OpenRGB", "OpenRGB.exe"))
            .FirstOrDefault(File.Exists);
    }

    // Abre o OpenRGB minimizado com o servidor ligado e espera ele terminar de detectar os aparelhos.
    public static async Task<Process?> StartAsync(string exe)
    {
        var process = Process.Start(new ProcessStartInfo(exe, "--server --startminimized"));

        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (TryGetDevices() is { Length: > 0 })
                break;
        }
        return process;
    }

    private static OpenRgbClient Connect() => new(Host, Port, "Perialls", true, 500);
}
