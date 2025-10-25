using System.Diagnostics;
using System.Text.Json;

namespace recontrol_win
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

        // Router maps command type -> factory that builds a command from payload
        private readonly Dictionary<string, Func<JsonElement, IAppCommand>> _commandFactories;

        public CommandDispatcher(CommandJsonParser jsonParser, KeyboardService keyboardService, MouseService mouseService, TerminalService terminalService)
        {
            _jsonParser = jsonParser;
            _keyboardService = keyboardService;
            _mouseService = mouseService;
            _terminalService = terminalService;

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
            };
        }

        /// <summary>
        /// Handle a parsed request by creating and executing a command.
        /// Returns a JSON response string, or null if no response is needed.
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

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing request: {ex.Message}\n{ex.StackTrace}");
                if (request?.Id != null)
                {
                    return _jsonParser.SerializeError(request.Id, ex.Message);
                }
                return null;
            }
        }
    }
}
