using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Elysium
{
    internal sealed class MouseHook : IDisposable
    {
        static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "#32768"
        };

        readonly Settings _s;
        readonly Dispatcher _ui;
        readonly Brightness _brightness;
        readonly Thread _thread;
        readonly Native.HookProc _proc;
        readonly uint _pid = (uint)Environment.ProcessId;
        readonly object _lock = new();
        readonly Dictionary<IntPtr, int> _alpha = new();
        readonly HashSet<IntPtr> _layered = new();

        IntPtr _hook;
        uint _threadId;
        bool _swallowMiddleUp;
        uint _lastDownTime;
        Native.POINT _lastDown;

        public event Action<string>? Flash;
        public event Action? DesktopDoubleClick;
        public event Action<string>? Corner;

        public PhysicsEngine? Physics;
        public volatile bool Quiet;
        public volatile int HoldX;
        public event Action<bool>? Hold;
        bool _xHeld;
        public event Action<string, Native.POINT>? Gesture;
        public event Action? ReplayRight;
        bool _gesturing;
        Native.POINT _gStart;
        readonly List<Native.POINT> _gPath = new();

        static readonly HashSet<string> GestureSkipClasses = new(StringComparer.Ordinal)
        {
            "DirectUIHWND", "SysListView32", "SysTreeView32", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "MSTaskSwWClass", "ToolbarWindow32"
        };

        bool GestureAllowed(Native.POINT p)
        {
            if (!_s.Gestures || Quiet || Native.ModifiersDown()) return false;
            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return false;
            if (GestureSkipClasses.Contains(Native.ClassName(h))) return false;
            IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
            Native.GetWindowThreadProcessId(root, out uint pid);
            if (pid == _pid) return false;
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && CursorLock.IsFullscreen(fg, out _)) return false;
            return true;
        }

        static string Recognize(List<Native.POINT> path)
        {
            if (path.Count < 2) return "";
            var sb = new System.Text.StringBuilder();
            var anchor = path[0];
            double total = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var p = path[i];
                total += Math.Abs(p.X - path[i - 1].X) + Math.Abs(p.Y - path[i - 1].Y);
                int dx = p.X - anchor.X, dy = p.Y - anchor.Y;
                if (Math.Abs(dx) < 46 && Math.Abs(dy) < 46) continue;
                char dir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 'R' : 'L') : (dy > 0 ? 'D' : 'U');
                if (sb.Length == 0 || sb[^1] != dir) sb.Append(dir);
                anchor = p;
                if (sb.Length > 3) return "";
            }
            return total < 40 ? "" : sb.ToString();
        }
        bool _lerping, _swallowLeftUp;
        IntPtr _lerpLast;
        uint _lerpLastTime;
        Native.POINT _lerpLastPt;

        string _corner = "";
        List<Native.RECT> _screens = new();
        long _screensAt;

        public MouseHook(Settings settings, Dispatcher ui, Brightness brightness)
        {
            _s = settings;
            _ui = ui;
            _brightness = brightness;
            _proc = Callback;
            _thread = new Thread(Run) { IsBackground = true, Name = "elysium-mouse" };
            _thread.Start();
        }

        void Run()
        {
            _threadId = Native.GetCurrentThreadId();
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            PhysicsEngine.Log(_hook == IntPtr.Zero ? "mouse hook failed" : "mouse hook on");
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                    if ((data.flags & 1) == 0 && Handle(wParam.ToInt32(), data)) return new IntPtr(1);
                }
                catch
                {
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        bool Handle(int msg, Native.MSLLHOOKSTRUCT m)
        {
            switch (msg)
            {
                case 0x0200:
                    if (_gesturing && _gPath.Count < 4000) _gPath.Add(m.pt);
                    CheckCorner(m.pt);
                    return false;

                case 0x020B:
                case 0x020C:
                    {
                        int which = (int)((m.mouseData >> 16) & 0xFFFF);
                        if (HoldX == 0 || which != HoldX) return false;
                        bool press = msg == 0x020B;
                        if (press == _xHeld) return true;
                        _xHeld = press;
                        Post(() => Hold?.Invoke(press));
                        return true;
                    }

                case 0x0204:
                    if (!GestureAllowed(m.pt)) return false;
                    _gesturing = true;
                    _gStart = m.pt;
                    _gPath.Clear();
                    _gPath.Add(m.pt);
                    return true;

                case 0x0205:
                    {
                        if (!_gesturing) return false;
                        _gesturing = false;
                        string dirs = Recognize(_gPath);
                        var start = _gStart;
                        _gPath.Clear();
                        if (dirs.Length == 0) Post(() => ReplayRight?.Invoke());
                        else Post(() => Gesture?.Invoke(dirs, start));
                        return true;
                    }

                case Native.WM_MOUSEWHEEL:
                    int delta = (short)(m.mouseData >> 16);
                    if (delta == 0) return false;
                    int dir = delta > 0 ? 1 : -1;
                    if (_s.GestureBrightness && AtBrightnessEdge(m.pt))
                    {
                        _brightness.Nudge(dir, m.pt.X, m.pt.Y);
                        return true;
                    }
                    if (_s.TitleOpacity)
                    {
                        IntPtr target = TitleAt(m.pt);
                        if (target != IntPtr.Zero)
                        {
                            NudgeOpacity(target, dir);
                            return true;
                        }
                    }
                    return false;

                case Native.WM_MBUTTONDOWN:
                    if (!_s.TitlePin) return false;
                    IntPtr win = TitleAt(m.pt);
                    if (win == IntPtr.Zero) return false;
                    TogglePin(win);
                    _swallowMiddleUp = true;
                    return true;

                case Native.WM_MBUTTONUP:
                    if (!_swallowMiddleUp) return false;
                    _swallowMiddleUp = false;
                    return true;

                case 0x0202:
                    if (_lerping)
                    {
                        _lerping = false;
                        Physics?.EndLerp();
                        return true;
                    }
                    if (_swallowLeftUp)
                    {
                        _swallowLeftUp = false;
                        return true;
                    }
                    return false;

                case Native.WM_LBUTTONDOWN:
                    if (TryLerp(m)) return true;
                    bool dbl = m.time - _lastDownTime <= Native.GetDoubleClickTime()
                               && Math.Abs(m.pt.X - _lastDown.X) < 5 && Math.Abs(m.pt.Y - _lastDown.Y) < 5;
                    _lastDownTime = dbl ? 0 : m.time;
                    _lastDown = m.pt;
                    if (dbl && _s.HideDesktop && OnDesktop(m.pt)) Post(() => DesktopDoubleClick?.Invoke());
                    return false;
            }
            return false;
        }

        bool TryLerp(Native.MSLLHOOKSTRUCT m)
        {
            var phys = Physics;
            if (phys == null || !phys.LerpWanted) return false;
            if (Native.ModifiersDown()) return false;
            IntPtr cap = TitleAt(m.pt);
            if (cap == IntPtr.Zero || Native.IsZoomed(cap) || !phys.CanLerp(cap)) return false;
            bool dbl = cap == _lerpLast && m.time - _lerpLastTime <= Native.GetDoubleClickTime()
                       && Math.Abs(m.pt.X - _lerpLastPt.X) < 6 && Math.Abs(m.pt.Y - _lerpLastPt.Y) < 6;
            _lerpLast = cap;
            _lerpLastTime = dbl ? 0 : m.time;
            _lerpLastPt = m.pt;
            if (dbl)
            {
                _swallowLeftUp = true;
                Native.PostMessage(cap, Native.WM_SYSCOMMAND, new IntPtr(Native.SC_MAXIMIZE), IntPtr.Zero);
                return true;
            }
            _lerping = true;
            phys.BeginLerp(cap, m.pt);
            return true;
        }

        void CheckCorner(Native.POINT p)
        {
            long now = Environment.TickCount64;
            if (now - _screensAt > 3000)
            {
                _screensAt = now;
                _screens = Displays.All().Select(d => d.Bounds).ToList();
            }
            string hit = "";
            foreach (var r in _screens)
            {
                bool left = p.X <= r.Left + 1 && p.X >= r.Left;
                bool right = p.X >= r.Right - 2 && p.X < r.Right;
                bool top = p.Y <= r.Top + 1 && p.Y >= r.Top;
                bool bottom = p.Y >= r.Bottom - 2 && p.Y < r.Bottom;
                if (top && left) hit = "tl";
                else if (top && right) hit = "tr";
                else if (bottom && left) hit = "bl";
                else if (bottom && right) hit = "br";
                if (hit.Length > 0) break;
            }
            if (hit == _corner) return;
            _corner = hit;
            if (hit.Length > 0) Post(() => Corner?.Invoke(hit));
        }

        void Post(Action action) => _ui.BeginInvoke(DispatcherPriority.Normal, action);

        void Say(string text) => Post(() => Flash?.Invoke(text));

        bool AtBrightnessEdge(Native.POINT pt)
        {
            IntPtr under = Native.WindowFromPoint(pt);
            if (under != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(under, out uint pid);
                if (pid == _pid) return false;
            }
            var mon = Native.MonitorAt(pt.X, pt.Y);
            return _s.DockSide == "left" ? pt.X >= mon.Right - 2 : pt.X <= mon.Left + 1;
        }

        static bool OnDesktop(Native.POINT pt)
        {
            IntPtr h = Native.WindowFromPoint(pt);
            if (h == IntPtr.Zero) return false;
            string cls = Native.ClassName(h);
            return cls == "SHELLDLL_DefView" || cls == "Progman" || cls == "WorkerW";
        }

        IntPtr TitleAt(Native.POINT pt)
        {
            IntPtr h = Native.WindowFromPoint(pt);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
            if (root == IntPtr.Zero) return IntPtr.Zero;
            Native.GetWindowThreadProcessId(root, out uint pid);
            if (pid == _pid) return IntPtr.Zero;
            if (Skip.Contains(Native.ClassName(root))) return IntPtr.Zero;

            var bounds = Native.FrameBounds(root);
            if (pt.Y < bounds.Top - 2 || pt.Y - bounds.Top > 72) return IntPtr.Zero;

            int packed = ((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF);
            IntPtr ok = Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero, new IntPtr(packed), 2, 40, out IntPtr hit);
            if (ok == IntPtr.Zero) return IntPtr.Zero;
            return hit.ToInt64() == Native.HTCAPTION ? root : IntPtr.Zero;
        }

        void NudgeOpacity(IntPtr hwnd, int dir)
        {
            int next;
            lock (_lock)
            {
                int cur = _alpha.TryGetValue(hwnd, out int a) ? a : 255;
                next = Math.Clamp(cur + dir * 20, 55, 255);
                if (next == cur) return;

                long ex = Native.ExStyle(hwnd);
                if ((ex & Native.WS_EX_LAYERED) == 0)
                {
                    Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_LAYERED));
                    _layered.Add(hwnd);
                }
                Native.SetLayeredWindowAttributes(hwnd, 0, (byte)next, 2);

                if (next >= 255)
                {
                    Unlayer(hwnd);
                    _alpha.Remove(hwnd);
                }
                else
                {
                    _alpha[hwnd] = next;
                }
            }
            Say($"opacity {Math.Round(next / 255.0 * 100)}%");
        }

        void Unlayer(IntPtr hwnd)
        {
            if (!_layered.Remove(hwnd)) return;
            long ex = Native.ExStyle(hwnd);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_LAYERED));
        }

        void TogglePin(IntPtr hwnd)
        {
            bool top = (Native.ExStyle(hwnd) & Native.WS_EX_TOPMOST) != 0;
            Native.SetWindowPos(hwnd, top ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Say(top ? "unpinned" : "pinned on top");
        }

        public void RestoreAll()
        {
            lock (_lock)
            {
                foreach (var hwnd in new List<IntPtr>(_alpha.Keys))
                {
                    if (!Native.IsWindow(hwnd)) continue;
                    Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2);
                    Unlayer(hwnd);
                }
                _alpha.Clear();
                _layered.Clear();
            }
        }

        public void Dispose()
        {
            RestoreAll();
            if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
