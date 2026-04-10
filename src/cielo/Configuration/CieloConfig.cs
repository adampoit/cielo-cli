using System.Text.Json.Serialization;

namespace CieloCli.Configuration;

internal sealed class CieloConfig
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("x_api_key")]
    public string XApiKey { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken) ||
            string.IsNullOrWhiteSpace(RefreshToken) ||
            string.IsNullOrWhiteSpace(SessionId) ||
            string.IsNullOrWhiteSpace(UserId) ||
            string.IsNullOrWhiteSpace(XApiKey))
        {
            throw new InvalidOperationException(
                "Config is missing one or more required keys: access_token, refresh_token, session_id, user_id, x_api_key.");
        }
    }
}
