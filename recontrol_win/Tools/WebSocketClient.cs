using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using recontrol_win.Internal;
using System.Threading;

namespace recontrol_win.Tools
{
    /// <summary>
    /// Lightweight WebSocket client wrapper with token-based auth and single-refresh retry.
    /// Emits raw messages and status notifications.
    /// </summary>
    public class WebSocketClient : IDisposable
    {
        private readonly Uri _uri;
        private readonly Func<Task<string?>> _getAccessToken;
        private readonly Func<Task<bool>> _refreshTokens;
        private ClientWebSocket? _ws;

        // guard to avoid multiple concurrent reconnects
        private int _reconnecting = 0;
        private volatile bool _disposed = false;

        public event Action<string>? MessageReceived;
        public event Action<string>? InfoMessage;
        public event Action<bool>? ConnectionStatusChanged;

        public WebSocketClient(Uri uri, Func<Task<string?>> getAccessToken, Func<Task<bool>> refreshTokens)
        {
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
            _getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
            _refreshTokens = refreshTokens ?? throw new ArgumentNullException(nameof(refreshTokens));
        }

        public async Task<bool> ConnectAsync()
        {
            if (_disposed) return false;

            // Try up to 2 attempts: initial, then refresh+retry
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var token = await _getAccessToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    InfoMessage?.Invoke("No access token available");
                    InternalLogger.Log("WebSocketClient.ConnectAsync: no access token available");
                    return false;
                }

                try
                {
                    await CloseInternalAsync();

                    _ws = new ClientWebSocket();
                    var uriWithToken = new Uri($"{_uri}?access_token={Uri.EscapeDataString(token)}");
                    InternalLogger.Log($"WebSocketClient.ConnectAsync attempt {attempt + 1}: uri={uriWithToken}");
                    await _ws.ConnectAsync(uriWithToken, CancellationToken.None);

                    ConnectionStatusChanged?.Invoke(true);

                    // start receive loop without awaiting
                    _ = ReceiveLoopAsync(_ws);

                    // send subscribe
                    var subscribe = new
                    {
                        command = "subscribe",
                        identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" })
                    };
                    await SendObjectAsync(subscribe);

                    return true;
                }
                catch (Exception ex)
                {
                    InfoMessage?.Invoke($"Connect attempt {attempt + 1} failed: {ex.Message}");
                    InternalLogger.LogException("WebSocketClient.ConnectAsync", ex);

                    // if first attempt, try to refresh tokens and retry
                    if (attempt == 0)
                    {
                        var refreshed = await _refreshTokens();
                        InternalLogger.Log($"WebSocketClient.ConnectAsync token refresh result: {refreshed}");
                        if (!refreshed)
                        {
                            InfoMessage?.Invoke("Token refresh failed");
                            break;
                        }

                        // otherwise loop will retry
                        continue;
                    }

                    break;
                }
            }

