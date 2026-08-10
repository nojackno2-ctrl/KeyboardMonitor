using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardDiagnostic
{
    public sealed class GlobalInputHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        private readonly object _sync = new object();
        private readonly LowLevelKeyboardProc _keyboardProc;
        private readonly LowLevelMouseProc _mouseProc;
        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;

        public GlobalInputHook()
        {
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
        }

        public event Action<string, bool> KeyChanged;

        public event Action<string, bool> MouseButtonChanged;

        public event Action<int> MouseWheelScrolled;

        public event Action<Exception> CallbackError;

        public bool IsStarted
        {
            get
            {
                lock (_sync)
                {
                    return _keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero)
                {
                    return;
                }

                IntPtr moduleHandle = GetCurrentModuleHandle();
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
                if (_keyboardHook == IntPtr.Zero)
                {
                    throw CreateLastWin32Exception();
                }

                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                if (_mouseHook == IntPtr.Zero)
                {
                    Win32Exception exception = CreateLastWin32Exception();
                    StopCore();
                    throw exception;
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopCore();
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        private void StopCore()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = unchecked((int)(long)wParam);
                    if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN ||
                        message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        KBDLLHOOKSTRUCT keyboardData = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                        string keyName = KeyboardInput.ParseKey(
                            keyboardData.VirtualKey,
                            keyboardData.ScanCode,
                            keyboardData.Flags);
                        if (keyName != null)
                        {
                            bool isPressed = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                            KeyChanged?.Invoke(keyName, isPressed);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ReportCallbackError(exception);
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = unchecked((int)(long)wParam);
                    MSLLHOOKSTRUCT mouseData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                    switch (message)
                    {
                        case WM_LBUTTONDOWN:
                            MouseButtonChanged?.Invoke("L_BUTTON", true);
                            break;
                        case WM_LBUTTONUP:
                            MouseButtonChanged?.Invoke("L_BUTTON", false);
                            break;
                        case WM_RBUTTONDOWN:
                            MouseButtonChanged?.Invoke("R_BUTTON", true);
                            break;
                        case WM_RBUTTONUP:
                            MouseButtonChanged?.Invoke("R_BUTTON", false);
                            break;
                        case WM_MBUTTONDOWN:
                            MouseButtonChanged?.Invoke("M_BUTTON", true);
                            break;
                        case WM_MBUTTONUP:
                            MouseButtonChanged?.Invoke("M_BUTTON", false);
                            break;
                        case WM_XBUTTONDOWN:
                        case WM_XBUTTONUP:
                            int xButton = (int)((mouseData.MouseData >> 16) & 0xFFFF);
                            MouseButtonChanged?.Invoke(
                                xButton == 1 ? "X1_BUTTON" : "X2_BUTTON",
                                message == WM_XBUTTONDOWN);
                            break;
                        case WM_MOUSEWHEEL:
                            short delta = unchecked((short)((mouseData.MouseData >> 16) & 0xFFFF));
                            MouseWheelScrolled?.Invoke(delta);
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                ReportCallbackError(exception);
            }

            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void ReportCallbackError(Exception exception)
        {
            Action<Exception> handler = CallbackError;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(exception);
            }
            catch (Exception)
            {
                // Never allow an application callback exception to escape into user32.
            }
        }

        private static IntPtr GetCurrentModuleHandle()
        {
            using (Process currentProcess = Process.GetCurrentProcess())
            using (ProcessModule currentModule = currentProcess.MainModule)
            {
                return GetModuleHandle(currentModule.ModuleName);
            }
        }

        private static Win32Exception CreateLastWin32Exception()
        {
            return new Win32Exception(Marshal.GetLastWin32Error());
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            Delegate lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
