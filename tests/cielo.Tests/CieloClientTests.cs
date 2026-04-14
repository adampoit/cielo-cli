using CieloCli.Configuration;
using CieloCli.Models;
using CieloCli.Services;
using Xunit;

namespace cielo.Tests;

public sealed class CieloClientTests
{
	[Fact]
	public void BuildActionControlMessages_SendsIndividualMessagesForMultipleChanges()
	{
		using var client = CreateClient();
		var device = CreateDevice();

		var messages = client.BuildActionControlMessages(
			device,
			new DeviceChanges { Temperature = 66, FanSpeed = "high" }
		);

		Assert.Equal(2, messages.Count);
		Assert.Equal("temp 66", messages[0].Summary);
		Assert.Equal("temp", messages[0].Payload["actionType"]);
		Assert.Equal("66", messages[0].Payload["actionValue"]);

		Assert.Equal("fan high", messages[1].Summary);
		Assert.Equal("fanspeed", messages[1].Payload["actionType"]);
		Assert.Equal("high", messages[1].Payload["actionValue"]);
	}

	[Fact]
	public void BuildActionControlMessages_SkipsNoOpChanges()
	{
		using var client = CreateClient();
		var device = CreateDevice();

		var messages = client.BuildActionControlMessages(
			device,
			new DeviceChanges
			{
				Temperature = 65,
				FanSpeed = "medium",
				Mode = "heat",
			}
		);

		Assert.Empty(messages);
	}

	[Fact]
	public void BuildActionControlMessages_SkipsNoOpButSendsActualChanges()
	{
		using var client = CreateClient();
		var device = CreateDevice();

		var messages = client.BuildActionControlMessages(
			device,
			new DeviceChanges
			{
				Temperature = 65,
				FanSpeed = "high",
				Mode = "heat",
			}
		);

		Assert.Single(messages);
		Assert.Equal("fan high", messages[0].Summary);
	}

	[Fact]
	public void BuildActionControlMessages_PowerOnWhenNeededForOtherChanges()
	{
		using var client = CreateClient();
		var device = CreateDevice();
		device.LatestAction.Power = "off";

		var messages = client.BuildActionControlMessages(
			device,
			new DeviceChanges { Temperature = 66, FanSpeed = "high" }
		);

		Assert.Equal(3, messages.Count);
		Assert.Equal("power on", messages[0].Summary);
		Assert.Equal("temp 66", messages[1].Summary);
		Assert.Equal("fan high", messages[2].Summary);
	}

	[Fact]
	public void BuildActionControlMessages_DoesNotBatchPresetChanges()
	{
		using var client = CreateClient();
		var device = CreateDevice();

		var messages = client.BuildActionControlMessages(
			device,
			new DeviceChanges { Temperature = 66, Preset = "Turbo" }
		);

		Assert.Equal(2, messages.Count);
		Assert.Equal("temp 66", messages[0].Summary);
		Assert.Equal("preset Turbo", messages[1].Summary);
	}

	private static CieloClient CreateClient()
	{
		return new CieloClient(
			new CieloConfig
			{
				AccessToken = "token",
				RefreshToken = "refresh",
				SessionId = "session",
				UserId = "user",
				XApiKey = "key",
			},
			"/tmp/cielo-test-config.json"
		);
	}

	private static CieloDevice CreateDevice()
	{
		return new CieloDevice
		{
			DeviceName = "Living Room",
			MacAddress = "B0A732C61784",
			ApplianceId = 2990,
			ApplianceType = "AC",
			DeviceType = "BREEZ-MAX",
			DeviceTypeVersion = "BM01",
			FwVersion = "1.1.1,1.0.4",
			ConnectionSource = 1,
			IsFahrenheit = 1,
			LatestAction = new CieloActionState
			{
				Power = "on",
				Mode = "heat",
				FanSpeed = "medium",
				Temperature = "65",
				Swing = "auto",
				Turbo = "off",
				FollowMe = "off",
				Light = string.Empty,
			},
			Appliance = new CieloAppliance
			{
				ApplianceId = 2990,
				Mode = "cool",
				TemperatureRange = "60:90",
			},
			BreezPresets =
			[
				new CieloPreset { Title = "Home", PresetId = 1 },
				new CieloPreset { Title = "Away", PresetId = 2 },
			],
		};
	}
}
