using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace recontrol_win.Internal
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
        public void MoveMouseTo(int x, int y)
        {
            InternalLogger.Log($"MoveMouseTo called: x={x}, y={y}");
            EnsureWindows();

            // Use the virtual screen to properly support multi-monitor and DPI scenarios
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // Normalize to [0, 65535] inclusive per Win32 docs: x * 65535 / (width - 1)
            int nx = NormalizeToAbsolute(x - vx, vw);
            int ny = NormalizeToAbsolute(y - vy, vh);

            SendMouseInput(nx, ny, 0, MouseEventFlags.MOVE | MouseEventFlags.ABSOLUTE | MouseEventFlags.VIRTUALDESK);
        }

        public void MouseDown(MouseButton button)
        {
            InternalLogger.Log($"MouseDown called: button={button}");
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
            InternalLogger.Log($"MouseUp called: button={button}");
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
            InternalLogger.Log($"Scroll called: wheelClicks={wheelClicks}");
            EnsureWindows();
            // Each wheel click = WHEEL_DELTA (120)
            int amount = WHEEL_DELTA * wheelClicks;
            SendMouseInput(0, 0, amount, MouseEventFlags.WHEEL);
        }

        public void Click(MouseButton button = MouseButton.Left, int delayBetweenMs = 30)
        {
            InternalLogger.Log($"Click called: button={button}, delayMs={delayBetweenMs}");
            MouseDown(button);
            Thread.Sleep(delayBetweenMs);
            MouseUp(button);
        }

        public void DoubleClick(int delayBetweenClicksMs = 120)
        {
            InternalLogger.Log($"DoubleClick called: delayBetweenClicksMs={delayBetweenClicksMs}");
            Click(MouseButton.Left);
            Thread.Sleep(delayBetweenClicksMs);
            Click(MouseButton.Left);
        }

        public void RightClick() => Click(MouseButton.Right);

        // Helpers
        private static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                InternalLogger.Log("EnsureWindows failed: not running on Windows");
                throw new PlatformNotSupportedException("MouseService currently supports only Windows (user32 SendInput).");
            }
        }

        private static int NormalizeToAbsolute(int value, int range)
        {
            if (range <= 1)
                return 0;
            double norm = (double)value * 65535.0 / (range - 1);
            int rounded = (int)Math.Round(norm);
            if (rounded < 0) return 0;
            if (rounded > 65535) return 65535;
            return rounded;
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
                        dwExtraInfo = nint.Zero
                    }
                }
            };

            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                InternalLogger.Log($"SendInput failed. Win32Error={err}, flags={flags}, dx={dx}, dy={dy}, mouseData={mouseData}");
                throw new InvalidOperationException($"SendInput failed. Win32Error={err}");
            }
        }

        // Win32 interop
        private const int WHEEL_DELTA = 120;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

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
            public nint dwExtraInfo;
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
            VIRTUALDESK = 0x4000,
            ABSOLUTE = 0x8000,
            // Additional flags omitted for brevity
        }

        private enum InputType : uint
        {
            MOUSE = 0,
        }
    }
}
