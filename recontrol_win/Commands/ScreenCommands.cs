using recontrol_win.Internal;
using recontrol_win.Tools;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace recontrol_win.Commands
{
    internal class ScreenStartPayload
    {
        public int Quality { get; set; } = 30;
        public int IntervalMs { get; set; } = 200;
    }

    internal sealed class ScreenStartCommand : IAppCommand
    {
        private readonly ScreenService _service;
        private readonly ScreenStartPayload _args;
        private readonly Func<string, Task> _sender;

        public ScreenStartCommand(ScreenService service, ScreenStartPayload args, Func<string, Task> sender)
        {
            _service = service;
            _args = args;
            _sender = sender;
        }

        public Task<object?> ExecuteAsync()
        {
            if (_service.IsRunning)
            {
                return Task.FromResult<object?>("already_running");
            }

            _service.Start(bytes =>
            {
                try
                {
                    // send frame as base64 JSON message without awaiting
                    var payload = new
                    {
                        command = "screen.frame",
                        payload = new
                        {
                            image = Convert.ToBase64String(bytes)
                        }
                    };
                    var data = new
                    {
                        command = "message",
                        identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }),
                        data = JsonSerializer.Serialize(payload)
                    };
                    var json = JsonSerializer.Serialize(data);
                    _ = _sender(json);
                }
                catch { }
            }, _args.Quality, _args.IntervalMs);

            return Task.FromResult<object?>("started");
        }
    }

    internal sealed class ScreenStopCommand : IAppCommand
    {
        private readonly ScreenService _service;
        public ScreenStopCommand(ScreenService service) { _service = service; }
        public Task<object?> ExecuteAsync()
        {
            _service.Stop();
            return Task.FromResult<object?>("stopped");
        }
    }
}
