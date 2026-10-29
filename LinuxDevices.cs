using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perialls;

// Lista de aparelhos no Linux, lida de /sys (o mesmo lugar de onde o udev e o "lsusb" tiram as informações).
// Juntamos as partes pelo aparelho USB de onde elas vêm, ou pelo endereço Bluetooth.
// O Bluetooth pareado/desconectado e a bateria dos fones vêm do BlueZ (pelo "busctl", que vem com o systemd).
[SupportedOSPlatform("linux")]
public static class LinuxDevices
{
    // Um aparelho em montagem: as partes e os caminhos em /sys de cada uma (para achar a bateria depois).
    private sealed class Group
    {
        public List<Part> Parts { get; } = [];
        public List<string> Paths { get; } = [];
        public string? UsbPath { get; init; }
        public bool IsBluetooth { get; init; }
        public string? Status { get; set; }
        public string Battery { get; set; } = "—";
    }

    // Nome da pasta de um aparelho HID: "barramento:VID:PID.número" (ex.: "0003:046D:C547.0002").
    // Barramento 0003 = USB, 0005 = Bluetooth.
    private static readonly Regex HidName = new(@"^([0-9A-F]{4}):([0-9A-F]{4}):([0-9A-F]{4})\.[0-9A-F]+$", RegexOptions.IgnoreCase);

    public static List<PartGroup> List()
    {
        var groups = new Dictionary<string, Group>();

        AddInputs(groups);
        AddHidOnlyDevices(groups);
        AddSoundCards(groups);
        AddBluetooth(groups);
        AddBatteries(groups.Values);

        return groups.Values
            .Select(g => new PartGroup(g.Parts, g.Status ?? (g.IsBluetooth ? "Conectado (Bluetooth)" : "Conectado"), g.Battery))
            .ToList();
    }

    // Teclados, mouses, touchpads e controles (/sys/class/input).
    private static void AddInputs(Dictionary<string, Group> groups)
    {
        foreach (var dir in Directories("/sys/class/input", "input*"))
        {
            var cls = InputClass(dir);
            if (cls == null)
                continue; // botão de ligar, alto-falante do PC, conector de fone etc.

            var path = RealPath(dir);
            var group = GroupFor(groups, path, Read(dir, "uniq"));
            if (group == null)
                continue;

            var hid = FindHid(path);
            group.Parts.Add(new Part(cls.Value, Read(dir, "name") ?? "",
                HardwareId: hid?.HardwareId, VendorId: hid?.VendorId));
            group.Paths.Add(path);
        }
    }

    // Aparelhos USB que só têm a parte HID, sem teclado/mouse (ex.: controladora RGB da placa-mãe).
    // No Windows eles aparecem na classe "HIDClass".
    private static void AddHidOnlyDevices(Dictionary<string, Group> groups)
    {
        foreach (var dir in Directories("/sys/class/hidraw", "hidraw*"))
        {
            var path = RealPath(Path.Combine(dir, "device"));
            if (FindHid(path) is not { Bus: "0003" } hid || FindUsbDevice(path) == null)
                continue;

            var group = GroupFor(groups, path, null)!;
            group.Parts.Add(new Part(PartClass.Other, "", HardwareId: hid.HardwareId, VendorId: hid.VendorId));
            group.Paths.Add(path);
        }
    }

    // Placas de som (/proc/asound/cards). As de USB (headsets) entram no mesmo cartão do aparelho.
    private static void AddSoundCards(Dictionary<string, Group> groups)
    {
        var names = ReadSoundCardNames();
        foreach (var dir in Directories("/sys/class/sound", "card*"))
        {
            if (!int.TryParse(Path.GetFileName(dir)[4..], out var number) || !names.TryGetValue(number, out var name))
                continue;

            var path = RealPath(Path.Combine(dir, "device"));
            var key = FindUsbDevice(path) ?? path;
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = new Group();
            group.Parts.Add(new Part(PartClass.Audio, name));
            group.Paths.Add(path);
        }
    }

    // " 0 [PCH            ]: HDA-Intel - HDA Intel PCH" → 0: "HDA Intel PCH"
    private static Dictionary<int, string> ReadSoundCardNames()
    {
        var names = new Dictionary<int, string>();
        try
        {
            foreach (var line in File.ReadLines("/proc/asound/cards"))
            {
                var match = Regex.Match(line, @"^\s*(\d+)\s+\[.*\]:\s*.*? - (.+)$");
                if (match.Success)
                    names[int.Parse(match.Groups[1].Value)] = match.Groups[2].Value.Trim();
            }
        }
        catch
        {
            // Sem /proc/asound: só não listamos o áudio.
        }
        return names;
    }

    // Aparelhos Bluetooth pareados (mesmo desligados) e a bateria que eles informam, pelo BlueZ.
    private static void AddBluetooth(Dictionary<string, Group> groups)
    {
        foreach (var bt in BlueZ.Devices())
        {
            var key = "bt:" + bt.Address.ToLowerInvariant();
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = new Group { IsBluetooth = true };

            group.Parts.Insert(0, new Part(PartClass.Bluetooth, bt.Name, VendorId: bt.VendorId, Icon: bt.Icon));
            group.Status = bt.Connected ? "Conectado (Bluetooth)" : "Pareado, desconectado";
            if (bt.Battery is byte level)
                group.Battery = $"{level}%";
        }
    }

