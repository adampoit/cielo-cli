using System.Diagnostics;
using System.Text.Json;
using CieloCli.Configuration;
using CieloCli.Models;

namespace CieloCli.Services;

internal sealed class CieloMonitorRulesEngine
{
	private readonly MonitorRulesConfig _config;
	private readonly bool _dryRun;
	private readonly string _rulesPath;
	private readonly IReadOnlyDictionary<string, string> _extraEnvironment;
	private readonly Dictionary<string, RuleRuntimeState> _state = new(
		StringComparer.OrdinalIgnoreCase
	);

	public CieloMonitorRulesEngine(
		MonitorRulesConfig config,
		string rulesPath,
		bool dryRun,
		IReadOnlyDictionary<string, string>? extraEnvironment = null
	)
	{
		_config = config;
		_rulesPath = rulesPath;
		_dryRun = dryRun;
		_extraEnvironment =
			extraEnvironment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public void WriteStartupMessage()
	{
		Console.Error.WriteLine(
			$"Loaded {_config.Rules.Count} rule(s) from {_rulesPath}{(_dryRun ? " (dry-run)" : string.Empty)}."
		);
	}

	public async Task EvaluateAsync(MonitorSample sample)
	{
		foreach (var rule in _config.Rules)
		{
			if (!AppliesTo(rule, sample))
			{
				continue;
			}

			var state = GetState(rule, sample);
			await ObserveCompletedExecutionAsync(state);

			if (!IsActive(rule, sample.Timestamp))
			{
				ResetState(state);
				continue;
			}

			if (!TryGetMetricValue(rule.When, sample, out var metricValue) || metricValue is null)
			{
				state.ConsecutiveMatches = 0;
				continue;
			}

			var metric = metricValue.Value;

			if (state.InAlert)
			{
				if (HasRecovered(rule.When, sample, metric))
				{
					state.InAlert = false;
					state.ConsecutiveMatches = 0;
					Console.Error.WriteLine(
						$"Rule '{rule.Name}' re-armed for {sample.DeviceName}."
					);
					continue;
				}

				if (IsTriggered(rule.When, metric) && ShouldFireInAlert(rule, sample, state))
				{
					await FireRuleAsync(rule, sample, state);
				}

				continue;
			}

			if (IsTriggered(rule.When, metric))
			{
				state.ConsecutiveMatches++;
				if (state.ConsecutiveMatches >= rule.When.ForSamples)
				{
					state.ConsecutiveMatches = 0;
					state.InAlert = true;

					if (
						state.LastTriggeredAt is not null
						&& sample.Timestamp - state.LastTriggeredAt.Value
							< TimeSpan.FromSeconds(rule.CooldownSeconds)
					)
					{
						Console.Error.WriteLine(
							$"Rule '{rule.Name}' matched for {sample.DeviceName} but is cooling down."
						);
						continue;
					}

					await FireRuleAsync(rule, sample, state);
				}
			}
			else
			{
				state.ConsecutiveMatches = 0;
			}
		}
	}

	public async Task FlushAsync()
	{
		foreach (var runtime in _state.Values)
		{
			if (runtime.RunningTask is not null)
			{
				await runtime.RunningTask;
				runtime.RunningTask = null;
			}
		}
	}

	private async Task FireRuleAsync(MonitorRule rule, MonitorSample sample, RuleRuntimeState state)
	{
		if (state.RunningTask is { IsCompleted: false })
		{
			Console.Error.WriteLine(
				$"Rule '{rule.Name}' skipped for {sample.DeviceName} because the previous action is still running."
			);
			return;
		}

		state.LastTriggeredAt = sample.Timestamp;
		var renderedArgs = rule
			.Action.Args.Select(arg => RenderTemplate(arg, rule, sample))
			.ToArray();
		var summary =
			$"Rule '{rule.Name}' matched for {sample.DeviceName}: {DescribeCondition(rule.When, sample)}";

		if (_dryRun)
		{
			Console.Error.WriteLine(
				$"{summary} [dry-run] -> {rule.Action.Exec} {string.Join(' ', renderedArgs)}"
			);
			return;
		}

		Console.Error.WriteLine(
			$"{summary} -> {rule.Action.Exec} {string.Join(' ', renderedArgs)}"
		);
		state.RunningTask = RunActionAsync(
			rule,
			sample,
			renderedArgs,
			_rulesPath,
			_extraEnvironment
		);
		await ObserveCompletedExecutionAsync(state);
	}

	private static async Task ObserveCompletedExecutionAsync(RuleRuntimeState state)
	{
		if (state.RunningTask is { IsCompleted: true })
		{
			await state.RunningTask;
			state.RunningTask = null;
		}
	}

	private static async Task RunActionAsync(
		MonitorRule rule,
		MonitorSample sample,
		string[] renderedArgs,
		string rulesPath,
		IReadOnlyDictionary<string, string> extraEnvironment
	)
	{
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = rule.Action.Exec,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			foreach (var arg in renderedArgs)
			{
				startInfo.ArgumentList.Add(arg);
			}

			AddEnvironmentVariables(rulesPath, startInfo, rule, sample, extraEnvironment);

			using var process =
				Process.Start(startInfo)
				?? throw new InvalidOperationException($"Failed to start '{rule.Action.Exec}'.");

			var stdinPayload = JsonSerializer.Serialize(
				new
				{
					rule = rule.Name,
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
					historyFile = extraEnvironment.TryGetValue(
						"CIELO_HISTORY_FILE",
						out var historyFile
					)
						? historyFile
						: null,
					timestamp = sample.Timestamp,
				}
			);

			try
			{
				await process.StandardInput.WriteAsync(stdinPayload);
				await process.StandardInput.FlushAsync();
			}
			catch (IOException)
			{
				// The hook may exit immediately and close stdin before consuming the payload.
			}
			finally
			{
				process.StandardInput.Close();
			}

			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();

			var stdout = await stdoutTask;
			var stderr = await stderrTask;

			if (!string.IsNullOrWhiteSpace(stdout))
			{
				Console.Error.WriteLine($"Rule '{rule.Name}' stdout:\n{stdout.TrimEnd()}");
			}

			if (!string.IsNullOrWhiteSpace(stderr))
			{
				Console.Error.WriteLine($"Rule '{rule.Name}' stderr:\n{stderr.TrimEnd()}");
			}

			Console.Error.WriteLine(
				$"Rule '{rule.Name}' action exited with code {process.ExitCode}."
			);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Rule '{rule.Name}' action failed: {ex.Message}");
		}
	}

