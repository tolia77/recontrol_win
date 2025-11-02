using System;
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
        private readonly Uri _wsUri = new Uri(Environment.GetEnvironmentVariable("WS_BASE_URL") ?? "ws://localhost:3000/cable");

        // new: command parser/dispatcher
        private readonly CommandJsonParser _cmdParser;
        private readonly CommandDispatcher _dispatcher;
        private readonly ScreenService _screenService;
        private readonly PowerService _powerService;
        private readonly WebRTCClient _webrtc;

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
            _screenService = new ScreenService(); // This is still needed for your *old* screen.start command
            _powerService = new PowerService(new TerminalService());
            _dispatcher = new CommandDispatcher(_cmdParser, new KeyboardService(), new MouseService(), new TerminalService(), _screenService, _powerService, async (msg) => { try { await _wsClient.SendAsync(msg); } catch { } });

            // WebRTC helper: use ws send to relay signaling to Rails WebRtcChannel
            // --- THIS IS THE FIX ---
            _webrtc = new WebRTCClient(async (signal) =>
            {
                var data = new
                {
                    command = "message",
                    identifier = JsonSerializer.Serialize(new { channel = "WebRtcChannel" }),
                    data = JsonSerializer.Serialize(new { command = "signal", payload = signal })
                };
                await _wsClient.SendObjectAsync(data);
            }); // <-- No more _screenService argument
            // ---------------------

            _webrtc.Log += (m) => Debug.WriteLine($"WebRTC: {m}");

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

                // ActionCable wraps messages. If an identifier exists, we can route based on channel.
                if (doc.RootElement.TryGetProperty("identifier", out var identifierEl) && identifierEl.ValueKind == JsonValueKind.String)
                {
                    var identifierStr = identifierEl.GetString();
                    if (!string.IsNullOrEmpty(identifierStr))
                    {
                        try
                        {
                            using var idDoc = JsonDocument.Parse(identifierStr);
                            var chan = idDoc.RootElement.TryGetProperty("channel", out var chEl) ? chEl.GetString() : null;
                            if (chan == "WebRtcChannel")
                            {
                                // Signaling messages will be in message.payload
                                if (doc.RootElement.TryGetProperty("message", out var rtcMsg) && rtcMsg.ValueKind == JsonValueKind.Object)
                                {
                                    if (rtcMsg.TryGetProperty("payload", out var payload))
                                    {
                                        _ = _webrtc.HandleSignalAsync(payload);
                                        AddMessage("webrtc", "signal", new { raw = payload.GetRawText() });
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

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

                        // show the message in UI (display raw payload text)
                        AddMessage(from, command, new { raw = payloadRaw });

                        // Handle local webrtc.start/stop convenience commands
                        if (command == "webrtc.start")
                        {
                            _ = _webrtc.StartAsOffererAsync();
                            return;
                        }
                        else if (command == "webrtc.stop")
                        {
                            _webrtc.StopStreaming();
                            return;
                        }

                        // Dispatch command in background to avoid blocking receive loop
                        _ = Task.Run(async () =>
                        {
                            Debug.WriteLine($"Dispatching command: {command} from {from}");
                            try
                            {
                                using var payloadDoc = JsonDocument.Parse(payloadRaw);
                                var request = new BaseRequest
                                {
                                    Command = command,
                                    Payload = payloadDoc.RootElement
                                };

                                var response = await _dispatcher.HandleRequestAsync(request);
                                Debug.WriteLine($"Command {command} completed. Response: {response}");

                                if (!string.IsNullOrEmpty(response))
                                {
                                    try
                                    {
                                        await _wsClient.SendAsync(response);
                                        Debug.WriteLine($"Sent response for command {command}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Failed to send response: {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error dispatching command {command}: {ex.Message}");
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { _wsClient.Dispose(); } catch { }
            try { _auth.Dispose(); } catch { }
            try { _screenService.Dispose(); } catch { }
            try { _webrtc.Dispose(); } catch { }
        }
    }
}

