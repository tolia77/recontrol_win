using System;
using System.IO;
using System.Text;

namespace recontrol_win.Internal
{
    internal static class InternalLogger
    {
        private static readonly object _sync = new object();
        private static readonly string _logPath;

        static InternalLogger()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "recontrol_win");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "internal.log");
            }
            catch
            {
                _logPath = Path.Combine(Path.GetTempPath(), "recontrol_win_internal.log");
            }
        }

        public static void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
                lock (_sync)
                {
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // swallow logging errors to avoid impacting application behavior
            }
        }

        public static void LogException(string context, Exception ex)
        {
            try
            {
                var line = $"[{DateTime.UtcNow:O}] EXCEPTION in {context}: {ex}\n";
                lock (_sync)
                {
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
