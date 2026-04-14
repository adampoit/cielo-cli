using System.Text.Json;

namespace CieloCli.Configuration;

internal static class CieloConfigStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public static string GetDefaultPath()
	{
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".config",
			"cielo",
			"config.json"
		);
	}

	public static string ExpandPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}

		if (path == "~")
		{
			return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		if (
			path.StartsWith("~/", StringComparison.Ordinal)
			|| path.StartsWith("~\\", StringComparison.Ordinal)
		)
		{
			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				path[2..]
			);
		}

		return Path.GetFullPath(path);
	}

	public static async Task<CieloConfig> LoadAsync(
		string path,
		CancellationToken cancellationToken
	)
	{
		var expandedPath = ExpandPath(path);
		if (!File.Exists(expandedPath))
		{
			throw new FileNotFoundException(
				$"Config file not found at {expandedPath}. Run 'config init' first.",
				expandedPath
			);
		}

		await using var stream = File.OpenRead(expandedPath);
		var config =
			await JsonSerializer.DeserializeAsync<CieloConfig>(
				stream,
				JsonOptions,
				cancellationToken
			)
			?? throw new InvalidOperationException(
				$"Could not parse config JSON at {expandedPath}."
			);

		config.Validate();
		return config;
	}

	public static async Task SaveAsync(
		string path,
		CieloConfig config,
		CancellationToken cancellationToken
	)
	{
		var expandedPath = ExpandPath(path);
		var directory = Path.GetDirectoryName(expandedPath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		await using var stream = File.Create(expandedPath);
		await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
	}

	public static async Task WriteTemplateAsync(
		string path,
		bool force,
		CancellationToken cancellationToken
	)
	{
		var expandedPath = ExpandPath(path);
		if (File.Exists(expandedPath) && !force)
		{
			throw new InvalidOperationException(
				$"Config already exists at {expandedPath}. Re-run with --force to overwrite it."
			);
		}

		var template = new CieloConfig
		{
			AccessToken = "paste-access-token-here",
			RefreshToken = "paste-refresh-token-here",
			SessionId = "paste-session-id-here",
			UserId = "paste-user-id-here",
			XApiKey = "paste-x-api-key-here",
		};

		await SaveAsync(expandedPath, template, cancellationToken);
	}
}
