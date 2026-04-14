using System.Globalization;
using System.Text.Json.Serialization;

namespace CieloCli.Configuration;

internal sealed class MonitorRulesConfig
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	[JsonPropertyName("planDefaults")]
	public PlanDefaults? PlanDefaults { get; set; }

	[JsonPropertyName("rules")]
	public List<MonitorRule> Rules { get; set; } = [];

	public void Validate()
	{
		if (Version != 1)
		{
			throw new InvalidOperationException(
				$"Unsupported rules version '{Version}'. Expected version 1."
			);
		}

		PlanDefaults?.Validate();

		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < Rules.Count; index++)
		{
			var rule = Rules[index];
			rule.Validate(index + 1);

			if (!seenNames.Add(rule.Name))
			{
				throw new InvalidOperationException(
					$"Duplicate rule name '{rule.Name}'. Rule names must be unique."
				);
			}
		}
	}
}

internal sealed class PlanDefaults
{
	[JsonPropertyName("allowedDevices")]
	public List<string> AllowedDevices { get; set; } = [];

	[JsonPropertyName("minSetpoint")]
	public int MinSetpoint { get; set; } = 64;

	[JsonPropertyName("maxSetpoint")]
	public int MaxSetpoint { get; set; } = 72;

	public void Validate()
	{
		if (MinSetpoint >= MaxSetpoint)
		{
			throw new InvalidOperationException(
				$"planDefaults.minSetpoint ({MinSetpoint}) must be less than maxSetpoint ({MaxSetpoint})."
			);
		}
	}
}

internal sealed class MonitorRule
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("device")]
	public string? Device { get; set; }

	[JsonPropertyName("when")]
	public MonitorRuleCondition When { get; set; } = new();

	[JsonPropertyName("active")]
	public MonitorRuleActiveWindow? Active { get; set; }

	[JsonPropertyName("cooldownSeconds")]
	public int CooldownSeconds { get; set; }

	[JsonPropertyName("action")]
	public MonitorRuleAction Action { get; set; } = new();

	public void Validate(int index)
	{
		if (string.IsNullOrWhiteSpace(Name))
		{
			throw new InvalidOperationException($"Rule #{index} is missing a name.");
		}

		When.Validate(Name);
		Active?.Validate(Name);
		Action.Validate(Name);

		if (CooldownSeconds < 0)
		{
			throw new InvalidOperationException(
				$"Rule '{Name}' has a negative cooldownSeconds value."
			);
		}
	}
}

internal sealed class MonitorRuleActiveWindow
{
	[JsonPropertyName("timezone")]
	public string Timezone { get; set; } = string.Empty;

	[JsonPropertyName("start")]
	public string Start { get; set; } = string.Empty;

	[JsonPropertyName("end")]
	public string End { get; set; } = string.Empty;

	public void Validate(string ruleName)
	{
		if (
			string.IsNullOrWhiteSpace(Timezone)
			|| string.IsNullOrWhiteSpace(Start)
			|| string.IsNullOrWhiteSpace(End)
		)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' active window requires timezone, start, and end."
			);
		}

		try
		{
			TimeZoneInfo.FindSystemTimeZoneById(Timezone);
		}
		catch (TimeZoneNotFoundException)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' uses unknown timezone '{Timezone}'."
			);
		}
		catch (InvalidTimeZoneException)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' uses invalid timezone '{Timezone}'."
			);
		}

		var start = ParseTime(ruleName, Start, "start");
		var end = ParseTime(ruleName, End, "end");
		if (start == end)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' active window start and end must differ."
			);
		}
	}

	public TimeOnly GetStart(string ruleName)
	{
		return ParseTime(ruleName, Start, "start");
	}

	public TimeOnly GetEnd(string ruleName)
	{
		return ParseTime(ruleName, End, "end");
	}

	private static TimeOnly ParseTime(string ruleName, string value, string fieldName)
	{
		if (
			!TimeOnly.TryParseExact(
				value,
				"HH:mm",
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out var parsed
			)
		)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' uses invalid active.{fieldName} value '{value}'. Expected HH:mm."
			);
		}

		return parsed;
	}
}

internal sealed class MonitorRuleCondition
{
	[JsonPropertyName("metric")]
	public string Metric { get; set; } = string.Empty;

	[JsonPropertyName("below")]
	public decimal? Below { get; set; }

	[JsonPropertyName("above")]
	public decimal? Above { get; set; }

	[JsonPropertyName("unit")]
	public string? Unit { get; set; }

	[JsonPropertyName("forSamples")]
	public int ForSamples { get; set; } = 1;

	[JsonPropertyName("hysteresis")]
	public decimal? Hysteresis { get; set; }

	public void Validate(string ruleName)
	{
		if (
			!string.Equals(Metric, "roomTemp", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(Metric, "targetTemp", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(Metric, "humidity", StringComparison.OrdinalIgnoreCase)
		)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' uses unsupported metric '{Metric}'. Supported metrics: roomTemp, targetTemp, humidity."
			);
		}

		if ((Below is null && Above is null) || (Below is not null && Above is not null))
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' must define exactly one of 'below' or 'above'."
			);
		}

		if (ForSamples < 1)
		{
			throw new InvalidOperationException($"Rule '{ruleName}' must use forSamples >= 1.");
		}

		if (Hysteresis is < 0)
		{
			throw new InvalidOperationException($"Rule '{ruleName}' must use hysteresis >= 0.");
		}

		if (
			string.Equals(Metric, "humidity", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(Unit)
		)
		{
			throw new InvalidOperationException(
				$"Rule '{ruleName}' should not specify a unit for humidity."
			);
		}
	}
}

internal sealed class MonitorRuleAction
{
	[JsonPropertyName("exec")]
	public string Exec { get; set; } = string.Empty;

	[JsonPropertyName("args")]
	public List<string> Args { get; set; } = [];

	public void Validate(string ruleName)
	{
		if (string.IsNullOrWhiteSpace(Exec))
		{
			throw new InvalidOperationException($"Rule '{ruleName}' is missing action.exec.");
		}
	}
}
