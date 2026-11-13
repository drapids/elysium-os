using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Heft.Ui;

namespace Heft
{
    internal sealed class Pip
    {
        sealed class Entry
        {
            public IntPtr H;
            public Native.RECT Rect;
            public bool Max;
            public bool WasTop;
            public bool Layered;
            public byte Alpha = 255;
            public PipChip? Chip;
            public Native.RECT Last;
        }

        static readonly HashSet<string> Shell = new(StringComparer.Ordinal)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
        };

        readonly Settings _s;
        readonly List<Entry> _list = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

        public event Action<string>? Say;

        public Pip(Settings s)
        {
            _s = s;
            _timer.Tick += (_, _) => Tick();
        }

        public bool Has(IntPtr h) => _list.Any(e => e.H == h);

        public void Toggle()
        {
            IntPtr fg = Native.GetForegroundWindow();
            var hit = _list.FirstOrDefault(e => e.H == fg);
            if (hit != null)
            {
                Restore(hit, true);
                return;
            }
            if (!Pop(fg)) Say?.Invoke("can't pop that one out");
        }

        public bool Pop(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h)) return false;
            if ((Native.Style(h) & Native.WS_CHILD) != 0) return false;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;
            if (Shell.Contains(Native.ClassName(h))) return false;
            if (Has(h)) return false;

            var e = new Entry { H = h, Max = Native.IsZoomed(h), WasTop = (Native.ExStyle(h) & Native.WS_EX_TOPMOST) != 0 };
            if (e.Max || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out e.Rect);
            var frame = Native.FrameBounds(h);
            double aspect = frame.Height > 0 ? frame.Width / (double)frame.Height : 16 / 9.0;
            aspect = Math.Clamp(aspect, 1.15, 2.1);

            var d = Displays.AtCursor();
            int width = (int)(d.Work.Width * Math.Clamp(_s.PipSize, 0.12, 0.5));
            int height = (int)(width / aspect);
            int margin = (int)(16 * d.Scale);
            int x = d.Work.Right - width - margin;
            int y = d.Work.Bottom - height - margin;
            foreach (var other in _list)
            {
                var of = Native.FrameBounds(other.H);
                if (of.Right > x && of.Left < x + width && of.Bottom > y && of.Top < y + height) y = of.Top - height - margin - (int)(34 * d.Scale);
            }
            y = Math.Max(d.Work.Top + margin, y);
            Fit(h, new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height }, Native.HWND_TOPMOST);

            e.Chip = new PipChip();
            var entry = e;
            e.Chip.Back += () => Restore(entry, true);
            e.Chip.Fade += () => CycleAlpha(entry);
            e.Chip.Grow += dir => Resize(entry, dir);
            _list.Add(e);
            Follow(e, true);
            _timer.Start();
            Sfx.Play("open");
            return true;
        }

        static void Fit(IntPtr h, Native.RECT frame, IntPtr after)
        {
            Native.GetWindowRect(h, out var wr);
            var fb = Native.FrameBounds(h);
            int padL = Math.Clamp(fb.Left - wr.Left, 0, 40), padT = Math.Clamp(fb.Top - wr.Top, 0, 40);
            int padR = Math.Clamp(wr.Right - fb.Right, 0, 40), padB = Math.Clamp(wr.Bottom - fb.Bottom, 0, 40);
            Native.SetWindowPos(h, after, frame.Left - padL, frame.Top - padT, frame.Width + padL + padR, frame.Height + padT + padB, Native.SWP_NOACTIVATE);
        }

        void Resize(Entry e, int dir)
        {
            if (!Native.IsWindow(e.H)) return;
            var f = Native.FrameBounds(e.H);
            if (f.Width <= 0 || f.Height <= 0) return;
            double k = dir > 0 ? 1.1 : 1 / 1.1;
            var work = Native.WorkAreaAt(f.Left + f.Width / 2, f.Top + f.Height / 2);
            int w = (int)Math.Clamp(f.Width * k, 220, work.Width * 0.8);
            int h = (int)(w * f.Height / (double)f.Width);
            var r = new Native.RECT { Left = f.Right - w, Top = f.Bottom - h, Right = f.Right, Bottom = f.Bottom };
            if (r.Top < work.Top)
            {
                r.Top = work.Top;
                r.Bottom = r.Top + h;
            }
            if (r.Left < work.Left)
            {
                r.Left = work.Left;
                r.Right = r.Left + w;
            }
            Fit(e.H, r, Native.HWND_TOPMOST);
            Follow(e, true);
        }

        void CycleAlpha(Entry e)
        {
            if (!Native.IsWindow(e.H)) return;
            byte next = e.Alpha switch
            {
                255 => (byte)210,
                210 => (byte)150,
                _ => (byte)255
            };
            long ex = Native.ExStyle(e.H);
            if ((ex & Native.WS_EX_LAYERED) == 0)
            {
                Native.SetWindowLongPtr(e.H, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_LAYERED));
                e.Layered = true;
            }
            Native.SetLayeredWindowAttributes(e.H, 0, next, 2);
            e.Alpha = next;
            Say?.Invoke($"opacity {Math.Round(next / 255.0 * 100)}%");
        }

        void Restore(Entry e, bool focus)
        {
            _list.Remove(e);
            try
            {
                e.Chip?.Close();
            }
            catch
            {
            }
            e.Chip = null;
            if (_list.Count == 0) _timer.Stop();
            if (!Native.IsWindow(e.H)) return;
            if (e.Alpha != 255 || e.Layered)
            {
                Native.SetLayeredWindowAttributes(e.H, 0, 255, 2);
                if (e.Layered)
                {
                    long ex = Native.ExStyle(e.H);
                    Native.SetWindowLongPtr(e.H, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_LAYERED));
                }
            }
            Native.SetWindowPos(e.H, e.WasTop ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, e.Rect.Left, e.Rect.Top, e.Rect.Width, e.Rect.Height, Native.SWP_NOACTIVATE);
            if (e.Max) Native.ShowWindow(e.H, Native.SW_MAXIMIZE);
            if (focus) Native.ForceForeground(e.H);
            Sfx.Play("close");
        }

        public void ReleaseAll()
        {
            foreach (var e in _list.ToList()) Restore(e, false);
        }

        void Tick()
        {
            foreach (var e in _list.ToList())
            {
                if (!Native.IsWindow(e.H))
                {
                    _list.Remove(e);
                    try
                    {
                        e.Chip?.Close();
                    }
                    catch
                    {
                    }
                    continue;
                }
                if (Native.IsZoomed(e.H))
                {
                    e.Max = false;
                    Restore(e, false);
                    continue;
                }
                Follow(e, false);
            }
            if (_list.Count == 0) _timer.Stop();
        }

        static void Follow(Entry e, bool force)
        {
            if (e.Chip == null) return;
            bool show = Native.IsWindowVisible(e.H) && !Native.IsIconic(e.H);
            var f = Native.FrameBounds(e.H);
            if (!force && f.Left == e.Last.Left && f.Top == e.Last.Top && f.Right == e.Last.Right && e.Chip.IsVisible == show) return;
            e.Last = f;
            e.Chip.Place(f, show);
        }
    }

    internal sealed class PipChip : Window
    {
        readonly Border _surface;

        public event Action? Back;
        public event Action? Fade;
        public event Action<int>? Grow;

        public PipChip()
        {
            Glass.Attach(this, 10, true);
            Title = "heft pip";
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(10);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
            var label = Kit.Text("pip", 11, true, "SubText");
            label.Margin = new Thickness(6, 0, 6, 0);
            row.Children.Add(label);
            row.Children.Add(Kit.Plain("\uE7A7", () => Back?.Invoke(), "put it back", 10));
            row.Children.Add(Kit.Plain("\uE706", () => Fade?.Invoke(), "see-through", 10));
            row.Children.Add(Kit.Plain("\uE740", () => Grow?.Invoke(1), "bigger (or scroll here)", 10));
            row.Children.Add(Kit.Plain("\uE73F", () => Grow?.Invoke(-1), "smaller", 10));
            _surface.Child = row;
            Content = _surface;
            MouseWheel += (_, e) =>
            {
                e.Handled = true;
                Grow?.Invoke(e.Delta > 0 ? 1 : -1);
            };
        }

        public void Place(Native.RECT f, bool show)
        {
            if (!show)
            {
                if (IsVisible) Hide();
                return;
            }
            var h = new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible)
            {
                Show();
                Glass.SetBlur(this, true);
            }
            UpdateLayout();
            Native.GetWindowRect(h, out var me);
            int gap = (int)(6 * Native.DpiScaleAt(f.Left + 10, f.Top + 10));
            int x = f.Right - me.Width;
            int y = f.Top - me.Height - gap;
            var work = Native.WorkAreaAt(f.Left + f.Width / 2, f.Top + f.Height / 2);
            if (y < work.Top) y = f.Bottom + gap;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }
}
