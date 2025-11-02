using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
            // Try up to 2 attempts: initial, then refresh+retry
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var token = await _getAccessToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    InfoMessage?.Invoke("No access token available");
                    return false;
                }

                try
                {
                    await CloseInternalAsync();

                    _ws = new ClientWebSocket();
                    var uriWithToken = new Uri($"{_uri}?access_token={Uri.EscapeDataString(token)}");
                    await _ws.ConnectAsync(uriWithToken, CancellationToken.None);

                    ConnectionStatusChanged?.Invoke(true);

                    // start receive loop without awaiting
                    _ = ReceiveLoopAsync(_ws);

                    // send subscribe for both channels
                    var subscribeCmd = new
                    {
                        command = "subscribe",
                        identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" })
                    };
                    await SendObjectAsync(subscribeCmd);

                    var subscribeRtc = new
                    {
                        command = "subscribe",
                        identifier = JsonSerializer.Serialize(new { channel = "WebRtcChannel" })
                    };
                    await SendObjectAsync(subscribeRtc);

                    return true;
                }
                catch (Exception ex)
                {
                    InfoMessage?.Invoke($"Connect attempt {attempt + 1} failed: {ex.Message}");

                    // if first attempt, try to refresh tokens and retry
                    if (attempt == 0)
                    {
                        var refreshed = await _refreshTokens();
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
            return false;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws)
        {
            var buffer = new byte[8192];
            try
            {
                while (ws != null && ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Notify raw text; caller may parse JSON
                    MessageReceived?.Invoke(text);
                }
            }
            catch (Exception ex)
            {
                InfoMessage?.Invoke($"ReceiveLoop error: {ex.Message}");
            }

            ConnectionStatusChanged?.Invoke(false);
        }

        public async Task SendObjectAsync(object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            await SendAsync(json);
        }

        public async Task SendAsync(string message)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket is not connected");
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task CloseInternalAsync()
        {
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived || _ws.State == WebSocketState.CloseSent)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
                }

                _ws.Dispose();
                _ws = null;
            }
        }

        public async Task DisconnectAsync()
        {
            await CloseInternalAsync();
            ConnectionStatusChanged?.Invoke(false);
        }

        public void Dispose()
        {
            try { _ws?.Dispose(); } catch { }
        }
    }
}
