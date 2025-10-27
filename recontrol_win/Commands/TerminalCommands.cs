using recontrol_win.Internal;
using recontrol_win.Tools;
using System.Threading.Tasks;

namespace recontrol_win.Commands
{
    internal sealed class TerminalExecuteCommand : IAppCommand
    {
        private readonly TerminalService _service; private readonly TerminalCommandPayload _args;
        public TerminalExecuteCommand(TerminalService service, TerminalCommandPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.Execute(_args.Command, _args.Timeout)); }
    }

    internal sealed class TerminalPowerShellCommand : IAppCommand
    {
        private readonly TerminalService _service; private readonly TerminalCommandPayload _args;
        public TerminalPowerShellCommand(TerminalService service, TerminalCommandPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.PowerShell(_args.Command, _args.Timeout)); }
    }

    internal sealed class TerminalListProcessesCommand : IAppCommand
    {
        private readonly TerminalService _service; public TerminalListProcessesCommand(TerminalService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.ListProcesses()); }
    }

    internal sealed class TerminalKillProcessCommand : IAppCommand
    {
        private readonly TerminalService _service; private readonly TerminalKillPayload _args;
        public TerminalKillProcessCommand(TerminalService service, TerminalKillPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.KillProcess(_args.Pid, _args.Force)); }
    }

    internal sealed class TerminalStartProcessCommand : IAppCommand
    {
        private readonly TerminalService _service; private readonly TerminalStartPayload _args;
        public TerminalStartProcessCommand(TerminalService service, TerminalStartPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.StartProcess(_args.FileName, _args.Arguments, _args.RedirectOutput)); }
    }

    internal sealed class TerminalGetCwdCommand : IAppCommand
    {
        private readonly TerminalService _service; public TerminalGetCwdCommand(TerminalService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.GetCwd()); }
    }

    internal sealed class TerminalSetCwdCommand : IAppCommand
    {
        private readonly TerminalService _service; private readonly TerminalSetCwdPayload _args;
        public TerminalSetCwdCommand(TerminalService service, TerminalSetCwdPayload args){ _service = service; _args = args; }
        public Task<object?> ExecuteAsync(){ _service.SetCwd(_args.Path); return Task.FromResult<object?>(null); }
    }

    internal sealed class TerminalWhoAmICommand : IAppCommand
    {
        private readonly TerminalService _service; public TerminalWhoAmICommand(TerminalService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.WhoAmI()); }
    }

    internal sealed class TerminalGetUptimeCommand : IAppCommand
    {
        private readonly TerminalService _service; public TerminalGetUptimeCommand(TerminalService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ return Task.FromResult<object?>(_service.GetUptime()); }
    }

    internal sealed class TerminalAbortCommand : IAppCommand
    {
        private readonly TerminalService _service; public TerminalAbortCommand(TerminalService service){ _service = service; }
        public Task<object?> ExecuteAsync(){ _service.Abort(); return Task.FromResult<object?>(null); }
    }
}
