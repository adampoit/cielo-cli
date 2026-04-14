using System.Text.Json;
using CieloCli;
using CieloCli.Models;
using Xunit;

namespace cielo.Tests;

public sealed class CieloCommandAppTests
{
	[Fact]
	public void TryGetRequestedMode_ReturnsModeActionValue()
	{
		var message = new PendingMessage(
			new Dictionary<string, object?> { ["actionType"] = "mode", ["actionValue"] = "heat" },
			"mode heat"
		);

		var result = CieloCommandApp.TryGetRequestedMode(message, out var expectedMode);

		Assert.True(result);
		Assert.Equal("heat", expectedMode);
	}

	[Fact]
	public void TryGetRequestedMode_IgnoresNonModeMessages()
	{
		var message = new PendingMessage(
			new Dictionary<string, object?> { ["actionType"] = "temp", ["actionValue"] = "66" },
			"temp 66"
		);

		var result = CieloCommandApp.TryGetRequestedMode(message, out var expectedMode);

		Assert.False(result);
		Assert.Equal(string.Empty, expectedMode);
	}

	[Fact]
	public void IsMatchingModeState_ReturnsTrueForMatchingStateUpdate()
	{
		using var response = JsonDocument.Parse(
			"""
			{
			  "message_type": "StateUpdate",
			  "action": {
			    "mode": "auto"
			  }
			}
			"""
		);

		var result = CieloCommandApp.IsMatchingModeState(response.RootElement, "auto");

		Assert.True(result);
	}

	[Fact]
	public void IsMatchingModeState_ReturnsFalseForOtherMessages()
	{
		using var response = JsonDocument.Parse(
			"""
			{
			  "message_type": "DeviceSettingsAck",
			  "action": {
			    "mode": "auto"
			  }
			}
			"""
		);

		var result = CieloCommandApp.IsMatchingModeState(response.RootElement, "auto");

		Assert.False(result);
	}

	[Fact]
	public void BuildRootCommand_RejectsMonitorDaemonWithSamples()
	{
		var parseResult = CieloCommandApp
			.BuildRootCommand()
			.Parse(["monitor", "--daemon", "--interval", "60", "--samples", "1"]);

		Assert.Contains(
			parseResult.Errors,
			error => error.Message == "--daemon cannot be combined with --samples."
		);
	}

	[Fact]
	public void BuildRootCommand_AllowsMonitorDaemonWithoutSamples()
	{
		var parseResult = CieloCommandApp
			.BuildRootCommand()
			.Parse(["monitor", "--daemon", "--interval", "60"]);

		Assert.Empty(parseResult.Errors);
	}
}
