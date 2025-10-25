using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace recontrol_console
{
    // Limited set of virtual keys for convenience. Users can cast any VK code (see WinUser.h)
    internal enum VirtualKey : ushort
    {
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47, H = 0x48,
        I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E, O = 0x4F, P = 0x50,
        Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55, V = 0x56, W = 0x57, X = 0x58,
        Y = 0x59, Z = 0x5A,
        D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34, D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,
        SPACE = 0x20, ESCAPE = 0x1B,
        LEFT = 0x25, UP = 0x26, RIGHT = 0x27, DOWN = 0x28,
        SHIFT = 0x10, CONTROL = 0x11, MENU = 0x12, // MENU = Alt
        RETURN = 0x0D, TAB = 0x09, BACK = 0x08
    }

    internal class KeyboardService
    {
        public void KeyDown(VirtualKey key)
        {
            EnsureWindows();
            SendKey((ushort)key, 0);
        }

        public void KeyUp(VirtualKey key)
        {
            EnsureWindows();
            SendKey((ushort)key, KEYEVENTF_KEYUP);
        }

        public void Press(VirtualKey key, int holdMs = 30)
        {
            KeyDown(key);
            if (holdMs > 0) Thread.Sleep(holdMs);
            KeyUp(key);
        }

        private static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("KeyboardService currently supports only Windows (user32 SendInput).");
        }

        private void SendKey(ushort vk, uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD
            };
            input.U.ki.wVk = vk;
            input.U.ki.wScan = 0;
            input.U.ki.dwFlags = flags;
            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = IntPtr.Zero;

            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"SendInput (keyboard) failed. Win32Error={err}");
            }
        }

        // Win32 interop
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
    }
}
