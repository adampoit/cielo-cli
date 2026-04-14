using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CieloCli.Models;

namespace CieloCli.Services;

internal sealed class OpenMeteoWeatherClient
{
	private readonly HttpClient _httpClient = new();
	private readonly double _latitude;
	private readonly double _longitude;
	private readonly string _temperatureUnit;
	private readonly TimeSpan _refreshInterval;

	private OutdoorWeatherSnapshot? _cachedSnapshot;
	private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

	public OpenMeteoWeatherClient(
		double latitude,
		double longitude,
		string temperatureUnit,
		TimeSpan refreshInterval
	)
	{
		_latitude = latitude;
		_longitude = longitude;
		_temperatureUnit = string.Equals(temperatureUnit, "F", StringComparison.OrdinalIgnoreCase)
			? "fahrenheit"
			: "celsius";
		_refreshInterval = refreshInterval;
	}

	public async Task<OutdoorWeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken)
	{
		if (_cachedSnapshot is not null && DateTimeOffset.UtcNow < _refreshAfter)
		{
			return _cachedSnapshot;
		}

		var url = string.Join(
			'&',
			"https://api.open-meteo.com/v1/forecast?current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m",
			$"latitude={_latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
			$"longitude={_longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
			$"temperature_unit={_temperatureUnit}",
			"wind_speed_unit=mph",
			"timezone=auto",
			"forecast_days=1"
		);

		try
		{
			var response =
				await _httpClient.GetFromJsonAsync<OpenMeteoForecastResponse>(
					url,
					cancellationToken
				) ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");

			if (response.Current is null)
			{
				throw new InvalidOperationException(
					"Open-Meteo response did not contain current weather data."
				);
			}

			_cachedSnapshot = new OutdoorWeatherSnapshot(
				DateTimeOffset.TryParse(response.Current.Time, out var timestamp)
					? timestamp
					: DateTimeOffset.UtcNow,
				response.Current.Temperature,
				response.Current.RelativeHumidity,
				response.Current.ApparentTemperature,
				response.Current.WindSpeed,
				response.Current.WeatherCode,
				_temperatureUnit == "fahrenheit" ? "F" : "C",
				"mph"
			);
		}
		catch
		{
			_refreshAfter = DateTimeOffset.UtcNow.Add(_refreshInterval);
			return _cachedSnapshot;
		}

		_refreshAfter = DateTimeOffset.UtcNow.Add(_refreshInterval);
		return _cachedSnapshot;
	}

	private sealed class OpenMeteoForecastResponse
	{
		[JsonPropertyName("current")]
		public OpenMeteoCurrentWeather? Current { get; set; }
	}

	private sealed class OpenMeteoCurrentWeather
	{
		[JsonPropertyName("time")]
		public string Time { get; set; } = string.Empty;

		[JsonPropertyName("temperature_2m")]
		public decimal? Temperature { get; set; }

		[JsonPropertyName("relative_humidity_2m")]
		public decimal? RelativeHumidity { get; set; }

		[JsonPropertyName("apparent_temperature")]
		public decimal? ApparentTemperature { get; set; }

		[JsonPropertyName("wind_speed_10m")]
		public decimal? WindSpeed { get; set; }

		[JsonPropertyName("weather_code")]
		public int? WeatherCode { get; set; }
	}
}
