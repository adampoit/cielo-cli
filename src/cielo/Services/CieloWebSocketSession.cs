using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CieloCli.Services;

internal sealed class CieloWebSocketSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ClientWebSocket _socket = new();

    private CieloWebSocketSession()
    {
    }

    public static async Task<CieloWebSocketSession> ConnectAsync(string sessionId, string accessToken, CancellationToken cancellationToken)
    {
        var session = new CieloWebSocketSession();
        session._socket.Options.SetRequestHeader("Host", "apiwss.smartcielo.com");
        session._socket.Options.SetRequestHeader("Cache-control", "no-cache");
        session._socket.Options.SetRequestHeader("Pragma", "no-cache");
        session._socket.Options.SetRequestHeader("User-agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36");
        session._socket.Options.SetRequestHeader("Origin", "https://home.cielowigle.com");

        var uri = new Uri($"wss://apiwss.smartcielo.com/websocket/?sessionId={Uri.EscapeDataString(sessionId)}&token={Uri.EscapeDataString(accessToken)}");
        await session._socket.ConnectAsync(uri, cancellationToken);
        return session;
    }

    public async Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    public async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        await SendTextAsync(JsonSerializer.Serialize(payload), cancellationToken);
    }

    public async Task<JsonDocument?> ReceiveJsonAsync(CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        while (true)
        {
            var segment = new ArraySegment<byte>(new byte[4096]);
            var result = await _socket.ReceiveAsync(segment, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            buffer.Write(segment.AsSpan(0, result.Count));
            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (buffer.WrittenCount == 0)
        {
            return null;
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    public async Task<JsonDocument> SendAndWaitForDeviceAsync(
        object payload,
        string macAddress,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        trace?.Invoke($">>> {JsonSerializer.Serialize(payload, TraceJsonOptions)}");
        await SendJsonAsync(payload, cancellationToken);

        return await WaitForDeviceAsync(macAddress, timeout, cancellationToken, trace);
    }

    public async Task<JsonDocument> WaitForDeviceAsync(
        string macAddress,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? trace = null,
        Func<JsonElement, bool>? predicate = null)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var message = await ReceiveJsonAsync(timeoutSource.Token)
                    ?? throw new InvalidOperationException("Websocket closed before Cielo acknowledged the command.");

                var root = message.RootElement;
                trace?.Invoke($"<<< {root.GetRawText()}");
                var currentMac = TryGetString(root, "mac_address") ?? TryGetString(root, "macAddress");

                if (string.Equals(currentMac, macAddress, StringComparison.OrdinalIgnoreCase) &&
                    (predicate is null ? IsDeviceAck(root) : predicate(root)))
                {
                    return message;
                }

                message.Dispose();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for Cielo to acknowledge the update for {macAddress}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationTokenSource.Token);
        }

        _socket.Dispose();
    }

    private static bool IsDeviceAck(JsonElement element)
    {
        var messageType = TryGetString(element, "message_type");
        return string.Equals(messageType, "StateUpdate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(messageType, "DeviceSettingsAck", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => value.ToString()
        };
    }
}
