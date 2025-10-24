using System;
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
            try
            {
                var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var message))
                {
                    var from = message.GetProperty("from").GetString() ?? "unknown";
                    var command = message.GetProperty("command").GetString() ?? "";
                    var payload = message.GetProperty("payload");
                    AddMessage(from, command, payload);
                    return;
                }

                // fallback: show raw payload
                AddMessage("server", "raw", new { text });
            }
            catch
            {
                AddMessage("server", "raw", new { text });
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