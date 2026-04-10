using System.Net.Http.Json;
using System.Text.Json;
using CieloCli.Configuration;
using CieloCli.Models;

namespace CieloCli.Services;

internal sealed class CieloClient : IDisposable
{
    private const string ApiHost = "https://api.smartcielo.com";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static readonly string[] ValidPower = ["on", "off"];
    public static readonly string[] ValidModes = ["auto", "heat", "cool", "dry", "fan"];
    public static readonly string[] ValidFanModes = ["auto", "low", "medium", "high", "fanspeed"];
    public static readonly string[] ValidSwingModes = ["auto", "auto/stop", "adjust", "pos1", "pos2", "pos3", "pos4", "pos5", "pos6"];

    private readonly HttpClient _httpClient;
    private readonly string _configPath;
    private readonly CieloConfig _config;
    private long _lastTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public CieloClient(CieloConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("referer", "https://home.cielowigle.com/");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("origin", "https://home.cielowigle.com/");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("host", "api.smartcielo.com");
        TokenExpiresAtUtc = DateTimeOffset.MinValue;
    }

    public string SessionId => _config.SessionId;

    public string AccessToken => _config.AccessToken;

    public DateTimeOffset TokenExpiresAtUtc { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiHost}/web/token/refresh")
        {
            Content = JsonContent.Create(new
            {
                local = "en",
                refreshToken = _config.RefreshToken
            })
        };

        AddAuthHeaders(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<RefreshTokenResponse>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Cielo token refresh returned an empty response.");

        if (payload.Status != 200 || !string.Equals(payload.Message, "SUCCESS", StringComparison.OrdinalIgnoreCase) || payload.Data is null)
        {
            throw new InvalidOperationException($"Cielo token refresh failed: {payload.Message}");
        }

        _config.AccessToken = payload.Data.AccessToken;
        _config.RefreshToken = payload.Data.RefreshToken;

        var now = DateTimeOffset.UtcNow;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(payload.Data.ExpiresIn - 300, 0));
        TokenExpiresAtUtc = expiresAt > now ? expiresAt : now.AddMinutes(55);

