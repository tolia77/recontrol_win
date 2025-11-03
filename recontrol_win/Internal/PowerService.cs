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
            InternalLogger.Log("PowerService.ShutdownAsync called");
            return Task.FromResult(_terminalService.Execute("shutdown /s /t 0"));
        }

        /// <summary>
        /// Restart the computer immediately.
        /// </summary>
        public Task<string> RestartAsync()
        {
            InternalLogger.Log("PowerService.RestartAsync called");
            return Task.FromResult(_terminalService.Execute("shutdown /r /t 0"));
        }

        /// <summary>
        /// Put the computer to sleep.
        /// </summary>
        public Task<string> SleepAsync()
        {
            InternalLogger.Log("PowerService.SleepAsync called");
            return Task.FromResult(_terminalService.Execute("rundll32.exe powrprof.dll,SetSuspendState 0,1,0"));
        }

        /// <summary>
        /// Hibernate the computer.
        /// </summary>
        public Task<string> HibernateAsync()
        {
            InternalLogger.Log("PowerService.HibernateAsync called");
            return Task.FromResult(_terminalService.Execute("rundll32.exe powrprof.dll,SetSuspendState"));
        }

        /// <summary>
        /// Log off the current user.
        /// </summary>
        public Task<string> LogOffAsync()
        {
            InternalLogger.Log("PowerService.LogOffAsync called");
            return Task.FromResult(_terminalService.Execute("shutdown /l"));
        }

        /// <summary>
        /// Lock the workstation.
        /// </summary>
        public Task<string> LockAsync()
        {
            InternalLogger.Log("PowerService.LockAsync called");
            return Task.FromResult(_terminalService.Execute("rundll32.exe user32.dll,LockWorkStation"));
        }
    }
}