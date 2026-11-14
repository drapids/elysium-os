using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shapes = System.Windows.Shapes;

namespace Elysium.Ui
{
    internal static class Overlay
    {
        public static void ClickThrough(Window w)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.ShowActivated = false;
            w.ResizeMode = ResizeMode.NoResize;
            w.IsHitTestVisible = false;
            w.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                Native.MakeToolWindow(h, true);
                long ex = Native.ExStyle(h);
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | 0x20 | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE));
            };
        }

        public static bool Trackable(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !WinList.IsApp(h)) return false;
            if (Native.IsIconic(h)) return false;
            if (CursorLock.IsFullscreen(h, out _)) return false;
            return true;
        }
    }

    internal sealed class FocusRing : Window
    {
        readonly Border _ring;
        IntPtr _target;
        bool _on;
        Native.RECT _last;

        public FocusRing()
        {
            Overlay.ClickThrough(this);
            Title = "elysium focus ring";
            _ring = new Border { BorderThickness = new Thickness(2), Margin = new Thickness(6) };
            Content = _ring;
            Theme.Applied += Paint;
            Paint();
        }

        void Paint()
        {
            var a = Theme.Accent;
            _ring.BorderBrush = new SolidColorBrush(Color.FromArgb(0xF0, a.R, a.G, a.B));
            _ring.Effect = new DropShadowEffect { Color = a, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.75 };
            _ring.CornerRadius = new CornerRadius(9);
        }

        public void Enable(bool on)
        {
            if (on == _on) return;
            _on = on;
            WinEvents.FollowForeground(on);
            if (on)
            {
                WinEvents.Foreground += OnFg;
                WinEvents.Moved += OnMove;
                Track(Native.GetForegroundWindow());
            }
            else
            {
                WinEvents.Foreground -= OnFg;
                WinEvents.Moved -= OnMove;
                Hide();
            }
        }

        void OnFg(IntPtr h) => Track(h);

        void OnMove(IntPtr h)
        {
            if (h == _target) Track(h);
        }

        public void Track(IntPtr h)
        {
            if (!_on) return;
            _target = h;
            if (!Overlay.Trackable(h) || Native.IsZoomed(h))
            {
                if (IsVisible) Hide();
                return;
            }
            var f = Native.FrameBounds(h);
            double scale = Native.DpiScaleAt(f.Left + f.Width / 2, f.Top + f.Height / 2);
            int pad = (int)Math.Round(8 * scale);
            var r = new Native.RECT { Left = f.Left - pad, Top = f.Top - pad, Right = f.Right + pad, Bottom = f.Bottom + pad };
            var me = new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible) Show();
            if (r.Left == _last.Left && r.Top == _last.Top && r.Width == _last.Width && r.Height == _last.Height) return;
            bool resized = r.Width != _last.Width || r.Height != _last.Height;
            _last = r;
            Native.SetWindowPos(me, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE | (resized ? 0 : Native.SWP_NOSIZE));
        }
    }

    internal sealed class Spotlight
    {
        readonly List<Window> _layers = new();
        readonly Dictionary<Window, (Display d, Shapes.Path path)> _info = new();
        IntPtr _target;
        double _dim = 0.6;

        public bool On { get; private set; }

        public void Toggle(double dim)
        {
            if (On) Off();
            else Start(dim);
        }

        public void Start(double dim)
        {
            Off();
            _dim = Math.Clamp(dim, 0.2, 0.9);
            On = true;
            foreach (var d in Displays.All())
            {
                var w = new Window { Title = "elysium spotlight" };
                Overlay.ClickThrough(w);
                var path = new Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb((byte)(_dim * 255), 0, 0, 0)), IsHitTestVisible = false };
                w.Content = path;
                _layers.Add(w);
                _info[w] = (d, path);
                new WindowInteropHelper(w).EnsureHandle();
                w.Opacity = 0;
                w.Show();
                Glass.Fill(w, d.Bounds);
            }
            WinEvents.FollowForeground(true);
            WinEvents.Foreground += OnFg;
            WinEvents.Moved += OnMove;
            Track(Native.GetForegroundWindow());
            Frames.Run(260 * Theme.Speed, t =>
            {
                foreach (var w in _layers) w.Opacity = Frames.Out(t);
            }, null, this);
        }

        public void Off()
        {
            if (!On) return;
            On = false;
            WinEvents.FollowForeground(false);
            WinEvents.Foreground -= OnFg;
            WinEvents.Moved -= OnMove;
            var dying = _layers.ToArray();
            _layers.Clear();
            _info.Clear();
            Frames.Run(200 * Theme.Speed, t =>
            {
                foreach (var w in dying) w.Opacity = 1 - t;
            }, () =>
            {
                foreach (var w in dying)
                {
                    try
                    {
                        w.Close();
                    }
                    catch
                    {
                    }
                }
            }, dying);
        }

        void OnFg(IntPtr h) => Track(h);

        void OnMove(IntPtr h)
        {
            if (h == _target) Track(h);
        }

        void Track(IntPtr h)
        {
            if (!On) return;
            bool ok = h != IntPtr.Zero && Native.IsWindow(h) && WinList.IsApp(h) && !Native.IsIconic(h);
            if (ok) _target = h;
            Native.RECT f = ok ? Native.FrameBounds(h) : (Native.IsWindow(_target) ? Native.FrameBounds(_target) : default);
            foreach (var w in _layers)
            {
                var (d, path) = _info[w];
                var full = new RectangleGeometry(new Rect(0, 0, d.Bounds.Width / d.Scale, d.Bounds.Height / d.Scale));
                if (f.Width <= 0)
                {
                    path.Data = full;
                    continue;
                }
                var hole = new Rect((f.Left - d.Bounds.Left) / d.Scale - 4, (f.Top - d.Bounds.Top) / d.Scale - 4, f.Width / d.Scale + 8, f.Height / d.Scale + 8);
                path.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole, 10, 10));
            }
        }
    }
}