        await CieloConfigStore.SaveAsync(_configPath, _config, cancellationToken);
    }

    public async Task<IReadOnlyList<CieloDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        await EnsureFreshTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiHost}/web/devices?limit=420");
        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<DeviceListResponse>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Cielo device discovery returned an empty response.");

        if (payload.Status != 200 || !string.Equals(payload.Message, "SUCCESS", StringComparison.OrdinalIgnoreCase) || payload.Data is null)
        {
            throw new InvalidOperationException($"Cielo device discovery failed: {payload.Message}");
        }

        var devices = payload.Data.ListDevices
            .Where(device => device.ApplianceId > 0)
            .ToList();

        var applianceMap = await GetAppliancesAsync(devices.Select(device => device.ApplianceId).Distinct(), cancellationToken);
        foreach (var device in devices)
        {
            if (applianceMap.TryGetValue(device.ApplianceId, out var appliance))
            {
                device.Appliance = appliance;
            }
        }

        return devices;
    }

    public async Task<CieloWebSocketSession> OpenWebSocketAsync(CancellationToken cancellationToken)
    {
        await EnsureFreshTokenAsync(cancellationToken);

        return await CieloWebSocketSession.ConnectAsync(_config.SessionId, _config.AccessToken, cancellationToken);
    }

    private Task EnsureFreshTokenAsync(CancellationToken cancellationToken)
    {
        return DateTimeOffset.UtcNow >= TokenExpiresAtUtc
            ? RefreshTokenAsync(cancellationToken)
            : Task.CompletedTask;
    }

    public IReadOnlyList<PendingMessage> BuildActionControlMessages(CieloDevice device, DeviceChanges changes)
    {
        var power = Normalize(changes.Power);
        var mode = Normalize(changes.Mode);
        var fan = Normalize(changes.FanSpeed);
        var swing = Normalize(changes.Swing);
        var preset = changes.Preset?.Trim();

        var messages = new List<PendingMessage>();

        var needPower = power is not null && !string.Equals(power, device.LatestAction.Power, StringComparison.OrdinalIgnoreCase);
        var actualMode = mode is not null ? NormalizeModeForDevice(device, mode) : null;
        var needMode = mode is not null && !string.Equals(actualMode, device.LatestAction.Mode, StringComparison.OrdinalIgnoreCase);
        var needTemp = changes.Temperature is not null &&
            (!int.TryParse(device.LatestAction.Temperature, out var currentTemp) || currentTemp != changes.Temperature.Value);
        var needFan = fan is not null && !string.Equals(fan, device.LatestAction.FanSpeed, StringComparison.OrdinalIgnoreCase);
        var needSwing = swing is not null && !string.Equals(swing, device.LatestAction.Swing, StringComparison.OrdinalIgnoreCase);

        if (needPower)
        {
            messages.Add(BuildPowerMessage(device, power!));
        }

        if (needMode || needTemp || needFan || needSwing || !string.IsNullOrWhiteSpace(preset))
        {
            EnsurePoweredOn(device, messages);
        }

        if (needMode)
        {
            messages.Add(BuildModeMessage(device, mode!));
        }

        if (needTemp)
        {
            messages.Add(BuildTemperatureMessage(device, changes.Temperature!.Value));
        }

        if (needFan)
        {
            messages.Add(BuildFanMessage(device, fan!));
        }

        if (needSwing)
        {
            messages.Add(BuildSwingMessage(device, swing!));
        }

        if (!string.IsNullOrWhiteSpace(preset))
        {
            messages.Add(BuildPresetMessage(device, preset!));
        }

        return messages;
    }

    private async Task<Dictionary<long, CieloAppliance>> GetAppliancesAsync(IEnumerable<long> applianceIds, CancellationToken cancellationToken)
    {
        var ids = applianceIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var encodedList = Uri.EscapeDataString($"[{string.Join(',', ids)}]");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiHost}/web/sync/db/6?applianceIdList={encodedList}");
        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<ApplianceListResponse>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Cielo appliance metadata returned an empty response.");

        if (payload.Status != 200 || !string.Equals(payload.Message, "SUCCESS", StringComparison.OrdinalIgnoreCase) || payload.Data is null)
        {
            throw new InvalidOperationException($"Cielo appliance metadata lookup failed: {payload.Message}");
        }

        return payload.Data.ListAppliances.ToDictionary(appliance => appliance.ApplianceId);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private void EnsurePoweredOn(CieloDevice device, ICollection<PendingMessage> messages)
    {
        if (!string.Equals(device.LatestAction.Power, "on", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(BuildPowerMessage(device, "on"));
        }
    }

    private PendingMessage BuildPowerMessage(CieloDevice device, string power)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();
        action["power"] = power;
        device.LatestAction.Power = power;

        var payload = CreateMessage(device, "actionControl", action, oldPower, "power", power);
        return new PendingMessage(payload, $"power {power}");
    }

    private PendingMessage BuildModeMessage(CieloDevice device, string mode)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();
        var actualMode = NormalizeModeForDevice(device, mode);
        action["mode"] = actualMode;
        device.LatestAction.Mode = actualMode;

        var payload = CreateMessage(device, "actionControl", action, oldPower, "mode", actualMode);
        return new PendingMessage(payload, $"mode {mode}");
    }

    private PendingMessage BuildTemperatureMessage(CieloDevice device, int temperature)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();
        string actionValue;
        var targetValue = temperature;
        var currentValue = int.TryParse(device.LatestAction.Temperature, out var parsedCurrent) ? parsedCurrent : temperature;

        if (!device.SupportsTargetTemperature)
        {
            if (currentValue < temperature)
            {
                actionValue = "inc";
                targetValue = temperature - 1;
            }
            else
            {
                actionValue = "dec";
                targetValue = temperature + 1;
            }
        }
        else
        {
            actionValue = temperature.ToString();
        }

        action["temp"] = targetValue.ToString();
        device.LatestAction.Temperature = targetValue.ToString();

        var payload = CreateMessage(device, "actionControl", action, oldPower, "temp", actionValue);
        return new PendingMessage(payload, $"temp {temperature}");
    }

    private PendingMessage BuildFanMessage(CieloDevice device, string fan)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();
        action["fanspeed"] = fan;
        device.LatestAction.FanSpeed = fan;

        var payload = CreateMessage(device, "actionControl", action, oldPower, "fanspeed", fan);
        return new PendingMessage(payload, $"fan {fan}");
    }

    private PendingMessage BuildSwingMessage(CieloDevice device, string swing)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();
        action["swing"] = swing;
        device.LatestAction.Swing = swing;

        var payload = CreateMessage(device, "actionControl", action, oldPower, "swing", swing);
        return new PendingMessage(payload, $"swing {swing}");
    }

    private PendingMessage BuildPresetMessage(CieloDevice device, string preset)
    {
        var oldPower = device.LatestAction.Power;
        var action = device.CreateActionPayload();

        if (device.TryResolvePresetId(preset, out var presetId))
        {
            action["mode"] = "auto";
            device.LatestAction.Mode = "auto";
            device.LatestAction.Preset = JsonDocument.Parse(presetId.ToString()).RootElement;

            var payload = CreateMessage(device, "actionControl", action, oldPower, "mode", "auto", presetId);
            return new PendingMessage(payload, $"preset {preset}");
        }

        if (TryNormalizeTurboPreset(preset, out var turbo))
        {
            action["turbo"] = turbo;
            device.LatestAction.Turbo = turbo;

            var payload = CreateMessage(device, "actionControl", action, oldPower, "turbo", turbo);
            return new PendingMessage(payload, $"preset {preset}");
        }

        throw new InvalidOperationException($"Preset '{preset}' was not found on {device.DeviceName}.");
    }

    private Dictionary<string, object?> CreateMessage(
        CieloDevice device,
        string actionName,
        Dictionary<string, object?> action,
        string oldPower,
        string? actionType,
        string? actionValue,
        int preset = 0)
    {
        var message = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = actionName,
            ["actionSource"] = "WEB",
            ["macAddress"] = device.MacAddress,
            ["user_id"] = _config.UserId,
            ["fw_version"] = device.FwVersion,
            ["deviceTypeVersion"] = device.DeviceTypeVersion,
            ["mid"] = "WEB",
            ["connection_source"] = device.ConnectionSource,
            ["application_version"] = "1.4.4",
            ["ts"] = NextTimestamp(),
            ["fwVersion"] = device.FwVersion,
            ["applianceType"] = device.ApplianceType,
            ["applianceId"] = device.ApplianceId,
            ["myRuleConfiguration"] = device.MyRuleConfiguration ?? JsonDocument.Parse("{}").RootElement,
            ["preset"] = preset,
            ["actions"] = action,
            ["oldPower"] = oldPower
        };

        if (actionType is not null)
        {
            message["actionType"] = actionType;
            message["actionValue"] = actionValue;
        }

        return message;
    }

    private long NextTimestamp()
    {
        while (true)
        {
            var current = Interlocked.Read(ref _lastTimestamp);
            var candidate = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), current + 1);
            if (Interlocked.CompareExchange(ref _lastTimestamp, candidate, current) == current)
            {
                return candidate;
            }
        }
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("authorization", _config.AccessToken);
        request.Headers.TryAddWithoutValidation("x-api-key", _config.XApiKey);
    }

    private static string NormalizeModeForDevice(CieloDevice device, string mode)
    {
        if (string.Equals(mode, "cool", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Appliance?.Mode, "mode", StringComparison.OrdinalIgnoreCase))
        {
            return "mode";
        }

        return mode;
    }

    private static bool TryNormalizeTurboPreset(string preset, out string turbo)
    {
        if (string.Equals(preset, "turbo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preset, "on", StringComparison.OrdinalIgnoreCase))
        {
            turbo = "on";
            return true;
        }

        if (string.Equals(preset, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preset, "off", StringComparison.OrdinalIgnoreCase))
        {
            turbo = "off";
            return true;
        }

        turbo = string.Empty;
        return false;
    }
}
