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
        bool _winDown, _other, _spaceHeld;

        public event Action? WinTapped;
        public event Action? ExplorerSpace;

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

                    if (!injected && k.vkCode == 0x20)
                    {
                        if (up && _spaceHeld)
                        {
                            _spaceHeld = false;
                            return new IntPtr(1);
                        }
                        if (down && _spaceHeld) return new IntPtr(1);
                        if (down && _s.PreviewSpace && ExplorerItemsFocused())
                        {
                            _spaceHeld = true;
                            _ui.BeginInvoke(new Action(() => ExplorerSpace?.Invoke()));
                            return new IntPtr(1);
                        }
                    }

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

        static bool ExplorerItemsFocused()
        {
            if (Native.ModifiersDown()) return false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            string cls = Native.ClassName(fg);
            if (cls != "CabinetWClass" && cls != "ExploreWClass") return false;
            uint thread = Native.GetWindowThreadProcessId(fg, out _);
            var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
            if (!Native.GetGUIThreadInfo(thread, ref info)) return false;
            if (info.hwndFocus == IntPtr.Zero || info.hwndCaret != IntPtr.Zero) return false;
            if (Native.ClassName(info.hwndFocus) != "DirectUIHWND") return false;
            IntPtr parent = Native.GetParent(info.hwndFocus);
            return parent != IntPtr.Zero && Native.ClassName(parent) == "SHELLDLL_DefView";
        }

        public void Dispose()
        {
            if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
