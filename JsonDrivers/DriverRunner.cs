using HidSharp;

namespace Perialls;

// Envia os comandos de um driver JSON pelo HidSharp (funciona no Windows e no Linux).
// Só manda para um aparelho que está no cartão E na lista do driver, e só na interface declarada.
public static class DriverRunner
{
    // Alguns aparelhos precisam de um tempo entre um comando e outro ("delayMs" no driver).
    private static readonly object WriteLock = new();
    private static readonly Dictionary<string, DateTime> LastWrite = [];

    // Manda o comando do controle e, se o driver tiver, o "save" que grava na memória do aparelho.
    public static void Send(Driver driver, DriverControl control, IReadOnlyDictionary<string, int> values, DeviceInfo device)
    {
        List<byte[]> packets = [driver.Build(control, values)];
        if (driver.SaveCommand is { } save)
            packets.Add(driver.Build(save, values));

        // Aparelhos com driver embutido nunca recebem comandos de um driver JSON.
        var targets = driver.HardwareIds.Where(id => device.HardwareIds.Contains(id) && !DriverCatalog.BuiltInDevices.Contains(id)).ToList();
        if (targets.Count == 0)
            throw new DriverException("este aparelho não está na lista do driver");

        HidDevice? hid = null;
        int length = 0;
        foreach (var id in targets)
        {
            var vid = Convert.ToInt32(id[4..8], 16);
            var pid = Convert.ToInt32(id[^4..], 16);
            foreach (var candidate in DeviceList.Local.GetHidDevices(vid, pid))
            {
                length = ReportLength(candidate, driver.UsesFeatureReport);
                if (length >= driver.ReportLength + 1 && HasUsage(candidate, driver.UsagePage, driver.Usage))
                {
                    hid = candidate;
                    break;
                }
            }
            if (hid != null)
                break;
        }
        if (hid == null)
            throw new DriverException($"o aparelho não tem a interface que o driver pede (usage page 0x{driver.UsagePage:X4})");

        if (!hid.TryOpen(out HidStream stream))
            throw new DriverException(OperatingSystem.IsLinux()
                ? "sem permissão para abrir o aparelho (no Linux, veja a regra do udev no README)"
                : "o aparelho está ocupado (feche o software oficial)");

        using (stream)
        {
            foreach (var data in packets)
            {
                // [report ID] + dados, completando com zeros até o tamanho que o aparelho espera.
                var buffer = new byte[length];
                buffer[0] = driver.ReportId;
                data.CopyTo(buffer, 1);

                lock (WriteLock)
                {
                    var key = driver.Name;
                    var wait = LastWrite.GetValueOrDefault(key).AddMilliseconds(driver.DelayMs) - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                        Thread.Sleep(wait);

                    if (driver.UsesFeatureReport)
                        stream.SetFeature(buffer);
                    else
                        stream.Write(buffer);
                    LastWrite[key] = DateTime.UtcNow;
                }
            }
        }
    }

    private static int ReportLength(HidDevice d, bool feature)
    {
        try { return feature ? d.GetMaxFeatureReportLength() : d.GetMaxOutputReportLength(); }
        catch { return 0; }
    }

    // A "usage page" diz para que serve a interface (ex.: 0xFF00 = do fabricante, para configuração).
    private static bool HasUsage(HidDevice d, int usagePage, int? usage)
    {
        try
        {
            return d.GetReportDescriptor().DeviceItems.Any(item => item.Usages.GetAllValues().Any(u =>
                (u >> 16) == usagePage && (usage == null || (u & 0xFFFF) == usage)));
        }
        catch
        {
            return false;
        }
    }
}
