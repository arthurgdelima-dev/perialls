using System.Text.RegularExpressions;

namespace Perialls;

// Descobre os periféricos conectados usando só o que o sistema já oferece.
// Nada aqui é específico do hardware de alguém: funciona para qualquer PC.
// A lista básica vem de WindowsDevices ou LinuxDevices; o resto (RGB, Logitech, Akko) é igual nos dois.
public static class DeviceScanner
{
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

    // Regra: a busca só LÊ. Nada aqui manda cor, DPI, Hz ou outra configuração para os aparelhos;
    // comandos só saem quando você muda algo na tela (veja MainWindow).
    public static ScanResult Scan()
    {
        // 1) Pergunta ao sistema quais aparelhos estão conectados, já com as partes de cada um juntas.
        //    Ex.: um teclado aparece como 5 ou 6 partes, mas é um aparelho só.
        var groups = OperatingSystem.IsWindows() ? WindowsDevices.List()
            : OperatingSystem.IsLinux() ? LinuxDevices.List()
            : [];

        // 2) Transforma cada grupo em linhas da lista.
        var devices = new List<DeviceInfo>();
        foreach (var group in groups)
            devices.AddRange(ToDevices(group));

        // 3) Controles XInput (só Windows): bateria e conexão vêm de outra API.
        //    No Linux a bateria dos controles já vem junto com a lista.
        if (OperatingSystem.IsWindows())
            AddXInputControllers(devices);

        // 4) RGB: tudo que o OpenRGB conhece ganha os controles de cor.
        var openRgbRunning = AddOpenRgbDevices(devices);

        // 5) Logitech: nome real, bateria e DPI pelo protocolo HID++.
        AddLogitechInfo(devices);

        // 6) Akko/MonsGeek: iluminação e Hz pelo protocolo do driver oficial.
        AddMonsGeekInfo(devices);

        // No Linux, sem a regra do udev o app não pode abrir os aparelhos HID (Logitech e Akko).
        var hidBlocked = OperatingSystem.IsLinux() && LinuxDevices.HidAccessDenied();

        devices = devices.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToList();
        return new ScanResult(devices, openRgbRunning, hidBlocked);
    }

