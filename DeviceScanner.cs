using System.Management;
using System.Text.RegularExpressions;

namespace Perialls;

// Descobre os periféricos conectados usando só o que o Windows já oferece.
// Nada aqui é específico do hardware de alguém: funciona para qualquer PC.
public static class DeviceScanner
{
    // Tipos (classes) de dispositivo do Windows que nos interessam.
    private static readonly string[] Classes =
        ["Mouse", "Keyboard", "HIDClass", "XnaComposite", "XboxComposite", "AudioEndpoint", "Bluetooth"];

    // Alguns fabricantes comuns, pelo código USB (VID). Só deixa o nome mais claro;
    // aparelhos de outras marcas aparecem normalmente, só sem o nome da marca na frente.
    // (A Microsoft fica de fora de propósito: muitos controles genéricos usam o código dela.)
    private static readonly Dictionary<string, string> Vendors = new()
    {
        ["046D"] = "Logitech",
        ["1532"] = "Razer",
        ["1B1C"] = "Corsair",
        ["1038"] = "SteelSeries",
        ["0951"] = "HyperX",
        ["03F0"] = "HyperX",
        ["3151"] = "Akko/MonsGeek",
        ["054C"] = "Sony",
        ["057E"] = "Nintendo",
        ["1462"] = "MSI",
        ["0B05"] = "ASUS",
        ["3434"] = "Keychron",
        ["258A"] = "Redragon",
    };

    // Uma "parte" de um aparelho, como o Windows a enxerga.
    private sealed record Part(string Id, string Name, string Class, int ErrorCode, string? BusName);

