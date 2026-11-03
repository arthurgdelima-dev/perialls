using OpenRGB.NET;
using static Perialls.Tests.Helpers;

namespace Perialls.Tests;

// Conferência da cor depois do envio: o que foi lido de volta bate com o que foi enviado?
public class ColorCheckTests
{
    [Fact]
    public void KeyboardSameLightingIsConfirmed() =>
        Assert.Null(MonsGeekKeyboard.Mismatch(Lighting(r: 255, g: 0, b: 0), Lighting(r: 255, g: 0, b: 0)));

    [Fact]
    public void KeyboardDifferentColorIsReported()
    {
        var problem = MonsGeekKeyboard.Mismatch(Lighting(r: 255, g: 0, b: 0), Lighting(r: 0, g: 0, b: 255));
        Assert.NotNull(problem);
        Assert.Contains("#FF0000", problem);
        Assert.Contains("#0000FF", problem);
    }

    [Fact]
    public void KeyboardStillMulticolorIsReported() =>
        Assert.NotNull(MonsGeekKeyboard.Mismatch(Lighting(r: 255), Lighting(r: 255, multicolor: true)));

    [Fact]
    public void KeyboardOffIgnoresColor() =>
        Assert.Null(MonsGeekKeyboard.Mismatch(Lighting(brightness: 0, r: 1), Lighting(brightness: 0, r: 99)));

    [Fact]
    public void KeyboardWrongEffectOrBrightnessIsReported()
    {
        Assert.NotNull(MonsGeekKeyboard.Mismatch(Lighting(effect: 1), Lighting(effect: 2)));
        Assert.NotNull(MonsGeekKeyboard.Mismatch(Lighting(brightness: 4), Lighting(brightness: 2)));
    }

    [Fact]
    public void OpenRgbMatchingLedsAreConfirmed()
    {
        var red = new Color(255, 0, 0);
        Assert.Null(OpenRgb.CheckApplied([red, red, red], null, red));
    }

    [Fact]
    public void OpenRgbWrongLedsAreReported()
    {
        var red = new Color(255, 0, 0);
        var problem = OpenRgb.CheckApplied([red, new Color(0, 0, 0)], null, red);
        Assert.NotNull(problem);
        Assert.Contains("#000000", problem);
    }

    [Fact]
    public void OpenRgbModeColorsWinOverLeds()
    {
        var red = new Color(255, 0, 0);
        Assert.Null(OpenRgb.CheckApplied([new Color(0, 0, 0)], [red], red));
        Assert.NotNull(OpenRgb.CheckApplied([red], [new Color(1, 2, 3)], red));
    }

    [Fact]
    public void OpenRgbWithoutColorsIsAFailure() =>
        Assert.NotNull(OpenRgb.CheckApplied([], null, new Color(1, 2, 3)));
}

// Placa-mãe: o cartão da controladora RGB ou, sem ela, o cartão com o aviso do SMBus.
public class MotherboardTests
{
    private static readonly BoardInfo Asus = new("ASUSTeK COMPUTER INC.", "TUF GAMING A520M-PLUS");

    private static DeviceInfo AuraUsb(bool isInternal = true)
    {
        var d = Device("VID_0B05&PID_19AF", "AURA LED Controller");
        d.Icon = "device";
        d.IsInternal = isInternal;
        d.OpenRgbSupported = true;
        return d;
    }

    [Fact]
    public void AuraUsbBecomesTheBoardCard()
    {
        var aura = AuraUsb();
        var devices = new List<DeviceInfo> { aura };
        Motherboard.Attach(devices, Asus, "aviso");

        Assert.Single(devices);
        Assert.Equal("ASUS TUF GAMING A520M-PLUS", aura.Name);
        Assert.True(aura.IsMotherboardRgb);
        Assert.False(aura.HasBoardNotice);
        Assert.Contains("VID_0B05&PID_19AF", aura.BoardRgbPath);
    }

    [Fact]
    public void WithoutUsbControllerTheBoardGetsTheSmbusNotice()
    {
        var keyboard = Device("VID_0B05&PID_1866", "ASUS keyboard");
        keyboard.Icon = "keyboard";
        keyboard.OpenRgbSupported = true;
        var devices = new List<DeviceInfo> { keyboard };
        Motherboard.Attach(devices, Asus, "aviso");

        Assert.Equal(2, devices.Count);
        Assert.Equal("ASUS keyboard", keyboard.Name); // teclado da mesma marca não vira a placa
        var board = devices[1];
        Assert.Equal("ASUS TUF GAMING A520M-PLUS", board.Name);
        Assert.Equal("aviso", board.BoardNotice);
        Assert.False(board.HasRgb);
    }

    [Fact]
    public void OpenRgbMotherboardWinsOverUsbGuess()
    {
        var smbus = new DeviceInfo { Name = "ASUS TUF", RgbLocation = "I2C: PIIX4, address 0x40", IsMotherboardRgb = true };
        var devices = new List<DeviceInfo> { AuraUsb(), smbus };
        Motherboard.Attach(devices, Asus, "aviso");
        Assert.Equal(2, devices.Count);
        Assert.Equal("ASUS TUF GAMING A520M-PLUS", smbus.Name);
        Assert.Contains("I2C", smbus.BoardRgbPath);
    }

    [Fact]
    public void BoardsWithoutRgbVendorGetNoCard()
    {
        var devices = new List<DeviceInfo>();
        Motherboard.Attach(devices, new BoardInfo("Dell Inc.", "0K240Y"), "aviso");
        Motherboard.Attach(devices, null, "aviso");
        Assert.Empty(devices);
    }

    [Fact]
    public void UnusedInternalDevicesAreDropped()
    {
        var aura = AuraUsb();
        var other = Device("VID_8087&PID_0029", "Bluetooth HID");
        other.IsInternal = true;
        var usb = Device("VID_1111&PID_2222");
        var devices = new List<DeviceInfo> { aura, other, usb };
        DeviceScanner.DropUnusedInternal(devices);
        Assert.Equal([aura, usb], devices);
    }
}

// O texto do "Copiar diagnóstico".
public class DiagnosticsTests
{
    [Fact]
    public void ListsDevicesWithVidPidPathAndErrors()
    {
        var aura = Device("VID_0B05&PID_19AF", "ASUS TUF GAMING A520M-PLUS");
        aura.OpenRgbSupported = true;
        var keyboard = Device("VID_3151&PID_502D", "Akko");
        keyboard.KeyboardLighting = Lighting();
        Diagnostics.RecordError("Não deu para mudar a cor de Akko: teste");

        var text = Diagnostics.Build([aura, keyboard], new BoardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING A520M-PLUS"),
            "servidor SDK: não respondeu", rgbOn: false);

        Assert.Contains("VID_0B05&PID_19AF", text);
        Assert.Contains("VID_3151&PID_502D", text);
        Assert.Contains("embutido Akko/MonsGeek", text);
        Assert.Contains("OpenRGB (suportado", text);
        Assert.Contains("TUF GAMING A520M-PLUS", text);
        Assert.Contains("servidor SDK: não respondeu", text);
        Assert.Contains("Akko: teste", text);
    }

    [Fact]
    public void DeviceWithoutAnyPathSaysSo() =>
        Assert.StartsWith("nenhum", Diagnostics.PathUsed(Device("VID_1111&PID_2222")));

    [Fact]
    public void UserFolderIsHidden()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(home, Diagnostics.HideUserFolder($"exe: {home}/openrgb/AppRun"));
    }
}
