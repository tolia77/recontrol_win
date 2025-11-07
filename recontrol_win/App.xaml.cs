using System.Windows;
using recontrol_win.Services;
using recontrol_win.Tools;

namespace recontrol_win
{
    public partial class App : System.Windows.Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DotNetEnv.Env.Load();


            var tokenStore = new TokenStore();
            var tokens = tokenStore.Load();
            if (tokens is not null && !string.IsNullOrWhiteSpace(tokens.DeviceId) && !string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow; // ensure main window is set
                mainWindow.Show();
                return;
            }

            // Temporarily prevent app from auto-shutdown when dialog (treated as MainWindow) closes
            var originalMode = ShutdownMode;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var loginWindow = new LoginWindow();
            // ShowDialog() blocks until the login window is closed
            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                // If login succeeded, open main app window
                var mainWindow = new MainWindow();
                MainWindow = mainWindow; // set actual main window
                mainWindow.Show();
                ShutdownMode = originalMode; // restore
            }
            else
            {
                // restore before shutdown (optional)
                ShutdownMode = originalMode;
                // If user canceled or login failed → close app
                Current.Shutdown();
            }
        }
    }
}
