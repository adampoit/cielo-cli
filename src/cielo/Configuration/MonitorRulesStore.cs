using System.Text.Json;

namespace CieloCli.Configuration;

internal static class MonitorRulesStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
	};

	public static string GetDefaultPath()
	{
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".config",
			"cielo",
			"rules.json"
		);
	}

	public static async Task<(MonitorRulesConfig? Rules, string? Path)> LoadOptionalAsync(
		string? requestedPath,
		CancellationToken cancellationToken
	)
	{
		var resolvedPath = ResolvePath(requestedPath);
		if (resolvedPath is null)
		{
			return (null, null);
		}

		if (!File.Exists(resolvedPath))
		{
			throw new FileNotFoundException(
				$"Rules file not found at {resolvedPath}.",
				resolvedPath
			);
		}

		await using var stream = File.OpenRead(resolvedPath);
		var config =
			await JsonSerializer.DeserializeAsync<MonitorRulesConfig>(
				stream,
				JsonOptions,
				cancellationToken
			)
			?? throw new InvalidOperationException(
				$"Could not parse rules JSON at {resolvedPath}."
			);

		config.Validate();
		return (config, resolvedPath);
	}

	private static string? ResolvePath(string? requestedPath)
	{
		if (!string.IsNullOrWhiteSpace(requestedPath))
		{
			return CieloConfigStore.ExpandPath(requestedPath);
		}

		var defaultPath = GetDefaultPath();
		return File.Exists(defaultPath) ? defaultPath : null;
	}
}
