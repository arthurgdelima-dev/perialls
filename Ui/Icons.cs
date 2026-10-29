using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Perialls;

// Ícones: desenhos de linha simples numa área de 24x24.
public static class Icons
{
    public static readonly Geometry Keyboard = Parse("M4,6.5 H20 A2,2 0 0 1 22,8.5 V15.5 A2,2 0 0 1 20,17.5 H4 A2,2 0 0 1 2,15.5 V8.5 A2,2 0 0 1 4,6.5 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M8,14 H16");
    public static readonly Geometry Mouse = Parse("M12,3 A6,6 0 0 1 18,9 V15 A6,6 0 0 1 6,15 V9 A6,6 0 0 1 12,3 Z M12,7 V10");
    public static readonly Geometry Headset = Parse("M4,15 V12 A8,8 0 0 1 20,12 V15 M4,15 H7 V20 H5 A1,1 0 0 1 4,19 Z M20,15 H17 V20 H19 A1,1 0 0 0 20,19 Z");
    public static readonly Geometry Controller = Parse("M7.5,7 H16.5 A4.5,4.5 0 0 1 21,11.5 L21.6,16.2 A2.2,2.2 0 0 1 17.7,17.9 L15.8,15.5 H8.2 L6.3,17.9 A2.2,2.2 0 0 1 2.4,16.2 L3,11.5 A4.5,4.5 0 0 1 7.5,7 Z M7,10 V13 M5.5,11.5 H8.5 M16,10.5 H16.01 M18,12.5 H18.01");
    public static readonly Geometry Fan = Parse("M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 Z M12,10.5 A1.5,1.5 0 1 1 12,13.5 A1.5,1.5 0 1 1 12,10.5 Z M12,10.5 C11,8 12,5.5 14.5,5.5 C15.5,7.5 14.5,9.8 13.2,10.9 Z M13.3,12.75 C15.96,13.13 17.63,15.25 16.38,17.42 C14.15,17.28 12.66,15.27 12.35,13.59 Z M10.7,12.75 C9.04,14.87 6.37,15.25 5.12,13.09 C6.35,11.22 8.85,10.94 10.45,11.51 Z");
    public static readonly Geometry Device = Parse("M9,3 V8 M15,3 V8 M7,8 H17 V13 A5,5 0 0 1 7,13 Z M12,18 V21");
    public static readonly Geometry Refresh = Parse("M20,12 A8,8 0 1 1 17.66,6.34 M17.5,2.5 V6.5 H21.5");
    public static readonly Geometry Eye = Parse("M2,12 C5,6.5 19,6.5 22,12 C19,17.5 5,17.5 2,12 Z M12,9.5 A2.5,2.5 0 1 1 12,14.5 A2.5,2.5 0 1 1 12,9.5 Z");
    public static readonly Geometry EyeOff = Parse("M2,12 C5,6.5 19,6.5 22,12 C19,17.5 5,17.5 2,12 Z M12,9.5 A2.5,2.5 0 1 1 12,14.5 A2.5,2.5 0 1 1 12,9.5 Z M4,4 L20,20");
    public static readonly Geometry Close = Parse("M7,7 L17,17 M17,7 L7,17");
    public static readonly Geometry Minimize = Parse("M7,12 H17");
    public static readonly Geometry Download = Parse("M12,4 V15 M7.5,10.5 L12,15 L16.5,10.5 M5,19.5 H19");
    public static readonly Geometry Plus = Parse("M12,6 V18 M6,12 H18");
    public static readonly Geometry Sync = Parse("M12,3 C12,3 5.5,10 5.5,14.5 A6.5,6.5 0 0 0 18.5,14.5 C18.5,10 12,3 12,3 Z M9,15 A3,3 0 0 0 12,18");
    public static readonly Geometry Check = Parse("M6,12.5 L10,16.5 L18,8");

    // O ícone escolhido conforme o tipo do aparelho (propriedade Icon do DeviceInfo).
    public static readonly IValueConverter ForKind = new FuncValueConverter<string?, Geometry>(icon => icon switch
    {
        "keyboard" => Keyboard,
        "mouse" => Mouse,
        "headset" => Headset,
        "controller" => Controller,
        "fan" => Fan,
        _ => Device,
    });

    // Olho aberto para aparelhos ocultos (clicar mostra), riscado para os visíveis (clicar oculta).
    public static readonly IValueConverter ForHidden = new FuncValueConverter<bool, Geometry>(hidden => hidden ? Eye : EyeOff);

    // O ícone do canto do cartão, conforme o que dá para fazer com o driver do aparelho.
    public static readonly IValueConverter ForDriver = new FuncValueConverter<DriverAction, Geometry?>(action => action switch
    {
        DriverAction.Download => Download,
        DriverAction.Update => Refresh,
        DriverAction.Installed => Check,
        DriverAction.RequestSupport => Plus,
        _ => null,
    });

    private static Geometry Parse(string data) => StreamGeometry.Parse(data);
}
