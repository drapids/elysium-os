using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Heft
{
    internal sealed class Display
    {
        public IntPtr Handle;
        public Native.RECT Bounds;
        public Native.RECT Work;
        public bool Primary;
        public int Index;
        public double Scale = 1;

        public string Key => $"{Bounds.Left},{Bounds.Top},{Bounds.Width}x{Bounds.Height}";
    }

    internal static class Displays
    {
        public static List<Display> All()
        {
            var list = new List<Display>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr hdc, ref Native.RECT rect, IntPtr data) =>
            {
                var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                if (!Native.GetMonitorInfo(mon, ref info)) return true;
                double scale = 1;
                try
                {
                    if (Native.GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                }
                catch
                {
                }
                list.Add(new Display
                {
                    Handle = mon,
                    Bounds = info.rcMonitor,
                    Work = info.rcWork,
                    Primary = (info.dwFlags & 1) != 0,
                    Scale = scale
                });
                return true;
            }, IntPtr.Zero);

            list = list.OrderBy(d => d.Bounds.Left).ThenBy(d => d.Bounds.Top).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Index = i;
            return list;
        }

        public static Display Primary(List<Display>? all = null)
        {
            all ??= All();
            return all.FirstOrDefault(d => d.Primary) ?? all.First();
        }

        public static Display At(int x, int y, List<Display>? all = null)
        {
            all ??= All();
            foreach (var d in all)
                if (d.Bounds.Contains(x, y)) return d;
            return all.OrderBy(d => Dist(d.Bounds, x, y)).First();
        }

        public static Display AtCursor(List<Display>? all = null)
        {
            Native.GetCursorPos(out var p);
            return At(p.X, p.Y, all);
        }

        static double Dist(Native.RECT r, int x, int y)
        {
            double dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right);
            double dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
            return dx * dx + dy * dy;
        }

        public static bool IsOuter(Display d, string side, List<Display> all)
        {
            var b = d.Bounds;
            foreach (var o in all)
            {
                if (o == d) continue;
                var r = o.Bounds;
                bool vOverlap = Math.Min(b.Bottom, r.Bottom) - Math.Max(b.Top, r.Top) > 40;
                bool hOverlap = Math.Min(b.Right, r.Right) - Math.Max(b.Left, r.Left) > 40;
                switch (side)
                {
                    case "left":
                        if (vOverlap && Math.Abs(r.Right - b.Left) <= 2) return false;
                        break;
                    case "right":
                        if (vOverlap && Math.Abs(r.Left - b.Right) <= 2) return false;
                        break;
                    case "top":
                        if (hOverlap && Math.Abs(r.Bottom - b.Top) <= 2) return false;
                        break;
                    default:
                        if (hOverlap && Math.Abs(r.Top - b.Bottom) <= 2) return false;
                        break;
                }
            }
            return true;
        }

        static string Mirror(string side) => side switch
        {
            "left" => "right",
            "right" => "left",
            "top" => "bottom",
            _ => "top"
        };

        public static List<(Display display, string side)> DockEdges(Settings s, List<Display>? all = null)
        {
            all ??= All();
            string side = s.DockSide is "left" or "top" or "bottom" ? s.DockSide : "right";
            var result = new List<(Display, string)>();

            switch (s.DockScreens)
            {
                case "primary":
                    result.Add((Primary(all), side));
                    break;
                case "all":
                    foreach (var d in all)
                    {
                        if (IsOuter(d, side, all)) result.Add((d, side));
                        else if (IsOuter(d, Mirror(side), all)) result.Add((d, Mirror(side)));
                    }
                    break;
                case "mirror":
                    foreach (var d in all)
                    {
                        if (IsOuter(d, side, all)) result.Add((d, side));
                        else if (all.Count > 1 && IsOuter(d, Mirror(side), all)) result.Add((d, Mirror(side)));
                    }
                    break;
                default:
                    var outer = all.Where(d => IsOuter(d, side, all)).ToList();
                    if (outer.Count == 0) outer = all.ToList();
                    var pick = outer.FirstOrDefault(d => d.Primary) ?? side switch
                    {
                        "left" => outer.OrderBy(d => d.Bounds.Left).First(),
                        "top" => outer.OrderBy(d => d.Bounds.Top).First(),
                        "bottom" => outer.OrderByDescending(d => d.Bounds.Bottom).First(),
                        _ => outer.OrderByDescending(d => d.Bounds.Right).First()
                    };
                    result.Add((pick, side));
                    break;
            }

            if (result.Count == 0) result.Add((Primary(all), side));
            return result;
        }

        public static (Display display, string side) DockForCursor(Settings s)
        {
            var all = All();
            var here = AtCursor(all);
            var edges = DockEdges(s, all);
            foreach (var e in edges)
                if (e.display.Index == here.Index) return e;
            string side = s.DockSide is "left" or "top" or "bottom" ? s.DockSide : "right";
            if (s.DockScreens == "cursor" || s.DockScreens == "all" || s.DockScreens == "mirror")
                return (here, IsOuter(here, side, all) ? side : Mirror(side));
            return edges[0];
        }

        public static List<Display> BarScreens(Settings s, List<Display>? all = null)
        {
            all ??= All();
            return s.BarScreens == "all" ? all : new List<Display> { Primary(all) };
        }
    }

    internal sealed class CursorLock : IDisposable
    {
        readonly Settings _s;
        readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        Native.RECT _rect;
        bool _auto;

        public bool Locked { get; private set; }
        public event Action<bool>? Changed;

        public CursorLock(Settings settings)
        {
            _s = settings;
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        public void Toggle()
        {
            if (Locked) Unlock();
            else Lock(Displays.AtCursor());
        }

        public void Lock(Display d)
        {
            _rect = d.Bounds;
            Locked = true;
            _auto = false;
            Native.ClipCursor(ref _rect);
            Changed?.Invoke(true);
        }

        public void Unlock()
        {
            if (!Locked) return;
            Locked = false;
            _auto = false;
            Native.ClipCursor(IntPtr.Zero);
            Changed?.Invoke(false);
        }

        void Tick()
        {
            if (_s.AutoLockFullscreen)
            {
                IntPtr fg = Native.GetForegroundWindow();
                Native.RECT mon = default;
                bool full = fg != IntPtr.Zero && IsFullscreen(fg, out mon);
                if (full && !Locked)
                {
                    Lock(Displays.At(mon.Left + 1, mon.Top + 1));
                    _auto = true;
                }
                else if (!full && Locked && _auto)
                {
                    Unlock();
                }
            }
            else if (_auto && Locked)
            {
                Unlock();
            }

            if (!Locked) return;
            if (Native.GetClipCursor(out var cur) && (cur.Left != _rect.Left || cur.Top != _rect.Top || cur.Right != _rect.Right || cur.Bottom != _rect.Bottom))
                Native.ClipCursor(ref _rect);
        }

        public static bool IsFullscreen(IntPtr hwnd, out Native.RECT monitor)
        {
            monitor = default;
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            string cls = Native.ClassName(hwnd);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            Native.GetWindowRect(hwnd, out var r);
            monitor = Native.MonitorAt(r.Left + r.Width / 2, r.Top + r.Height / 2);
            return r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
        }

        public void Dispose()
        {
            _timer.Stop();
            if (Locked) Native.ClipCursor(IntPtr.Zero);
        }
    }
}
