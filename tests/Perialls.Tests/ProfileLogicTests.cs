using System.Text;
using System.Text.Json;
using static Perialls.Tests.Helpers;

namespace Perialls.Tests;

// ProfileLogic: a qual aparelho cada entrada vai e o que seria enviado (sem enviar nada).
public class MatchTests
{
    private static Profile With(params ProfileDevice[] devices) => new() { Name = "P", Devices = [.. devices] };

    [Fact]
    public void MatchesByUsbCode()
    {
        var mouse = Device("VID_046D&PID_C547");
        var plan = ProfileLogic.Match(With(new ProfileDevice { Id = "VID_046D&PID_C547" }), [Device("VID_1111&PID_2222"), mouse]);
        Assert.Same(mouse, Assert.Single(plan).Device);
    }

    [Fact]
    public void MatchesAnyOfTheHardwareIds()
    {
        var receiver = Device("VID_046D&PID_C547");
        receiver.HardwareIds.Add("VID_046D&PID_4093"); // o mouse dentro do receptor
        Assert.Single(ProfileLogic.Match(With(new ProfileDevice { Id = "VID_046D&PID_4093" }), [receiver]));
    }

    [Fact]
    public void DisconnectedDevicesAreSkipped()
    {
        var plan = ProfileLogic.Match(With(new ProfileDevice { Id = "VID_046D&PID_C547" }), [Device("VID_1111&PID_2222")]);
        Assert.Empty(plan);
    }

    [Fact]
    public void SerialSeparatesTwoEqualDevices()
    {
        var a = Device("VID_373B&PID_1014", "A", serial: "AAA");
        var b = Device("VID_373B&PID_1014", "B", serial: "BBB");
        var profile = With(
            new ProfileDevice { Id = "VID_373B&PID_1014", Serial = "BBB", Dpi = 800 },
            new ProfileDevice { Id = "VID_373B&PID_1014", Serial = "AAA", Dpi = 1600 });

        var plan = ProfileLogic.Match(profile, [a, b]);
        Assert.Equal(2, plan.Count);
        Assert.Same(b, plan.Single(x => x.Entry.Dpi == 800).Device);
        Assert.Same(a, plan.Single(x => x.Entry.Dpi == 1600).Device);
    }

    [Fact]
    public void DifferentSerialDoesNotMatch()
    {
        var plan = ProfileLogic.Match(With(new ProfileDevice { Id = "VID_373B&PID_1014", Serial = "AAA" }),
            [Device("VID_373B&PID_1014", serial: "ZZZ")]);
        Assert.Empty(plan);
    }

    [Fact]
    public void SerialKnownOnOnlyOneSideStillMatches()
    {
        Assert.Single(ProfileLogic.Match(With(new ProfileDevice { Id = "VID_373B&PID_1014", Serial = "AAA" }),
            [Device("VID_373B&PID_1014")]));
        Assert.Single(ProfileLogic.Match(With(new ProfileDevice { Id = "VID_373B&PID_1014" }),
            [Device("VID_373B&PID_1014", serial: "AAA")]));
    }

    [Fact]
    public void EachConnectedDeviceGetsOneEntry()
    {
        var profile = With(
            new ProfileDevice { Id = "VID_373B&PID_1014", Dpi = 400 },
            new ProfileDevice { Id = "VID_373B&PID_1014", Dpi = 800 },
            new ProfileDevice { Id = "VID_373B&PID_1014", Dpi = 1600 });
        var plan = ProfileLogic.Match(profile, [Device("VID_373B&PID_1014"), Device("VID_373B&PID_1014")]);

        Assert.Equal(2, plan.Count);
        Assert.Equal(2, plan.Select(x => x.Device).Distinct().Count());
    }

    [Fact]
    public void MatchesDevicesWithoutUsbCodeByName()
    {
        var ram = new DeviceInfo { Name = "Minha RAM", OriginalName = "Corsair Vengeance RGB" };
        Assert.Single(ProfileLogic.Match(With(new ProfileDevice { Id = "Corsair Vengeance RGB" }), [ram]));
    }
}