    // Baterias que o kernel informa (/sys/class/power_supply): mouses Logitech, controles, aparelhos Bluetooth...
    private static void AddBatteries(IEnumerable<Group> groups)
    {
        foreach (var dir in Directories("/sys/class/power_supply", "*"))
        {
            // "Device" = bateria de um periférico (a do notebook é "System").
            if (Read(dir, "scope") != "Device")
                continue;

            var device = RealPath(Path.Combine(dir, "device"));
            var group = groups.FirstOrDefault(g =>
                g.Paths.Any(p => p.StartsWith(device + "/") || p == device)
                || (g.UsbPath != null && device.StartsWith(g.UsbPath + "/")));
            if (group == null || group.Battery != "—")
                continue;

            var level = int.TryParse(Read(dir, "capacity"), out var percent) ? $"{percent}%"
                : Read(dir, "capacity_level") switch
                {
                    "Full" => "Cheia",
                    "High" or "Normal" => "Boa",
                    "Low" => "Baixa",
                    "Critical" => "Crítica",
                    _ => null,
                };
            if (level == null)
                continue;

            group.Battery = level + Read(dir, "status") switch
            {
                "Charging" => " · carregando",
                "Full" => " · carregada",
                _ => "",
            };
        }
    }

    // Em qual aparelho físico uma parte entra:
    //   USB: o aparelho USB de onde ela vem (as partes de um teclado vêm todas do mesmo).
    //   Bluetooth: o endereço do aparelho ("uniq").
    //   Outros (teclado e touchpad do notebook): a própria parte. Coisas virtuais ficam de fora.
    private static Group? GroupFor(Dictionary<string, Group> groups, string path, string? uniq)
    {
        string key;
        string? usb = FindUsbDevice(path);
        var bluetooth = FindHid(path)?.Bus == "0005";

        if (usb != null)
            key = usb;
        else if (bluetooth)
            key = string.IsNullOrEmpty(uniq) ? path : "bt:" + uniq.ToLowerInvariant();
        else if (path.StartsWith("/sys/devices/virtual/"))
            return null;
        else
            key = FindHid(path)?.Path ?? path;

        if (groups.TryGetValue(key, out var group))
            return group;

        group = new Group { UsbPath = usb, IsBluetooth = bluetooth };
        if (usb != null)
        {
            // O próprio aparelho USB entra como a primeira parte: é dele o nome que ele informa
            // ("USB Receiver", "MYSTIC LIGHT") e o código USB principal.
            var vendor = Read(usb, "idVendor") ?? "";
            var product = Read(usb, "product");
            group.Parts.Add(new Part(PartClass.Other, product ?? "Aparelho USB", BusName: product, IsUsb: true,
                HardwareId: DeviceScanner.HardwareId(vendor, Read(usb, "idProduct") ?? ""), VendorId: vendor));
        }
        return groups[key] = group;
    }

    // Que tipo de parte é, pelo que ela consegue fazer (as mesmas regras do udev).
    private static PartClass? InputClass(string dir)
    {
        var keys = Bits(Path.Combine(dir, "capabilities", "key"));
        var rel = Bits(Path.Combine(dir, "capabilities", "rel"));
        var abs = Bits(Path.Combine(dir, "capabilities", "abs"));

        // Teclado: tem as teclas de Esc até D (códigos 1 a 31).
        if (Enumerable.Range(1, 31).All(k => HasBit(keys, k)))
            return PartClass.Keyboard;
        // Mouse: anda em X e Y e tem botão esquerdo (BTN_LEFT = 0x110).
        if (HasBit(rel, 0) && HasBit(rel, 1) && HasBit(keys, 0x110))
            return PartClass.Mouse;
        // Touchpad: posição absoluta e "dedo" (BTN_TOOL_FINGER = 0x145), sem caneta (0x140).
        if (HasBit(abs, 0) && HasBit(keys, 0x145) && !HasBit(keys, 0x140))
            return PartClass.Mouse;
        // Controle: botões de gamepad (0x130 a 0x13F) ou de joystick (0x120 a 0x12F) com eixos.
        if (Enumerable.Range(0x130, 16).Any(k => HasBit(keys, k))
            || (HasBit(abs, 0) && Enumerable.Range(0x120, 16).Any(k => HasBit(keys, k))))
            return PartClass.Controller;
        return null;
    }