	private static void AddEnvironmentVariables(
		string rulesPath,
		ProcessStartInfo startInfo,
		MonitorRule rule,
		MonitorSample sample,
		IReadOnlyDictionary<string, string> extraEnvironment
	)
	{
		startInfo.Environment["CIELO_RULE_NAME"] = rule.Name;
		startInfo.Environment["CIELO_DEVICE_NAME"] = sample.DeviceName;
		startInfo.Environment["CIELO_DEVICE_MAC"] = sample.MacAddress;
		startInfo.Environment["CIELO_APPLIANCE_ID"] = sample.ApplianceId.ToString();
		startInfo.Environment["CIELO_ROOM_TEMP"] = sample.RoomTemp?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_HUMIDITY"] = sample.Humidity?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_TARGET_TEMP"] = sample.TargetTemp;
		startInfo.Environment["CIELO_TARGET_TEMP_UNIT"] = sample.TargetTempUnit;
		startInfo.Environment["CIELO_MODE"] = sample.Mode;
		startInfo.Environment["CIELO_POWER"] = sample.Power;
		startInfo.Environment["CIELO_FAN"] = sample.Fan;
		startInfo.Environment["CIELO_ONLINE"] = sample.Online ? "true" : "false";
		startInfo.Environment["CIELO_OUTDOOR_TEMP"] =
			sample.OutdoorTemp?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_HUMIDITY"] =
			sample.OutdoorHumidity?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_APPARENT_TEMP"] =
			sample.OutdoorApparentTemp?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_WIND_SPEED"] =
			sample.OutdoorWindSpeed?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_WEATHER_CODE"] =
			sample.OutdoorWeatherCode?.ToString() ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_TEMP_UNIT"] = sample.OutdoorTempUnit ?? string.Empty;
		startInfo.Environment["CIELO_OUTDOOR_WIND_SPEED_UNIT"] =
			sample.OutdoorWindSpeedUnit ?? string.Empty;
		startInfo.Environment["CIELO_TS"] = sample.Timestamp.ToString("O");
		startInfo.Environment["CIELO_RULES_FILE"] = rulesPath;

