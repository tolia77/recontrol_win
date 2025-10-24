using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using recontrol_win.Services;

namespace recontrol_win
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Net.WebSockets.ClientWebSocket? _webSocket;
        private readonly TokenStore _tokenStore = new TokenStore();
        private readonly AuthService _auth = new AuthService();
        private readonly Uri _wsUri = new Uri("ws://localhost:3000/cable");
        private bool _connected = false;

        public MainWindow()
        {
            InitializeComponent();

            // Start with Send button disabled until connected
            SendPingButton.IsEnabled = false;

            // Start connection
            _ = EnsureConnectedAsync();
        }

        private async Task<string?> GetAccessTokenAsync()
        {
            var access = _tokenStore.GetAccessToken();
            if (!string.IsNullOrWhiteSpace(access))
                return access;

            // try refresh
            var ok = await _auth.RefreshTokensAsync();
            return ok ? _tokenStore.GetAccessToken() : null;
        }

        private async Task EnsureConnectedAsync()
        {
            var token = await GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                AddMessage("system", "error", new { msg = "No access token; please login again" });
                return;
            }

            try
            {
                // Using clientwebsocket to be able to receive messages
                _webSocket = new System.Net.WebSockets.ClientWebSocket();
                var uriWithToken = new Uri($"{_wsUri}?access_token={Uri.EscapeDataString(token)}");
                await _webSocket.ConnectAsync(uriWithToken, System.Threading.CancellationToken.None);
                _connected = true;
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Connected";
                    SendPingButton.IsEnabled = true;
                });

                _ = ReceiveLoopAsync();

                // subscribe
                var subscribe = new
                {
                    command = "subscribe",
                    identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" })
                };
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(subscribe));
                await _webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddMessage("system", "error", new { msg = ex.Message });
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Disconnected";
                    SendPingButton.IsEnabled = false;
                });
                _connected = false;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            while (_webSocket != null && _webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closing", System.Threading.CancellationToken.None);
                        break;
                    }

                    var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("message", out var message))
                        {
                            var msgJson = message;
                            var from = msgJson.GetProperty("from").GetString() ?? "unknown";
                            var command = msgJson.GetProperty("command").GetString() ?? "";
                            var payload = msgJson.GetProperty("payload");
                            AddMessage(from, command, payload);
                        }
                        else if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "reject_subscription")
                        {
                            // token likely invalid, attempt refresh
                            AddMessage("system", "info", new { msg = "Subscription rejected, attempting token refresh" });
                            var refreshed = await _auth.RefreshTokensAsync();
                            if (refreshed)
                            {
                                await EnsureConnectedAsync();
                            }
                        }
                    }
                    catch
                    {
                        // raw message
                        AddMessage("server", "raw", new { text });
                    }
                }
                catch (Exception ex)
                {
                    AddMessage("system", "error", new { msg = ex.Message });
                    break;
                }
            }

            _connected = false;
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Disconnected";
                SendPingButton.IsEnabled = false;
            });
        }

        private async void SendPingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webSocket == null || _webSocket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                AddMessage("system", "error", new { msg = "WebSocket not connected" });
                return;
            }

            var payload = new
            {
                command = "ping",
                payload = new { msg = "hello desktop" }
            };

            var data = new
            {
                command = "message",
                identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }),
                data = JsonSerializer.Serialize(payload)
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
            await _webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
        }

        private void AddMessage(string from, string command, object payload)
        {
            Dispatcher.Invoke(() =>
            {
                var tb = new TextBlock { Text = $"{from}: {command} - {JsonSerializer.Serialize(payload)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2) };
                MessagesPanel.Children.Insert(0, tb);
            });
        }
    }
}