using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace recontrol_win.Tools
{
    /// <summary>
    /// Encapsulates system tray icon behavior: initialization, restore, exit, and close interception.
    /// </summary>
    internal sealed class TrayIconManager : IDisposable
    {
        private readonly Window _window;
        private NotifyIcon? _notifyIcon;
        private bool _isExitRequested;
        private bool _disposed;

        public TrayIconManager(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                var icon = TryGetAppIcon();
                _notifyIcon = new NotifyIcon
                {
                    Icon = icon,
                    Text = "ReControl",
                    Visible = true
                };

                var menu = new ContextMenuStrip();
                menu.Items.Add("Restore", null, (_, __) => Restore());
                menu.Items.Add("Exit", null, (_, __) => Exit());
                _notifyIcon.ContextMenuStrip = menu;
                _notifyIcon.DoubleClick += (_, __) => Restore();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        private static Icon? TryGetAppIcon()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
                return Icon.ExtractAssociatedIcon(exePath);
            }
            catch { return null; }
        }

        public void Restore()
        {
            if (_disposed) return;
            _window.Dispatcher.Invoke(() =>
            {
                _window.ShowInTaskbar = true;
                _window.Show();
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;
                _window.Activate();
                _window.Topmost = true;  // bring to front
                _window.Topmost = false; // reset
                _window.Focus();
            });
        }

        public void Exit()
        {
            if (_disposed) return;
            _isExitRequested = true;
            try { if (_notifyIcon is not null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } } catch { }
            _window.Dispatcher.Invoke(() => _window.Close());
        }

        /// <summary>
        /// Intercepts the window closing event to implement hide-to-tray behavior.
        /// Call from the window's OnClosing override.
        /// </summary>
        public void HandleClosing(CancelEventArgs e)
        {
            if (_disposed) return;
            if (!_isExitRequested)
            {
                e.Cancel = true;
                _window.ShowInTaskbar = false;
                _window.Hide();
                try { _notifyIcon?.ShowBalloonTip(1500, "ReControl", "The app is still running here.", ToolTipIcon.Info); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_notifyIcon is not null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch { }
        }
    }
}
