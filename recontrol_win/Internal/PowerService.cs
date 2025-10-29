using System.Threading.Tasks;

namespace recontrol_win.Internal
{
    internal class PowerService
    {
        private readonly TerminalService _terminalService;

        public PowerService(TerminalService terminalService)
        {
            _terminalService = terminalService;
        }

        /// <summary>
        /// Shut down the computer immediately.
        /// </summary>
        public Task<string> ShutdownAsync()
        {
            return Task.FromResult(_terminalService.Execute("shutdown /s /t 0"));
        }

        /// <summary>
        /// Restart the computer immediately.
        /// </summary>
        public Task<string> RestartAsync()
        {
            return Task.FromResult(_terminalService.Execute("shutdown /r /t 0"));
        }

        /// <summary>
        /// Put the computer to sleep.
        /// </summary>
        public Task<string> SleepAsync()
        {
            return Task.FromResult(_terminalService.Execute("rundll32.exe powrprof.dll,SetSuspendState 0,1,0"));
        }

        /// <summary>
        /// Hibernate the computer.
        /// </summary>
        public Task<string> HibernateAsync()
        {
            return Task.FromResult(_terminalService.Execute("rundll32.exe powrprof.dll,SetSuspendState"));
        }

        /// <summary>
        /// Log off the current user.
        /// </summary>
        public Task<string> LogOffAsync()
        {
            return Task.FromResult(_terminalService.Execute("shutdown /l"));
        }

        /// <summary>
        /// Lock the workstation.
        /// </summary>
        public Task<string> LockAsync()
        {
            return Task.FromResult(_terminalService.Execute("rundll32.exe user32.dll,LockWorkStation"));
        }
    }
}