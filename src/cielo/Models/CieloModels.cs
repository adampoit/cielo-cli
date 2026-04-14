using System.Text.Json;
using System.Text.Json.Serialization;

namespace CieloCli.Models;

internal sealed class ApiEnvelope<T>
{
	[JsonPropertyName("status")]
	public int Status { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("data")]
	public T? Data { get; set; }
}

internal sealed class RefreshTokenResponse
{
	[JsonPropertyName("accessToken")]
	public string AccessToken { get; set; } = string.Empty;

	[JsonPropertyName("refreshToken")]
	public string RefreshToken { get; set; } = string.Empty;

	[JsonPropertyName("expiresIn")]
	public long ExpiresIn { get; set; }
}

internal sealed class DeviceListResponse
{
	[JsonPropertyName("listDevices")]
	public List<CieloDevice> ListDevices { get; set; } = [];
}

internal sealed class ApplianceListResponse
{
	[JsonPropertyName("listAppliances")]
	public List<CieloAppliance> ListAppliances { get; set; } = [];
}

internal sealed class CieloDevice
{
	[JsonPropertyName("deviceName")]
	public string DeviceName { get; set; } = string.Empty;

	[JsonPropertyName("macAddress")]
	public string MacAddress { get; set; } = string.Empty;

	[JsonPropertyName("applianceId")]
	public long ApplianceId { get; set; }

	[JsonPropertyName("applianceType")]
	public string ApplianceType { get; set; } = string.Empty;

	[JsonPropertyName("deviceType")]
	public string DeviceType { get; set; } = string.Empty;

	[JsonPropertyName("deviceTypeVersion")]
	public string DeviceTypeVersion { get; set; } = string.Empty;

	[JsonPropertyName("fwVersion")]
	public string FwVersion { get; set; } = string.Empty;

	[JsonPropertyName("connectionSource")]
	public int ConnectionSource { get; set; }

	[JsonPropertyName("deviceStatus")]
	public JsonElement DeviceStatusRaw { get; set; }

	[JsonPropertyName("isFaren")]
	public int IsFahrenheit { get; set; }

	[JsonPropertyName("latEnv")]
	public CieloEnvironment Environment { get; set; } = new();

	[JsonPropertyName("latestAction")]
	public CieloActionState LatestAction { get; set; } = new();

	[JsonPropertyName("myRuleConfiguration")]
	public JsonElement? MyRuleConfiguration { get; set; }

	[JsonPropertyName("deviceSettings")]
	public JsonElement? DeviceSettings { get; set; }

	[JsonPropertyName("breezPresets")]
	public List<CieloPreset> BreezPresets { get; set; } = [];

	[JsonIgnore]
	public CieloAppliance? Appliance { get; set; }