            ConnectionStatusChanged?.Invoke(false);
            InternalLogger.Log("WebSocketClient.ConnectAsync failed after retries");
            return false;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws)
        {
            var buffer = new byte[8192];
            try
            {
                while (ws != null && ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult? result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            ConnectionStatusChanged?.Invoke(false);
                            // Start reconnect loop for generic closes (non-unauthorized case)
                            _ = Task.Run(async () => await HandleReconnectOnDisconnectAsync(reason: null));
                            return;
                        }

                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    var text = sb.ToString();

                    // Handle ActionCable control frames like disconnect/ping/welcome
                    if (TryHandleControlMessage(text))
                    {
                        continue; // control message consumed
                    }

                    // Notify raw text; caller may parse JSON
                    MessageReceived?.Invoke(text);
                    InternalLogger.Log($"WebSocketClient.MessageReceived: {text}");
                }
            }
            catch (Exception ex)
            {
                InfoMessage?.Invoke($"ReceiveLoop error: {ex.Message}");
                InternalLogger.LogException("WebSocketClient.ReceiveLoopAsync", ex);
                // Assume network disruption; try to reconnect periodically
                _ = Task.Run(async () => await HandleReconnectOnDisconnectAsync(reason: null));
            }

            ConnectionStatusChanged?.Invoke(false);
        }

        private bool TryHandleControlMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var type = typeProp.GetString();
                switch (type)
                {
                    case "ping":
                        // ignore
                        return true;
                    case "welcome":
                        InfoMessage?.Invoke("WebSocket welcome received");
                        return true;
                    case "disconnect":
                        bool reconnect = root.TryGetProperty("reconnect", out var recProp) && recProp.ValueKind == JsonValueKind.True ? true : (recProp.ValueKind == JsonValueKind.False ? false : (recProp.ValueKind == JsonValueKind.String && bool.TryParse(recProp.GetString(), out var b) && b));
                        string? reason = null;
                        if (root.TryGetProperty("reason", out var reasonProp))
                        {
                            reason = reasonProp.ValueKind == JsonValueKind.String ? reasonProp.GetString() : reasonProp.GetRawText();
                        }
                        InfoMessage?.Invoke($"Server disconnect: reason={reason ?? "unknown"}, reconnect={reconnect}");
                        InternalLogger.Log($"WebSocketClient: disconnect received. reason={reason}, reconnect={reconnect}");

                        if (reconnect)
                        {
                            _ = Task.Run(async () => await HandleReconnectOnDisconnectAsync(reason));
                        }
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task HandleReconnectOnDisconnectAsync(string? reason)
        {
            if (_disposed) return;

            // Avoid concurrent reconnect storms
            if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            {
                return;
            }

            try
            {
                // Close any existing socket
                await CloseInternalAsync();

                bool unauthorized = false;
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    var r = reason!;
                    unauthorized = r.Contains("unauth", StringComparison.OrdinalIgnoreCase) || r.Contains("token", StringComparison.OrdinalIgnoreCase);
                }

                if (unauthorized)
                {
                    try
                    {
                        var refreshed = await _refreshTokens();
                        InfoMessage?.Invoke($"Token refresh on disconnect: {(refreshed ? "ok" : "failed")}");
                        if (!refreshed)
                        {
                            // If refresh fails, do not start a reconnect loop to avoid churn.
                            return;
                        }

                        // Single reconnect attempt after successful refresh
                        await ConnectAsync();
                        return;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.LogException("WebSocketClient.HandleReconnectOnDisconnectAsync refresh", ex);
                        return;
                    }
                }

                // Not unauthorized: keep trying to reconnect every 5 seconds until success
                await ReconnectLoopAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        private async Task ReconnectLoopAsync(TimeSpan interval)
        {
            int attempt = 0;
            while (!_disposed)
            {
                attempt++;
                InfoMessage?.Invoke($"Reconnecting (attempt {attempt})...");
                var ok = await ConnectAsync();
                if (ok)
                {
                    InfoMessage?.Invoke("Reconnected");
                    return;
                }

                try
                {
                    await Task.Delay(interval);
                }
                catch { }
            }
        }

        public async Task SendObjectAsync(object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            await SendAsync(json);
        }

        public async Task SendAsync(string message)
        {
            InternalLogger.Log($"WebSocketClient.SendAsync: {message}");
            if (_ws == null || _ws.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket is not connected");
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task CloseInternalAsync()
        {
            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived || _ws.State == WebSocketState.CloseSent)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.LogException("WebSocketClient.CloseInternalAsync", ex);
                }

                try { _ws.Dispose(); } catch { }
                _ws = null;
            }
        }

        public async Task DisconnectAsync()
        {
            _disposed = true;
            await CloseInternalAsync();
            ConnectionStatusChanged?.Invoke(false);
            InternalLogger.Log("WebSocketClient.DisconnectAsync called");
        }

        public void Dispose()
        {
            _disposed = true;
            try { _ws?.Dispose(); } catch { }
        }
    }
}
