using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Elysium
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

        public volatile bool Paused;
        public volatile uint HoldVk;
        public volatile bool HoldSwallow;
        public event Action<bool>? Hold;
        bool _holdDown;
        public event Action<int, string>? Expand;
        volatile (string trigger, string text)[] _snippets = Array.Empty<(string, string)>();
        volatile bool _resetTyped;
        readonly StringBuilder _typed = new();
        readonly uint _ownPid = (uint)Environment.ProcessId;

        public void SetSnippets(IEnumerable<Snippet> list)
        {
            _snippets = list.Where(s => s.Trigger.Length > 1 && s.Text.Length > 0).Select(s => (s.Trigger, s.Text)).OrderByDescending(s => s.Trigger.Length).ToArray();
            _resetTyped = true;
        }

        public void ResetTyped() => _resetTyped = true;

        void Track(KBDLLHOOKSTRUCT k)
        {
            if (_resetTyped)
            {
                _typed.Clear();
                _resetTyped = false;
            }
            var snips = _snippets;
            if (snips.Length == 0 || Paused) return;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint fgPid);
            if (fgPid == _ownPid)
            {
                _typed.Clear();
                return;
            }
            uint vk = k.vkCode;
            if (vk is 0x10 or 0xA0 or 0xA1 or 0x14) return;
            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
            if (ctrl || alt || _winDown)
            {
                _typed.Clear();
                return;
            }
            if (vk == 0x08)
            {
                if (_typed.Length > 0) _typed.Length--;
                return;
            }
            string ch = Expander.CharFor(vk, k.scanCode);
            if (ch.Length == 0 || char.IsControl(ch[0]))
            {
                _typed.Clear();
                return;
            }
            _typed.Append(ch);
            if (_typed.Length > 64) _typed.Remove(0, _typed.Length - 64);
            string sofar = _typed.ToString();
            foreach (var (trigger, text) in snips)
            {
                if (!sofar.EndsWith(trigger, StringComparison.Ordinal)) continue;
                _typed.Clear();
                int back = trigger.Length;
                string body = text;
                _ui.BeginInvoke(new Action(() => Expand?.Invoke(back, body)));
                return;
            }
        }
        volatile Dictionary<uint, (byte[] mods, byte vk)> _remaps = new();
        readonly HashSet<uint> _remapHeld = new();

        public void SetRemaps(Dictionary<string, string> map)
        {
            var next = new Dictionary<uint, (byte[] mods, byte vk)>();
            foreach (var kv in map)
            {
                if (!Hotkeys.TryParse(kv.Key, out uint fm, out uint fv) || fm != 0) continue;
                if (!Hotkeys.TryParse(kv.Value, out uint tm, out uint tv)) continue;
                var mods = new List<byte>();
                if ((tm & Native.MOD_CONTROL) != 0) mods.Add(0x11);
                if ((tm & Native.MOD_ALT) != 0) mods.Add(0x12);
                if ((tm & Native.MOD_SHIFT) != 0) mods.Add(0x10);
                if ((tm & 0x0008) != 0) mods.Add(0x5B);
                next[fv] = (mods.ToArray(), (byte)tv);
            }
            _remaps = next;
        }

        public KeyHook(Settings s, Dispatcher ui)
        {
            _s = s;
            _ui = ui;
            _proc = Callback;
            _thread = new Thread(Run) { IsBackground = true, Name = "elysium-keys" };
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

                    if (!injected && HoldVk != 0 && k.vkCode == HoldVk && !Paused)
                    {
                        if (down && !_holdDown)
                        {
                            _holdDown = true;
                            _ui.BeginInvoke(new Action(() => Hold?.Invoke(true)));
                        }
                        else if (up && _holdDown)
                        {
                            _holdDown = false;
                            _ui.BeginInvoke(new Action(() => Hold?.Invoke(false)));
                        }
                        if (HoldSwallow) return new IntPtr(1);
                    }

                    if (!injected && !Paused && _remaps.TryGetValue(k.vkCode, out var to))
                    {
                        if (down)
                        {
                            if (_remapHeld.Add(k.vkCode))
                                foreach (var m in to.mods) Native.keybd_event(m, 0, 0, UIntPtr.Zero);
                            Native.keybd_event(to.vk, 0, Extended(to.vk), UIntPtr.Zero);
                        }
                        else if (up)
                        {
                            Native.keybd_event(to.vk, 0, Extended(to.vk) | 2, UIntPtr.Zero);
                            if (_remapHeld.Remove(k.vkCode))
                                for (int i = to.mods.Length - 1; i >= 0; i--) Native.keybd_event(to.mods[i], 0, 2, UIntPtr.Zero);
                        }
                        return new IntPtr(1);
                    }

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

                    if (!injected && down && !win) Track(k);

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
                                byte vk = (byte)k.vkCode;
                                Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                                Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                                Native.keybd_event(vk, 0, 2 | 1, UIntPtr.Zero);
                                _ui.BeginInvoke(new Action(() => WinTapped?.Invoke()));
                                return new IntPtr(1);
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

        static uint Extended(byte vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0xA3 or 0xA5 ? 1u : 0u;

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
