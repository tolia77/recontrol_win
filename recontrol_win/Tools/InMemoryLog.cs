using System;
using System.Collections.Generic;

namespace recontrol_win.Tools
{
    public static class InMemoryLog
    {
        private static readonly object _gate = new();
        private static readonly List<string> _lines = new();
        public static event Action<string>? LogAdded;

        public static IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }

        public static void Add(string line)
        {
            lock (_gate)
            {
                _lines.Add(line);
            }
            try { LogAdded?.Invoke(line); } catch { }
        }
    }
}
