using CieloCli.Configuration;
using CieloCli.Models;

namespace CieloCli.Services;

internal sealed class CieloMonitorRunner
{
    public async Task<int> RunAsync(CieloClient client, MonitorCommandOptions options, CancellationToken cancellationToken)
    {
        var historyFilePath = string.IsNullOrWhiteSpace(options.HistoryFile)
            ? null
            : CieloConfigStore.ExpandPath(options.HistoryFile);
        if (!string.IsNullOrWhiteSpace(historyFilePath))
        {
            var historyDirectory = Path.GetDirectoryName(historyFilePath);
            if (!string.IsNullOrWhiteSpace(historyDirectory))
            {
                Directory.CreateDirectory(historyDirectory);
            }
        }

        var (rulesConfig, resolvedRulesPath) = await MonitorRulesStore.LoadOptionalAsync(options.RulesPath, cancellationToken);

        var initialDevices = await client.GetDevicesAsync(cancellationToken);
        string? targetMacAddress = null;
        if (!string.IsNullOrWhiteSpace(options.Device))
        {
            targetMacAddress = CieloDeviceResolver.Resolve(initialDevices, options.Device!).MacAddress;
        }

        var initialSelectedDevices = string.IsNullOrWhiteSpace(targetMacAddress)
            ? initialDevices.OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase).ToList()
            : initialDevices.Where(device => string.Equals(device.MacAddress, targetMacAddress, StringComparison.OrdinalIgnoreCase)).ToList();

        var weatherClient = options.WeatherLatitude.HasValue && options.WeatherLongitude.HasValue && initialSelectedDevices.Count > 0
            ? new OpenMeteoWeatherClient(
                options.WeatherLatitude.Value,
                options.WeatherLongitude.Value,
                initialSelectedDevices[0].TemperatureUnit,
                options.WeatherRefresh)
            : null;

        var rulesEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(historyFilePath))
        {
            rulesEnvironment["CIELO_HISTORY_FILE"] = historyFilePath;
        }

        var rulesEngine = rulesConfig is null || resolvedRulesPath is null
            ? null
            : new CieloMonitorRulesEngine(rulesConfig, resolvedRulesPath, options.DryRun, rulesEnvironment);

        rulesEngine?.WriteStartupMessage();

        if (!options.Json)
        {
            Console.WriteLine(options.Daemon
                ? $"Daemon mode active. Polling every {options.Interval.TotalSeconds:0} seconds."
                : $"Polling every {options.Interval.TotalSeconds:0} seconds. Press Ctrl+C to stop.");
            Console.WriteLine();
        }

        var lastSamples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completedSamples = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var devices = await client.GetDevicesAsync(cancellationToken);
                var selectedDevices = string.IsNullOrWhiteSpace(targetMacAddress)
                    ? devices.OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase).ToList()
                    : devices.Where(device => string.Equals(device.MacAddress, targetMacAddress, StringComparison.OrdinalIgnoreCase)).ToList();

                if (selectedDevices.Count == 0)
                {
                    throw new InvalidOperationException("The selected device was not returned by Cielo during monitoring.");
                }

                var timestamp = DateTimeOffset.UtcNow;
                var outdoorWeather = weatherClient is null
                    ? null
                    : await weatherClient.GetCurrentAsync(cancellationToken);

                foreach (var device in selectedDevices)
                {
                    var sample = MonitorSample.FromDevice(device, timestamp, "poll", outdoorWeather);
                    if (rulesEngine is not null)
                    {
                        await rulesEngine.EvaluateAsync(sample);
                    }

                    if (!string.IsNullOrWhiteSpace(historyFilePath))
                    {
                        await File.AppendAllTextAsync(historyFilePath, CieloOutput.ToMonitorSampleJson(sample) + Environment.NewLine, cancellationToken);
                    }

                    var signature = CieloOutput.GetMonitorSampleSignature(sample);
                    if (options.ChangesOnly &&
                        lastSamples.TryGetValue(sample.MacAddress, out var previousSignature) &&
                        previousSignature == signature)
                    {
                        continue;
                    }

                    lastSamples[sample.MacAddress] = signature;
                    CieloOutput.WriteMonitorSample(sample, options.Json);
                }

                completedSamples++;
                if (options.SampleLimit is not null && completedSamples >= options.SampleLimit.Value)
                {
                    break;
                }

                await Task.Delay(options.Interval, cancellationToken);
            }
        }
        finally
        {
            if (rulesEngine is not null)
            {
                await rulesEngine.FlushAsync();
            }
        }

        return 0;
    }
}