    // As capacidades vêm como números hexadecimais de 64 bits separados por espaço, o mais alto primeiro.
    private static ulong[] Bits(string file) =>
        (ReadFile(file) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Reverse()
            .Select(w => ulong.TryParse(w, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0)
            .ToArray();

    private static bool HasBit(ulong[] words, int bit) =>
        bit / 64 < words.Length && ((words[bit / 64] >> (bit % 64)) & 1) == 1;

    private sealed record Hid(string Path, string Bus, string VendorId, string HardwareId);

    // Sobe pelas pastas até achar a do aparelho HID (ex.: ".../0003:046D:4093.0005").
    private static Hid? FindHid(string path)
    {
        for (var dir = path; dir.Length > "/sys/devices".Length; dir = Path.GetDirectoryName(dir)!)
        {
            var match = HidName.Match(Path.GetFileName(dir));
            if (match.Success)
                return new Hid(dir, match.Groups[1].Value, match.Groups[2].Value.ToUpperInvariant(),
                    DeviceScanner.HardwareId(match.Groups[2].Value, match.Groups[3].Value));
        }
        return null;
    }

    // Sobe pelas pastas até achar o aparelho USB (a pasta que tem idVendor; as interfaces não têm).
    private static string? FindUsbDevice(string path)
    {
        for (var dir = path; dir.Length > "/sys/devices".Length; dir = Path.GetDirectoryName(dir)!)
        {
            if (File.Exists(Path.Combine(dir, "idVendor")))
                return dir;
        }
        return null;
    }

    // Diz se há um aparelho Logitech ou Akko/MonsGeek que o app não tem permissão para abrir.
    // Por padrão só o root abre /dev/hidraw*; a regra do udev (linux/70-perialls.rules) libera.
    public static bool HidAccessDenied()
    {
        foreach (var dir in Directories("/sys/class/hidraw", "hidraw*"))
        {
            var hid = FindHid(RealPath(Path.Combine(dir, "device")));
            if (hid?.VendorId is not ("046D" or "3151"))
                continue;
            try
            {
                using var _ = File.Open("/dev/" + Path.GetFileName(dir), FileMode.Open, FileAccess.ReadWrite);
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                // Outro erro (aparelho saiu no meio): não é falta de permissão.
            }
        }
        return false;
    }

    private static IEnumerable<string> Directories(string folder, string pattern)
    {
        try
        {
            // As entradas de /sys/class são links, e não pastas: por isso EnumerateFileSystemEntries.
            return Directory.EnumerateFileSystemEntries(folder, pattern).Order().ToList();
        }
        catch
        {
            return [];
        }
    }

    // Caminho real, sem links. Os links de /sys são relativos à pasta real, por isso usamos o realpath da libc.
    private static string RealPath(string path)
    {
        var resolved = realpath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
            return path;
        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? path;
        }
        finally
        {
            free(resolved);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr realpath(string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr ptr);

    private static string? Read(string dir, string file) => ReadFile(Path.Combine(dir, file));

    private static string? ReadFile(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    // Conversa com o BlueZ (o serviço de Bluetooth do Linux) pelo "busctl", que devolve JSON.
    private static class BlueZ
    {
        public sealed record Device(string Address, string Name, bool Connected, string? Icon, string? VendorId, byte? Battery);

        public static List<Device> Devices()
        {
            var json = Run("busctl", "--system --json=short call org.bluez / org.freedesktop.DBus.ObjectManager GetManagedObjects");
            if (json == null)
                return [];

            var devices = new List<Device>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var obj in doc.RootElement.GetProperty("data")[0].EnumerateObject())
                {
                    if (!obj.Value.TryGetProperty("org.bluez.Device1", out var dev))
                        continue;
                    if (Get(dev, "Paired")?.GetBoolean() != true && Get(dev, "Connected")?.GetBoolean() != true)
                        continue;

                    // Modalias: "usb:v046DpB023d0008" ou "bluetooth:v046DpB023d0008" → fabricante 046D.
                    var modalias = Get(dev, "Modalias")?.GetString();
                    var vendor = modalias == null ? null : Regex.Match(modalias, @":v([0-9A-F]{4})", RegexOptions.IgnoreCase) is { Success: true } m
                        ? m.Groups[1].Value : null;

                    byte? battery = obj.Value.TryGetProperty("org.bluez.Battery1", out var bat) && Get(bat, "Percentage") is { } pct
                        ? pct.GetByte() : null;

                    devices.Add(new Device(
                        Get(dev, "Address")?.GetString() ?? "",
                        Get(dev, "Alias")?.GetString() ?? Get(dev, "Name")?.GetString() ?? "Aparelho Bluetooth",
                        Get(dev, "Connected")?.GetBoolean() == true,
                        Get(dev, "Icon")?.GetString(),
                        vendor,
                        battery));
                }
            }
            catch
            {
                // Resposta diferente do esperado: seguimos sem o BlueZ.
            }
            return devices;
        }

        // No JSON do busctl cada propriedade vem como { "type": "s", "data": valor }.
        private static JsonElement? Get(JsonElement iface, string name) =>
            iface.TryGetProperty(name, out var prop) && prop.TryGetProperty("data", out var data) ? data : null;

        private static string? Run(string program, string args)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(program, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                if (process == null)
                    return null;
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    return null;
                }
                return process.ExitCode == 0 ? output.Result : null;
            }
            catch
            {
                return null; // sem busctl ou sem Bluetooth
            }
        }
    }
}