	[JsonIgnore]
	public bool IsOnline =>
		DeviceStatusRaw.ValueKind == JsonValueKind.Number && DeviceStatusRaw.GetInt32() == 1
		|| DeviceStatusRaw.ValueKind == JsonValueKind.String
			&& string.Equals(DeviceStatusRaw.GetString(), "on", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool IsBreezMax =>
		string.Equals(DeviceType, "BREEZ-MAX", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public string NormalizedMode =>
		string.Equals(LatestAction.Mode, "mode", StringComparison.OrdinalIgnoreCase)
			? "cool"
			: LatestAction.Mode;

	[JsonIgnore]
	public string TemperatureUnit => IsFahrenheit == 1 ? "F" : "C";

	[JsonIgnore]
	public bool SupportsTargetTemperature =>
		!string.Equals(Appliance?.TemperatureRange, "inc:dec", StringComparison.OrdinalIgnoreCase);

	public Dictionary<string, object?> CreateActionPayload()
	{
		var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
		{
			["power"] = LatestAction.Power,
			["mode"] = LatestAction.Mode,
			["fanspeed"] = LatestAction.FanSpeed,
			["temp"] = LatestAction.Temperature,
			["swing"] = LatestAction.Swing,
			["swinginternal"] = "",
		};

		if (!string.IsNullOrWhiteSpace(LatestAction.Turbo))
		{
			payload["turbo"] = LatestAction.Turbo;
		}

		if (!string.IsNullOrWhiteSpace(LatestAction.Light))
		{
			payload["light"] = LatestAction.Light == "on/off" ? "off" : LatestAction.Light;
		}
		else if (!string.Equals(Appliance?.Mode, "mode", StringComparison.OrdinalIgnoreCase))
		{
			payload["light"] = "off";
		}

		if (!string.IsNullOrWhiteSpace(LatestAction.FollowMe))
		{
			payload["followme"] = LatestAction.FollowMe;
		}

		return payload;
	}

	public bool TryResolvePresetId(string preset, out int presetId)
	{
		var match = BreezPresets.FirstOrDefault(candidate =>
			string.Equals(candidate.Title, preset, StringComparison.OrdinalIgnoreCase)
		);

		if (match is not null)
		{
			presetId = match.PresetId;
			return true;
		}

		presetId = 0;
		return false;
	}

	public string? GetPresetTitle()
	{
		if (IsBreezMax && LatestAction.Preset.HasValue)
		{
			var presetId = GetInt32(LatestAction.Preset.Value);
			var match = BreezPresets.FirstOrDefault(candidate => candidate.PresetId == presetId);
			if (match is not null)
			{
				return match.Title;
			}
		}

		if (string.Equals(LatestAction.Turbo, "on", StringComparison.OrdinalIgnoreCase))
		{
			return "Turbo";
		}

		return null;
	}

	private static int GetInt32(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number => element.GetInt32(),
			JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
			_ => 0,
		};
	}
}

internal sealed class CieloEnvironment
{
	[JsonPropertyName("temp")]
	public decimal? Temperature { get; set; }

	[JsonPropertyName("humidity")]
	public decimal? Humidity { get; set; }
}

internal sealed class CieloActionState
{
	[JsonPropertyName("power")]
	public string Power { get; set; } = "off";

	[JsonPropertyName("mode")]
	public string Mode { get; set; } = string.Empty;

	[JsonPropertyName("fanspeed")]
	public string FanSpeed { get; set; } = string.Empty;

	[JsonPropertyName("temp")]
	public string Temperature { get; set; } = string.Empty;

	[JsonPropertyName("swing")]
	public string Swing { get; set; } = string.Empty;

	[JsonPropertyName("turbo")]
	public string Turbo { get; set; } = string.Empty;

	[JsonPropertyName("followme")]
	public string FollowMe { get; set; } = string.Empty;

	[JsonPropertyName("light")]
	public string Light { get; set; } = string.Empty;

	[JsonPropertyName("preset")]
	public JsonElement? Preset { get; set; }
}

internal sealed class CieloAppliance
{
	[JsonPropertyName("applianceId")]
	public long ApplianceId { get; set; }

	[JsonPropertyName("mode")]
	public string Mode { get; set; } = string.Empty;

	[JsonPropertyName("fan")]
	public string Fan { get; set; } = string.Empty;

	[JsonPropertyName("swing")]
	public string Swing { get; set; } = string.Empty;

	[JsonPropertyName("temp")]
	public string TemperatureRange { get; set; } = string.Empty;

	[JsonPropertyName("tempIncrement")]
	public decimal? TemperatureIncrement { get; set; }

	[JsonPropertyName("isFaren")]
	public int IsFahrenheit { get; set; }
}

internal sealed class CieloPreset
{
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("presetId")]
	public int PresetId { get; set; }
}

internal sealed class DeviceChanges
{
	public string? Power { get; init; }
	public string? Mode { get; init; }
	public int? Temperature { get; init; }
	public string? FanSpeed { get; init; }
	public string? Swing { get; init; }
	public string? Preset { get; init; }
}

internal sealed record PendingMessage(Dictionary<string, object?> Payload, string Summary);