public class KeyboardLightingTests
{
    private static Dictionary<string, int> Values(params (string Key, int Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    [Fact]
    public void NewEffectUsesItsDefaultOption()
    {
        // Onda (4) só tem as opções 2 e 3: trocar para ela usa a 2, como a lista do cartão.
        var result = ProfileLogic.KeyboardLighting(Lighting(effect: 1, option: 0), Values(("effect", 4)), null);
        Assert.Equal(4, result.Effect);
        Assert.Equal(2, result.Option);
    }

    [Fact]
    public void SameEffectKeepsOption()
    {
        var result = ProfileLogic.KeyboardLighting(Lighting(effect: 4, option: 3), Values(("effect", 4)), null);
        Assert.Equal(3, result.Option);
    }

    [Fact]
    public void IgnoresUnknownEffectAndOutOfRangeLevels()
    {
        var current = Lighting(effect: 1, speed: 2, brightness: 3);
        var result = ProfileLogic.KeyboardLighting(current, Values(("effect", 13), ("brightness", 5), ("speed", 200)), null);
        Assert.Equal(current, result);
    }

    [Fact]
    public void AppliesBrightnessAndSpeed()
    {
        var result = ProfileLogic.KeyboardLighting(Lighting(), Values(("brightness", 0), ("speed", 4)), null);
        Assert.Equal(0, result.Brightness);
        Assert.Equal(4, result.Speed);
    }

    [Fact]
    public void BlackTurnsLedsOffAndKeepsColor()
    {
        var result = ProfileLogic.KeyboardLighting(Lighting(brightness: 3, r: 10, g: 20, b: 30), Values(), "#000000");
        Assert.Equal(0, result.Brightness);
        Assert.Equal((10, 20, 30), (result.R, result.G, result.B));
    }

    [Fact]
    public void ColorTurnsLedsBackOnAndSetsSingleColor()
    {
        var result = ProfileLogic.KeyboardLighting(Lighting(brightness: 0, multicolor: true), Values(), "#FF8000");
        Assert.Equal(4, result.Brightness);
        Assert.False(result.Multicolor);
        Assert.Equal((0xFF, 0x80, 0x00), (result.R, result.G, result.B));
    }

    [Fact]
    public void ColorKeepsBrightnessFromProfile()
    {
        var result = ProfileLogic.KeyboardLighting(Lighting(brightness: 4), Values(("brightness", 1)), "#FF0000");
        Assert.Equal(1, result.Brightness);
    }

    [Fact]
    public void NoColorKeepsMulticolor()
    {
        Assert.True(ProfileLogic.KeyboardLighting(Lighting(multicolor: true), Values(("speed", 1)), null).Multicolor);
    }
}

public class DriverCommandsTests
{
    [Fact]
    public void SendsOnlyOptionsTheDriverOffers()
    {
        var commands = ProfileLogic.DriverCommands(LoadDriver(),
            new Dictionary<string, int> { ["brightness"] = 3, ["effect"] = 1, ["pollingRate"] = 4, ["r"] = 9 });

        Assert.Equal(["brightness", "effect", "pollingRate"], commands.Select(c => c.Control.Key));
        Assert.All(commands, c => Assert.Single(c.Sets));
        Assert.Equal(3, commands[0].Sets["brightness"]);
        Assert.Equal(1, commands[1].Sets["effect"]);
        Assert.Equal(4, commands[2].Sets["pollingRate"]);
    }

    [Fact]
    public void SkipsValuesOutsideTheDriver()
    {
        // Brilho 9 está fora de 0 a 4; efeito 7 e Hz 2 não são opções do driver.
        var commands = ProfileLogic.DriverCommands(LoadDriver(),
            new Dictionary<string, int> { ["brightness"] = 9, ["effect"] = 7, ["pollingRate"] = 2 });
        Assert.Empty(commands);
    }

    [Fact]
    public void NeverSendsTheColorControl()
    {
        var commands = ProfileLogic.DriverCommands(LoadDriver(), new Dictionary<string, int> { ["r"] = 1, ["g"] = 2, ["b"] = 3 });
        Assert.Empty(commands);
    }
}

public class ExportImportTests
{
    private static Profile Sample() => new()
    {
        Name = "Jogo",
        Color = "#0A84FF",
        Devices =
        [
            new ProfileDevice { Id = "VID_373B&PID_1014", Serial = "SN1", Name = "VXE R1", Dpi = 1600, ReportRateHz = 1000 },
            new ProfileDevice { Id = "VID_1532&PID_0084", Serial = "SN2", Name = "Razer DeathAdder", Color = "#FF0000" },
        ],
    };

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void ExportLeavesOutSerialsAndDoesNotChangeTheProfile()
    {
        var profile = Sample();
        var text = Encoding.UTF8.GetString(ProfileLogic.Export(profile));

        Assert.DoesNotContain("SN1", text);
        Assert.DoesNotContain("serial", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"app\": \"Perialls\"", text);
        Assert.Equal("SN1", profile.Devices[0].Serial);
    }

    [Fact]
    public void ImportKeepsOnlyConnectedDevices()
    {
        var mouse = Device("VID_373B&PID_1014", serial: "OUTRA-SERIE");
        var result = ProfileLogic.Import(ProfileLogic.Export(Sample()), [mouse]);

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Profile!.Devices);
        Assert.Equal(1600, entry.Dpi);
        Assert.Null(entry.Serial);
        Assert.Equal(["Razer DeathAdder"], result.Ignored);
    }

    [Fact]
    public void ImportIgnoresSerialFromTheOtherPc()
    {
        var file = Json("""
            { "app": "Perialls", "format": 1, "profile": { "name": "P", "color": "#FF3B30",
              "devices": [ { "id": "VID_373B&PID_1014", "serial": "DO-AMIGO", "dpi": 800 } ] } }
            """);
        var result = ProfileLogic.Import(file, [Device("VID_373B&PID_1014", serial: "MEU")]);
        Assert.Single(result.Profile!.Devices);
    }

    [Fact]
    public void ImportWithNoConnectedDeviceIsRefused()
    {
        var result = ProfileLogic.Import(ProfileLogic.Export(Sample()), [Device("VID_1111&PID_2222")]);
        Assert.Null(result.Profile);
        Assert.NotNull(result.Error);
        Assert.Equal(2, result.Ignored.Count);
    }

    [Fact]
    public void ImportKeepsSyncColorEvenWithoutDevices()
    {
        var profile = Sample();
        profile.SyncColor = "#00FF00";
        var result = ProfileLogic.Import(ProfileLogic.Export(profile), []);
        Assert.Null(result.Error);
        Assert.Equal("#00FF00", result.Profile!.SyncColor);
        Assert.Empty(result.Profile.Devices);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "app": "OutroApp", "format": 1, "profile": { "name": "P", "color": "#FF3B30", "devices": [] } }""")]
    [InlineData("""{ "app": "Perialls", "format": 2, "profile": { "name": "P", "color": "#FF3B30", "devices": [] } }""")]
    [InlineData("""{ "app": "Perialls", "format": 1, "profile": { "name": "", "color": "#FF3B30", "devices": [] } }""")]
    [InlineData("""{ "app": "Perialls", "format": 1, "profile": { "name": "P", "color": "#FF3B30", "devices": [ { "id": "VID_373B&PID_1014", "dpi": 10 } ] } }""")]
    [InlineData("""{ "app": "Perialls", "format": 1, "profile": { "name": "P", "color": "#FF3B30", "devices": [ { "id": "VID_373B&PID_1014", "dpi": "muito" } ] } }""")]
    public void ImportRejectsBadFiles(string text)
    {
        var result = ProfileLogic.Import(Json(text), [Device("VID_373B&PID_1014")]);
        Assert.Null(result.Profile);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ImportRejectsTooBigFiles()
    {
        var result = ProfileLogic.Import(new byte[Profile.MaxFileBytes + 1], []);
        Assert.Null(result.Profile);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ExportedFileIsReadableJson()
    {
        using var doc = JsonDocument.Parse(ProfileLogic.Export(Sample()));
        Assert.Equal(1, doc.RootElement.GetProperty("format").GetInt32());
        Assert.Equal("Jogo", doc.RootElement.GetProperty("profile").GetProperty("name").GetString());
    }
}

public class StoreTests
{
    [Fact]
    public void UniqueNameAddsANumber()
    {
        var store = new ProfileStore { Profiles = [new Profile { Name = "Jogo" }, new Profile { Name = "Jogo (2)" }] };
        Assert.Equal("jogo (3)", store.UniqueName("jogo")); // maiúsculas não contam, e o nome digitado é mantido
        Assert.Equal("Noite", store.UniqueName("Noite"));
    }

    [Fact]
    public void UniqueNameIgnoresTheProfileBeingRenamed()
    {
        var jogo = new Profile { Name = "Jogo" };
        var store = new ProfileStore { Profiles = [jogo] };
        Assert.Equal("Jogo", store.UniqueName("Jogo", except: jogo));
    }

    [Fact]
    public void UniqueNameStaysWithinTheLimit()
    {
        var name = new string('a', Profile.MaxNameLength);
        var store = new ProfileStore { Profiles = [new Profile { Name = name }] };
        var unique = store.UniqueName(name);
        Assert.True(unique.Length <= Profile.MaxNameLength);
        Assert.EndsWith(" (2)", unique);
    }

    [Fact]
    public void NextColorPicksAnUnusedOne()
    {
        var store = new ProfileStore { Profiles = [new Profile { Color = Profile.Colors[0] }] };
        Assert.Equal(Profile.Colors[1], store.NextColor());
    }

    [Theory]
    [InlineData("#FF8000", 0xFF, 0x80, 0x00)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ffffff", 0xFF, 0xFF, 0xFF)]
    public void ParsesHexColors(string hex, int r, int g, int b) =>
        Assert.Equal(((byte)r, (byte)g, (byte)b), ProfileLogic.ParseHex(hex));
}
