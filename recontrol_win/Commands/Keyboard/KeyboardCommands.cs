using System.Threading.Tasks;

namespace recontrol_win
{
    internal sealed class KeyDownCommand : IAppCommand
    {
        private readonly KeyboardService _service;
        private readonly KeyPayload _args;
        public KeyDownCommand(KeyboardService service, KeyPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.KeyDown(_args.Key); return Task.FromResult<object?>(null); }
    }

    internal sealed class KeyUpCommand : IAppCommand
    {
        private readonly KeyboardService _service; private readonly KeyPayload _args;
        public KeyUpCommand(KeyboardService service, KeyPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.KeyUp(_args.Key); return Task.FromResult<object?>(null); }
    }

    internal sealed class KeyPressCommand : IAppCommand
    {
        private readonly KeyboardService _service; private readonly KeyPressPayload _args;
        public KeyPressCommand(KeyboardService service, KeyPressPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.Press(_args.Key, _args.HoldMs); return Task.FromResult<object?>(null); }
    }
}
