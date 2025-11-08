using recontrol_win.Internal;
using System.Text.Json;
using System.Security.Cryptography;

namespace recontrol_win.Commands
{
    internal sealed class ScreenStartCommand : IAppCommand
    {
        private readonly ScreenService _service;
        private readonly Func<string, Task> _sender;

        public ScreenStartCommand(ScreenService service, Func<string, Task> sender)
        {
            _service = service;
            _sender = sender;
        }

        public Task<object?> ExecuteAsync()
        {
            if (_service.IsRunning)
            {
                return Task.FromResult<object?>("already_running");
            }

            // Track previous frame hash to avoid sending identical frames.
            byte[]? previousHash = null;

            _service.Start(bytes =>
            {
                try
                {
                    // Compute hash of current frame (static helper avoids lifetime issues)
                    var currentHash = MD5.HashData(bytes);
                    if (previousHash != null && AreEqual(previousHash, currentHash))
                    {
                        InternalLogger.Log("ScreenStartCommand: duplicate frame skipped");
                        return;
                    }
                    previousHash = currentHash;

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
                catch (Exception ex)
                {
                    InternalLogger.LogException("ScreenStartCommand.FrameSend", ex);
                }
            }, 30, 100); // hardcoded 30% quality, 100ms interval

            return Task.FromResult<object?>("started");
        }

        private static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
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
