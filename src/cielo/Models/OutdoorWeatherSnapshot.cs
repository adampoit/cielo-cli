namespace CieloCli.Models;

internal sealed record OutdoorWeatherSnapshot(
	DateTimeOffset Timestamp,
	decimal? Temperature,
	decimal? RelativeHumidity,
	decimal? ApparentTemperature,
	decimal? WindSpeed,
	int? WeatherCode,
	string TemperatureUnit,
	string WindSpeedUnit
);
