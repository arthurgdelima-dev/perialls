using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Perialls;

// Lista de aparelhos no Windows: pergunta ao WMI e junta as partes pelo "contêiner"
// (o Windows dá o mesmo ID para todas as partes do mesmo aparelho físico).
[SupportedOSPlatform("windows")]
public static class WindowsDevices
{
    // Tipos (classes) de dispositivo do Windows que nos interessam.
    private static readonly string[] Classes =
        ["Mouse", "Keyboard", "HIDClass", "XnaComposite", "XboxComposite", "AudioEndpoint", "Bluetooth"];

    private sealed record WinPart(string Id, string Name, string Class, int ErrorCode, string? BusName);

    public static List<PartGroup> List()
    {
        var where = string.Join(" OR ", Classes.Select(c => $"PNPClass = '{c}'"));
        using var searcher = new ManagementObjectSearcher(
            $"SELECT DeviceID, Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE {where}");

        var groups = new Dictionary<string, List<WinPart>>();
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                var id = obj["DeviceID"] as string ?? "";
                var cls = obj["PNPClass"] as string ?? "";
                var errorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);

                if (errorCode == 45) // 45 = "aparelho não está conectado"
                    continue;
                if (cls == "Bluetooth" && !IsBluetoothDevice(id)) // ignora o adaptador e serviços internos
                    continue;

                var container = DeviceProperties.GetGuid(id, DeviceProperties.ContainerId);
                if (container == null || container == DeviceProperties.InternalContainer)
                {
                    // Coisas internas ou virtuais: só mostramos mouse, teclado e áudio (ex.: teclado PS/2).
                    if (cls is "HIDClass" or "Bluetooth")
                        continue;
                }

                var key = container == null || container == DeviceProperties.InternalContainer
                    ? id
                    : container.Value.ToString();

                var part = new WinPart(id, obj["Name"] as string ?? "", cls, errorCode,
                    DeviceProperties.GetString(id, DeviceProperties.BusReportedName));

                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [];
                list.Add(part);
            }
        }

        return groups.Values.Select(ToGroup).ToList();
    }

    private static PartGroup ToGroup(List<WinPart> parts)
    {
        parts.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var bluetooth = parts.FirstOrDefault(p => p.Class == "Bluetooth");

        // Status de conexão.
        string status;
        if (parts.Any(p => p.ErrorCode != 0))
            status = Lang.T("device.problem");
        else if (bluetooth != null)
            // Um aparelho Bluetooth pareado mas desligado fica só com a parte "Bluetooth".
            // Quando conecta, o Windows cria as outras partes (mouse, áudio...).
            status = parts.Any(p => p.Class != "Bluetooth") ? Lang.T("device.connectedBluetooth") : Lang.T("device.pairedOff");
        else
            status = Lang.T("device.connected");

        // Bateria Bluetooth (quando o aparelho informa).
        var battery = "—";
        if (bluetooth != null && DeviceProperties.GetByte(bluetooth.Id, DeviceProperties.BluetoothBattery) is byte level)
            battery = $"{level}%";

        var converted = parts.Select(p => new Part(
            Class: p.Class switch
            {
                "Keyboard" => PartClass.Keyboard,
                "Mouse" => PartClass.Mouse,
                "XnaComposite" or "XboxComposite" => PartClass.Controller,
                "AudioEndpoint" => PartClass.Audio,
                "Bluetooth" => PartClass.Bluetooth,
                _ => PartClass.Other,
            },
            Name: p.Name,
            BusName: p.BusName,
            IsUsb: p.Id.StartsWith(@"USB\"),
            HardwareId: HardwareId(p.Id),
            VendorId: FindVendor(p.Id))).ToList();

        return new PartGroup(converted, status, battery);
    }

    private static string? HardwareId(string deviceId)
    {
        var match = Regex.Match(deviceId, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? DeviceScanner.HardwareId(match.Groups[1].Value, match.Groups[2].Value) : null;
    }

    // Lê o código do fabricante no ID do aparelho.
    // USB: "VID_046D". Bluetooth: "VID&0002046D".
    private static string? FindVendor(string deviceId)
    {
        var match = Regex.Match(deviceId, @"VID(?:_|&000[12])([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    // Aparelhos Bluetooth de verdade (não o adaptador do PC nem serviços internos).
    private static bool IsBluetoothDevice(string id) =>
        id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);
}
