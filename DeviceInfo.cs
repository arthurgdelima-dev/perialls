using Avalonia.Media;

namespace Perialls;

// Uma linha da lista: um aparelho físico e o que sabemos sobre ele.
public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Battery { get; set; } = "—";
    public bool HasBattery => Battery != "—";

    // Qual ícone mostrar: "keyboard", "mouse", "headset", "controller", "fan" ou "device".
    public string Icon { get; set; } = "device";

    // Códigos USB do aparelho (ex.: "VID_046D&PID_C547"). Servem para achar o mesmo
    // aparelho no OpenRGB e para lembrar quais aparelhos você ocultou.
    public List<string> HardwareIds { get; } = [];

    // Endereço do aparelho no OpenRGB. Se estiver vazio, o aparelho não tem RGB controlável.
    public string? RgbLocation { get; set; }
    public bool HasRgb => RgbLocation != null || HasKeyboardLighting || DriverHasColor;

    // Iluminação e Hz de teclados Akko/MonsGeek, pelo protocolo do driver oficial (sem OpenRGB).
    public MonsGeekKeyboard.Lighting? KeyboardLighting { get; set; }
    public bool HasKeyboardLighting => KeyboardLighting != null;
    public MonsGeekKeyboard.Effect? KeyboardEffect =>
        MonsGeekKeyboard.Effects.FirstOrDefault(e => e.Id == KeyboardLighting?.Effect);
    public int? ReportRateHz { get; set; }

    // DPI (só mouses Logitech com HID++, por enquanto). Para mudar, precisamos saber
    // em qual receptor (código USB) e em qual número dentro dele o mouse está.
    public int? Dpi { get; set; }
    public List<int> DpiPresets { get; set; } = [];
    public bool HasDpi => Dpi != null;
    public int LogitechProductId { get; set; }
    public byte LogitechIndex { get; set; }

    // Foto do aparelho (de Assets/Devices). Se for null, a tela mostra o ícone grande.
    public IImage? Photo { get; set; }
    public bool HasPhoto => Photo != null;

    // Nome que o aparelho/sistema informa, antes de você renomear. Usado para voltar ao original.
    public string OriginalName { get; set; } = "";
    public bool IsRenaming { get; set; }

    public bool IsHidden { get; set; }
    public string HideLabel => IsHidden ? "Mostrar" : "Ocultar";
    public double Opacity => IsHidden ? 0.45 : 1.0;

    // Identificação estável do aparelho, usada para lembrar se ele está oculto e o nome que você deu.
    // Sem código USB (ex.: áudio), usa o nome original: o nome atual muda quando você renomeia.
    public string Key => HardwareIds.FirstOrDefault() ?? (OriginalName != "" ? OriginalName : Name);

    // Ícone do canto: baixar driver, OpenRGB, pedir suporte ou driver instalado (veja DriverCatalog).
    public DriverAction DriverAction { get; set; }
    public string DriverTip { get; set; } = "";
    public DriverIndexEntry? DriverEntry { get; set; }
    public bool HasDriverAction => DriverAction != DriverAction.None;

    // Driver JSON instalado para este aparelho e os valores atuais das variáveis dele.
    public Driver? Driver { get; set; }
    public Dictionary<string, int> DriverValues { get; set; } = [];
    public bool DriverHasColor => Driver?.Controls.Any(c => c.Kind == DriverControlKind.Color) == true;
    public bool HasDriverControls => DriverControlViews.Any();
    public IEnumerable<DriverControlView> DriverControlViews =>
        Driver?.Controls.Where(c => c.Kind != DriverControlKind.Color).Select(c => new DriverControlView(this, c)) ?? [];

    // Usado só para ordenar a lista (controles primeiro, áudio no fim).
    public int SortOrder { get; set; }

    // Botões do cartão. Cada botão sabe de qual aparelho é e se é o valor atual (fica destacado).
    public IEnumerable<ColorChoice> Swatches => SwatchColors.Select(hex => new ColorChoice(this, hex));
    public IEnumerable<Choice> DpiChoices => DpiPresets.Select(d => new Choice(this, d, d == Dpi));
    public IEnumerable<Choice> BrightnessChoices => Levels.Select(l => new Choice(this, l, l == KeyboardLighting?.Brightness));
    public IEnumerable<Choice> SpeedChoices => Levels.Select(l => new Choice(this, l, l == KeyboardLighting?.Speed));
    public IEnumerable<Choice> ReportRateChoices => MonsGeekKeyboard.ReportRates.Select(hz => new Choice(this, hz, hz == ReportRateHz));

    // Cores rápidas para RGB. A última (preto) desliga os LEDs.
    private static readonly string[] SwatchColors =
        ["#FF0000", "#FF6A00", "#FFD000", "#00FF40", "#00E5FF", "#0040FF", "#8A00FF", "#FF00A0", "#FFFFFF", "#000000"];

    // Níveis de brilho e velocidade dos teclados Akko/MonsGeek (0 a 4).
    private static readonly int[] Levels = [0, 1, 2, 3, 4];
}

// Um botão de valor (DPI, brilho, velocidade, Hz).
public sealed record Choice(DeviceInfo Device, int Value, bool IsCurrent);

// Um botão de cor.
public sealed record ColorChoice(DeviceInfo Device, string Hex)
{
    public IBrush Brush { get; } = SolidColorBrush.Parse(Hex);
}

// Um controle do driver JSON como aparece no cartão: título, valor atual e botões (ou lista, se forem muitos).
public sealed class DriverControlView
{
    public DriverControlView(DeviceInfo device, DriverControl control)
    {
        Label = control.Label;
        var values = device.DriverValues;
        Choices = control.Kind == DriverControlKind.Range
            ? Enumerable.Range(control.Min, control.Max - control.Min + 1)
                .Select(v => new DriverChoice(device, control, v.ToString(), new Dictionary<string, int> { [control.Key] = v },
                    values.GetValueOrDefault(control.Key, -1) == v))
                .ToList()
            : control.Options
                .Select(o => new DriverChoice(device, control, o.Label, o.Sets,
                    o.Sets.All(kv => values.TryGetValue(kv.Key, out var v) && v == kv.Value)))
                .ToList();
        Selected = Choices.FirstOrDefault(c => c.IsCurrent);
    }

    public string Label { get; }
    public List<DriverChoice> Choices { get; }
    public DriverChoice? Selected { get; }
    public string Current => Selected?.Label ?? "—";

    // Até 7 opções viram botões; mais que isso, uma lista para escolher.
    public bool IsList => Choices.Count > 7;
}

// Uma escolha de um controle do driver: o nome na tela e as variáveis que ela muda.
public sealed record DriverChoice(DeviceInfo Device, DriverControl Control, string Label,
    IReadOnlyDictionary<string, int> Sets, bool IsCurrent);
