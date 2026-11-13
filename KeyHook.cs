using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Heft
{
    internal sealed class KeyHook : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        readonly Settings _s;
        readonly Dispatcher _ui;
        readonly Native.HookProc _proc;
        readonly Thread _thread;
        IntPtr _hook;
        uint _threadId;
        bool _winDown, _other;

        public event Action? WinTapped;

        public KeyHook(Settings s, Dispatcher ui)
        {
            _s = s;
            _ui = ui;
            _proc = Callback;
            _thread = new Thread(Run) { IsBackground = true, Name = "heft-keys" };
            _thread.Start();
        }

        void Run()
        {
            _threadId = Native.GetCurrentThreadId();
            _hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool injected = (k.flags & 0x10) != 0;
                    int msg = wParam.ToInt32();
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                    bool win = k.vkCode == 0x5B || k.vkCode == 0x5C;

                    if (!injected)
                    {
                        if (win && down)
                        {
                            if (!_winDown) _other = false;
                            _winDown = true;
                        }
                        else if (!win && down && _winDown)
                        {
                            _other = true;
                        }
                        else if (win && up)
                        {
                            bool solo = _winDown && !_other && !Native.MouseDown();
                            _winDown = false;
                            if (solo && _s.WinKeyLauncher)
                            {
                                Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                                Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                                _ui.BeginInvoke(new Action(() => WinTapped?.Invoke()));
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
