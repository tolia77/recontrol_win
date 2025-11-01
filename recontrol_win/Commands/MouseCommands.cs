using recontrol_win.Internal;
using recontrol_win.Tools;
using System.Threading.Tasks;

namespace recontrol_win.Commands
{
    internal sealed class MouseMoveCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseMovePayload _args;
        public MouseMoveCommand(MouseService service, MouseMovePayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.MoveMouseTo(_args.X, _args.Y); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseDownCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseButtonPayload _args;
        public MouseDownCommand(MouseService service, MouseButtonPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.MouseDown(_args.Button); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseUpCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseButtonPayload _args;
        public MouseUpCommand(MouseService service, MouseButtonPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.MouseUp(_args.Button); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseScrollCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseScrollPayload _args;
        public MouseScrollCommand(MouseService service, MouseScrollPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.Scroll(_args.Clicks); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseClickCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseClickPayload _args;
        public MouseClickCommand(MouseService service, MouseClickPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.Click(_args.Button, _args.DelayMs); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseDoubleClickCommand : IAppCommand
    {
        private readonly MouseService _service; private readonly MouseDoubleClickPayload _args;
        public MouseDoubleClickCommand(MouseService service, MouseDoubleClickPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.DoubleClick(_args.DelayMs); return Task.FromResult<object?>(null); }
    }

    internal sealed class MouseRightClickCommand : IAppCommand
    {
        private readonly MouseService _service;
        public MouseRightClickCommand(MouseService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ _service.RightClick(); return Task.FromResult<object?>(null); }
    }
}
