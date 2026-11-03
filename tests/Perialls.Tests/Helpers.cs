using System.Text;

namespace Perialls.Tests;

// Aparelhos e drivers "de mentira" para os testes. Nada aqui abre um aparelho de verdade.
internal static class Helpers
{
    public static DeviceInfo Device(string id, string name = "Aparelho", string? serial = null)
    {
        var device = new DeviceInfo { Name = name, OriginalName = name, Serial = serial };
        device.HardwareIds.Add(id);
        return device;
    }

    // Um aparelho com RGB pelo OpenRGB, já com a cor que ele informa.
    public static DeviceInfo RgbDevice(string id, string? color, string name = "RGB")
    {
        var device = Device(id, name);
        device.RgbLocation = "loc:" + id;
        device.ReportedColor = color;
        return device;
    }

    public static MonsGeekKeyboard.Lighting Lighting(byte effect = 1, byte speed = 2, byte brightness = 3, byte option = 0,
        bool multicolor = false, byte r = 10, byte g = 20, byte b = 30) =>
        new(effect, speed, brightness, option, multicolor, r, g, b);

    // O mesmo driver de teste de tests/drivers-teste/teste.json (teclado Dell KB216).
    public const string DriverJson = """
        {
          "name": "Teclado Dell (teste)",
          "devices": [{ "vid": "0x413C", "pid": "0x2113", "model": "Dell KB216" }],
          "interface": { "usagePage": "0xFF00", "reportId": 0, "reportLength": 64, "type": "feature" },
          "checksum": "none",
          "defaults": { "r": 255, "g": 255, "b": 255 },
          "controls": {
            "color": { "bytes": "07 01 {r} {g} {b}" },
            "brightness": { "min": 0, "max": 4, "default": 4, "bytes": "07 02 {brightness}" },
            "effect": { "options": { "Estático": 0, "Onda": 1 }, "default": "Estático", "bytes": "07 03 {effect}" },
            "pollingRate": { "options": { "1000 Hz": 1, "8000 Hz": 4 }, "default": "1000 Hz", "bytes": "07 04 {pollingRate}" }
          }
        }
        """;

    public const string DriverDeviceId = "VID_413C&PID_2113";

    public static Driver LoadDriver()
    {
        var driver = Driver.Load(Encoding.UTF8.GetBytes(DriverJson), isLocal: true, out var error);
        Assert.True(driver != null, error);
        return driver!;
    }

    // Um perfil válido com um aparelho, para os testes que mexem num campo de cada vez.
    public static Profile ValidProfile() => new()
    {
        Name = "Jogo",
        Color = "#FF3B30",
        Devices = [new ProfileDevice { Id = "VID_046D&PID_C547", Name = "Mouse", Dpi = 1600 }],
    };
}
