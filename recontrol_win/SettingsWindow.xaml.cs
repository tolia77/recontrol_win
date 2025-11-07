using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace recontrol_win
{
    public partial class SettingsWindow : Window
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "ReControl";

        public SettingsWindow()
        {
            InitializeComponent();
            LoadAutostart();
        }

        private void LoadAutostart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                var value = key?.GetValue(AppName) as string;
                AutostartCheck.IsChecked = !string.IsNullOrEmpty(value);
            }
            catch { AutostartCheck.IsChecked = false; }
        }

        private void SaveAutostart(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Failed to update autostart: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (AutostartCheck.IsChecked is bool b)
                SaveAutostart(b);
        }
    }
}
