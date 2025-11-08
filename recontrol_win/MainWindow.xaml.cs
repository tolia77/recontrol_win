using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using recontrol_win.Services;
using recontrol_win.Tools;
using recontrol_win.Internal;

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
        private readonly Uri _wsUri; // loaded from .env / environment vars

        // new: command parser/dispatcher
        private readonly CommandJsonParser _cmdParser;
        private readonly CommandDispatcher _dispatcher;
        private readonly ScreenService _screenService;
        private readonly PowerService _powerService;

        // Tray manager
        private readonly TrayIconManager _trayManager;
        private LogsWindow? _logsWindow;

        public MainWindow()
        {
            InitializeComponent();

            var wsUrl = Environment.GetEnvironmentVariable("WS_URL");
            var env = Environment.GetEnvironmentVariable("ENVIRONMENT");
            if (string.Equals(env, "dev", StringComparison.OrdinalIgnoreCase))
            {
                var wsDev = Environment.GetEnvironmentVariable("WS_URL_DEV");
                if (!string.IsNullOrWhiteSpace(wsDev))
                {
                    wsUrl = wsDev;
                }
            }

            if (string.IsNullOrWhiteSpace(wsUrl))
            {
                wsUrl = "ws://localhost:3000/cable"; // fallback
            }
            try
            {
                _wsUri = new Uri(wsUrl);
            }
            catch
            {
                _wsUri = new Uri("ws://localhost:3000/cable");
            }

            _wsClient = new WebSocketClient(_wsUri, GetAccessTokenAsync, async () => await _auth.RefreshTokensAsync());
            _wsClient.ConnectionStatusChanged += OnConnectionStatusChanged;
            _wsClient.MessageReceived += OnMessageReceived;
            _wsClient.InfoMessage += OnInfoMessage;

            // initialize command handling
            _cmdParser = new CommandJsonParser();
            _screenService = new ScreenService();
            _powerService = new PowerService(new TerminalService());
            _dispatcher = new CommandDispatcher(_cmdParser, new KeyboardService(), new MouseService(), new TerminalService(), _screenService, _powerService, async (msg) => { try { await _wsClient.SendAsync(msg); } catch { } });

            _trayManager = new TrayIconManager(this);

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
            });
        }

        private void OnMessageReceived(string text)
        {
            Debug.WriteLine($"Received: {text}");
            try
            {
                using var doc = JsonDocument.Parse(text);

                if (doc.RootElement.TryGetProperty("message", out var message))
                {
                    if (message.ValueKind == JsonValueKind.Object)
                    {
                        var from = "unknown";
                        if (message.TryGetProperty("from", out var fromProp) && fromProp.ValueKind == JsonValueKind.String)
                            from = fromProp.GetString() ?? "unknown";

                        string command = "";
                        if (message.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.String)
                            command = cmdProp.GetString() ?? "";

                        string payloadRaw = "{}";
                        if (message.TryGetProperty("payload", out var payloadProp))
                            payloadRaw = payloadProp.GetRawText();

                        string? id = null;
                        if (message.TryGetProperty("id", out var idProp))
                        {
                            if (idProp.ValueKind == JsonValueKind.String)
                                id = idProp.GetString();
                            else if (idProp.ValueKind == JsonValueKind.Number)
                                id = idProp.GetRawText();
                        }

                        InMemoryLog.Add($"{DateTime.Now:HH:mm:ss} {from} {command} {payloadRaw}");

                        var capturedCommand = command;
                        var capturedPayloadRaw = payloadRaw;
                        var capturedId = id;

                        _ = Task.Run(async () =>
                        {
                            Debug.WriteLine($"Dispatching command: {capturedCommand} from {from} (id={capturedId})");
                            try
                            {
                                JsonDocument? payloadDoc = null;
                                try { payloadDoc = JsonDocument.Parse(capturedPayloadRaw); } catch (Exception ex) { Debug.WriteLine($"Invalid payload JSON: {ex.Message}"); }

                                var request = new BaseRequest { Id = capturedId, Command = capturedCommand, Payload = payloadDoc?.RootElement ?? default };
                                var responsePayload = await _dispatcher.HandleRequestAsync(request);
                                Debug.WriteLine($"Command {capturedCommand} completed. Response: {responsePayload}");

                                if (!string.IsNullOrEmpty(responsePayload))
                                {
                                    try
                                    {
                                        using var respDoc = JsonDocument.Parse(responsePayload);
                                        if (respDoc.RootElement.TryGetProperty("Status", out _))
                                        {
                                            var rpcData = new { command = "message", identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }), data = responsePayload };
                                            await _wsClient.SendObjectAsync(rpcData);
                                        }
                                        else
                                        {
                                            var channelData = new { command = "message", identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }), data = JsonSerializer.Serialize(respDoc.RootElement) };
                                            await _wsClient.SendObjectAsync(channelData);
                                        }
                                        Debug.WriteLine($"Sent response for command {capturedCommand}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Failed to send response: {ex.Message}");
                                    }
                                }

                                try { payloadDoc?.Dispose(); } catch { }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error dispatching command {capturedCommand}: {ex.Message}");
                            }
                        });

                        return;
                    }

                    InMemoryLog.Add($"{DateTime.Now:HH:mm:ss} server raw {text}");
                    return;
                }

                InMemoryLog.Add($"{DateTime.Now:HH:mm:ss} server raw {text}");
            }
            catch (Exception ex)
            {
                InMemoryLog.Add($"{DateTime.Now:HH:mm:ss} server error {ex.Message}");
            }
        }

        private void OnInfoMessage(string info)
        {
            InMemoryLog.Add($"{DateTime.Now:HH:mm:ss} system info {info}");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new SettingsWindow { Owner = this };
            wnd.ShowDialog();
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logsWindow == null || !_logsWindow.IsVisible)
            {
                _logsWindow = new LogsWindow { Owner = this };
                _logsWindow.Show();
            }
            else
            {
                _logsWindow.Activate();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _trayManager.HandleClosing(e);
            if (e.Cancel) return;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { _wsClient.Dispose(); } catch { }
            try { _auth.Dispose(); } catch { }
            try { _screenService.Dispose(); } catch { }
            try { _trayManager.Dispose(); } catch { }
        }
    }
}