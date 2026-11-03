using static Perialls.Tests.Helpers;

namespace Perialls.Tests;

// Profile.Capture: a "foto" das configurações atuais. Só entra o que cada aparelho suporta.
public class CaptureTests
{
    [Fact]
    public void LeavesOutDevicesWithNothingToKeep()
    {
        var profile = Profile.Capture("Jogo", "#FF3B30", [Device("VID_413C&PID_301A", "Mouse Dell")]);
        Assert.Empty(profile.Devices);
        Assert.Null(profile.SyncColor);
    }

    [Fact]
    public void KeepsDpiReportRateSerialAndName()
    {
        var mouse = Device("VID_373B&PID_1014", "VXE R1", serial: "SN1");
        mouse.Dpi = 1600;
        mouse.ReportRateHz = 1000;
        mouse.ReportRates = [125, 500, 1000];

        var entry = Assert.Single(Profile.Capture("Jogo", "#FF3B30", [mouse]).Devices);
        Assert.Equal("VID_373B&PID_1014", entry.Id);
        Assert.Equal("SN1", entry.Serial);
        Assert.Equal("VXE R1", entry.Name);
        Assert.Equal(1600, entry.Dpi);
        Assert.Equal(1000, entry.ReportRateHz);
        Assert.Null(entry.Color);
    }

    [Fact]
    public void ReportRateOnlyWhenTheDeviceOffersIt()
    {
        var mouse = Device("VID_046D&PID_C547");
        mouse.ReportRateHz = 1000; // sem lista de opções: o app não sabe mudar o Hz dele
        Assert.Empty(Profile.Capture("Jogo", "#FF3B30", [mouse]).Devices);
    }

    [Fact]
    public void SameColorEverywhereBecomesSyncRgb()
    {
        var profile = Profile.Capture("Jogo", "#FF3B30",
            [RgbDevice("VID_1462&PID_7C56", "#ff0000"), RgbDevice("VID_1B1C&PID_1B2D", "#FF0000")]);

        Assert.Equal("#FF0000", profile.SyncColor);
        Assert.All(profile.Devices, d => Assert.Null(d.Color));
    }

    [Fact]
    public void DifferentColorsAreKeptPerDevice()
    {
        var profile = Profile.Capture("Jogo", "#FF3B30",
            [RgbDevice("VID_1462&PID_7C56", "#FF0000"), RgbDevice("VID_1B1C&PID_1B2D", "#00ff00")]);

        Assert.Null(profile.SyncColor);
        Assert.Equal(["#FF0000", "#00FF00"], profile.Devices.Select(d => d.Color));
    }

    [Fact]
    public void OneRgbDeviceIsNotSync()
    {
        var profile = Profile.Capture("Jogo", "#FF3B30", [RgbDevice("VID_1462&PID_7C56", "#FF0000")]);
        Assert.Null(profile.SyncColor);
        Assert.Equal("#FF0000", Assert.Single(profile.Devices).Color);
    }

    [Fact]
    public void UnknownColorIsNotSync()
    {
        var profile = Profile.Capture("Jogo", "#FF3B30",
            [RgbDevice("VID_1462&PID_7C56", null), RgbDevice("VID_1B1C&PID_1B2D", null)]);
        Assert.Null(profile.SyncColor);
        Assert.Empty(profile.Devices); // sem cor conhecida, não há o que guardar
    }

    [Fact]
    public void KeepsAkkoLightingAndColor()
    {
        var keyboard = Device(MonsGeekKeyboard.HardwareId, "Akko");
        keyboard.KeyboardLighting = Lighting(effect: 4, speed: 1, brightness: 2, r: 0xFF, g: 0x80, b: 0x00);

        var entry = Assert.Single(Profile.Capture("Jogo", "#FF3B30", [keyboard]).Devices);
        Assert.Equal("#FF8000", entry.Color);
        Assert.Equal(new Dictionary<string, int> { ["effect"] = 4, ["brightness"] = 2, ["speed"] = 1 }, entry.Values);
    }

    [Fact]
    public void AkkoMulticolorHasNoColor()
    {
        var keyboard = Device(MonsGeekKeyboard.HardwareId, "Akko");
        keyboard.KeyboardLighting = Lighting(multicolor: true);

        var entry = Assert.Single(Profile.Capture("Jogo", "#FF3B30", [keyboard]).Devices);
        Assert.Null(entry.Color);
        Assert.NotNull(entry.Values);
    }

    [Fact]
    public void KeepsJsonDriverChoicesButNotColorVariables()
    {
        var device = Device(DriverDeviceId, "Dell KB216");
        device.Driver = LoadDriver();
        device.DriverValues = new() { ["r"] = 1, ["g"] = 2, ["b"] = 3, ["brightness"] = 2, ["effect"] = 1, ["pollingRate"] = 4 };
        device.SavedColor = "#00FF00";

        var entry = Assert.Single(Profile.Capture("Jogo", "#FF3B30", [device]).Devices);
        Assert.Equal("#00FF00", entry.Color);
        Assert.Equal(new Dictionary<string, int> { ["brightness"] = 2, ["effect"] = 1, ["pollingRate"] = 4 }, entry.Values);
    }

    [Fact]
    public void CapturedProfileIsValid()
    {
        var mouse = Device("VID_373B&PID_1014", "VXE R1", serial: "SN1");
        mouse.Dpi = 1600;
        var profile = Profile.Capture("Jogo", "#FF3B30", [mouse, RgbDevice("VID_1462&PID_7C56", "#123456")]);
        Assert.Null(profile.Validate());
    }
}
