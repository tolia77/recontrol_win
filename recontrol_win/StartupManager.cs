using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace recontrol_win
{
    public static class StartupManager
    {
        // The key name used in the Windows Registry to identify this application's startup entry.
        private const string StartupKeyName = "MyAppAutoStart";

        // The registry path where applications can register for automatic startup (HKEY_CURRENT_USER).
        private const string RegistryRunPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        /// <summary>
        /// Gets the full path to the executable file of the currently running application.
        /// </summary>
        private static string ExecutablePath
        {
            get
            {
                // Use Assembly.GetExecutingAssembly().Location to get the path to the main executable.
                return Assembly.GetExecutingAssembly().Location;
            }
        }

        /// <summary>
        /// Determines whether the application should be set to run at Windows startup,
        /// based on a command-line argument passed from the installer.
        /// </summary>
        /// <param name="commandLineArgs">The command-line arguments passed to the application.</param>
        public static void HandleInstallerAutoStart(string[] commandLineArgs)
        {
            // Check if the installer passed the '/AUTOSTART=1' argument.
            // This argument is set in the MSI's Custom Action properties.
            bool shouldAutoStart = Array.Exists(commandLineArgs, arg =>
                arg.Equals("/AUTOSTART=1", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/AUTOSTART=true", StringComparison.OrdinalIgnoreCase));

            Debug.WriteLine($"StartupManager: Checking command-line arguments...");
            Debug.WriteLine($"StartupManager: /AUTOSTART=1 found: {shouldAutoStart}");

            if (shouldAutoStart)
            {
                // If the property was set (checkbox was checked in MSI).
                SetAutoStart(true);
            }
            else
            {
                // We check for /AUTOSTART=0 just in case the installer passes it, 
                // but primarily we use this call to ensure we don't accidentally leave 
                // the startup key if the user unchecked the box during an upgrade/repair install.
                bool shouldRemoveAutoStart = Array.Exists(commandLineArgs, arg =>
                    arg.Equals("/AUTOSTART=0", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/AUTOSTART=false", StringComparison.OrdinalIgnoreCase));

                if (shouldRemoveAutoStart)
                {
                    SetAutoStart(false);
                }
            }
            // Note: If no AUTOSTART argument is passed at all, the registry setting is left alone,
            // as the user likely launched the app normally after installation.
        }

        /// <summary>
        /// Adds or removes the application from the Windows startup list.
        /// </summary>
        /// <param name="isEnabled">True to enable startup, false to disable it.</param>
        public static void SetAutoStart(bool isEnabled)
        {
            try
            {
                // Open the registry key with write permissions.
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true))
                {
                    if (key == null)
                    {
                        Debug.WriteLine("StartupManager Error: Cannot access the Run registry key.");
                        return;
                    }

                    if (isEnabled)
                    {
                        // Add the entry: KeyName = "C:\Path\To\MyApp.exe"
                        key.SetValue(StartupKeyName, ExecutablePath);
                        Debug.WriteLine($"StartupManager: Enabled autostart. Path: {ExecutablePath}");
                    }
                    else
                    {
                        // Remove the entry.
                        if (key.GetValue(StartupKeyName) != null)
                        {
                            key.DeleteValue(StartupKeyName);
                            Debug.WriteLine("StartupManager: Disabled autostart.");
                        }
                        else
                        {
                            Debug.WriteLine("StartupManager: Autostart already disabled (no key found).");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // In a real application, you might log this error or show a discrete message.
                Debug.WriteLine($"StartupManager Exception: Failed to modify registry: {ex.Message}");
            }
        }
    }
}
