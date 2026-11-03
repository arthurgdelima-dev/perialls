using static Perialls.Tests.Helpers;

namespace Perialls.Tests;

// Profile.Validate: o que entra pelo profiles.json ou por um arquivo importado.
public class ValidateTests
{
    [Fact]
    public void ValidProfilePasses() => Assert.Null(ValidProfile().Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nome\ncom quebra")]
    [InlineData("12345678901234567890123456789012345678901")] // 41 letras
    public void RejectsBadNames(string name)
    {
        var profile = ValidProfile();
        profile.Name = name;
        Assert.NotNull(profile.Validate());
    }

    [Fact]
    public void AcceptsNameAtTheLimit()
    {
        var profile = ValidProfile();
        profile.Name = new string('a', Profile.MaxNameLength);
        Assert.Null(profile.Validate());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("FF0000")]
    public void RejectsBadColors(string color)
    {
        var profile = ValidProfile();
        profile.Color = color;
        Assert.NotNull(profile.Validate());

        profile = ValidProfile();
        profile.SyncColor = color;
        Assert.NotNull(profile.Validate());

        profile = ValidProfile();
        profile.Devices[0].Color = color;
        Assert.NotNull(profile.Validate());
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(50000, true)]
    [InlineData(50001, false)]
    public void ChecksDpiLimits(int dpi, bool valid)
    {
        var profile = ValidProfile();
        profile.Devices[0].Dpi = dpi;
        Assert.Equal(valid, profile.Validate() == null);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1000, true)]
    [InlineData(8000, true)]
    [InlineData(8001, false)]
    public void ChecksReportRateLimits(int hz, bool valid)
    {
        var profile = ValidProfile();
        profile.Devices[0].ReportRateHz = hz;
        Assert.Equal(valid, profile.Validate() == null);
    }

    [Theory]
    [InlineData("VID_046D&PID_C547", true)]
    [InlineData("Corsair Vengeance RGB", true)] // sem código USB: o nome
    [InlineData("", false)]
    [InlineData("nome\tcom tab", false)]
    public void ChecksDeviceIds(string id, bool valid)
    {
        var profile = ValidProfile();
        profile.Devices[0].Id = id;
        Assert.Equal(valid, profile.Validate() == null);
    }

    [Fact]
    public void RejectsVeryLongDeviceId()
    {
        var profile = ValidProfile();
        profile.Devices[0].Id = new string('x', 101);
        Assert.NotNull(profile.Validate());
    }

    [Theory]
    [InlineData("brightness", 0, true)]
    [InlineData("brightness", 255, true)]
    [InlineData("brightness", 256, false)]
    [InlineData("brightness", -1, false)]
    [InlineData("1brightness", 1, false)]
    [InlineData("bri-ght", 1, false)]
    public void ChecksValues(string key, int value, bool valid)
    {
        var profile = ValidProfile();
        profile.Devices[0].Values = new() { [key] = value };
        Assert.Equal(valid, profile.Validate() == null);
    }

    [Fact]
    public void RejectsTooManyValues()
    {
        var profile = ValidProfile();
        profile.Devices[0].Values = Enumerable.Range(0, 33).ToDictionary(i => $"v{i}", i => 1);
        Assert.NotNull(profile.Validate());
    }

    [Fact]
    public void RejectsTooManyDevices()
    {
        var profile = ValidProfile();
        profile.Devices = Enumerable.Range(0, 65).Select(_ => new ProfileDevice { Id = "VID_046D&PID_C547", Dpi = 800 }).ToList();
        Assert.NotNull(profile.Validate());
    }

    [Fact]
    public void RejectsBadSerial()
    {
        var profile = ValidProfile();
        profile.Devices[0].Serial = "";
        Assert.NotNull(profile.Validate());
        profile.Devices[0].Serial = new string('1', 65);
        Assert.NotNull(profile.Validate());
        profile.Devices[0].Serial = "ABC123";
        Assert.Null(profile.Validate());
    }
}
