using recontrol_win.Internal;
using System.Threading.Tasks;

namespace recontrol_win.Commands
{
    internal sealed class PowerShutdownCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerShutdownCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.ShutdownAsync();
    }

    internal sealed class PowerRestartCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerRestartCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.RestartAsync();
    }

    internal sealed class PowerSleepCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerSleepCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.SleepAsync();
    }

    internal sealed class PowerHibernateCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerHibernateCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.HibernateAsync();
    }

    internal sealed class PowerLogOffCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerLogOffCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.LogOffAsync();
    }

    internal sealed class PowerLockCommand : IAppCommand
    {
        private readonly PowerService _service;
        public PowerLockCommand(PowerService service) { _service = service; }
        public async Task<object?> ExecuteAsync() => await _service.LockAsync();
    }
}