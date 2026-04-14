using CieloCli.Configuration;
using CieloCli.Models;
using CieloCli.Services;
using Xunit;

namespace cielo.Tests;

public sealed class CieloMonitorRulesEngineTests
{
	[Fact]
	public async Task EvaluateAsync_DryRunFiresThenRearmsAfterRecovery()
	{
		var config = new MonitorRulesConfig
		{
			Rules =
			[
				new MonitorRule
				{
					Name = "too-cold",
					Device = "Living Room",
					When = new MonitorRuleCondition
					{
						Metric = "roomTemp",
						Below = 64,
						Unit = "F",
						ForSamples = 1,
						Hysteresis = 1,
					},
					Action = new MonitorRuleAction
					{
						Exec = "/usr/bin/true",
						Args = ["{{device}}", "{{roomTemp}}"],
					},
				},
			],
		};

		var engine = new CieloMonitorRulesEngine(config, "/tmp/rules.json", dryRun: true);
		var originalError = Console.Error;
		using var errorWriter = new StringWriter();
		Console.SetError(errorWriter);

		try
		{
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T15:00:00Z"))
			);
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T15:01:00Z"))
			);
			await engine.EvaluateAsync(
				CreateSample(65m, DateTimeOffset.Parse("2026-04-10T15:02:00Z"))
			);
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T15:03:00Z"))
			);
		}
		finally
		{
			Console.SetError(originalError);
		}

		var output = errorWriter.ToString();
		Assert.Equal(3, CountOccurrences(output, "Rule 'too-cold' matched for Living Room"));
		Assert.Contains("Rule 'too-cold' re-armed for Living Room.", output);
	}

	[Fact]
	public async Task EvaluateAsync_HonorsForSamplesBeforeFiring()
	{
		var config = new MonitorRulesConfig
		{
			Rules =
			[
				new MonitorRule
				{
					Name = "too-cold",
					Device = "Living Room",
					When = new MonitorRuleCondition
					{
						Metric = "roomTemp",
						Below = 64,
						Unit = "F",
						ForSamples = 2,
						Hysteresis = 1,
					},
					Action = new MonitorRuleAction { Exec = "/usr/bin/true" },
				},
			],
		};

		var engine = new CieloMonitorRulesEngine(config, "/tmp/rules.json", dryRun: true);
		var originalError = Console.Error;
		using var errorWriter = new StringWriter();
		Console.SetError(errorWriter);

		try
		{
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T15:00:00Z"))
			);
			Assert.DoesNotContain("matched", errorWriter.ToString());

			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T15:01:00Z"))
			);
		}
		finally
		{
			Console.SetError(originalError);
		}

		Assert.Contains("Rule 'too-cold' matched for Living Room", errorWriter.ToString());
	}

	[Fact]
	public async Task EvaluateAsync_SkipsRulesOutsideActiveWindow()
	{
		var config = new MonitorRulesConfig
		{
			Rules =
			[
				new MonitorRule
				{
					Name = "daytime-cold",
					Device = "Living Room",
					Active = new MonitorRuleActiveWindow
					{
						Timezone = "UTC",
						Start = "07:00",
						End = "22:00",
					},
					When = new MonitorRuleCondition
					{
						Metric = "roomTemp",
						Below = 64,
						Unit = "F",
						ForSamples = 1,
						Hysteresis = 1,
					},
					Action = new MonitorRuleAction { Exec = "/usr/bin/true" },
				},
			],
		};

		var engine = new CieloMonitorRulesEngine(config, "/tmp/rules.json", dryRun: true);
		var originalError = Console.Error;
		using var errorWriter = new StringWriter();
		Console.SetError(errorWriter);

		try
		{
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T03:00:00Z"))
			);
		}
		finally
		{
			Console.SetError(originalError);
		}

		Assert.DoesNotContain("matched", errorWriter.ToString());
	}

	[Fact]
	public async Task EvaluateAsync_MatchesRulesInsideOvernightWindow()
	{
		var config = new MonitorRulesConfig
		{
			Rules =
			[
				new MonitorRule
				{
					Name = "night-cold",
					Device = "Living Room",
					Active = new MonitorRuleActiveWindow
					{
						Timezone = "UTC",
						Start = "22:00",
						End = "07:00",
					},
					When = new MonitorRuleCondition
					{
						Metric = "roomTemp",
						Below = 64,
						Unit = "F",
						ForSamples = 1,
						Hysteresis = 1,
					},
					Action = new MonitorRuleAction { Exec = "/usr/bin/true" },
				},
			],
		};

		var engine = new CieloMonitorRulesEngine(config, "/tmp/rules.json", dryRun: true);
		var originalError = Console.Error;
		using var errorWriter = new StringWriter();
		Console.SetError(errorWriter);

		try
		{
			await engine.EvaluateAsync(
				CreateSample(63m, DateTimeOffset.Parse("2026-04-10T23:30:00Z"))
			);
		}
		finally
		{
			Console.SetError(originalError);
		}

		Assert.Contains("Rule 'night-cold' matched for Living Room", errorWriter.ToString());
	}

	private static MonitorSample CreateSample(decimal roomTemp, DateTimeOffset timestamp)
	{
		return new MonitorSample(
			timestamp,
			"poll",
			"Living Room",
			"B0A732C61784",
			2990,
			roomTemp,
			51m,
			"68",
			"F",
			"cool",
			"off",
			"medium",
			true,
			55m,
			70m,
			53m,
			8m,
			3,
			"F",
			"mph"
		);
	}

	private static int CountOccurrences(string value, string token)
	{
		var count = 0;
		var index = 0;
		while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += token.Length;
		}

		return count;
	}
}
