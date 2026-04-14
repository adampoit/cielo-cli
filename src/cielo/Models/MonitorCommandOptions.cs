namespace CieloCli.Models;

internal sealed record MonitorCommandOptions(
	string ConfigPath,
	string? Device,
	TimeSpan Interval,
	int? SampleLimit,
	bool Json,
	string? RulesPath,
	bool DryRun,
	string? HistoryFile,
	double? WeatherLatitude,
	double? WeatherLongitude,
	TimeSpan WeatherRefresh,
	bool ChangesOnly,
	bool Daemon
);
