using System.Text.Json;
using CieloCli.Models;

namespace CieloCli.Services;

internal static class CieloOutput
{
	internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	internal static readonly JsonSerializerOptions JsonLineOptions = new();

	public static void WriteDevices(IReadOnlyList<CieloDevice> devices, bool json)
	{
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(devices, JsonOptions));
			return;
		}

		foreach (
			var device in devices.OrderBy(
				device => device.DeviceName,
				StringComparer.OrdinalIgnoreCase
			)
		)
		{
			var room = device.Environment.Temperature is null
				? "n/a"
				: $"{device.Environment.Temperature}{device.TemperatureUnit}";
			Console.WriteLine(
				$"{device.DeviceName} | {device.MacAddress} | {device.LatestAction.Power} {device.NormalizedMode} {device.LatestAction.Temperature}{device.TemperatureUnit} | room {room} | {(device.IsOnline ? "online" : "offline")}"
			);
		}
	}

	public static void WriteDeviceStatus(CieloDevice device, bool json)
	{
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(device, JsonOptions));
			return;
		}

		Console.WriteLine(device.DeviceName);
		Console.WriteLine($"MAC: {device.MacAddress}");
		Console.WriteLine($"Appliance ID: {device.ApplianceId}");
		Console.WriteLine($"Online: {(device.IsOnline ? "yes" : "no")}");
		Console.WriteLine($"Power: {device.LatestAction.Power}");
		Console.WriteLine($"Mode: {device.NormalizedMode}");
		Console.WriteLine(
			$"Target Temp: {device.LatestAction.Temperature}{device.TemperatureUnit}"
		);
		Console.WriteLine($"Fan: {device.LatestAction.FanSpeed}");
		Console.WriteLine($"Swing: {device.LatestAction.Swing}");

		if (device.Environment.Temperature is not null)
		{
			Console.WriteLine(
				$"Room Temp: {device.Environment.Temperature}{device.TemperatureUnit}"
			);
		}

		if (device.Environment.Humidity is not null)
		{
			Console.WriteLine($"Humidity: {device.Environment.Humidity}%");
		}

		var preset = device.GetPresetTitle();
		if (!string.IsNullOrWhiteSpace(preset))
		{
			Console.WriteLine($"Preset: {preset}");
		}
	}

	public static void WriteCommandResult(string summary, JsonDocument response, bool json)
	{
		if (json)
		{
			Console.WriteLine(response.RootElement.GetRawText());
			return;
		}

		var messageType = TryGetString(response.RootElement, "message_type") ?? "message";
		var macAddress =
			TryGetString(response.RootElement, "mac_address")
			?? TryGetString(response.RootElement, "macAddress")
			?? "unknown-device";
		Console.WriteLine($"Applied {summary} ({messageType} for {macAddress})");
	}

	public static string GetMonitorSampleSignature(MonitorSample sample)
	{
		return sample.Signature;
	}

	public static void WriteMonitorSample(MonitorSample sample, bool json)
	{
		if (json)
		{
			Console.WriteLine(ToMonitorSampleJson(sample));
			return;
		}

		var roomTemp = sample.RoomTemp is null
			? "n/a"
			: $"{sample.RoomTemp}{sample.TargetTempUnit}";
		var humidity = sample.Humidity is null ? "n/a" : $"{sample.Humidity}%";
		var outdoor = sample.OutdoorTemp is null
			? string.Empty
			: $", outdoor {sample.OutdoorTemp}{sample.OutdoorTempUnit}"
				+ (
					sample.OutdoorApparentTemp is null
						? string.Empty
						: $" feels {sample.OutdoorApparentTemp}{sample.OutdoorTempUnit}"
				)
				+ (
					sample.OutdoorWindSpeed is null
						? string.Empty
						: $", wind {sample.OutdoorWindSpeed}{sample.OutdoorWindSpeedUnit}"
				);
		Console.WriteLine(
			$"[{sample.Timestamp:u}] {sample.DeviceName}: room {roomTemp}, humidity {humidity}, target {sample.TargetTemp}{sample.TargetTempUnit}, {sample.Power} {sample.Mode}, fan {sample.Fan}{outdoor}"
		);
	}

	public static string ToMonitorSampleJson(MonitorSample sample)
	{
		return JsonSerializer.Serialize(
			new
			{
				ts = sample.Timestamp,
				source = sample.Source,
				device = sample.DeviceName,
				macAddress = sample.MacAddress,
				applianceId = sample.ApplianceId,
				roomTemp = sample.RoomTemp,
				humidity = sample.Humidity,
				targetTemp = sample.TargetTemp,
				targetTempUnit = sample.TargetTempUnit,
				mode = sample.Mode,
				power = sample.Power,
				fan = sample.Fan,
				online = sample.Online,
				outdoorTemp = sample.OutdoorTemp,
				outdoorHumidity = sample.OutdoorHumidity,
				outdoorApparentTemp = sample.OutdoorApparentTemp,
				outdoorWindSpeed = sample.OutdoorWindSpeed,
				outdoorWeatherCode = sample.OutdoorWeatherCode,
				outdoorTempUnit = sample.OutdoorTempUnit,
				outdoorWindSpeedUnit = sample.OutdoorWindSpeedUnit,
			},
			JsonLineOptions
		);
	}

	public static bool ShouldWriteMessage(JsonElement message, string? targetMacAddress)
	{
		if (string.IsNullOrWhiteSpace(targetMacAddress))
		{
			return true;
		}

		var macAddress =
			TryGetString(message, "mac_address") ?? TryGetString(message, "macAddress");
		return string.Equals(macAddress, targetMacAddress, StringComparison.OrdinalIgnoreCase);
	}

	public static void WriteWatchMessage(
		JsonElement message,
		IReadOnlyDictionary<string, CieloDevice> devices,
		bool json
	)
	{
		if (json)
		{
			Console.WriteLine(message.GetRawText());
			return;
		}

		var messageType = TryGetString(message, "message_type") ?? "message";
		var macAddress =
			TryGetString(message, "mac_address")
			?? TryGetString(message, "macAddress")
			?? string.Empty;
		var deviceName = devices.TryGetValue(macAddress, out var device)
			? device.DeviceName
			: macAddress;
		var timestamp = DateTimeOffset.Now.ToString("u");

		if (string.Equals(messageType, "StateUpdate", StringComparison.OrdinalIgnoreCase))
		{
			var action = message.TryGetProperty("action", out var actionElement)
				? actionElement
				: default;
			var latEnv = message.TryGetProperty("lat_env_var", out var latEnvElement)
				? latEnvElement
				: default;
			var temp = TryGetString(action, "temp") ?? "?";
			var mode = TryGetString(action, "mode") ?? "?";
			if (string.Equals(mode, "mode", StringComparison.OrdinalIgnoreCase))
			{
				mode = "cool";
			}

			var power = TryGetString(action, "power") ?? "?";
			var roomTemp = TryGetString(latEnv, "temperature") ?? "?";
			var humidity = TryGetString(latEnv, "humidity");
			var humidityPart = humidity is null ? string.Empty : $", humidity {humidity}%";
			Console.WriteLine(
				$"[{timestamp}] {deviceName}: {power} {mode}, target {temp}, room {roomTemp}{humidityPart}"
			);
			return;
		}

		Console.WriteLine($"[{timestamp}] {deviceName}: {messageType}");
	}

	private static string? TryGetString(JsonElement element, string propertyName)
	{
		if (
			element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty(propertyName, out var value)
		)
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.ToString(),
			JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
			JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
			_ => value.ToString(),
		};
	}
}
