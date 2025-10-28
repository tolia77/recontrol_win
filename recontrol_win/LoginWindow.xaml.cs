using System;
using System.Threading.Tasks;
using System.Windows;
using recontrol_win.Services;
using MessageBox = System.Windows.MessageBox;

namespace recontrol_win
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        public void SignUpLink_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://recontrol.app/signup",
                UseShellExecute = true
            });
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide error label initially
            ErrorLabel.Visibility = Visibility.Collapsed;

            // Disable the login button while the request is in progress
            LoginButton.IsEnabled = false;

            var email = EmailTextBox.Text?.Trim();
            var password = PasswordBox.Password ?? string.Empty;

            using var auth = new AuthService();

            try
            {
                var response = await auth.LoginAsync(email, password);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Show invalid credentials error
                    ErrorLabel.Visibility = Visibility.Visible;
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();

                MessageBox.Show(this, $"Status: {(int)response.StatusCode} - {response.ReasonPhrase}\n\n{content}", "Login response", MessageBoxButton.OK, MessageBoxImage.Information);

                // Optionally close dialog with success to let App open main window
                // this.DialogResult = true; // enable if server returns success
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable the button regardless of outcome
                LoginButton.IsEnabled = true;
            }
        }
    }
}

