using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Heft.Ui;

namespace Heft
{
    internal sealed class SnapZones : IDisposable
    {
        public static readonly (string id, string label)[] Layouts =
        {
            ("auto", "Auto"),
            ("halves", "Halves"),
            ("grid", "Quarters"),
            ("thirds", "Thirds"),
            ("focus", "Focus"),
            ("mix", "Mix")
        };

        readonly Settings _s;
        readonly PhysicsEngine _physics;
        readonly Native.WinEventProc _proc;
        readonly IntPtr _hook;
        readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(15) };
        readonly Dictionary<string, ZoneOverlay> _overlays = new();
        IntPtr _moving;
        bool _viaLerp;
        long _waitSince;
        ZoneOverlay? _shown;
        int _zone = -1;

        public SnapZones(Settings s, PhysicsEngine physics)
        {
            _s = s;
            _physics = physics;
            _proc = OnEvent;
            _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            _poll.Tick += (_, _) => Poll();
            var ui = Application.Current.Dispatcher;
            _physics.LerpBegan += h => ui.BeginInvoke(new Action(() =>
            {
                if (!_s.SnapZones) return;
                _moving = h;
                _viaLerp = true;
                _waitSince = 0;
                _zone = -1;
                _poll.Start();
            }));
            _physics.LerpDropped += h => ui.BeginInvoke(new Action(() => Finish(h)));
        }

        public static List<Rect> Zones(string layout, Display d)
        {
            if (layout == "auto")
            {
                double aspect = d.Work.Height > 0 ? (double)d.Work.Width / d.Work.Height : 1.7;
                layout = aspect > 2.1 ? "thirds" : aspect < 1.1 ? "grid" : "mix";
            }
            return layout switch
            {
                "halves" => new List<Rect> { new(0, 0, 0.5, 1), new(0.5, 0, 0.5, 1) },
                "grid" => new List<Rect> { new(0, 0, 0.5, 0.5), new(0.5, 0, 0.5, 0.5), new(0, 0.5, 0.5, 0.5), new(0.5, 0.5, 0.5, 0.5) },
                "thirds" => new List<Rect> { new(0, 0, 1 / 3.0, 1), new(1 / 3.0, 0, 1 / 3.0, 1), new(2 / 3.0, 0, 1 / 3.0, 1) },
                "focus" => new List<Rect> { new(0, 0, 0.22, 1), new(0.22, 0, 0.56, 1), new(0.78, 0, 0.22, 1) },
                _ => new List<Rect> { new(0, 0, 0.5, 1), new(0.5, 0, 0.5, 0.5), new(0.5, 0.5, 0.5, 0.5) }
            };
        }

        public string LayoutFor(Display d) => _s.SnapLayouts.TryGetValue(d.Key, out var l) && l.Length > 0 ? l : "auto";

        public static Native.RECT ToPx(Rect frac, Display d, int gapPx)
        {
            var w = d.Work;
            int l = w.Left + (int)Math.Round(frac.X * w.Width);
            int t = w.Top + (int)Math.Round(frac.Y * w.Height);
            int r = w.Left + (int)Math.Round((frac.X + frac.Width) * w.Width);
            int b = w.Top + (int)Math.Round((frac.Y + frac.Height) * w.Height);
            int half = gapPx / 2;
            return new Native.RECT
            {
                Left = l + (frac.X <= 0.001 ? gapPx : half),
                Top = t + (frac.Y <= 0.001 ? gapPx : half),
                Right = r - (frac.X + frac.Width >= 0.999 ? gapPx : half),
                Bottom = b - (frac.Y + frac.Height >= 0.999 ? gapPx : half)
            };
        }