    public static ScanResult Scan()
    {
        // 1) Pergunta ao Windows (via WMI) quais dispositivos dessas classes estão presentes.
        var where = string.Join(" OR ", Classes.Select(c => $"PNPClass = '{c}'"));
        using var searcher = new ManagementObjectSearcher(
            $"SELECT DeviceID, Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE {where}");

        // 2) Junta as partes que pertencem ao mesmo aparelho físico (mesmo "contêiner").
        //    Ex.: um teclado aparece como 5 ou 6 partes, mas é um aparelho só.
        var groups = new Dictionary<string, List<Part>>();
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

                var part = new Part(id, obj["Name"] as string ?? "", cls, errorCode,
                    DeviceProperties.GetString(id, DeviceProperties.BusReportedName));

                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [];
                list.Add(part);
            }
        }

        // 3) Transforma cada grupo em linhas da lista.
        var devices = new List<DeviceInfo>();
        foreach (var parts in groups.Values)
            devices.AddRange(ToDevices(parts));

        // 4) Controles XInput: bateria e conexão vêm de outra API.
        AddXInputControllers(devices);

        // 5) RGB: tudo que o OpenRGB conhece ganha os controles de cor.
        var openRgbRunning = AddOpenRgbDevices(devices);

        // 6) Logitech: nome real, bateria e DPI pelo protocolo HID++.
        AddLogitechInfo(devices);

        // 7) Akko/MonsGeek: iluminação e Hz pelo protocolo do driver oficial.
        AddMonsGeekInfo(devices);

        devices = devices.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToList();
        return new ScanResult(devices, openRgbRunning);
    }

    public sealed record ScanResult(List<DeviceInfo> Devices, bool OpenRgbRunning);

    // Liga cada aparelho do OpenRGB à linha do mesmo aparelho USB (pelo código VID/PID).
    // Se não houver linha (ex.: memória RAM, placa de vídeo), cria uma nova.
    private static bool AddOpenRgbDevices(List<DeviceInfo> devices)
    {
        var rgbDevices = OpenRgb.TryGetDevices();
        if (rgbDevices == null)
            return false;

        foreach (var rgb in rgbDevices)
        {
            var hardwareId = OpenRgb.HardwareId(rgb);
            var row = hardwareId == null ? null : devices.FirstOrDefault(d => d.HardwareIds.Contains(hardwareId));
            if (row == null)
            {
                row = new DeviceInfo { Name = rgb.Name, Status = "Conectado" };
                if (hardwareId != null)
                    row.HardwareIds.Add(hardwareId);
                devices.Add(row);
            }

            row.RgbLocation = rgb.Location;

            // Aparelhos que estavam como "Outro" ganham o nome e o tipo que o OpenRGB conhece.
            if (row.Kind is "" or "Outro")
            {
                row.Name = rgb.Name;
                (row.Kind, row.Icon, row.SortOrder) = rgb.Type switch
                {
                    OpenRGB.NET.DeviceType.Motherboard => ("Placa-mãe · fans e RGB", "fan", 3),
                    OpenRGB.NET.DeviceType.Cooler => ("Cooler · RGB", "fan", 3),
                    OpenRGB.NET.DeviceType.Ledstrip => ("Fita de LED", "fan", 3),
                    OpenRGB.NET.DeviceType.Keyboard => ("Teclado", "keyboard", 2),
                    OpenRGB.NET.DeviceType.Mouse => ("Mouse", "mouse", 1),
                    OpenRGB.NET.DeviceType.Headset => ("Headset", "headset", 5),
                    OpenRGB.NET.DeviceType.Gamepad => ("Controle", "controller", 0),
                    _ => ("RGB", "device", 4),
                };
            }
        }
        return true;
    }

    // Para cada aparelho/receptor Logitech, pergunta pelo HID++ quem está ligado nele.
    // O primeiro aparelho completa a linha do receptor; se houver mais (ex.: teclado e mouse
    // no mesmo receptor Unifying), cada um ganha sua própria linha.
    private static void AddLogitechInfo(List<DeviceInfo> devices)
    {
        foreach (var row in devices.ToList())
        {
            var id = row.HardwareIds.FirstOrDefault(h => h.StartsWith("VID_046D&PID_"));
            if (id == null)
                continue;

            var productId = Convert.ToInt32(id[^4..], 16);
            var found = LogitechHidpp.Query(productId);
            if (found == null)
                continue; // esse produto não fala HID++

            if (found.Count == 0)
            {
                row.Status = "Receptor conectado · aparelho desligado";
                continue;
            }

            for (int i = 0; i < found.Count; i++)
            {
                var target = i == 0 ? row : new DeviceInfo { Status = "Conectado" };
                ApplyLogitech(target, found[i], productId);
                if (i > 0)
                    devices.Add(target);
            }
        }
    }

    private static void ApplyLogitech(DeviceInfo row, LogitechHidpp.Device device, int productId)
    {
        if (device.Name != "")
            row.Name = device.Name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase)
                ? device.Name
                : "Logitech " + device.Name;

        (string Kind, string Icon, int Order)? kind = device.Type switch
        {
            0 => ("Teclado", "keyboard", 2),
            3 or 4 or 5 => ("Mouse", "mouse", 1),
            8 => ("Headset", "headset", 5),
            12 => ("Controle", "controller", 0),
            _ => null,
        };
        if (kind != null)
            (row.Kind, row.Icon, row.SortOrder) = kind.Value;

        if (device.HardwareId != null && !row.HardwareIds.Contains(device.HardwareId))
            row.HardwareIds.Add(device.HardwareId);

        row.Battery = device.Battery;
        row.Dpi = device.Dpi;
        row.DpiPresets = device.DpiPresets;
        row.LogitechProductId = productId;
        row.LogitechIndex = device.Index;
    }

    private static void AddMonsGeekInfo(List<DeviceInfo> devices)
    {
        var row = devices.FirstOrDefault(d => d.HardwareIds.Contains("VID_3151&PID_502D"));
        if (row == null)
            return;

        MonsGeekKeyboard.State? state;
        try
        {
            state = MonsGeekKeyboard.Query();
        }
        catch
        {
            return; // teclado ocupado ou respondeu errado: o cartão aparece sem os controles
        }
        if (state == null)
            return; // não é um modelo que conhecemos

        row.Name = $"Akko/MonsGeek {state.Model}";
        row.KeyboardLighting = state.Lighting;
        row.ReportRateHz = state.ReportRateHz;
    }

    private static IEnumerable<DeviceInfo> ToDevices(List<Part> parts)
    {
        parts.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var bluetooth = parts.FirstOrDefault(p => p.Class == "Bluetooth");
        var hasProblem = parts.Any(p => p.ErrorCode != 0);

        // Status de conexão.
        string status;
        if (hasProblem)
            status = "Com problema";
        else if (bluetooth != null)
            // Um aparelho Bluetooth pareado mas desligado fica só com a parte "Bluetooth".
            // Quando conecta, o Windows cria as outras partes (mouse, áudio...).
            status = parts.Any(p => p.Class != "Bluetooth") ? "Conectado (Bluetooth)" : "Pareado, desconectado";
        else
            status = "Conectado";

        // Bateria Bluetooth (quando o aparelho informa).
        var battery = "—";
        if (bluetooth != null && DeviceProperties.GetByte(bluetooth.Id, DeviceProperties.BluetoothBattery) is byte level)
            battery = $"{level}%";

        // Áudio: cada saída/entrada vira uma linha (ex.: "Alto-falantes (...)", "Microfone (...)").
        foreach (var audio in parts.Where(p => p.Class == "AudioEndpoint"))
        {
            yield return new DeviceInfo
            {
                Name = audio.Name,
                Kind = "Áudio",
                Icon = "headset",
                Status = status,
                Battery = battery,
                SortOrder = 5,
            };
        }

        var others = parts.Where(p => p.Class != "AudioEndpoint").ToList();
        if (others.Count == 0)
            yield break;

        var name = PickName(others);
        var (kind, icon, order) = DescribeKind(others, name);
        var device = new DeviceInfo
        {
            Name = name,
            Kind = kind,
            Icon = icon,
            Status = status,
            Battery = battery,
            SortOrder = order,
        };
        device.HardwareIds.AddRange(others.Select(p => HardwareId(p.Id)).OfType<string>().Distinct());
        yield return device;
    }

    // Diz o tipo do aparelho a partir das partes que ele tem.
    // Mouses e teclados gamer costumam ter partes dos DOIS tipos, então usamos um palpite:
    // 1) o nome ajuda ("Keyboard", "Mouse"); 2) senão, ganha quem tiver mais partes
    //    (teclados têm várias partes de teclado; mouses e receptores têm uma de cada).
    private static (string Kind, string Icon, int Order) DescribeKind(List<Part> parts, string name)
    {
        if (parts.Any(p => p.Class is "XnaComposite" or "XboxComposite"))
            return ("Controle", "controller", 0);

        var keyboards = parts.Count(p => p.Class == "Keyboard");
        var mice = parts.Count(p => p.Class == "Mouse");

        if (keyboards > 0 || mice > 0)
        {
            var isKeyboard = Regex.IsMatch(name, @"keyboard|teclado|\bkb", RegexOptions.IgnoreCase)
                || (!Regex.IsMatch(name, "mouse", RegexOptions.IgnoreCase) && keyboards > mice);
            return isKeyboard ? ("Teclado", "keyboard", 2) : ("Mouse", "mouse", 1);
        }

        return (parts.Any(p => p.Class == "Bluetooth") ? "Bluetooth" : "Outro", "device", 4);
    }

    private static string? HardwareId(string deviceId)
    {
        var match = Regex.Match(deviceId, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? $"VID_{match.Groups[1].Value}&PID_{match.Groups[2].Value}".ToUpperInvariant() : null;
    }

    // Escolhe o nome mais útil entre as partes. Os nomes "genéricos" do Windows
    // ("HID Keyboard Device", "USB Input Device") são a última opção.
    private static string PickName(List<Part> parts)
    {
        var name =
            parts.FirstOrDefault(p => p.Class == "Bluetooth")?.Name
            ?? parts.FirstOrDefault(p => p.Class is "XnaComposite" or "XboxComposite")?.Name
            ?? parts.FirstOrDefault(p => p.Id.StartsWith(@"USB\") && !string.IsNullOrWhiteSpace(p.BusName))?.BusName
            ?? parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BusName))?.BusName
            ?? parts[0].Name;

        var vendor = parts.Select(p => FindVendor(p.Id)).FirstOrDefault(v => v != null);
        if (vendor != null && !vendor.Split('/').Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase)))
            name = $"{vendor} {name}";

        return name;
    }

    // Lê o código do fabricante no ID do aparelho.
    // USB: "VID_046D". Bluetooth: "VID&0002046D".
    private static string? FindVendor(string deviceId)
    {
        var match = Regex.Match(deviceId, @"VID(?:_|&000[12])([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success && Vendors.TryGetValue(match.Groups[1].Value.ToUpperInvariant(), out var v) ? v : null;
    }

    // Aparelhos Bluetooth de verdade (não o adaptador do PC nem serviços internos).
    private static bool IsBluetoothDevice(string id) =>
        id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);

    // O XInput não diz qual aparelho USB é qual controle, só "controle 1, 2, 3, 4".
    // Então colocamos a bateria nos controles já listados, na ordem; sobrando, criamos linhas novas.
    private static void AddXInputControllers(List<DeviceInfo> devices)
    {
        var controllers = devices.Where(d => d.Kind == "Controle").ToList();
        var next = 0;

        for (int slot = 0; slot < XInput.MaxControllers; slot++)
        {
            if (!XInput.IsConnected(slot))
                continue;

            var battery = XInput.GetBattery(slot);
            if (next < controllers.Count)
            {
                controllers[next++].Battery = battery;
            }
            else
            {
                devices.Add(new DeviceInfo
                {
                    Name = $"Controle XInput {slot + 1}",
                    Kind = "Controle",
                    Icon = "controller",
                    Status = "Conectado",
                    Battery = battery,
                    SortOrder = 0,
                });
            }
        }
    }
}
