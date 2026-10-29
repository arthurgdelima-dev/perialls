using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using OpenRGB.NET;

namespace Perialls;

// Conversa com o OpenRGB pela rede (o "SDK Server" dele, na porta 6742, só no próprio PC).
// O OpenRGB é um programa separado (GPL), que vem junto na pasta "openrgb" da release: nós só
// mandamos comandos para ele. Cada ação abre uma conexão rápida e fecha logo depois.
// Ele só é aberto quando a pessoa usa o RGB, nunca ao abrir o Perialls (veja EnsureRunningAsync).
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
        }
        else
        {
            if (mode != null)
                client.UpdateMode(device.Index, mode.Index);
            client.UpdateLeds(device.Index, Enumerable.Repeat(color, device.Leds.Length).ToArray());
        }

        // Aparelhos com memória própria: grava, para a cor continuar mesmo sem o OpenRGB aberto.
        if (mode != null && ((int)mode.Flags & ManualSaveFlag) != 0)
        {
            try
            {
                client.SaveMode(device.Index, mode.Index);
            }
            catch
            {
                // OpenRGB antigo (sem esse comando): a cor já foi aplicada, só não fica gravada.
            }
        }
    }

    // MODE_FLAG_MANUAL_SAVE do OpenRGB: o modo pode ser gravado na memória do aparelho.
    // (A biblioteca OpenRGB.NET ainda não tem esse nome.)
    private const int ManualSaveFlag = 1 << 8;

    private static Device FindDevice(OpenRgbClient client, string location) =>
        client.GetAllControllerData().FirstOrDefault(d => d.Location == location)
        ?? throw new InvalidOperationException(Lang.T("error.openRgbLost"));

    // Descobre os códigos USB no endereço que o OpenRGB informa (ex.: "HID: \\?\HID#VID_1462&PID_7C56#...").
    public static string? HardwareId(Device device)
    {
        var match = Regex.Match(device.Location, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? $"VID_{match.Groups[1].Value}&PID_{match.Groups[2].Value}".ToUpperInvariant() : null;
    }

    // O OpenRGB que vem junto com o Perialls (pasta "openrgb" ao lado do executável). Rodando pelo
    // código-fonte (sem a pasta), usa um OpenRGB instalado no sistema, se houver.
    public static string? FindExe()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "openrgb", OperatingSystem.IsWindows() ? "OpenRGB.exe" : "AppRun");
        if (File.Exists(bundled))
            return bundled;

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

    // O OpenRGB que nós abrimos. Fica aberto enquanto o Perialls estiver aberto (para não detectar
    // os aparelhos de novo a cada cor) e fecha junto com ele.
    private static Process? _started;

    // Configuração própria, separada da do OpenRGB da pessoa: sem perfis, nada é aplicado ao abrir.
    private static readonly string ConfigFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perialls", "openrgb");

    // Liga o RGB. Se já houver um OpenRGB aberto com o servidor ligado (o da pessoa ou o nosso), usa ele.
    // Senão, abre o nosso só como servidor (sem janela, sem perfil) e espera ele achar os aparelhos.
    // Precisa ser chamado na thread da tela: no Linux, o OpenRGB fecha quando a thread que o abriu termina.
    // Devolve null se deu certo, ou o motivo.
    public static async Task<string?> EnsureRunningAsync()
    {
        if (await Task.Run(TryGetDevices) != null)
            return null;

        if (_started is not { HasExited: false })
        {
            if (FindExe() is not { } exe)
                return Lang.T("error.openRgbMissing");
            try
            {
                _started = StartServer(exe);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // A detecção leva alguns segundos e os aparelhos aparecem aos poucos: espera a lista parar de crescer.
        int last = -1, stable = 0;
        for (int i = 0; i < 30 && stable < 3; i++)
        {
            await Task.Delay(1000);
            var count = (await Task.Run(TryGetDevices))?.Length ?? -1;
            stable = count > 0 && count == last ? stable + 1 : 0;
            last = count;
            if (_started.HasExited)
                return Lang.T("error.openRgbClosed");
        }
        return last >= 0 ? null : Lang.T("error.openRgbNoReply");
    }

    private static Process StartServer(string exe)
    {
        Directory.CreateDirectory(ConfigFolder);
        string[] args = ["--server", "--config", ConfigFolder, "--noautoconnect"];

        // Linux: o setpriv faz o kernel encerrar o OpenRGB se o Perialls fechar de repente (sinal de morte do pai).
        var setpriv = OperatingSystem.IsLinux() && File.Exists("/usr/bin/setpriv");
        var info = new ProcessStartInfo(setpriv ? "/usr/bin/setpriv" : exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        if (setpriv)
        {
            foreach (var a in new[] { "--pdeathsig", "TERM", "--", exe })
                info.ArgumentList.Add(a);
        }
        foreach (var a in args)
            info.ArgumentList.Add(a);

        var process = Process.Start(info) ?? throw new InvalidOperationException(Lang.T("error.openRgbNoReply"));

        // Windows: um "job object" faz o Windows encerrar o OpenRGB junto com o Perialls, mesmo se ele travar.
        if (OperatingSystem.IsWindows())
            WindowsJob.Add(process);
        return process;
    }

    // Fecha o OpenRGB que nós abrimos (o da pessoa continua aberto).
    public static void StopIfStarted()
    {
        try
        {
            if (_started is { HasExited: false })
                _started.Kill(entireProcessTree: true);
        }
        catch
        {
            // Já tinha fechado.
        }
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsJob
    {
        private static IntPtr _job;

        public static void Add(Process process)
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                var info = new ExtendedLimitInformation();
                info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                SetInformationJobObject(_job, 9, ref info, Marshal.SizeOf<ExtendedLimitInformation>());
            }
            // O "job" fica aberto até o Perialls fechar; aí o Windows encerra tudo o que está nele.
            AssignProcessToJobObject(_job, process.Handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation Basic;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, int length);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }

    private static OpenRgbClient Connect() => new(Host, Port, "Perialls", true, 500);
}
