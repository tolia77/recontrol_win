using System.Text.Json;
using System.Text.Json.Serialization;

namespace recontrol_win
{
    /// <summary>
    /// Handles parsing JSON requests, dispatching them to the correct service,
    /// and serializing the response.
    /// </summary>
    internal class CommandDispatcher
    {
        private readonly KeyboardService _keyboardService;
        private readonly MouseService _mouseService;
        private readonly TerminalService _terminalService;

        private readonly JsonSerializerOptions _jsonOptions;

        // This dictionary is the core router. It maps a "type" string
        // to an async function that takes a JsonElement (the payload)
        // and returns the result as an object.
        private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _commandHandlers;

        public CommandDispatcher(KeyboardService keyboardService, MouseService mouseService, TerminalService terminalService)
        {
            _keyboardService = keyboardService;
            _mouseService = mouseService;
            _terminalService = terminalService;

            // Configure JSON options for case-insensitivity and enum conversion
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());

            // Initialize the command handler mapping
            _commandHandlers = new Dictionary<string, Func<JsonElement, Task<object?>>>
            {
                // KeyboardService
                { "keyboard.keyDown", async (payload) => {
                    var args = Deserialize<KeyPayload>(payload);
                    await Task.Run(() => _keyboardService.KeyDown(args.Key));
                    return null; // 'void' methods return null
                }},
                { "keyboard.keyUp", async (payload) => {
                    var args = Deserialize<KeyPayload>(payload);
                    await Task.Run(() => _keyboardService.KeyUp(args.Key));
                    return null;
                }},
                { "keyboard.press", async (payload) => {
                    var args = Deserialize<KeyPressPayload>(payload);
                    await Task.Run(() => _keyboardService.Press(args.Key, args.HoldMs));
                    return null;
                }},

                // MouseService
                { "mouse.move", async (payload) => {
                    var args = Deserialize<MouseMovePayload>(payload);
                    await Task.Run(() => _mouseService.MoveMouse(args.DeltaX, args.DeltaY));
                    return null;
                }},
                { "mouse.down", async (payload) => {
                    var args = Deserialize<MouseButtonPayload>(payload);
                    await Task.Run(() => _mouseService.MouseDown(args.Button));
                    return null;
                }},
                { "mouse.up", async (payload) => {
                    var args = Deserialize<MouseButtonPayload>(payload);
                    await Task.Run(() => _mouseService.MouseUp(args.Button));
                    return null;
                }},
                { "mouse.scroll", async (payload) => {
                    var args = Deserialize<MouseScrollPayload>(payload);
                    await Task.Run(() => _mouseService.Scroll(args.Clicks));
                    return null;
                }},
                { "mouse.click", async (payload) => {
                    var args = Deserialize<MouseClickPayload>(payload);
                    await Task.Run(() => _mouseService.Click(args.Button, args.DelayMs));
                    return null;
                }},
                { "mouse.doubleClick", async (payload) => {
                    var args = Deserialize<MouseDoubleClickPayload>(payload);
                    await Task.Run(() => _mouseService.DoubleClick(args.DelayMs));
                    return null;
                }},
                { "mouse.rightClick", async (payload) => {
                    await Task.Run(() => _mouseService.RightClick());
                    return null;
                }},

                // TerminalService
                { "terminal.execute", (payload) => {
                    var args = Deserialize<TerminalCommandPayload>(payload);
                    // Wrap synchronous, blocking calls in Task.Run to avoid blocking the network listener
                    return RunSync(() => _terminalService.Execute(args.Command, args.Timeout));
                }},
                { "terminal.powershell", (payload) => {
                    var args = Deserialize<TerminalCommandPayload>(payload);
                    return RunSync(() => _terminalService.PowerShell(args.Command, args.Timeout));
                }},
                { "terminal.listProcesses", (payload) => {
                    return RunSync(() => _terminalService.ListProcesses());
                }},
                { "terminal.killProcess", (payload) => {
                    var args = Deserialize<TerminalKillPayload>(payload);
                    return RunSync(() => _terminalService.KillProcess(args.Pid, args.Force));
                }},
                { "terminal.startProcess", (payload) => {
                    var args = Deserialize<TerminalStartPayload>(payload);
                    return RunSync(() => _terminalService.StartProcess(args.FileName, args.Arguments, args.RedirectOutput));
                }},
                { "terminal.getCwd", (payload) => {
                    return RunSync(() => _terminalService.GetCwd());
                }},
                { "terminal.setCwd", async (payload) => {
                    var args = Deserialize<TerminalSetCwdPayload>(payload);
                    await Task.Run(() => _terminalService.SetCwd(args.Path));
                    return null;
                }},
                { "terminal.whoAmI", (payload) => {
                    return RunSync(() => _terminalService.WhoAmI());
                }},
                { "terminal.getUptime", (payload) => {
                    return RunSync(() => _terminalService.GetUptime());
                }},
                { "terminal.abort", async (payload) => {
                    await Task.Run(() => _terminalService.Abort());
                    return null;
                }},
            };
        }

        /// <summary>
        /// Main entry point for handling an incoming JSON request string.
        /// Returns a JSON response string, or null if no response is needed.
        /// </summary>
        public async Task<string?> HandleRequestAsync(string jsonRequest)
        {
            BaseRequest? request = null;
            try
            {
                // 1. Deserialize the base request to get ID, Type, and Payload
                request = JsonSerializer.Deserialize<BaseRequest>(jsonRequest, _jsonOptions);
                if (request == null || string.IsNullOrEmpty(request.Type))
                {
                    throw new InvalidOperationException("Invalid request object or missing 'type'.");
                }

                // 2. Find the correct handler for the 'type'
                if (!_commandHandlers.TryGetValue(request.Type, out var handler))
                {
                    throw new NotSupportedException($"Command type '{request.Type}' is not supported.");
                }

                // 3. Execute the handler
                // The handler itself is responsible for parsing the 'Payload' property
                object? result = await handler(request.Payload);

                // 4. If an 'id' was provided, format a success response
                if (request.Id != null)
                {
                    var response = new SuccessResponse(request.Id, result);
                    return JsonSerializer.Serialize(response, _jsonOptions);
                }

                return null; // No 'id', no response needed
            }
            catch (Exception ex)
            {
                // 5. If anything fails, format an error response (if an 'id' was present)
                Console.WriteLine($"Error processing request: {ex.Message}\n{ex.StackTrace}");
                if (request?.Id != null)
                {
                    var response = new ErrorResponse(request.Id, ex.Message);
                    return JsonSerializer.Serialize(response, _jsonOptions);
                }

                return null; // Error, but no 'id' to send the error back to
            }
        }

        /// <summary>
        /// Helper to deserialize a payload from a JsonElement.
        /// </summary>
        private T Deserialize<T>(JsonElement payload)
        {
            var args = payload.Deserialize<T>(_jsonOptions);
            if (args == null)
            {
                throw new InvalidOperationException($"Invalid payload for command. Could not deserialize to {typeof(T).Name}.");
            }
            return args;
        }

        /// <summary>
        /// Helper to wrap synchronous functions with a return value in a Task.
        /// </summary>
        private async Task<object?> RunSync<T>(Func<T> func)
        {
            T result = await Task.Run(func);
            return result;
        }
    }
}
