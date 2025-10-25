using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace recontrol_win
{
    internal enum MouseButton
    {
        Left,
        Right,
        Middle
    }

    internal class MouseService
    {
        // Public API
        public void MoveMouse(int deltaX, int deltaY)
        {
            EnsureWindows();
            SendMouseInput(deltaX, deltaY, 0, MouseEventFlags.MOVE);
        }

        public void MouseDown(MouseButton button)
        {
            EnsureWindows();
            var flags = button switch
            {
                MouseButton.Left => MouseEventFlags.LEFTDOWN,
                MouseButton.Right => MouseEventFlags.RIGHTDOWN,
                MouseButton.Middle => MouseEventFlags.MIDDLEDOWN,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            SendMouseInput(0, 0, 0, flags);
        }

        public void MouseUp(MouseButton button)
        {
            EnsureWindows();
            var flags = button switch
            {
                MouseButton.Left => MouseEventFlags.LEFTUP,
                MouseButton.Right => MouseEventFlags.RIGHTUP,
                MouseButton.Middle => MouseEventFlags.MIDDLEUP,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            SendMouseInput(0, 0, 0, flags);
        }

        public void Scroll(int wheelClicks)
        {
            EnsureWindows();
            // Each wheel click = WHEEL_DELTA (120)
            int amount = WHEEL_DELTA * wheelClicks;
            SendMouseInput(0, 0, amount, MouseEventFlags.WHEEL);
        }

        public void Click(MouseButton button = MouseButton.Left, int delayBetweenMs = 30)
        {
            MouseDown(button);
            Thread.Sleep(delayBetweenMs);
            MouseUp(button);
        }

        public void DoubleClick(int delayBetweenClicksMs = 120)
        {
            Click(MouseButton.Left);
            Thread.Sleep(delayBetweenClicksMs);
            Click(MouseButton.Left);
        }

        public void RightClick() => Click(MouseButton.Right);

        // Helpers
        private static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("MouseService currently supports only Windows (user32 SendInput).");
        }

        private void SendMouseInput(int dx, int dy, int mouseData, MouseEventFlags flags)
        {
            INPUT input = new()
            {
                type = InputType.MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = mouseData,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"SendInput failed. Win32Error={err}");
            }
        }

        // Win32 interop
        private const int WHEEL_DELTA = 120;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public InputType type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public MouseEventFlags dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [Flags]
        private enum MouseEventFlags : uint
        {
            MOVE = 0x0001,
            LEFTDOWN = 0x0002,
            LEFTUP = 0x0004,
            RIGHTDOWN = 0x0008,
            RIGHTUP = 0x0010,
            MIDDLEDOWN = 0x0020,
            MIDDLEUP = 0x0040,
            WHEEL = 0x0800,
            // Additional flags omitted for brevity
        }

        private enum InputType : uint
        {
            MOUSE = 0,
        }
    }
}
