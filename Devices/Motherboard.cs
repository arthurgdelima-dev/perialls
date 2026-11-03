using System.IO;
using System.Management;
using System.Runtime.Versioning;

namespace Perialls;

// A placa-mãe deste PC e o caminho do RGB dela.
//
// As placas controlam o RGB de dois jeitos:
//   - Aura USB / Mystic Light / RGB Fusion 2 / Polychrome USB: uma controladora USB ligada por dentro
//     da placa. Aparece como aparelho HID (ex.: ASUS 0B05:19AF "AURA LED Controller", usado na
//     TUF GAMING A520M-PLUS e em outras placas AMD 500). O OpenRGB fala com ela sem nada a mais.
//   - SMBus: um chip (ex.: ENE) no barramento interno da placa, sem aparelho USB. O OpenRGB só chega
//     nele com acesso especial: no Windows, o driver PawnIO e o programa aberto como administrador;
//     no Linux, o módulo i2c-dev e permissão nos /dev/i2c-*.
// Se a placa é de um fabricante com RGB e não achamos a controladora USB dela, mostramos no cartão
// da placa um aviso explicando o caminho do SMBus.
public sealed record BoardInfo(string Vendor, string Model)
{
    public string ShortVendor => Motherboard.VendorOf(Vendor)?.Short ?? Vendor;
    public string DisplayName => Model.StartsWith(ShortVendor, StringComparison.OrdinalIgnoreCase) ? Model : $"{ShortVendor} {Model}";
}

public static class Motherboard
{
    // Fabricantes de placas com RGB e o VID da controladora USB de RGB deles (null = só SMBus).
    private static readonly (string Match, string Short, string? UsbVid)[] Vendors =
    [
        ("ASUS", "ASUS", "0B05"),
        ("Micro-Star", "MSI", "1462"),
        ("MSI", "MSI", "1462"),
        ("Gigabyte", "Gigabyte", "048D"), // RGB Fusion 2 usa um chip ITE
        ("ASRock", "ASRock", "26CE"),
        ("NZXT", "NZXT", "1E71"),
        ("Biostar", "Biostar", null),
    ];

    internal static (string Short, string? UsbVid)? VendorOf(string vendor)
    {
        foreach (var v in Vendors)
            if (vendor.Contains(v.Match, StringComparison.OrdinalIgnoreCase))
                return (v.Short, v.UsbVid);
        return null;
    }

    // Lê o fabricante e o modelo da placa (Windows: WMI; Linux: /sys/class/dmi). Null se não deu.
    public static BoardInfo? Read()
    {
        try
        {
            var (vendor, model) = OperatingSystem.IsWindows() ? ReadWindows()
                : OperatingSystem.IsLinux() ? (ReadText("/sys/class/dmi/id/board_vendor"), ReadText("/sys/class/dmi/id/board_name"))
                : (null, null);
            return string.IsNullOrWhiteSpace(vendor) || string.IsNullOrWhiteSpace(model) ? null : new BoardInfo(vendor.Trim(), model.Trim());
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static (string?, string?) ReadWindows()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        foreach (ManagementObject obj in searcher.Get())
            using (obj)
                return (obj["Manufacturer"] as string, obj["Product"] as string);
        return (null, null);
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    // Acha o cartão da controladora RGB da placa e dá a ele o nome da placa. Sem controladora,
    // cria um cartão para a placa com o aviso do SMBus. Placas de fabricantes sem RGB não ganham cartão.
    // "smbusNotice" é o texto do aviso (depende do sistema; veja SmbusNotice).
    public static void Attach(List<DeviceInfo> devices, BoardInfo? board, string smbusNotice)
    {
        if (board == null || VendorOf(board.Vendor) is not { } vendor)
            return;

        var controller = devices.FirstOrDefault(d => d.IsMotherboardRgb)
            ?? devices.FirstOrDefault(d => IsBoardController(d, vendor.UsbVid, internalOnly: true))
            ?? devices.FirstOrDefault(d => IsBoardController(d, vendor.UsbVid, internalOnly: false));

        if (controller != null)
        {
            controller.Name = board.DisplayName;
            (controller.Kind, controller.Icon, controller.SortOrder) = (Lang.T("kind.motherboard"), "fan", 3);
            controller.IsMotherboardRgb = true;
            controller.BoardRgbPath = controller.HardwareIds.FirstOrDefault() is { } id
                ? Lang.T("board.viaUsb", id)
                : Lang.T("board.viaOpenRgb", controller.RgbLocation);
            return;
        }

        devices.Add(new DeviceInfo
        {
            Name = board.DisplayName,
            Kind = Lang.T("kind.motherboardOnly"),
            Icon = "fan",
            Status = Lang.T("board.status"),
            SortOrder = 3,
            IsMotherboardRgb = true,
            BoardRgbPath = Lang.T("board.viaSmbus"),
            BoardNotice = smbusNotice,
        });
    }

    // Uma controladora RGB USB do mesmo fabricante da placa (ex.: ASUS 0B05 que o OpenRGB conhece).
    // Teclados, mouses etc. da mesma marca ficam de fora.
    private static bool IsBoardController(DeviceInfo d, string? usbVid, bool internalOnly) =>
        usbVid != null
        && (d.OpenRgbSupported || d.RgbLocation != null)
        && (!internalOnly || d.IsInternal)
        && (d.Icon is "device" or "fan")
        && d.HardwareIds.Any(id => id.StartsWith($"VID_{usbVid}&", StringComparison.OrdinalIgnoreCase));

    // O aviso do SMBus, com o que falta neste PC.
    public static string SmbusNotice(bool openRgbSearched)
    {
        string text;
        if (OperatingSystem.IsWindows())
            text = Lang.T("board.smbusWindows",
                Lang.T(PawnIoInstalled() ? "board.yes" : "board.no"),
                Lang.T(Environment.IsPrivilegedProcess ? "board.yes" : "board.no"));
        else
            text = Lang.T("board.smbusLinux", LinuxI2c());
        return text + (openRgbSearched ? " " + Lang.T("board.openRgbSearched") : "");
    }

    // O PawnIO se instala como um serviço do Windows com esse nome.
    public static bool PawnIoInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    // Linux: quantos /dev/i2c-* existem e se dá para abrir (só abrir, sem mandar nada).
    public static string LinuxI2c()
    {
        try
        {
            var buses = Directory.GetFiles("/dev", "i2c-*");
            if (buses.Length == 0)
                return Lang.T("board.i2cNone");
            var readable = buses.Count(path =>
            {
                try
                {
                    using var _ = new FileStream(path, FileMode.Open, FileAccess.Read);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
            return Lang.T("board.i2cCount", buses.Length, readable);
        }
        catch
        {
            return Lang.T("board.i2cNone");
        }
    }
}
