using System.Windows;

namespace recontrol_win
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DotNetEnv.Env.Load();
            // Show login window first
            var loginWindow = new LoginWindow();
            // ShowDialog() blocks until the login window is closed
            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                // If login succeeded, open main app window
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            else
            {
                // If user canceled or login failed → close app
                Current.Shutdown();
            }
        }
    }
}
