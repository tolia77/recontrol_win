using recontrol_win.Commands;
using recontrol_win.Internal;
using System.Diagnostics;
using System.Text.Json;

namespace recontrol_win.Tools
{
    /// <summary>
    /// Dispatches parsed commands to dedicated command classes.
    /// </summary>
    internal class CommandDispatcher
    {
        private readonly KeyboardService _keyboardService;
        private readonly MouseService _mouseService;
        private readonly TerminalService _terminalService;
        private readonly CommandJsonParser _jsonParser;
        private readonly ScreenService _screenService;
        private readonly PowerService _powerService;
        private readonly Func<string, Task> _sender;

        private readonly Dictionary<string, Func<JsonElement, IAppCommand>> _commandFactories;

        public CommandDispatcher(CommandJsonParser jsonParser, KeyboardService keyboardService, MouseService mouseService, TerminalService terminalService, ScreenService screenService, PowerService powerService, Func<string, Task> sender)
        {
            _jsonParser = jsonParser;
            _keyboardService = keyboardService;
            _mouseService = mouseService;
            _terminalService = terminalService;
            _screenService = screenService;
            _powerService = powerService;
            _sender = sender;

            _commandFactories = new Dictionary<string, Func<JsonElement, IAppCommand>>
            {
                // KeyboardService
                { "keyboard.keyDown", payload => {
                    var args = _jsonParser.DeserializePayload<KeyPayload>(payload);
                    return new KeyDownCommand(_keyboardService, args);
                }},
                { "keyboard.keyUp", payload => {
                    var args = _jsonParser.DeserializePayload<KeyPayload>(payload);
                    return new KeyUpCommand(_keyboardService, args);
                }},
                { "keyboard.press", payload => {
                    var args = _jsonParser.DeserializePayload<KeyPressPayload>(payload);
                    return new KeyPressCommand(_keyboardService, args);
                }},

                // MouseService
                { "mouse.move", payload => {
                    var args = _jsonParser.DeserializePayload<MouseMovePayload>(payload);
                    return new MouseMoveCommand(_mouseService, args);
                }},
                { "mouse.down", payload => {
                    var args = _jsonParser.DeserializePayload<MouseButtonPayload>(payload);
                    return new MouseDownCommand(_mouseService, args);
                }},
                { "mouse.up", payload => {
                    var args = _jsonParser.DeserializePayload<MouseButtonPayload>(payload);
                    return new MouseUpCommand(_mouseService, args);
                }},
                { "mouse.scroll", payload => {
                    var args = _jsonParser.DeserializePayload<MouseScrollPayload>(payload);
                    return new MouseScrollCommand(_mouseService, args);
                }},
                { "mouse.click", payload => {
                    var args = _jsonParser.DeserializePayload<MouseClickPayload>(payload);
                    return new MouseClickCommand(_mouseService, args);
                }},
                { "mouse.doubleClick", payload => {
                    var args = _jsonParser.DeserializePayload<MouseDoubleClickPayload>(payload);
                    return new MouseDoubleClickCommand(_mouseService, args);
                }},
                { "mouse.rightClick", payload => new MouseRightClickCommand(_mouseService) },

                // TerminalService
                { "terminal.execute", payload => {
                    var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(payload);
                    return new TerminalExecuteCommand(_terminalService, args);
                }},
                { "terminal.powershell", payload => {
                    var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(payload);
                    return new TerminalPowerShellCommand(_terminalService, args);
                }},
                { "terminal.listProcesses", payload => new TerminalListProcessesCommand(_terminalService) },
                { "terminal.killProcess", payload => {
                    var args = _jsonParser.DeserializePayload<TerminalKillPayload>(payload);
                    return new TerminalKillProcessCommand(_terminalService, args);
                }},
                { "terminal.startProcess", payload => {
                    var args = _jsonParser.DeserializePayload<TerminalStartPayload>(payload);
                    return new TerminalStartProcessCommand(_terminalService, args);
                }},
                { "terminal.getCwd", payload => new TerminalGetCwdCommand(_terminalService) },
                { "terminal.setCwd", payload => {
                    var args = _jsonParser.DeserializePayload<TerminalSetCwdPayload>(payload);
                    return new TerminalSetCwdCommand(_terminalService, args);
                }},
                { "terminal.whoAmI", payload => new TerminalWhoAmICommand(_terminalService) },
                { "terminal.getUptime", payload => new TerminalGetUptimeCommand(_terminalService) },
                { "terminal.abort", payload => new TerminalAbortCommand(_terminalService) },

                // Screen capture / streaming
                { "screen.start", payload => new ScreenStartCommand(_screenService, _sender) },
                { "screen.stop", payload => new ScreenStopCommand(_screenService) },

                // PowerService
                { "power.shutdown", payload => new PowerShutdownCommand(_powerService) },
                { "power.restart", payload => new PowerRestartCommand(_powerService) },
                { "power.sleep", payload => new PowerSleepCommand(_powerService) },
                { "power.hibernate", payload => new PowerHibernateCommand(_powerService) },
                { "power.logOff", payload => new PowerLogOffCommand(_powerService) },
                { "power.lock", payload => new PowerLockCommand(_powerService) }
            };
        }

        /// <summary>
        /// Handle a parsed request by creating and executing a command.
        /// Returns a JSON response payload string to be sent to the server.
        /// - If request.Id is provided, returns an RPC-style success/error envelope with lowercase keys.
        /// - Otherwise, returns a channel payload like { command: "result", id: "...", request: "...", payload: ... }.
        /// </summary>
        public async Task<string?> HandleRequestAsync(BaseRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.Command))
                    throw new InvalidOperationException("Invalid request object or missing 'type'.");

                if (!_commandFactories.TryGetValue(request.Command, out var factory))
                    throw new NotSupportedException($"Command type '{request.Command}' is not supported.");
                Debug.WriteLine($"Executing {request.Command}");
                var command = factory(request.Payload);
                var result = await command.ExecuteAsync();

                if (request.Id != null)
                {
                    return _jsonParser.SerializeSuccess(request.Id, result);
                }

                var channelPayload = new { command = "result", id = request.Id, request = request.Command, payload = result };
                return JsonSerializer.Serialize(channelPayload);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing request: {ex.Message}\n{ex.StackTrace}");
                if (request?.Id != null)
                {
                    return _jsonParser.SerializeError(request.Id, ex.Message);
                }
                var errorPayload = new { command = "error", id = request?.Id, request = request?.Command ?? string.Empty, payload = ex.Message };
                return JsonSerializer.Serialize(errorPayload);
            }
        }
    }
}
