using System.Windows.Media;

namespace Perialls;

// Uma linha da lista: um aparelho físico e o que sabemos sobre ele.
public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Battery { get; set; } = "—";

    // Qual ícone mostrar: "keyboard", "mouse", "headset", "controller", "fan" ou "device".
    public string Icon { get; set; } = "device";

    // Códigos USB do aparelho (ex.: "VID_046D&PID_C547"). Servem para achar o mesmo
    // aparelho no OpenRGB e para lembrar quais aparelhos você ocultou.
    public List<string> HardwareIds { get; } = [];

    // Endereço do aparelho no OpenRGB. Se estiver vazio, o aparelho não tem RGB controlável.
    public string? RgbLocation { get; set; }
    public bool HasRgb => RgbLocation != null || HasKeyboardLighting;

    // Iluminação e Hz de teclados Akko/MonsGeek, pelo protocolo do driver oficial (sem OpenRGB).
    public MonsGeekKeyboard.Lighting? KeyboardLighting { get; set; }
    public bool HasKeyboardLighting => KeyboardLighting != null;
    public byte? KeyboardEffect => KeyboardLighting?.Effect;
    public int? ReportRateHz { get; set; }

    // DPI (só mouses Logitech com HID++, por enquanto). Para mudar, precisamos saber
    // em qual receptor (código USB) e em qual número dentro dele o mouse está.
    public int? Dpi { get; set; }
    public List<int> DpiPresets { get; set; } = [];
    public bool HasDpi => Dpi != null;
    public int LogitechProductId { get; set; }
    public byte LogitechIndex { get; set; }

    // Foto do aparelho (de Assets/Devices). Se for null, a tela mostra o ícone grande.
    public ImageSource? Photo { get; set; }

    // Nome que o aparelho/Windows informa, antes de você renomear. Usado para voltar ao original.
    public string OriginalName { get; set; } = "";
    public bool IsRenaming { get; set; }

    public bool IsHidden { get; set; }
    public string HideLabel => IsHidden ? "Mostrar" : "Ocultar";
    public double Opacity => IsHidden ? 0.45 : 1.0;

    // Identificação estável do aparelho, usada para lembrar se ele está oculto.
    public string Key => HardwareIds.FirstOrDefault() ?? Name;

    // Usado só para ordenar a lista (controles primeiro, áudio no fim).
    public int SortOrder { get; set; }
}