        public static void Place(IntPtr h, Native.RECT frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || !Native.IsWindow(h)) return;
            if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out var wr);
            var fb = Native.FrameBounds(h);
            int padL = Math.Clamp(fb.Left - wr.Left, 0, 40), padT = Math.Clamp(fb.Top - wr.Top, 0, 40);
            int padR = Math.Clamp(wr.Right - fb.Right, 0, 40), padB = Math.Clamp(wr.Bottom - fb.Bottom, 0, 40);
            Native.SetWindowPos(h, IntPtr.Zero, frame.Left - padL, frame.Top - padT, frame.Width + padL + padR, frame.Height + padT + padB, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        int Gap(Display d) => (int)Math.Round(Math.Clamp(_s.SnapGap, 0, 40) * d.Scale);

        void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0) return;
            if (evt == Native.EVENT_SYSTEM_MOVESIZESTART)
            {
                if (!_s.SnapZones) return;
                _moving = hwnd;
                _zone = -1;
                _poll.Start();
            }
            else if (evt == Native.EVENT_SYSTEM_MOVESIZEEND)
            {
                Finish(hwnd);
            }
        }

        void Finish(IntPtr hwnd)
        {
            _poll.Stop();
            var h = _moving;
            _moving = IntPtr.Zero;
            _viaLerp = false;
            var shown = _shown;
            int zone = _zone;
            Hide();
            if (shown != null && zone >= 0 && h == hwnd)
            {
                var d = shown.Display;
                var zones = Zones(LayoutFor(d), d);
                if (zone < zones.Count)
                {
                    var target = ToPx(zones[zone], d, Gap(d));
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Place(h, target);
                        Sfx.Play("tick");
                    }), DispatcherPriority.Background);
                }
            }
            Application.Current.Dispatcher.BeginInvoke(new Action(() => _physics.Hush = false), DispatcherPriority.ApplicationIdle);
        }

        void Poll()
        {
            if (_moving == IntPtr.Zero || !Native.IsWindow(_moving))
            {
                _poll.Stop();
                Hide();
                return;
            }
            if (_viaLerp && !_physics.LerpHeld)
            {
                long now = Environment.TickCount64;
                if (_waitSince == 0) _waitSince = now;
                if (!_physics.Hush || now - _waitSince > 1000)
                {
                    _poll.Stop();
                    _moving = IntPtr.Zero;
                    _viaLerp = false;
                    Hide();
                    _physics.Hush = false;
                }
                return;
            }
            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            if (!shift)
            {
                if (_shown != null) Hide();
                _physics.Hush = false;
                return;
            }
            _physics.Hush = true;
            var d = Displays.AtCursor();
            if (_shown == null || _shown.Display.Key != d.Key)
            {
                Hide();
                if (!_overlays.TryGetValue(d.Key, out var o))
                {
                    o = new ZoneOverlay(d);
                    _overlays[d.Key] = o;
                }
                o.Display = d;
                o.Open(Zones(LayoutFor(d), d), _s.SnapGap);
                _shown = o;
                _zone = -1;
            }
            Native.GetCursorPos(out var c);
            var zones = Zones(LayoutFor(d), d);
            int hit = -1;
            int gap = Gap(d);
            for (int i = 0; i < zones.Count; i++)
            {
                var r = ToPx(zones[i], d, gap);
                if (c.X >= r.Left - gap && c.X < r.Right + gap && c.Y >= r.Top - gap && c.Y < r.Bottom + gap)
                {
                    hit = i;
                    break;
                }
            }
            if (hit != _zone)
            {
                _zone = hit;
                _shown.Light(hit);
            }
        }

        void Hide()
        {
            _shown?.Away();
            _shown = null;
            _zone = -1;
        }

        public void Dispose()
        {
            _poll.Stop();
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
            foreach (var o in _overlays.Values)
            {
                try
                {
                    o.Close();
                }
                catch
                {
                }
            }
        }
    }

    internal sealed class ZoneOverlay : Window
    {
        public Display Display;
        readonly Canvas _canvas = new();
        readonly List<Border> _tiles = new();
        List<Rect> _zones = new();
        double _gap;

        public ZoneOverlay(Display d)
        {
            Display = d;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Title = "heft zones";
            Content = _canvas;
            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(h, true);
                long ex = Native.ExStyle(h);
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | 0x20 | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE));
            };
            _canvas.SizeChanged += (_, _) => Lay();
        }

        public void Open(List<Rect> zones, double gap)
        {
            _zones = zones;
            _gap = gap;
            _canvas.Children.Clear();
            _tiles.Clear();
            for (int i = 0; i < zones.Count; i++)
            {
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(14 * Theme.Corner),
                    BorderThickness = new Thickness(1.5),
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0x10, 0x14)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF))
                };
                _tiles.Add(tile);
                _canvas.Children.Add(tile);
            }
            var h = new WindowInteropHelper(this).EnsureHandle();
            var w = Display.Work;
            if (!IsVisible) Show();
            Native.SetWindowPos(h, Native.HWND_TOPMOST, w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE);
            Lay();
            Glass.FadeIn(_canvas, 0, 140);
        }

        void Lay()
        {
            double W = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : ActualWidth;
            double H = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : ActualHeight;
            if (W <= 0 || H <= 0) return;
            for (int i = 0; i < _tiles.Count && i < _zones.Count; i++)
            {
                var z = _zones[i];
                double l = z.X * W + (z.X <= 0.001 ? _gap : _gap / 2);
                double t = z.Y * H + (z.Y <= 0.001 ? _gap : _gap / 2);
                double r = (z.X + z.Width) * W - (z.X + z.Width >= 0.999 ? _gap : _gap / 2);
                double b = (z.Y + z.Height) * H - (z.Y + z.Height >= 0.999 ? _gap : _gap / 2);
                Canvas.SetLeft(_tiles[i], l);
                Canvas.SetTop(_tiles[i], t);
                _tiles[i].Width = Math.Max(0, r - l);
                _tiles[i].Height = Math.Max(0, b - t);
            }
        }

        public void Light(int index)
        {
            var accent = Theme.Accent;
            for (int i = 0; i < _tiles.Count; i++)
            {
                bool on = i == index;
                _tiles[i].Background = new SolidColorBrush(on ? Color.FromArgb(0x55, accent.R, accent.G, accent.B) : Color.FromArgb(0x30, 0x10, 0x10, 0x14));
                _tiles[i].BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0xE0, accent.R, accent.G, accent.B) : Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
            }
        }

        public void Away()
        {
            if (IsVisible) Hide();
        }
    }
}
