using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Elysium
{
    internal static class WinOps
    {
        static readonly Dictionary<IntPtr, (long style, long ex, Native.RECT rect, bool max)> Saved = new();
        static IntPtr _rescueH;
        static int _rescueStage;
        static long _rescueAt;

        public static Display DisplayOf(IntPtr h, List<Display>? all = null)
        {
            var fb = Native.FrameBounds(h);
            return Displays.At(fb.Left + fb.Width / 2, fb.Top + fb.Height / 2, all);
        }

        static bool Covers(IntPtr h, Display d)
        {
            Native.GetWindowRect(h, out var r);
            return r.Left <= d.Bounds.Left + 2 && r.Top <= d.Bounds.Top + 2 && r.Right >= d.Bounds.Right - 2 && r.Bottom >= d.Bounds.Bottom - 2;
        }

        public static bool IsBorderless(IntPtr h) => Saved.ContainsKey(h);

        public static bool IsTopmost(IntPtr h) => (Native.ExStyle(h) & Native.WS_EX_TOPMOST) != 0;

        public static void ToggleTopmost(IntPtr h)
        {
            bool on = IsTopmost(h);
            Native.SetWindowPos(h, on ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public static void Borderless(IntPtr h, Display? on = null)
        {
            if (!Native.IsWindow(h)) return;
            if (Saved.TryGetValue(h, out var old))
            {
                Saved.Remove(h);
                Native.SetWindowLongPtr(h, Native.GWL_STYLE, new IntPtr(old.style));
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(old.ex));
                Native.SetWindowPos(h, IntPtr.Zero, old.rect.Left, old.rect.Top, old.rect.Width, old.rect.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                if (old.max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                return;
            }
            var d = on ?? DisplayOf(h);
            bool max = Native.IsZoomed(h);
            if (max || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out var wr);
            long style = Native.Style(h), ex = Native.ExStyle(h);
            Saved[h] = (style, ex, wr, max);
            long strip = Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
            long stripEx = Native.WS_EX_DLGMODALFRAME | Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE | Native.WS_EX_WINDOWEDGE;
            Native.SetWindowLongPtr(h, Native.GWL_STYLE, new IntPtr(style & ~strip));
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex & ~stripEx));
            Native.SetWindowPos(h, IntPtr.Zero, d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
        }

        public static async void MoveTo(IntPtr h, Display to, Action<string>? say = null)
        {
            try
            {
                if (!Native.IsWindow(h)) return;
                var all = Displays.All();
                var from = DisplayOf(h, all);
                if (from.Key == to.Key) return;

                if (Saved.TryGetValue(h, out var saved))
                {
                    int dx = to.Bounds.Left - from.Bounds.Left, dy = to.Bounds.Top - from.Bounds.Top;
                    Saved[h] = (saved.style, saved.ex, new Native.RECT { Left = saved.rect.Left + dx, Top = saved.rect.Top + dy, Right = saved.rect.Right + dx, Bottom = saved.rect.Bottom + dy }, saved.max);
                    Native.SetWindowPos(h, IntPtr.Zero, to.Bounds.Left, to.Bounds.Top, to.Bounds.Width, to.Bounds.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    return;
                }

                if (Covers(h, from) && !Native.IsZoomed(h))
                {
                    Native.SetWindowPos(h, IntPtr.Zero, to.Bounds.Left, to.Bounds.Top, to.Bounds.Width, to.Bounds.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    await Task.Delay(400);
                    if (DisplayOf(h).Key == to.Key) return;
                    Native.ForceForeground(h);
                    await Task.Delay(150);
                    Native.Combo(0x12, 0x0D);
                    await Task.Delay(1100);
                    if (Covers(h, from))
                    {
                        say?.Invoke("this one won't let go of the screen. try borderless in its own settings");
                        return;
                    }
                    Center(h, to);
                    await Task.Delay(300);
                    Native.ForceForeground(h);
                    await Task.Delay(150);
                    Native.Combo(0x12, 0x0D);
                    say?.Invoke("moved it (had to flip fullscreen off and on)");
                    return;
                }

                bool max = Native.IsZoomed(h);
                if (max || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                var fb = Native.FrameBounds(h);
                int w = Math.Min(fb.Width, to.Work.Width), hh = Math.Min(fb.Height, to.Work.Height);
                double rx = from.Work.Width - fb.Width > 0 ? (fb.Left - from.Work.Left) / (double)(from.Work.Width - fb.Width) : 0.5;
                double ry = from.Work.Height - fb.Height > 0 ? (fb.Top - from.Work.Top) / (double)(from.Work.Height - fb.Height) : 0.5;
                int x = to.Work.Left + (int)Math.Round(Math.Clamp(rx, 0, 1) * (to.Work.Width - w));
                int y = to.Work.Top + (int)Math.Round(Math.Clamp(ry, 0, 1) * (to.Work.Height - hh));
                SnapZones.Place(h, new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + hh });
                if (max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("move to display failed " + ex.Message);
            }
        }

        public static void Center(IntPtr h, Display d)
        {
            var fb = Native.FrameBounds(h);
            int w = Math.Min(fb.Width, (int)(d.Work.Width * 0.9)), hh = Math.Min(fb.Height, (int)(d.Work.Height * 0.9));
            int x = d.Work.Left + (d.Work.Width - w) / 2, y = d.Work.Top + (d.Work.Height - hh) / 2;
            SnapZones.Place(h, new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + hh });
        }

        public static void Next(IntPtr h, int dir, Action<string>? say = null)
        {
            var all = Displays.All();
            if (all.Count < 2) return;
            var cur = DisplayOf(h, all);
            int i = all.FindIndex(d => d.Key == cur.Key);
            MoveTo(h, all[((i + dir) % all.Count + all.Count) % all.Count], say);
        }

        public static void Rescue(Action<string> say)
        {
            var h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero || !WinList.IsApp(h))
            {
                int n = BringBack();
                say(n == 0 ? "nothing stuck. click the window you want to rescue first" : $"brought back {n} window{(n == 1 ? "" : "s")}");
                return;
            }
            long now = Environment.TickCount64;
            if (h != _rescueH || now - _rescueAt > 8000) _rescueStage = 0;
            _rescueH = h;
            _rescueAt = now;
            var all = Displays.All();
            int stage = _rescueStage % 3;
            _rescueStage++;
            if (stage == 0 && all.Count < 2) stage = 1;
            switch (stage)
            {
                case 0:
                    Next(h, 1, say);
                    say("sent it to the other screen. press again to flip fullscreen");
                    break;
                case 1:
                    Native.ForceForeground(h);
                    Native.Combo(0x12, 0x0D);
                    say("flipped fullscreen. press again to force it borderless");
                    break;
                default:
                    Borderless(h, DisplayOf(h, all));
                    say(IsBorderless(h) ? "forced it borderless. press again to start over" : "put it back");
                    break;
            }
        }

        public static int BringBack()
        {
            var all = Displays.All();
            var main = Displays.Primary(all);
            int moved = 0;
            foreach (var w in WinList.All(false))
            {
                var f = w.Frame;
                bool seen = all.Any(d =>
                {
                    int ix = Math.Min(f.Right, d.Work.Right) - Math.Max(f.Left, d.Work.Left);
                    int iy = Math.Min(f.Bottom, d.Work.Bottom) - Math.Max(f.Top, d.Work.Top);
                    return ix >= 80 && iy >= 40;
                });
                if (seen) continue;
                if (Native.IsZoomed(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
                Center(w.Hwnd, main);
                moved++;
            }
            return moved;
        }

        public static void Tile(List<IntPtr> wins, Display d)
        {
            var list = wins.Where(Native.IsWindow).ToList();
            int n = list.Count;
            if (n == 0) return;
            int cols = n <= 3 ? n : (int)Math.Ceiling(Math.Sqrt(n));
            int rows = (int)Math.Ceiling(n / (double)cols);
            int gap = (int)Math.Round(8 * d.Scale);
            int cw = (d.Work.Width - gap * (cols + 1)) / cols;
            int ch = (d.Work.Height - gap * (rows + 1)) / rows;
            for (int i = 0; i < n; i++)
            {
                var h = list[i];
                int r = i / cols, c = i % cols;
                int inRow = Math.Min(cols, n - r * cols);
                int w = inRow < cols ? (d.Work.Width - gap * (inRow + 1)) / inRow : cw;
                int x = d.Work.Left + gap + c * (w + gap);
                int y = d.Work.Top + gap + r * (ch + gap);
                if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                SnapZones.Place(h, new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + ch });
            }
            Native.ForceForeground(list[0]);
        }

        public static string ZoneName(System.Windows.Rect z, int index, int count)
        {
            bool tall = z.Height >= 0.99, wide = z.Width >= 0.99;
            if (tall && wide) return "full screen";
            string col = z.X < 0.05 ? "left" : z.X + z.Width > 0.95 ? "right" : "middle";
            if (tall)
            {
                if (Math.Abs(z.Width - 0.5) < 0.02) return col + " half";
                if (Math.Abs(z.Width - 1 / 3.0) < 0.02) return col + " third";
                return col == "middle" ? "center" : col + " side";
            }
            string row = z.Y < 0.05 ? "top" : "bottom";
            if (wide) return row + " half";
            return row + " " + (col == "middle" ? "middle" : col);
        }
    }
}
