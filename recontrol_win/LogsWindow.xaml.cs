using System;
using System.Windows;
using recontrol_win.Tools;

namespace recontrol_win
{
    public partial class LogsWindow : Window
    {
        public LogsWindow()
        {
            InitializeComponent();
            foreach (var line in InMemoryLog.Snapshot())
                LogList.Items.Add(line);
            InMemoryLog.LogAdded += OnLogAdded;
        }

        private void OnLogAdded(string line)
        {
            Dispatcher.Invoke(() => LogList.Items.Add(line));
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            // simple clear of UI list; keeping log store as-is for now
            LogList.Items.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            InMemoryLog.LogAdded -= OnLogAdded;
        }
    }
}
