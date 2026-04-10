using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using CieloCli.Configuration;
using CieloCli.Models;
using CieloCli.Services;

namespace CieloCli;

internal static class CieloCommandApp
{
	private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);
	private static readonly TimeSpan ModeSettleDelay = TimeSpan.FromSeconds(2);

	public static Task<int> RunAsync(string[] args)
	{
		return BuildRootCommand().Parse(args).InvokeAsync();
	}

	internal static RootCommand BuildRootCommand()
	{
		var root = new RootCommand("CLI for Cielo Breez cloud control.");

		root.Subcommands.Add(BuildConfigCommand());
		root.Subcommands.Add(BuildDevicesCommand());
		root.Subcommands.Add(BuildStatusCommand());
		root.Subcommands.Add(BuildSetCommand());
		root.Subcommands.Add(BuildApplyPlanCommand());
		root.Subcommands.Add(BuildMonitorCommand());
		root.Subcommands.Add(BuildWatchCommand());

		root.SetAction(parseResult =>
		{
			Console.WriteLine("Use --help to see available commands.");
			return 0;
		});

		return root;
	}

	private static Command BuildConfigCommand()
	{
		var configOption = CreateConfigOption();
		var forceOption = new Option<bool>("--force")
		{
			Description = "Overwrite an existing config file."
		};

		var initCommand = new Command("init", "Write a starter config file with the expected auth keys.");
		initCommand.Options.Add(configOption);
		initCommand.Options.Add(forceOption);
		initCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			try
			{
				var path = parseResult.GetValue(configOption)!;
				var force = parseResult.GetValue(forceOption);
				await CieloConfigStore.WriteTemplateAsync(path, force, cancellationToken);
				Console.WriteLine($"Wrote starter config to {CieloConfigStore.ExpandPath(path)}");
				return 0;
			}
			catch (Exception ex)
			{
				await Console.Error.WriteLineAsync(ex.Message);
				return 1;
			}
		});

		var command = new Command("config", "Manage local CLI configuration.");
		command.Subcommands.Add(initCommand);
		return command;
	}

	private static Command BuildDevicesCommand()
	{
		var configOption = CreateConfigOption();
		var jsonOption = new Option<bool>("--json")
		{
			Description = "Print raw JSON instead of formatted text."
		};

		var command = new Command("devices", "List discovered Cielo devices.");
		command.Options.Add(configOption);
		command.Options.Add(jsonOption);
		command.SetAction((parseResult, cancellationToken) =>
			RunWithClientAsync(
				parseResult,
				configOption,
				async (client, _) =>
				{
					var devices = await client.GetDevicesAsync(cancellationToken);
					CieloOutput.WriteDevices(devices, parseResult.GetValue(jsonOption));
					return 0;
				},
				cancellationToken));

		return command;
	}

	private static Command BuildStatusCommand()
	{
		var configOption = CreateConfigOption();
		var deviceOption = new Option<string>("--device")
		{
			Description = "Device name, MAC address, or appliance id.",
			Required = true
		};

		var jsonOption = new Option<bool>("--json")
		{
			Description = "Print raw JSON instead of formatted text."
		};

		var command = new Command("status", "Show the current status for one device.");
		command.Options.Add(configOption);
		command.Options.Add(deviceOption);
		command.Options.Add(jsonOption);
		command.SetAction((parseResult, cancellationToken) =>
			RunWithClientAsync(
				parseResult,
				configOption,
				async (client, _) =>
				{
					var devices = await client.GetDevicesAsync(cancellationToken);
					var device = CieloDeviceResolver.Resolve(devices, parseResult.GetValue(deviceOption)!);
					CieloOutput.WriteDeviceStatus(device, parseResult.GetValue(jsonOption));
					return 0;
				},
				cancellationToken));

		return command;
	}

	private static Command BuildSetCommand()
	{
		var configOption = CreateConfigOption();
		var deviceOption = new Option<string>("--device")
		{
			Description = "Device name, MAC address, or appliance id.",
			Required = true
		};

		var powerOption = new Option<string?>("--power")
		{
			Description = "Power state: on or off."
		};

		var modeOption = new Option<string?>("--mode")
		{
			Description = "HVAC mode: auto, heat, cool, dry, or fan."
		};

		var tempOption = new Option<int?>("--temp")
		{
			Description = "Target temperature."
		};

		var fanOption = new Option<string?>("--fan")
		{
			Description = "Fan speed: auto, low, medium, high, or fanspeed."
		};

		var swingOption = new Option<string?>("--swing")
		{
			Description = "Swing mode: auto, auto/stop, adjust, pos1-pos6."
		};

		var presetOption = new Option<string?>("--preset")
		{
			Description = "Preset name. On Breez Max this matches a named preset title."
		};

		var jsonOption = new Option<bool>("--json")
		{
			Description = "Print the matching websocket event as JSON."
		};

		var debugWireOption = new Option<bool>("--debug-wire")
		{
			Description = "Print outbound and inbound websocket payloads while waiting for acknowledgements."
		};

		var command = new Command("set", "Send one or more actionControl commands to a device.");
		command.Options.Add(configOption);
		command.Options.Add(deviceOption);
		command.Options.Add(powerOption);
		command.Options.Add(modeOption);
		command.Options.Add(tempOption);
		command.Options.Add(fanOption);
		command.Options.Add(swingOption);
		command.Options.Add(presetOption);
		command.Options.Add(jsonOption);
		command.Options.Add(debugWireOption);
		command.Validators.Add(result =>
		{
			if (CountSpecified(result, powerOption, modeOption, tempOption, fanOption, swingOption, presetOption) == 0)
			{
				result.AddError("Specify at least one setting to change.");
			}

			ValidateChangeSet(
				result,
				result.GetValue(powerOption),
				result.GetValue(modeOption),
				result.GetValue(fanOption),
				result.GetValue(swingOption));
		});

		command.SetAction((parseResult, cancellationToken) =>
			RunWithClientAsync(
				parseResult,
				configOption,
				async (client, _) =>
				{
					var changes = ReadChanges(parseResult, powerOption, modeOption, tempOption, fanOption, swingOption, presetOption);
					var devices = await client.GetDevicesAsync(cancellationToken);
					var device = CieloDeviceResolver.Resolve(devices, parseResult.GetValue(deviceOption)!);
					var messages = client.BuildActionControlMessages(device, changes);

					await using var session = await client.OpenWebSocketAsync(cancellationToken);
					Action<string>? trace = parseResult.GetValue(debugWireOption)
						? line => Console.Error.WriteLine(line)
						: null;
					foreach (var message in messages)
					{
						using var response = await SendPendingMessageAsync(session, device, message, cancellationToken, trace);
						CieloOutput.WriteCommandResult(message.Summary, response, parseResult.GetValue(jsonOption));
					}

					return 0;
				},
				cancellationToken));

		return command;
	}

	private static Command BuildWatchCommand()
	{
		var configOption = CreateConfigOption();
		var deviceOption = new Option<string?>("--device")
		{
			Description = "Only print events for a single device name, MAC address, or appliance id."
		};

		var jsonOption = new Option<bool>("--json")
		{
			Description = "Print each websocket message as JSON."
		};

		var command = new Command("watch", "Stream live websocket updates.");
		command.Options.Add(configOption);
		command.Options.Add(deviceOption);
		command.Options.Add(jsonOption);
		command.SetAction((parseResult, cancellationToken) =>
			RunWithClientAsync(
				parseResult,
				configOption,
				async (client, _) =>
				{
					var devices = await client.GetDevicesAsync(cancellationToken);
					var filter = parseResult.GetValue(deviceOption);
					var selected = string.IsNullOrWhiteSpace(filter)
						? null
						: CieloDeviceResolver.Resolve(devices, filter!);
					var deviceIndex = devices.ToDictionary(device => device.MacAddress, StringComparer.OrdinalIgnoreCase);

					if (selected is null)
					{
						CieloOutput.WriteDevices(devices, false);
						Console.WriteLine();
					}
					else
					{
						CieloOutput.WriteDeviceStatus(selected, false);
						Console.WriteLine();
					}

					await WatchLoopAsync(client, deviceIndex, selected?.MacAddress, parseResult.GetValue(jsonOption), cancellationToken);
					return 0;
				},
				cancellationToken));

		return command;
	}

	private static Command BuildMonitorCommand()
	{
		var configOption = CreateConfigOption();
		var deviceOption = new Option<string?>("--device")
		{
			Description = "Only monitor a single device name, MAC address, or appliance id."
		};

		var intervalOption = new Option<int>("--interval")
		{
			Description = "Polling interval in seconds.",
			DefaultValueFactory = _ => 30
		};

		var samplesOption = new Option<int?>("--samples")
		{
			Description = "Stop after this many polling cycles. Defaults to running until cancelled."
		};

		var jsonOption = new Option<bool>("--json")
		{
			Description = "Emit one JSON object per sample line."
		};

		var rulesOption = new Option<string?>("--rules")
		{
			Description = $"Path to rules JSON. If omitted, {MonitorRulesStore.GetDefaultPath()} is used when present."
		};

		var dryRunOption = new Option<bool>("--dry-run")
		{
			Description = "Evaluate rules and log matching actions without executing external commands."
		};

		var historyFileOption = new Option<string?>("--history-file")
		{
			Description = "Append every monitor sample as NDJSON to this file."
		};

		var weatherLatitudeOption = new Option<double?>("--weather-lat")
		{
			Description = "Latitude for outdoor weather lookup via Open-Meteo."
		};

		var weatherLongitudeOption = new Option<double?>("--weather-lon")
		{
			Description = "Longitude for outdoor weather lookup via Open-Meteo."
		};

		var weatherRefreshMinutesOption = new Option<int>("--weather-refresh-minutes")
		{
			Description = "How often to refresh outdoor weather data.",
			DefaultValueFactory = _ => 15
		};

		var changesOnlyOption = new Option<bool>("--changes-only")
		{
			Description = "Only emit a sample when the monitored values changed since the previous poll."
		};

		var daemonOption = new Option<bool>("--daemon")
		{
			Description = "Run monitor inside a generic host with systemd integration for service management."
		};

		var command = new Command("monitor", "Continuously poll device temperature and humidity.");
		command.Options.Add(configOption);
		command.Options.Add(deviceOption);
		command.Options.Add(intervalOption);
		command.Options.Add(samplesOption);
		command.Options.Add(jsonOption);
		command.Options.Add(rulesOption);
		command.Options.Add(dryRunOption);
		command.Options.Add(historyFileOption);
		command.Options.Add(weatherLatitudeOption);
		command.Options.Add(weatherLongitudeOption);
		command.Options.Add(weatherRefreshMinutesOption);
		command.Options.Add(changesOnlyOption);
		command.Options.Add(daemonOption);
		command.Validators.Add(result =>
		{
			if (result.GetValue(intervalOption) < 1)
			{
				result.AddError("--interval must be at least 1 second.");
			}

			if (result.GetValue(samplesOption) is { } samples && samples < 1)
			{
				result.AddError("--samples must be at least 1.");
			}

			var weatherLat = result.GetValue(weatherLatitudeOption);
			var weatherLon = result.GetValue(weatherLongitudeOption);
			if (weatherLat.HasValue != weatherLon.HasValue)
			{
				result.AddError("Specify both --weather-lat and --weather-lon together.");
			}

			if (weatherLat is < -90 or > 90)
			{
				result.AddError("--weather-lat must be between -90 and 90.");
			}

			if (weatherLon is < -180 or > 180)
			{
				result.AddError("--weather-lon must be between -180 and 180.");
			}

			if (result.GetValue(daemonOption) && result.GetValue(samplesOption) is not null)
			{
				result.AddError("--daemon cannot be combined with --samples.");
			}

		});

		command.SetAction((parseResult, cancellationToken) =>
		{
			var options = BuildMonitorOptions(
				parseResult,
				configOption,
				deviceOption,
				intervalOption,
				samplesOption,
				jsonOption,
				rulesOption,
				dryRunOption,
				historyFileOption,
				weatherLatitudeOption,
				weatherLongitudeOption,
				weatherRefreshMinutesOption,
				changesOnlyOption,
				daemonOption);

			return options.Daemon
				? CieloMonitorExecution.RunHostedAsync(options)
				: CieloMonitorExecution.RunForegroundAsync(options, cancellationToken);
		});

		return command;
	}

	private static MonitorCommandOptions BuildMonitorOptions(
		ParseResult parseResult,
		Option<string> configOption,
		Option<string?> deviceOption,
		Option<int> intervalOption,
		Option<int?> samplesOption,
		Option<bool> jsonOption,
		Option<string?> rulesOption,
		Option<bool> dryRunOption,
		Option<string?> historyFileOption,
		Option<double?> weatherLatitudeOption,
		Option<double?> weatherLongitudeOption,
		Option<int> weatherRefreshMinutesOption,
		Option<bool> changesOnlyOption,
		Option<bool> daemonOption)
	{
		return new MonitorCommandOptions(
			parseResult.GetValue(configOption)!,
			parseResult.GetValue(deviceOption),
			TimeSpan.FromSeconds(parseResult.GetValue(intervalOption)),
			parseResult.GetValue(samplesOption),
			parseResult.GetValue(jsonOption),
			parseResult.GetValue(rulesOption),
			parseResult.GetValue(dryRunOption),
			parseResult.GetValue(historyFileOption),
			parseResult.GetValue(weatherLatitudeOption),
			parseResult.GetValue(weatherLongitudeOption),
			TimeSpan.FromMinutes(Math.Max(parseResult.GetValue(weatherRefreshMinutesOption), 1)),
			parseResult.GetValue(changesOnlyOption),
			parseResult.GetValue(daemonOption));
	}

	private static Command BuildApplyPlanCommand()
	{
		var configOption = CreateConfigOption();

		var modeOption = new Option<string>("--mode")
		{
			Description = "HVAC mode for all devices: heat or cool."
		};

		var setOption = new Option<List<string>>("--set")
		{
			Description = "Device setpoint in <name>=<setpoint> format. Repeat for each device.",
			AllowMultipleArgumentsPerToken = true
		};

		var planOption = new Option<string?>("--plan")
		{
			Description = "JSON plan from file path, - for stdin, or inline JSON string. Supersedes --mode/--set."
		};

		var rulesOption = new Option<string?>("--rules")
		{
			Description = $"Path to rules JSON for planDefaults (allowedDevices, setpoint bounds). If omitted, {MonitorRulesStore.GetDefaultPath()} is used when present."
		};

		var dryRunOption = new Option<bool>("--dry-run")
		{
			Description = "Validate and resolve the plan but do not send commands to devices."
		};

		var jsonOption = new Option<bool>("--json")
		{
			Description = "Output structured JSON result."
		};

		var debugWireOption = new Option<bool>("--debug-wire")
		{
			Description = "Print outbound and inbound websocket payloads while waiting for acknowledgements."
		};

		var resultFileOption = new Option<string?>("--result-file")
		{
			Description = "Write the JSON result to this file path."
		};

		var command = new Command("apply-plan", "Validate and apply a climate plan across multiple devices.");
		command.Options.Add(configOption);
		command.Options.Add(modeOption);
		command.Options.Add(setOption);
		command.Options.Add(planOption);
		command.Options.Add(rulesOption);
		command.Options.Add(dryRunOption);
		command.Options.Add(jsonOption);
		command.Options.Add(debugWireOption);
		command.Options.Add(resultFileOption);

		command.Validators.Add(result =>
		{
			var mode = result.GetValue(modeOption);
			var set = result.GetValue(setOption);
			var plan = result.GetValue(planOption);

			if (plan is null && string.IsNullOrWhiteSpace(mode))
			{
				result.AddError("Specify --mode or --plan.");
			}

			if (plan is null && (set is null || set.Count == 0))
			{
				result.AddError("Specify at least one --set entry or --plan.");
			}

			if (plan is not null && (!string.IsNullOrWhiteSpace(mode) || (set is not null && set.Count > 0)))
			{
				result.AddError("--plan cannot be combined with --mode or --set.");
			}

			if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "heat", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "cool", StringComparison.OrdinalIgnoreCase))
			{
				result.AddError("--mode must be 'heat' or 'cool'.");
			}

			if (set is not null)
			{
				foreach (var entry in set)
				{
					var eq = entry.IndexOf('=');
					if (eq < 0)
					{
						result.AddError($"--set value '{entry}' must use <name>=<setpoint> format.");
					}
					else if (!int.TryParse(entry[(eq + 1)..], out var temp))
					{
						result.AddError($"--set setpoint for '{entry[..eq]}' must be an integer.");
					}
				}
			}
		});

		command.SetAction((parseResult, cancellationToken) =>
			RunWithClientAsync(
				parseResult,
				configOption,
				async (client, _) =>
				{
					var dryRun = parseResult.GetValue(dryRunOption);
					var json = parseResult.GetValue(jsonOption);
					var debugWire = parseResult.GetValue(debugWireOption);
					var requestedRulesPath = parseResult.GetValue(rulesOption);
					var resultFilePath = parseResult.GetValue(resultFileOption);

					PlanDefaults? planDefaults = null;
					var defaultRulesPath = MonitorRulesStore.GetDefaultPath();
					if (!string.IsNullOrWhiteSpace(requestedRulesPath) || File.Exists(defaultRulesPath))
					{
						var (rulesConfig, _) = await MonitorRulesStore.LoadOptionalAsync(requestedRulesPath, cancellationToken);
						planDefaults = rulesConfig?.PlanDefaults;
					}

					ClimatePlan plan;
					if (parseResult.GetValue(planOption) is { } planSource)
					{
						plan = await LoadPlanAsync(planSource, cancellationToken);
					}
					else
					{
						plan = BuildPlanFromOptions(parseResult.GetValue(modeOption)!, parseResult.GetValue(setOption)!);
					}

					var validationResult = ValidatePlan(plan, planDefaults);
					if (validationResult is not null)
					{
						if (json)
						{
							Console.WriteLine(JsonSerializer.Serialize(validationResult, CieloOutput.JsonOptions));
						}
						else
						{
							await Console.Error.WriteLineAsync(validationResult.Error);
						}

						WriteResultFile(resultFilePath, validationResult);
						return 1;
					}

					var devices = await client.GetDevicesAsync(cancellationToken);
					var (entries, resolveError) = ResolvePlanDevices(plan, devices);
					if (resolveError is not null)
					{
						if (json)
						{
							Console.WriteLine(JsonSerializer.Serialize(resolveError, CieloOutput.JsonOptions));
						}
						else
						{
							await Console.Error.WriteLineAsync(resolveError.Error!);
						}

						WriteResultFile(resultFilePath, resolveError);
						return 1;
					}

					var effectiveMode = NormalizeMode(plan.Mode);
					var result = new ClimatePlanResult
					{
						Success = true,
						DryRun = dryRun,
						Mode = effectiveMode,
						Devices = []
					};

					foreach (var entry in entries)
					{
						var currentMode = entry.Device.NormalizedMode;
						var currentSetpoint = entry.Device.LatestAction.Temperature;
						var needsChange = !string.Equals(currentMode, effectiveMode, StringComparison.OrdinalIgnoreCase) ||
										  !string.Equals(currentSetpoint, entry.Setpoint.ToString(), StringComparison.OrdinalIgnoreCase);

						result.Devices.Add(new ClimatePlanDeviceResult
						{
							Name = entry.Device.DeviceName,
							Setpoint = entry.Setpoint,
							PreviousMode = currentMode,
							PreviousSetpoint = currentSetpoint,
							Applied = false
						});

						if (!needsChange)
						{
							if (!json)
							{
								Console.Error.WriteLine($"{entry.Device.DeviceName}: no change needed (already {currentMode} {currentSetpoint}{entry.Device.TemperatureUnit}).");
							}

							continue;
						}

						if (dryRun)
						{
							if (!json)
							{
								Console.Error.WriteLine($"Dry run: would apply {effectiveMode} {entry.Setpoint}{entry.Device.TemperatureUnit} to {entry.Device.DeviceName} (was {currentMode} {currentSetpoint}{entry.Device.TemperatureUnit}).");
							}

							result.Devices[^1].Applied = true;
							continue;
						}

						var changes = new DeviceChanges
						{
							Mode = effectiveMode,
							Temperature = entry.Setpoint
						};

						var messages = client.BuildActionControlMessages(entry.Device, changes);
						await using var session = await client.OpenWebSocketAsync(cancellationToken);
						Action<string>? trace = debugWire
							? line => Console.Error.WriteLine(line)
							: null;

						foreach (var message in messages)
						{
							using var response = await SendPendingMessageAsync(session, entry.Device, message, cancellationToken, trace);
							if (!json)
							{
								CieloOutput.WriteCommandResult(message.Summary, response, false);
							}
						}

						result.Devices[^1].Applied = true;

						if (!json)
						{
							Console.Error.WriteLine($"{entry.Device.DeviceName}: set {effectiveMode} {entry.Setpoint}{entry.Device.TemperatureUnit} (was {currentMode} {currentSetpoint}{entry.Device.TemperatureUnit}).");
						}
					}

					var appliedCount = result.Devices.Count(d => d.Applied);
					var skippedCount = result.Devices.Count - appliedCount;

					if (json)
					{
						Console.WriteLine(JsonSerializer.Serialize(result, CieloOutput.JsonOptions));
					}
					else
					{
						Console.Error.WriteLine($"Plan applied: {appliedCount} device(s) changed, {skippedCount} skipped.");
					}

					WriteResultFile(resultFilePath, result);
					return 0;
				},
				cancellationToken));

		return command;
	}

	private static async Task<ClimatePlan> LoadPlanAsync(string planSource, CancellationToken cancellationToken)
	{
		if (planSource == "-")
		{
			var stdin = await Console.In.ReadToEndAsync(cancellationToken);
			return JsonSerializer.Deserialize<ClimatePlan>(stdin, CieloOutput.JsonLineOptions)
				?? throw new InvalidOperationException("Could not parse plan JSON from stdin.");
		}

		if (File.Exists(planSource))
		{
			await using var stream = File.OpenRead(planSource);
			return await JsonSerializer.DeserializeAsync<ClimatePlan>(stream, CieloOutput.JsonLineOptions, cancellationToken)
				?? throw new InvalidOperationException($"Could not parse plan JSON from {planSource}.");
		}

		return JsonSerializer.Deserialize<ClimatePlan>(planSource, CieloOutput.JsonLineOptions)
			?? throw new InvalidOperationException("Could not parse plan JSON.");
	}

	private static ClimatePlan BuildPlanFromOptions(string mode, List<string> setEntries)
	{
		var devices = new List<ClimatePlanDevice>();
		foreach (var entry in setEntries)
		{
			var eq = entry.IndexOf('=');
			var name = entry[..eq].Trim();
			var setpoint = int.Parse(entry[(eq + 1)..]);
			devices.Add(new ClimatePlanDevice { Name = name, Setpoint = setpoint });
		}

		return new ClimatePlan { Mode = mode, Devices = devices };
	}

	private static string NormalizeMode(string mode)
	{
		return string.Equals(mode, "heat", StringComparison.OrdinalIgnoreCase) ? "heat" : "cool";
	}

	private static ClimatePlanResult? ValidatePlan(ClimatePlan plan, PlanDefaults? defaults)
	{
		if (!string.Equals(plan.Mode, "heat", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(plan.Mode, "cool", StringComparison.OrdinalIgnoreCase))
		{
			return new ClimatePlanResult { Success = false, Error = $"Mode must be 'heat' or 'cool', got '{plan.Mode}'." };
		}

		if (plan.Devices.Count == 0)
		{
			return new ClimatePlanResult { Success = false, Error = "Plan must specify at least one device." };
		}

		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var device in plan.Devices)
		{
			if (string.IsNullOrWhiteSpace(device.Name))
			{
				return new ClimatePlanResult { Success = false, Error = "Device name cannot be empty." };
			}

			if (!seenNames.Add(device.Name))
			{
				return new ClimatePlanResult { Success = false, Error = $"Duplicate device '{device.Name}' in plan." };
			}

			if (defaults is not null)
			{
				if (device.Setpoint < defaults.MinSetpoint || device.Setpoint > defaults.MaxSetpoint)
				{
					return new ClimatePlanResult { Success = false, Error = $"Setpoint {device.Setpoint} for '{device.Name}' must be between {defaults.MinSetpoint} and {defaults.MaxSetpoint}." };
				}
			}
		}

		if (defaults?.AllowedDevices is { Count: > 0 })
		{
			var expectedSet = new HashSet<string>(defaults.AllowedDevices, StringComparer.OrdinalIgnoreCase);
			var planSet = new HashSet<string>(plan.Devices.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

			if (!expectedSet.SetEquals(planSet))
			{
				var missing = expectedSet.Except(planSet, StringComparer.OrdinalIgnoreCase);
				var extra = planSet.Except(expectedSet, StringComparer.OrdinalIgnoreCase);
				var parts = new List<string>();
				if (missing.Any())
				{
					parts.Add($"missing: {string.Join(", ", missing)}");
				}

				if (extra.Any())
				{
					parts.Add($"extra: {string.Join(", ", extra)}");
				}

				return new ClimatePlanResult { Success = false, Error = $"Plan devices do not match allowed devices ({string.Join("; ", parts)})." };
			}
		}

		return null;
	}

	private static (List<ResolvedPlanDevice> Entries, ClimatePlanResult? Error) ResolvePlanDevices(ClimatePlan plan, IReadOnlyList<CieloDevice> allDevices)
	{
		var entries = new List<ResolvedPlanDevice>();
		foreach (var device in plan.Devices)
		{
			try
			{
				var resolved = CieloDeviceResolver.Resolve(allDevices, device.Name);
				entries.Add(new ResolvedPlanDevice { Device = resolved, Setpoint = device.Setpoint });
			}
			catch (InvalidOperationException ex)
			{
				return ([], new ClimatePlanResult { Success = false, Error = ex.Message });
			}
		}

		return (entries, null);
	}

	private static async Task<int> WatchLoopAsync(
		CieloClient client,
		IReadOnlyDictionary<string, CieloDevice> devices,
		string? targetMacAddress,
		bool json,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await using var session = await client.OpenWebSocketAsync(cancellationToken);
			var pingAt = DateTimeOffset.UtcNow.AddMinutes(9);

			while (!cancellationToken.IsCancellationRequested)
			{
				if (DateTimeOffset.UtcNow >= client.TokenExpiresAtUtc)
				{
					break;
				}

				using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				receiveTimeout.CancelAfter(TimeSpan.FromSeconds(5));

				try
				{
					var message = await session.ReceiveJsonAsync(receiveTimeout.Token);
					if (message is null)
					{
						break;
					}

					if (!CieloOutput.ShouldWriteMessage(message.RootElement, targetMacAddress))
					{
						continue;
					}

					CieloOutput.WriteWatchMessage(message.RootElement, devices, json);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					if (DateTimeOffset.UtcNow >= pingAt)
					{
						await session.SendTextAsync("ping", cancellationToken);
						pingAt = DateTimeOffset.UtcNow.AddMinutes(9);
					}
				}
			}

			await client.RefreshTokenAsync(cancellationToken);
		}

		return 0;
	}

	private static int CountSpecified(CommandResult parseResult, params object[] options)
	{
		var count = 0;

		foreach (var option in options)
		{
			switch (option)
			{
				case Option<string?> stringOption when parseResult.GetValue(stringOption) is not null:
					count++;
					break;
				case Option<int?> intOption when parseResult.GetValue(intOption) is not null:
					count++;
					break;
			}
		}

		return count;
	}

	private static void ValidateChangeSet(CommandResult result, string? power, string? mode, string? fan, string? swing)
	{
		if (!string.IsNullOrWhiteSpace(power) && !CieloClient.ValidPower.Contains(power, StringComparer.OrdinalIgnoreCase))
		{
			result.AddError("--power must be 'on' or 'off'.");
		}

		if (!string.IsNullOrWhiteSpace(mode) && !CieloClient.ValidModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
		{
			result.AddError("--mode must be one of: auto, heat, cool, dry, fan.");
		}

		if (!string.IsNullOrWhiteSpace(fan) && !CieloClient.ValidFanModes.Contains(fan, StringComparer.OrdinalIgnoreCase))
		{
			result.AddError("--fan must be one of: auto, low, medium, high, fanspeed.");
		}

		if (!string.IsNullOrWhiteSpace(swing) && !CieloClient.ValidSwingModes.Contains(swing, StringComparer.OrdinalIgnoreCase))
		{
			result.AddError("--swing must be one of: auto, auto/stop, adjust, pos1, pos2, pos3, pos4, pos5, pos6.");
		}

		if (string.Equals(power, "off", StringComparison.OrdinalIgnoreCase) &&
			(!string.IsNullOrWhiteSpace(mode) || !string.IsNullOrWhiteSpace(fan) || !string.IsNullOrWhiteSpace(swing)))
		{
			result.AddError("Do not combine --power off with active mode, fan, or swing changes.");
		}
	}

	private static DeviceChanges ReadChanges(
		ParseResult parseResult,
		Option<string?> powerOption,
		Option<string?> modeOption,
		Option<int?> tempOption,
		Option<string?> fanOption,
		Option<string?> swingOption,
		Option<string?> presetOption)
	{
		return new DeviceChanges
		{
			Power = parseResult.GetValue(powerOption),
			Mode = parseResult.GetValue(modeOption),
			Temperature = parseResult.GetValue(tempOption),
			FanSpeed = parseResult.GetValue(fanOption),
			Swing = parseResult.GetValue(swingOption),
			Preset = parseResult.GetValue(presetOption)
		};
	}

	private static async Task<JsonDocument> SendPendingMessageAsync(
		CieloWebSocketSession session,
		CieloDevice device,
		PendingMessage message,
		CancellationToken cancellationToken,
		Action<string>? trace)
	{
		var startedAt = DateTimeOffset.UtcNow;
		var response = await session.SendAndWaitForDeviceAsync(message.Payload, device.MacAddress, CommandTimeout, cancellationToken, trace);

		if (!TryGetRequestedMode(message, out var expectedMode))
		{
			return response;
		}

		if (!IsMatchingModeState(response.RootElement, expectedMode))
		{
			using var settledResponse = await session.WaitForDeviceAsync(
				device.MacAddress,
				CommandTimeout,
				cancellationToken,
				trace,
				root => IsMatchingModeState(root, expectedMode));
		}

		var remainingDelay = ModeSettleDelay - (DateTimeOffset.UtcNow - startedAt);
		if (remainingDelay > TimeSpan.Zero)
		{
			await Task.Delay(remainingDelay, cancellationToken);
		}

		return response;
	}

	internal static bool TryGetRequestedMode(PendingMessage message, out string expectedMode)
	{
		expectedMode = string.Empty;

		if (!message.Payload.TryGetValue("actionType", out var actionType) ||
			!string.Equals(actionType?.ToString(), "mode", StringComparison.OrdinalIgnoreCase) ||
			!message.Payload.TryGetValue("actionValue", out var actionValue))
		{
			return false;
		}

		expectedMode = actionValue?.ToString() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(expectedMode);
	}

	internal static bool IsMatchingModeState(JsonElement message, string expectedMode)
	{
		if (!string.Equals(TryGetString(message, "message_type"), "StateUpdate", StringComparison.OrdinalIgnoreCase) ||
			!message.TryGetProperty("action", out var action))
		{
			return false;
		}

		return string.Equals(TryGetString(action, "mode"), expectedMode, StringComparison.OrdinalIgnoreCase);
	}

	private static Option<string> CreateConfigOption()
	{
		return new Option<string>("--config")
		{
			Description = "Path to the Cielo auth config JSON.",
			DefaultValueFactory = _ => CieloConfigStore.GetDefaultPath()
		};
	}

	private static async Task<int> RunWithClientAsync(
		ParseResult parseResult,
		Option<string> configOption,
		Func<CieloClient, string, Task<int>> action,
		CancellationToken cancellationToken)
	{
		try
		{
			var configPath = parseResult.GetValue(configOption)!;
			var config = await CieloConfigStore.LoadAsync(configPath, cancellationToken);
			using var client = new CieloClient(config, CieloConfigStore.ExpandPath(configPath));
			return await action(client, configPath);
		}
		catch (OperationCanceledException)
		{
			return 130;
		}
		catch (Exception ex)
		{
			await Console.Error.WriteLineAsync(ex.Message);
			return 1;
		}
	}

	private static string? TryGetString(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.ToString(),
			JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
			JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
			_ => value.ToString()
		};
	}

	private static void WriteResultFile(string? path, ClimatePlanResult result)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var expandedPath = CieloConfigStore.ExpandPath(path!);
		var directory = Path.GetDirectoryName(expandedPath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllText(expandedPath, JsonSerializer.Serialize(result, CieloOutput.JsonOptions));
	}
}
