using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace recontrol_win.Internal
{
    internal class MouseService
    {
        // Import required user32.dll functions
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);

        // Mouse event constants
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        public void MoveMouseTo(int x, int y)
        {
            InternalLogger.Log($"MoveMouseTo called: x={x}, y={y}");
            EnsureWindows();
            try
            {
                if (!SetCursorPos(x, y))
                {
                    int err = Marshal.GetLastWin32Error();
                    InternalLogger.Log($"SetCursorPos failed. Win32Error={err}, x={x}, y={y}");
                    throw new InvalidOperationException($"SetCursorPos failed. Win32Error={err}");
                }
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("MouseService.MoveMouseTo", ex);
                throw;
            }
        }

        public void MouseDown(MouseButton button)
        {
            InternalLogger.Log($"MouseDown called: button={button}");
            EnsureWindows();
            try
            {
                switch (button)
                {
                    case MouseButton.Left:
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                        break;
                    case MouseButton.Right:
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                        break;
                    case MouseButton.Middle:
                        mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                        break;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("MouseService.MouseDown", ex);
                throw;
            }
        }

        public void MouseUp(MouseButton button)
        {
            InternalLogger.Log($"MouseUp called: button={button}");
            EnsureWindows();
            try
            {
                switch (button)
                {
                    case MouseButton.Left:
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                        break;
                    case MouseButton.Right:
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                        break;
                    case MouseButton.Middle:
                        mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                        break;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("MouseService.MouseUp", ex);
                throw;
            }
        }

        public void Scroll(int clicks)
        {
            InternalLogger.Log($"Scroll called: clicks={clicks}");
            EnsureWindows();
            try
            {
                // Positive dwData scrolls up, negative scrolls down. Each notch is 120.
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, clicks * 120, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                InternalLogger.LogException("MouseService.Scroll", ex);
                throw;
            }
        }

        public void Click(MouseButton button = MouseButton.Left, int delayMs = 30)
        {
            InternalLogger.Log($"Click called: button={button}, delayMs={delayMs}");
            EnsureWindows();
            if (delayMs < 0) delayMs = 0;
            MouseDown(button);
            if (delayMs > 0) Thread.Sleep(delayMs);
            MouseUp(button);
        }

        public void DoubleClick(int delayMs = 120)
        {
            InternalLogger.Log($"DoubleClick called: delayMs={delayMs}");
            EnsureWindows();
            if (delayMs < 0) delayMs = 0;
            Click(MouseButton.Left, delayMs / 2);
            if (delayMs > 0) Thread.Sleep(delayMs / 2);
            Click(MouseButton.Left, delayMs / 2);
        }

        public void RightClick(int delayMs = 30)
        {
            InternalLogger.Log($"RightClick called: delayMs={delayMs}");
            EnsureWindows();
            Click(MouseButton.Right, delayMs);
        }

        private static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                InternalLogger.Log("EnsureWindows failed: not running on Windows");
                throw new PlatformNotSupportedException("MouseService currently supports only Windows (user32.dll).");
            }
        }
    }

    internal enum MouseButton
    {
        Left,
        Right,
        Middle
    }
}