    public sealed record ScanResult(List<DeviceInfo> Devices, bool OpenRgbRunning, bool HidAccessDenied);

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
                row = new DeviceInfo { Name = rgb.Name, Status = Lang.T("device.connected") };
                if (hardwareId != null)
                    row.HardwareIds.Add(hardwareId);
                devices.Add(row);
            }

            row.RgbLocation = rgb.Location;
            if (rgb.Colors.Length > 0)
                row.ReportedColor = $"#{rgb.Colors[0].R:X2}{rgb.Colors[0].G:X2}{rgb.Colors[0].B:X2}";

            // Aparelhos sem tipo conhecido ("Outro", ícone genérico) ganham o nome e o tipo que o OpenRGB conhece.
            if (row.Kind == "" || row.Icon == "device")
            {
                row.Name = rgb.Name;
                (row.Kind, row.Icon, row.SortOrder) = rgb.Type switch
                {
                    OpenRGB.NET.DeviceType.Motherboard => (Lang.T("kind.motherboard"), "fan", 3),
                    OpenRGB.NET.DeviceType.Cooler => (Lang.T("kind.cooler"), "fan", 3),
                    OpenRGB.NET.DeviceType.Ledstrip => (Lang.T("kind.ledstrip"), "fan", 3),
                    OpenRGB.NET.DeviceType.Keyboard => (Lang.T("kind.keyboard"), "keyboard", 2),
                    OpenRGB.NET.DeviceType.Mouse => (Lang.T("kind.mouse"), "mouse", 1),
                    OpenRGB.NET.DeviceType.Headset => (Lang.T("kind.headset"), "headset", 5),
                    OpenRGB.NET.DeviceType.Gamepad => (Lang.T("kind.controller"), "controller", 0),
                    _ => (Lang.T("kind.rgb"), "device", 4),
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
                row.Status = Lang.T("device.receiverOff");
                continue;
            }

            for (int i = 0; i < found.Count; i++)
            {
                var target = i == 0 ? row : new DeviceInfo { Status = Lang.T("device.connected") };
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
            0 => (Lang.T("kind.keyboard"), "keyboard", 2),
            3 or 4 or 5 => (Lang.T("kind.mouse"), "mouse", 1),
            8 => (Lang.T("kind.headset"), "headset", 5),
            12 => (Lang.T("kind.controller"), "controller", 0),
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
        var row = devices.FirstOrDefault(d => d.HardwareIds.Contains(MonsGeekKeyboard.HardwareId));
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

    private static IEnumerable<DeviceInfo> ToDevices(PartGroup group)
    {
        var (parts, status, battery) = group;

        // Áudio: cada saída/entrada vira uma linha (ex.: "Alto-falantes (...)", "Microfone (...)").
        foreach (var audio in parts.Where(p => p.Class == PartClass.Audio))
        {
            yield return new DeviceInfo
            {
                Name = audio.Name,
                Kind = Lang.T("kind.audio"),
                Icon = "headset",
                Status = status,
                Battery = battery,
                SortOrder = 5,
            };
        }

        var others = parts.Where(p => p.Class != PartClass.Audio).ToList();
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
        device.HardwareIds.AddRange(others.Select(p => p.HardwareId).OfType<string>().Distinct());
        yield return device;
    }

    // Diz o tipo do aparelho a partir das partes que ele tem.
    // Mouses e teclados gamer costumam ter partes dos DOIS tipos, então usamos um palpite:
    // 1) o nome ajuda ("Keyboard", "Mouse"); 2) senão, ganha quem tiver mais partes
    //    (teclados têm várias partes de teclado; mouses e receptores têm uma de cada).
    private static (string Kind, string Icon, int Order) DescribeKind(List<Part> parts, string name)
    {
        if (parts.Any(p => p.Class == PartClass.Controller))
            return (Lang.T("kind.controller"), "controller", 0);

        var keyboards = parts.Count(p => p.Class == PartClass.Keyboard);
        var mice = parts.Count(p => p.Class == PartClass.Mouse);

        if (keyboards > 0 || mice > 0)
        {
            var isKeyboard = Regex.IsMatch(name, @"keyboard|teclado|\bkb", RegexOptions.IgnoreCase)
                || (!Regex.IsMatch(name, "mouse", RegexOptions.IgnoreCase) && keyboards > mice);
            return isKeyboard ? (Lang.T("kind.keyboard"), "keyboard", 2) : (Lang.T("kind.mouse"), "mouse", 1);
        }

        // Aparelho Bluetooth desconectado (ou fone): o tipo vem do ícone que o Bluetooth informa.
        return parts.Select(p => p.Icon).FirstOrDefault(i => i != null) switch
        {
            "input-keyboard" => (Lang.T("kind.keyboard"), "keyboard", 2),
            "input-mouse" or "input-tablet" => (Lang.T("kind.mouse"), "mouse", 1),
            "input-gaming" => (Lang.T("kind.controller"), "controller", 0),
            { } i when i.StartsWith("audio-") => (Lang.T("kind.audio"), "headset", 5),
            _ => (Lang.T(parts.Any(p => p.Class == PartClass.Bluetooth) ? "kind.bluetooth" : "kind.other"), "device", 4),
        };
    }

    // Monta o código "VID_xxxx&PID_yyyy" que usamos em todo o app (fotos, OpenRGB, ocultos).
    internal static string HardwareId(string vendorId, string productId) =>
        $"VID_{vendorId}&PID_{productId}".ToUpperInvariant();

    // Escolhe o nome mais útil entre as partes. Os nomes "genéricos" do sistema
    // ("HID Keyboard Device", "USB Input Device") são a última opção.
    private static string PickName(List<Part> parts)
    {
        var name =
            parts.FirstOrDefault(p => p.Class == PartClass.Bluetooth)?.Name
            ?? parts.FirstOrDefault(p => p.Class == PartClass.Controller)?.Name
            ?? parts.FirstOrDefault(p => p.IsUsb && !string.IsNullOrWhiteSpace(p.BusName))?.BusName
            ?? parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BusName))?.BusName
            ?? parts[0].Name;

        var vendor = parts.Select(p => p.VendorId is { } v ? Vendors.GetValueOrDefault(v.ToUpperInvariant()) : null)
            .FirstOrDefault(v => v != null);
        if (vendor != null && !vendor.Split('/').Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase)))
            name = $"{vendor} {name}";

        return name;
    }

    // O XInput não diz qual aparelho USB é qual controle, só "controle 1, 2, 3, 4".
    // Então colocamos a bateria nos controles já listados, na ordem; sobrando, criamos linhas novas.
    private static void AddXInputControllers(List<DeviceInfo> devices)
    {
        var controllers = devices.Where(d => d.Icon == "controller").ToList();
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
                    Name = Lang.T("device.xinput", slot + 1),
                    Kind = Lang.T("kind.controller"),
                    Icon = "controller",
                    Status = Lang.T("device.connected"),
                    Battery = battery,
                    SortOrder = 0,
                });
            }
        }
    }
}

// Que tipo de parte é. Um teclado gamer, por exemplo, tem partes Keyboard e Mouse.
public enum PartClass { Keyboard, Mouse, Controller, Audio, Bluetooth, Other }

// Uma "parte" de um aparelho, como o sistema a enxerga.
//   BusName: nome que o próprio aparelho informa pelo USB (ex.: "USB Receiver", "MYSTIC LIGHT").
//   VendorId: código do fabricante (ex.: "046D"), para pôr o nome da marca na frente.
//   Icon: tipo que o Bluetooth informa (ex.: "input-mouse"), para aparelhos desconectados.
public sealed record Part(PartClass Class, string Name, string? BusName = null, bool IsUsb = false,
    string? HardwareId = null, string? VendorId = null, string? Icon = null);

// As partes de um mesmo aparelho físico, com o status e a bateria que o sistema informa.
public sealed record PartGroup(List<Part> Parts, string Status, string Battery);
