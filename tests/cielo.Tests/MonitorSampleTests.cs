using CieloCli.Models;
using Xunit;

namespace cielo.Tests;

public sealed class MonitorSampleTests
{
    [Fact]
    public void FromDevice_IncludesOutdoorWeatherInSample()
    {
        var device = new CieloDevice
        {
            DeviceName = "Living Room",
            MacAddress = "B0A732C61784",
            ApplianceId = 2990,
            IsFahrenheit = 1,
            Environment = new CieloEnvironment
            {
                Temperature = 67m,
                Humidity = 49m
            },
            LatestAction = new CieloActionState
            {
                Temperature = "68",
                Mode = "mode",
                Power = "on",
                FanSpeed = "medium"
            }
        };

        var outdoor = new OutdoorWeatherSnapshot(
            DateTimeOffset.Parse("2026-04-10T16:00:00Z"),
            55m,
            71m,
            53m,
            12m,
            3,
            "F",
            "mph");

        var sample = MonitorSample.FromDevice(device, DateTimeOffset.Parse("2026-04-10T16:05:00Z"), "poll", outdoor);

        Assert.Equal(55m, sample.OutdoorTemp);
        Assert.Equal(71m, sample.OutdoorHumidity);
        Assert.Equal(53m, sample.OutdoorApparentTemp);
        Assert.Equal(12m, sample.OutdoorWindSpeed);
        Assert.Equal(3, sample.OutdoorWeatherCode);
        Assert.Equal("F", sample.OutdoorTempUnit);
        Assert.Equal("mph", sample.OutdoorWindSpeedUnit);
        Assert.Contains("55", sample.Signature);
    }
}
