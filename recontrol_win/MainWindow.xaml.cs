using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using recontrol_win.Services;
using recontrol_win.Tools;

namespace recontrol_win
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly TokenStore _tokenStore = new TokenStore();
        private readonly AuthService _auth = new AuthService();
        private readonly WebSocketClient _wsClient;
        private readonly Uri _wsUri = new Uri("ws://localhost:3000/cable");

        public MainWindow()
        {
            InitializeComponent();

            SendPingButton.IsEnabled = false;

            _wsClient = new WebSocketClient(_wsUri, GetAccessTokenAsync, async () => await _auth.RefreshTokensAsync());
            _wsClient.ConnectionStatusChanged += OnConnectionStatusChanged;
            _wsClient.MessageReceived += OnMessageReceived;
            _wsClient.InfoMessage += OnInfoMessage;

            _ = ConnectAsync();
        }

        private async Task<string?> GetAccessTokenAsync()
        {
            var access = _tokenStore.GetAccessToken();
            if (!string.IsNullOrWhiteSpace(access))
                return access;

            var ok = await _auth.RefreshTokensAsync();
            return ok ? _tokenStore.GetAccessToken() : null;
        }

        private async Task ConnectAsync()
        {
            await _wsClient.ConnectAsync();
        }

        private void OnConnectionStatusChanged(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = connected ? "Connected" : "Disconnected";
                SendPingButton.IsEnabled = connected;
            });
        }

        private void OnMessageReceived(string text)
        {
            // try parse as JSON and display message field if present
            Debug.WriteLine($"Received: {text}");
            try
            {
                using var doc = JsonDocument.Parse(text);

                if (doc.RootElement.TryGetProperty("message", out var message))
                {
                    // Only treat as structured message when it's a JSON object
                    if (message.ValueKind == JsonValueKind.Object)
                    {
                        var from = "unknown";
                        if (message.TryGetProperty("from", out var fromProp) && fromProp.ValueKind == JsonValueKind.String)
                            from = fromProp.GetString() ?? "unknown";

                        string command = "";
                        if (message.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.String)
                            command = cmdProp.GetString() ?? "";

                        object payload = new { }; // default empty payload
                        if (message.TryGetProperty("payload", out var payloadProp))
                        {
                            // Pass the raw JsonElement so AddMessage serializes it correctly
                            payload = payloadProp;
                        }

                        AddMessage(from, command, payload);
                        return;
                    }

                    // fallback: 'message' exists but is not an object (e.g. ping with number)
                    AddMessage("server", "raw", new { text });
                    return;
                }

                // fallback: no 'message' property
                AddMessage("server", "raw", new { text });
            }
            catch (Exception ex)
            {
                // include exception message for easier debugging
                AddMessage("server", "raw", new { text, error = ex.Message });
            }
        }

        private void OnInfoMessage(string info)
        {
            AddMessage("system", "info", new { msg = info });
        }

        private async void SendPingButton_Click(object sender, RoutedEventArgs e)
        {
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

            try
            {
                await _wsClient.SendObjectAsync(data);
            }
            catch (Exception ex)
            {
                AddMessage("system", "error", new { msg = ex.Message });
            }
        }

        private void AddMessage(string from, string command, object payload)
        {
            Dispatcher.Invoke(() =>
            {
                var tb = new TextBlock { Text = $"{from}: {command} - {JsonSerializer.Serialize(payload)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2) };
                MessagesPanel.Children.Insert(0, tb);
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { _wsClient.Dispose(); } catch { }
            try { _auth.Dispose(); } catch { }
        }
    }
}