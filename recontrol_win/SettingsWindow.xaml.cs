using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Collections.Generic;
using System.Net.Http;
using recontrol_win.Tools;

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

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable to prevent double click
            LogoutButton.IsEnabled = false;

            // Fire-and-forget logout request with tokens attached
            try
            {
                var store = new TokenStore();
                var access = store.GetAccessToken();
                var refresh = store.GetRefreshToken();
                var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");

                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    var api = new ApiClient(baseUrl, () => access, null);
                    var headers = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(refresh)) headers["Refresh-Token"] = refresh!;
                    _ = api.PostAsync("/auth/logout", new StringContent(string.Empty), headers)
                           .ContinueWith(t => api.Dispose());
                }
            }
            catch { }

            // Clear local tokens
            try { new TokenStore().Clear(); } catch { }

            // Redirect to login window
            var app = System.Windows.Application.Current;
            var originalMode = app.ShutdownMode;
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (Owner != null) Owner.Hide();
            this.Hide();

            var login = new LoginWindow();
            bool? result = null;
            try { result = login.ShowDialog(); } catch { }

            try { Close(); } catch { }

            if (result == true)
            {
                try { Owner?.Close(); } catch { }
                var main = new MainWindow();
                app.MainWindow = main;
                main.Show();
                app.ShutdownMode = originalMode;
            }
            else
            {
                app.Shutdown();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (AutostartCheck.IsChecked is bool b)
                SaveAutostart(b);
        }
    }
}
