using System;
using System.Windows;
using recontrol_win.Services;

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
            ErrorLabel.Text = string.Empty;

            // Disable the login button while the request is in progress
            LoginButton.IsEnabled = false;

            var email = EmailTextBox.Text?.Trim();
            var password = PasswordBox.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ErrorLabel.Text = "Email and password are required";
                ErrorLabel.Visibility = Visibility.Visible;
                LoginButton.IsEnabled = true;
                return;
            }

            using var auth = new AuthService();

            try
            {
                var response = await auth.LoginAsync(email, password);

                if (response.IsSuccessStatusCode)
                {
                    // Success -> close dialog and let App open main window
                    this.DialogResult = true;
                    this.Close();
                    return;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Show invalid credentials error
                    ErrorLabel.Text = "Invalid email or password";
                    ErrorLabel.Visibility = Visibility.Visible;
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                ErrorLabel.Text = string.IsNullOrWhiteSpace(content) ? $"Login failed: {(int)response.StatusCode} {response.ReasonPhrase}" : content;
                ErrorLabel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = ex.Message;
                ErrorLabel.Visibility = Visibility.Visible;
            }
            finally
            {
                // Re-enable the button regardless of outcome
                LoginButton.IsEnabled = true;
            }
        }
    }
}

