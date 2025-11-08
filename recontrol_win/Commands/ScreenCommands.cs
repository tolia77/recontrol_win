using recontrol_win.Internal;
using System.Text.Json;
using System.Security.Cryptography;
using System.Buffers;

namespace recontrol_win.Commands
{
    internal sealed class ScreenStartCommand : IAppCommand
    {
        private readonly ScreenService _service;
        private readonly Func<string, Task> _sender;

        // track last sent hash to avoid duplicates
        private ulong? _previousHash;

        // simple backpressure counter
        private int _pendingSends = 0;

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

            _previousHash = null;

            _service.Start(batch =>
            {
                try
                {
                    if (batch == null || batch.Regions == null || batch.Regions.Count == 0)
                        return;

                    // Compute combined hash for the batch
                    var currentHash = ComputeCombinedHash(batch.Regions);

                    if (_previousHash != null && _previousHash.Value == currentHash)
                    {
                        InternalLogger.Log("ScreenStartCommand: duplicate batch skipped");
                        return;
                    }

                    _previousHash = currentHash;

                    // Build payload with all regions in a single message
                    var regionList = batch.Regions.Select(r => new
                    {
                        image = Convert.ToBase64String(r.Jpeg),
                        isFull = r.IsFullFrame,
                        x = r.X,
                        y = r.Y,
                        width = r.Width,
                        height = r.Height
                    }).ToList();

                    var payloadObj = new
                    {
                        command = "screen.frame_batch",
                        payload = new
                        {
                            regions = regionList
                        }
                    };

                    var dataObj = new
                    {
                        command = "message",
                        identifier = JsonSerializer.Serialize(new { channel = "CommandChannel" }),
                        data = JsonSerializer.Serialize(payloadObj)
                    };

                    Interlocked.Increment(ref _pendingSends);
                    var sendTask = _sender(JsonSerializer.Serialize(dataObj));
                    sendTask.ContinueWith(t => Interlocked.Decrement(ref _pendingSends));

                    if (_pendingSends > 4)
                    {
                        InternalLogger.Log($"ScreenStartCommand: high pending sends={_pendingSends}, applying backpressure");
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.LogException("ScreenStartCommand.FrameSend", ex);
                }
            }, qualityPercent: 30, intervalMs: 100);

            return Task.FromResult<object?>("started");
        }

        private static ulong ComputeFnv1A64(byte[] data)
        {
            const ulong fnvOffset = 1469598103934665603UL;
            const ulong fnvPrime = 1099511628211UL;
            ulong hash = fnvOffset;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= fnvPrime;
            }
            return hash;
        }

        private static ulong ComputeCombinedHash(List<FrameRegion> regions)
        {
            const ulong fnvOffset = 1469598103934665603UL;
            const ulong fnvPrime = 1099511628211UL;
            ulong combined = fnvOffset;
            foreach (var r in regions)
            {
                // mix image bytes
                var h = ComputeFnv1A64(r.Jpeg);
                combined ^= h;
                combined *= fnvPrime;

                // mix metadata
                combined ^= (uint)r.X;
                combined *= fnvPrime;
                combined ^= (uint)r.Y;
                combined *= fnvPrime;
                combined ^= (uint)r.Width;
                combined *= fnvPrime;
                combined ^= (uint)r.Height;
                combined *= fnvPrime;
                combined ^= r.IsFullFrame ? 1UL : 0UL;
                combined *= fnvPrime;
            }
            return combined;
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
