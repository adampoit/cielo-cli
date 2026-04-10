using System.Text.Json.Serialization;

namespace CieloCli.Models;

internal sealed class ClimatePlan
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("devices")]
    public List<ClimatePlanDevice> Devices { get; set; } = [];
}

internal sealed class ClimatePlanDevice
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("setpoint")]
    public int Setpoint { get; set; }
}

internal sealed class ClimatePlanResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("devices")]
    public List<ClimatePlanDeviceResult> Devices { get; set; } = [];
}

internal sealed class ClimatePlanDeviceResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("setpoint")]
    public int Setpoint { get; set; }

    [JsonPropertyName("previousMode")]
    public string PreviousMode { get; set; } = string.Empty;

    [JsonPropertyName("previousSetpoint")]
    public string PreviousSetpoint { get; set; } = string.Empty;

    [JsonPropertyName("applied")]
    public bool Applied { get; set; }
}

internal sealed class ResolvedPlanDevice
{
    public required CieloDevice Device { get; init; }
    public required int Setpoint { get; init; }
}