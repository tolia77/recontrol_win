using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace recontrol_win.Internal
{
    internal enum TerminalType
    {
        Cmd,
        PowerShell,
        Bash
    }

    internal class ProcessInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public long MemoryMB { get; set; }
        public TimeSpan CpuTime { get; set; }
        public DateTime StartTime { get; set; }
    }

    internal class TerminalService
    {
        private string _workingDirectory = Environment.CurrentDirectory;
        private CancellationTokenSource? _abortTokenSource;

        // ==================== Basic Execution ====================
        
        public string Execute(string command, int timeoutMs = 30000)
        {
            InternalLogger.Log($"TerminalService.Execute called: command={command}, timeoutMs={timeoutMs}");
            EnsureWindows();
            return ExecuteInternal("cmd.exe", $"/C {command}", timeoutMs);
        }

        public string Shell(string command, int timeoutMs = 30000)
        {
            InternalLogger.Log($"TerminalService.Shell called: command={command}, timeoutMs={timeoutMs}");
            EnsureWindows();
            if (OperatingSystem.IsWindows())
                return ExecuteInternal("cmd.exe", $"/C {command}", timeoutMs);
            else
                return ExecuteInternal("/bin/bash", $"-c \"{command}\"", timeoutMs);
        }

        public string PowerShell(string script, int timeoutMs = 30000)
        {
            InternalLogger.Log($"TerminalService.PowerShell called: script={script}, timeoutMs={timeoutMs}");
            EnsureWindows();
            return ExecuteInternal("powershell.exe", $"-NoProfile -Command \"{script}\"", timeoutMs);
        }

        public string RunAs(string command)
        {
            InternalLogger.Log($"TerminalService.RunAs called: command={command}");
            EnsureWindows();
            if (!IsAdministrator())
                throw new UnauthorizedAccessException("This operation requires administrator privileges. Run as admin.");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {command}",
                Verb = "runas",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            using var process = Process.Start(psi);
            if (process == null) throw new InvalidOperationException("Failed to start elevated process.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return string.IsNullOrEmpty(error) ? output : $"{output}\n[ERROR]\n{error}";
        }

        // ==================== Process Management ====================

        public List<ProcessInfo> ListProcesses()
        {
            InternalLogger.Log("TerminalService.ListProcesses called");
            return Process.GetProcesses()
                .Select(p => {
                    try
                    {
                        return new ProcessInfo
                        {
                            Pid = p.Id,
                            Name = p.ProcessName,
                            MemoryMB = p.WorkingSet64 / (1024 * 1024),
                            CpuTime = p.TotalProcessorTime,
                            StartTime = p.StartTime
                        };
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.LogException("TerminalService.ListProcesses (process)", ex);
                        return null;
                    }
                })
                .Where(p => p != null)
                .ToList()!;
        }

        public ProcessInfo? GetProcessInfo(int pid)
        {
            try
            {
                InternalLogger.Log($"TerminalService.GetProcessInfo called: pid={pid}");
                var p = Process.GetProcessById(pid);
                return new ProcessInfo
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    MemoryMB = p.WorkingSet64 / (1024 * 1024),
                    CpuTime = p.TotalProcessorTime,
                    StartTime = p.StartTime
                };
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("TerminalService.GetProcessInfo", ex);
                return null;
            }
        }

        public bool KillProcess(int pid, bool force = false)
        {
            InternalLogger.Log($"TerminalService.KillProcess called: pid={pid}, force={force}");
            try
            {
                var process = Process.GetProcessById(pid);
                if (force)
                    process.Kill(true);
                else
                    process.Kill();
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("TerminalService.KillProcess", ex);
                return false;
            }
        }

        public int StartProcess(string fileName, string arguments = "", bool redirectOutput = false)
        {
            InternalLogger.Log($"TerminalService.StartProcess called: fileName={fileName}, arguments={arguments}, redirectOutput={redirectOutput}");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = !redirectOutput,
                CreateNoWindow = redirectOutput,
                WorkingDirectory = _workingDirectory
            };

            if (redirectOutput)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            var process = Process.Start(psi);
            return process?.Id ?? -1;
        }

        // ==================== Environment & System ====================

        public Dictionary<string, string> GetEnvironment()
        {
            InternalLogger.Log("TerminalService.GetEnvironment called");
            return Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString() ?? "");
        }

        public string GetCwd() => _workingDirectory;

        public void SetCwd(string path)
        {
            InternalLogger.Log($"TerminalService.SetCwd called: path={path}");
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Directory not found: {path}");
            _workingDirectory = Path.GetFullPath(path);
        }

        public string WhoAmI()
        {
            InternalLogger.Log("TerminalService.WhoAmI called");
            var identity = WindowsIdentity.GetCurrent();
            return $"{Environment.UserDomainName}\\{Environment.UserName} (Admin: {IsAdministrator()})";
        }

        public TimeSpan GetUptime()
        {
            InternalLogger.Log("TerminalService.GetUptime called");
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }

        // ==================== Control & Status ====================

        public void Abort()
        {
            InternalLogger.Log("TerminalService.Abort called");
            _abortTokenSource?.Cancel();
        }


        // ==================== Helpers ====================

        private string ExecuteInternal(string fileName, string arguments, int timeoutMs)
        {
            InternalLogger.Log($"TerminalService.ExecuteInternal called: fileName={fileName}, arguments={arguments}, timeoutMs={timeoutMs}");
            _abortTokenSource?.Dispose();
            _abortTokenSource = new CancellationTokenSource();
            var token = _abortTokenSource.Token;

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null) throw new InvalidOperationException("Failed to start process.");

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                var completed = process.WaitForExit(timeoutMs);
                if (!completed || token.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch (Exception ex) { InternalLogger.LogException("TerminalService.ExecuteInternal.Kill", ex); }
                    throw new TimeoutException("Command execution timed out or was aborted.");
                }

                var output = outputTask.Result;
                var error = errorTask.Result;

                return string.IsNullOrEmpty(error) ? output : $"{output}\n[ERROR]\n{error}";
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("TerminalService.ExecuteInternal", ex);
                throw;
            }
        }

        private static bool IsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                InternalLogger.Log("EnsureWindows failed: not running on Windows");
                throw new PlatformNotSupportedException("TerminalService currently supports only Windows.");
            }
        }
    }
}
