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
        private readonly Uri? _wsUri = Uri.TryCreate(Environment.GetEnvironmentVariable("WS_URL"), UriKind.Absolute, out var uri) ? uri : null;

        // new: command parser/dispatcher
        private readonly CommandJsonParser _cmdParser;
        private readonly CommandDispatcher _dispatcher;
        private readonly ScreenService _screenService;
        private readonly PowerService _powerService;

        // Tray manager
        private readonly TrayIconManager _trayManager;

        public MainWindow()
        {
            InitializeComponent();

            SendPingButton.IsEnabled = false;

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

                        string payloadRaw = "{}";
                        if (message.TryGetProperty("payload", out var payloadProp))
                            payloadRaw = payloadProp.GetRawText();

                        // Capture optional message id (string or number) for correlation BEFORE scheduling async work
                        string? id = null;
                        if (message.TryGetProperty("id", out var idProp))
                        {
                            if (idProp.ValueKind == JsonValueKind.String)
                                id = idProp.GetString();
                            else if (idProp.ValueKind == JsonValueKind.Number)
                                id = idProp.GetRawText();
                        }

                        // show the message in UI (display raw payload text)
                        AddMessage(from, command, new { raw = payloadRaw });

                        // Dispatch command in background to avoid blocking receive loop
                        var capturedCommand = command;
                        var capturedPayloadRaw = payloadRaw;
                        var capturedId = id;

                        _ = Task.Run(async () =>
                        {
                            Debug.WriteLine($"Dispatching command: {capturedCommand} from {from} (id={capturedId})");
                            try
                            {
                                JsonDocument? payloadDoc = null;
                                try
                                {
                                    payloadDoc = JsonDocument.Parse(capturedPayloadRaw);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Invalid payload JSON: {ex.Message}");
                                }

                                var request = new BaseRequest
                                {
                                    Id = capturedId,
                                    Command = capturedCommand,
                                    Payload = payloadDoc?.RootElement ?? default
                                };

                                var responsePayload = await _dispatcher.HandleRequestAsync(request);
                                Debug.WriteLine($"Command {capturedCommand} completed. Response: {responsePayload}");

                                if (!string.IsNullOrEmpty(responsePayload))
                                {
                                    try
                                    {
                                        using var respDoc = JsonDocument.Parse(responsePayload);
                                        if (respDoc.RootElement.TryGetProperty("Status", out _))
                                        {
                                            var rpcData = new
                                            {
                                                command = "message",
                                                identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }),
                                                data = responsePayload
                                            };
                                            await _wsClient.SendObjectAsync(rpcData);
                                        }
                                        else
                                        {
                                            var channelData = new
                                            {
                                                command = "message",
                                                identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }),
                                                data = JsonSerializer.Serialize(respDoc.RootElement)
                                            };
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