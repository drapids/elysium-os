using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace Elysium
{
    internal static class DesktopIcons
    {
        static IntPtr FindList()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr view = progman == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero)
            {
                Native.EnumWindows((h, _) =>
                {
                    if (Native.ClassName(h) != "WorkerW") return true;
                    IntPtr v = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (v == IntPtr.Zero) return true;
                    view = v;
                    return false;
                }, IntPtr.Zero);
            }
            return view == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        public static void Set(bool visible)
        {
            try
            {
                IntPtr list = FindList();
                if (list == IntPtr.Zero) return;
                long style = Native.Style(list);
                bool shown = (style & 0x10000000L) != 0;
                if (shown != visible) Native.ShowWindow(list, visible ? Native.SW_SHOWNA : Native.SW_HIDE);
            }
            catch
            {
            }
        }
    }

    internal sealed class TaskbarStyler : IDisposable
    {
        readonly Settings _s;
        readonly Thread _thread;
        readonly AutoResetEvent _wake = new(false);
        volatile bool _alive = true;

        bool _accent, _centered, _hidStart, _hidTray, _setAutoHide, _hidBar;

        public TaskbarStyler(Settings settings)
        {
            _s = settings;
            _thread = new Thread(Loop) { IsBackground = true, Name = "elysium-taskbar" };
            _thread.Start();
        }

        void Loop()
        {
            int tick = 0;
            while (_alive)
            {
                try
                {
                    IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                    if (tray != IntPtr.Zero)
                    {
                        Accent(tray);
                        if (tick % 8 == 0) Slow(tray);
                    }
                }
                catch
                {
                }
                tick++;
                _wake.WaitOne(60);
            }
            try
            {
                Restore();
            }
            catch
            {
            }
        }

        void Accent(IntPtr tray)
        {
            string style = _s.TaskbarStyle;
            if (style == "normal" || _s.TaskbarHide == "hidden")
            {
                if (_accent)
                {
                    _accent = false;
                    ResetAccent(tray);
                }
                return;
            }

            int state = style == "clear" ? 2 : style == "acrylic" ? 4 : 3;
            uint alpha = (uint)Math.Round(Math.Clamp(_s.TaskbarTint, 0, 1) * 255);
            if (state == 4 && alpha == 0) alpha = 1;
            uint color = alpha << 24;
            _accent = true;
            Native.SetAccent(tray, state, color);
            IntPtr second = IntPtr.Zero;
            while ((second = Native.FindWindowEx(IntPtr.Zero, second, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                Native.SetAccent(second, state, color);
        }

        static void ResetAccent(IntPtr tray)
        {
            Native.SetAccent(tray, 0, 0);
            Native.SendMessageTimeout(tray, Native.WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero, 2, 200, out _);
            IntPtr second = IntPtr.Zero;
            while ((second = Native.FindWindowEx(IntPtr.Zero, second, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                Native.SetAccent(second, 0, 0);
                Native.SendMessageTimeout(second, Native.WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero, 2, 200, out _);
            }
        }

        void Slow(IntPtr tray)
        {
            string hide = _s.TaskbarHide;
            if (hide == "auto" || hide == "hidden")
            {
                if ((State(tray) & Native.ABS_AUTOHIDE) == 0)
                {
                    SetState(tray, Native.ABS_AUTOHIDE);
                    _setAutoHide = true;
                }
            }
            else if (_setAutoHide)
            {
                _setAutoHide = false;
                SetState(tray, Native.ABS_ALWAYSONTOP);
            }

            bool visible = (Native.Style(tray) & 0x10000000L) != 0;
            if (hide == "hidden")
            {
                if (visible)
                {
                    Native.ShowWindow(tray, Native.SW_HIDE);
                    _hidBar = true;
                }
            }
            else if (_hidBar)
            {
                _hidBar = false;
                Native.ShowWindow(tray, Native.SW_SHOWNA);
            }

            Child(tray, "Start", _s.HideStart, ref _hidStart);
            Child(tray, "TrayNotifyWnd", _s.HideTray, ref _hidTray);

            if (_s.TaskbarCenter && hide != "hidden")
            {
                Center(tray, true);
                _centered = true;
            }
            else if (_centered)
            {
                _centered = false;
                Center(tray, false);
            }
        }

        static int State(IntPtr tray)
        {
            var abd = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray };
            return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd).ToUInt64();
        }

        static void SetState(IntPtr tray, int state)
        {
            var abd = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray, lParam = new IntPtr(state) };
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
        }

        static void Child(IntPtr tray, string cls, bool hide, ref bool hidden)
        {
            IntPtr h = Native.FindWindowEx(tray, IntPtr.Zero, cls, null);
            if (h == IntPtr.Zero) return;
            bool visible = (Native.Style(h) & 0x10000000L) != 0;
            if (hide)
            {
                if (visible)
                {
                    Native.ShowWindow(h, Native.SW_HIDE);
                    hidden = true;
                }
            }
            else if (hidden)
            {
                hidden = false;
                Native.ShowWindow(h, Native.SW_SHOWNA);
            }
        }

        static void Center(IntPtr tray, bool on)
        {
            IntPtr rebar = Native.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
            if (rebar == IntPtr.Zero) return;
            IntPtr sw = Native.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (sw == IntPtr.Zero) return;
            IntPtr list = Native.FindWindowEx(sw, IntPtr.Zero, "MSTaskListWClass", null);
            if (list == IntPtr.Zero) return;

            Native.GetWindowRect(tray, out var tr);
            Native.GetWindowRect(sw, out var swr);
            Native.GetWindowRect(list, out var lr);
            int curX = lr.Left - swr.Left;
            int curY = lr.Top - swr.Top;
            const uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;

            if (!on || tr.Width < tr.Height)
            {
                if (curX != 0) Native.SetWindowPos(list, IntPtr.Zero, 0, curY, 0, 0, flags);
                return;
            }

            var element = AutomationElement.FromHandle(list);
            var first = TreeWalker.ControlViewWalker.GetFirstChild(element);
            var last = TreeWalker.ControlViewWalker.GetLastChild(element);
            if (first == null || last == null) return;
            var fr = first.Current.BoundingRectangle;
            var er = last.Current.BoundingRectangle;
            if (fr.IsEmpty || er.IsEmpty) return;

            int icons = (int)(er.Right - fr.Left);
            if (icons <= 0) return;
            int inset = (int)fr.Left - lr.Left;
            int target = tr.Left + (tr.Width - icons) / 2;
            int x = Math.Max(0, target - swr.Left - inset);
            if (Math.Abs(curX - x) > 2) Native.SetWindowPos(list, IntPtr.Zero, x, curY, 0, 0, flags);
        }

        void Restore()
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            if (_accent) ResetAccent(tray);
            if (_hidBar) Native.ShowWindow(tray, Native.SW_SHOWNA);
            if (_setAutoHide) SetState(tray, Native.ABS_ALWAYSONTOP);
            bool f = false;
            if (_hidStart) Child(tray, "Start", false, ref _hidStart);
            if (_hidTray) Child(tray, "TrayNotifyWnd", false, ref _hidTray);
            if (_centered) Center(tray, false);
            _accent = _hidBar = _setAutoHide = _centered = f;
        }

        public void Dispose()
        {
            _alive = false;
            _wake.Set();
            _thread.Join(800);
        }
    }
}
