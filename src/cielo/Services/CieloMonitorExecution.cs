using CieloCli.Configuration;
using CieloCli.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CieloCli.Services;

internal static class CieloMonitorExecution
{
	public static Task<int> RunForegroundAsync(
		MonitorCommandOptions options,
		CancellationToken cancellationToken
	)
	{
		return RunWithExitCodeAsync(options, canceledExitCode: 130, cancellationToken);
	}

	public static async Task<int> RunHostedAsync(MonitorCommandOptions options)
	{
		Environment.ExitCode = 0;

		using var host = Host.CreateDefaultBuilder()
			.UseSystemd()
			.ConfigureServices(services =>
			{
				services.AddSingleton(options);
				services.AddHostedService<CieloMonitorHostedService>();
			})
			.Build();

		await host.RunAsync();
		return Environment.ExitCode;
	}

	internal static async Task<int> RunWithExitCodeAsync(
		MonitorCommandOptions options,
		int canceledExitCode,
		CancellationToken cancellationToken
	)
	{
		try
		{
			var config = await CieloConfigStore.LoadAsync(options.ConfigPath, cancellationToken);
			using var client = new CieloClient(
				config,
				CieloConfigStore.ExpandPath(options.ConfigPath)
			);
			var runner = new CieloMonitorRunner();
			return await runner.RunAsync(client, options, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return canceledExitCode;
		}
		catch (Exception ex)
		{
			await Console.Error.WriteLineAsync(ex.Message);
			return 1;
		}
	}
}