		foreach (var (key, value) in extraEnvironment)
		{
			startInfo.Environment[key] = value;
		}
	}

	private RuleRuntimeState GetState(MonitorRule rule, MonitorSample sample)
	{
		var key = $"{rule.Name}|{sample.MacAddress}";
		if (!_state.TryGetValue(key, out var state))
		{
			state = new RuleRuntimeState();
			_state[key] = state;
		}

		return state;
	}

	private static bool AppliesTo(MonitorRule rule, MonitorSample sample)
	{
		if (string.IsNullOrWhiteSpace(rule.Device))
		{
			return true;
		}

		return string.Equals(rule.Device, sample.DeviceName, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(rule.Device, sample.MacAddress, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(
				rule.Device,
				sample.ApplianceId.ToString(),
				StringComparison.OrdinalIgnoreCase
			);
	}

	private static bool IsActive(MonitorRule rule, DateTimeOffset timestamp)
	{
		if (rule.Active is null)
		{
			return true;
		}

		var timezone = TimeZoneInfo.FindSystemTimeZoneById(rule.Active.Timezone);
		var localTimestamp = TimeZoneInfo.ConvertTime(timestamp, timezone);
		var localTime = TimeOnly.FromDateTime(localTimestamp.DateTime);
		var start = rule.Active.GetStart(rule.Name);
		var end = rule.Active.GetEnd(rule.Name);

		if (start < end)
		{
			return localTime >= start && localTime < end;
		}

		return localTime >= start || localTime < end;
	}

	private static void ResetState(RuleRuntimeState state)
	{
		state.ConsecutiveMatches = 0;
		state.InAlert = false;
	}

	private static bool TryGetMetricValue(
		MonitorRuleCondition condition,
		MonitorSample sample,
		out decimal? value
	)
	{
		if (string.Equals(condition.Metric, "roomTemp", StringComparison.OrdinalIgnoreCase))
		{
			value = sample.RoomTemp;
			return UnitMatches(condition, sample.TargetTempUnit);
		}

		if (string.Equals(condition.Metric, "targetTemp", StringComparison.OrdinalIgnoreCase))
		{
			value = sample.TargetTempValue;
			return UnitMatches(condition, sample.TargetTempUnit);
		}

		value = sample.Humidity;
		return true;
	}

	private static bool UnitMatches(MonitorRuleCondition condition, string unit)
	{
		return string.IsNullOrWhiteSpace(condition.Unit)
			|| string.Equals(condition.Unit, unit, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTriggered(MonitorRuleCondition condition, decimal metricValue)
	{
		return condition.Below is { } below
			? metricValue < below
			: metricValue > condition.Above!.Value;
	}

	private static bool HasRecovered(
		MonitorRuleCondition condition,
		MonitorSample sample,
		decimal metricValue
	)
	{
		var hysteresis = condition.Hysteresis ?? GetDefaultHysteresis(condition, sample);
		return condition.Below is { } below
			? metricValue >= below + hysteresis
			: metricValue <= condition.Above!.Value - hysteresis;
	}

	private static bool ShouldFireInAlert(
		MonitorRule rule,
		MonitorSample sample,
		RuleRuntimeState state
	)
	{
		return state.LastTriggeredAt is null
			|| sample.Timestamp - state.LastTriggeredAt.Value
				>= TimeSpan.FromSeconds(rule.CooldownSeconds);
	}

	private static decimal GetDefaultHysteresis(
		MonitorRuleCondition condition,
		MonitorSample sample
	)
	{
		if (string.Equals(condition.Metric, "humidity", StringComparison.OrdinalIgnoreCase))
		{
			return 1m;
		}

		return string.Equals(sample.TargetTempUnit, "C", StringComparison.OrdinalIgnoreCase)
			? 0.5m
			: 1m;
	}

	private static string DescribeCondition(MonitorRuleCondition condition, MonitorSample sample)
	{
		if (string.Equals(condition.Metric, "humidity", StringComparison.OrdinalIgnoreCase))
		{
			var currentValue = sample.Humidity?.ToString() ?? "n/a";
			var threshold = (condition.Below ?? condition.Above ?? 0).ToString();
			var direction = condition.Below is not null ? "below" : "above";
			return $"humidity {currentValue}% {direction} {threshold}%";
		}

		var current = string.Equals(
			condition.Metric,
			"roomTemp",
			StringComparison.OrdinalIgnoreCase
		)
			? sample.RoomTemp?.ToString() ?? "n/a"
			: sample.TargetTemp;
		var limit = (condition.Below ?? condition.Above ?? 0).ToString();
		var relation = condition.Below is not null ? "below" : "above";
		return $"{condition.Metric} {current}{sample.TargetTempUnit} {relation} {limit}{sample.TargetTempUnit}";
	}

	private static string RenderTemplate(string template, MonitorRule rule, MonitorSample sample)
	{
		return template
			.Replace("{{rule}}", rule.Name, StringComparison.OrdinalIgnoreCase)
			.Replace("{{device}}", sample.DeviceName, StringComparison.OrdinalIgnoreCase)
			.Replace("{{macAddress}}", sample.MacAddress, StringComparison.OrdinalIgnoreCase)
			.Replace(
				"{{applianceId}}",
				sample.ApplianceId.ToString(),
				StringComparison.OrdinalIgnoreCase
			)
			.Replace(
				"{{roomTemp}}",
				sample.RoomTemp?.ToString() ?? string.Empty,
				StringComparison.OrdinalIgnoreCase
			)
			.Replace(
				"{{humidity}}",
				sample.Humidity?.ToString() ?? string.Empty,
				StringComparison.OrdinalIgnoreCase
			)
			.Replace("{{targetTemp}}", sample.TargetTemp, StringComparison.OrdinalIgnoreCase)
			.Replace(
				"{{targetTempUnit}}",
				sample.TargetTempUnit,
				StringComparison.OrdinalIgnoreCase
			)
			.Replace("{{mode}}", sample.Mode, StringComparison.OrdinalIgnoreCase)
			.Replace("{{power}}", sample.Power, StringComparison.OrdinalIgnoreCase)
			.Replace("{{fan}}", sample.Fan, StringComparison.OrdinalIgnoreCase)
			.Replace("{{ts}}", sample.Timestamp.ToString("O"), StringComparison.OrdinalIgnoreCase);
	}

	private sealed class RuleRuntimeState
	{
		public int ConsecutiveMatches { get; set; }
		public bool InAlert { get; set; }
		public DateTimeOffset? LastTriggeredAt { get; set; }
		public Task? RunningTask { get; set; }
	}
}
