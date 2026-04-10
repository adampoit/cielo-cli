using System.Globalization;

namespace CieloCli.Models;

internal sealed record MonitorSample(
    DateTimeOffset Timestamp,
    string Source,
    string DeviceName,
    string MacAddress,
    long ApplianceId,
    decimal? RoomTemp,
    decimal? Humidity,
    string TargetTemp,
    string TargetTempUnit,
    string Mode,
    string Power,
    string Fan,
    bool Online,
    decimal? OutdoorTemp,
    decimal? OutdoorHumidity,
    decimal? OutdoorApparentTemp,
    decimal? OutdoorWindSpeed,
    int? OutdoorWeatherCode,
    string? OutdoorTempUnit,
    string? OutdoorWindSpeedUnit)
{
    public decimal? TargetTempValue => decimal.TryParse(TargetTemp, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
        ? value
        : null;

    public string Signature => string.Join('|',
        FormatDecimal(RoomTemp),
        FormatDecimal(Humidity),
        TargetTemp,
        TargetTempUnit,
        Mode,
        Power,
        Fan,
        Online ? "1" : "0",
        FormatDecimal(OutdoorTemp),
        FormatDecimal(OutdoorHumidity),
        FormatDecimal(OutdoorApparentTemp),
        FormatDecimal(OutdoorWindSpeed),
        OutdoorWeatherCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        OutdoorTempUnit ?? string.Empty,
        OutdoorWindSpeedUnit ?? string.Empty);

    public static MonitorSample FromDevice(CieloDevice device, DateTimeOffset timestamp, string source, OutdoorWeatherSnapshot? outdoorWeather = null)
    {
        return new MonitorSample(
            timestamp,
            source,
            device.DeviceName,
            device.MacAddress,
            device.ApplianceId,
            device.Environment.Temperature,
            device.Environment.Humidity,
            device.LatestAction.Temperature,
            device.TemperatureUnit,
            device.NormalizedMode,
            device.LatestAction.Power,
            device.LatestAction.FanSpeed,
            device.IsOnline,
            outdoorWeather?.Temperature,
            outdoorWeather?.RelativeHumidity,
            outdoorWeather?.ApparentTemperature,
            outdoorWeather?.WindSpeed,
            outdoorWeather?.WeatherCode,
            outdoorWeather?.TemperatureUnit,
            outdoorWeather?.WindSpeedUnit);
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
