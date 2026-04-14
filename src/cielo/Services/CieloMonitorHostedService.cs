using CieloCli.Models;
using Microsoft.Extensions.Hosting;

namespace CieloCli.Services;

internal sealed class CieloMonitorHostedService : BackgroundService
{
	private readonly IHostApplicationLifetime _applicationLifetime;
	private readonly MonitorCommandOptions _options;

	public CieloMonitorHostedService(
		IHostApplicationLifetime applicationLifetime,
		MonitorCommandOptions options
	)
	{
		_applicationLifetime = applicationLifetime;
		_options = options;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		Environment.ExitCode = await CieloMonitorExecution.RunWithExitCodeAsync(
			_options,
			canceledExitCode: 0,
			stoppingToken
		);
		_applicationLifetime.StopApplication();
	}
}
