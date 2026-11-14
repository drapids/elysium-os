using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class WindowShade : IDisposable
    {
        sealed class Rolled
        {
            public IntPtr H;
            public int Full;
            public int Small;
            public ShadeTab Tab = null!;
            public bool Busy;
        }

        readonly Dictionary<IntPtr, Rolled> _rolled = new();
        readonly AppCatalog _cat;
        readonly DispatcherTimer _prune = new() { Interval = TimeSpan.FromSeconds(1) };

        public Action<string>? Say;

        public WindowShade(AppCatalog cat)
        {
            _cat = cat;
            WinEvents.Moved += h =>
            {
                if (_rolled.TryGetValue(h, out var r) && !r.Busy) r.Tab.Follow(h);
            };
            WinEvents.Minimized += (h, start) =>
            {
                if (_rolled.TryGetValue(h, out var r)) r.Tab.Follow(h);
            };
            _prune.Tick += (_, _) =>
            {
                foreach (var r in _rolled.Values.Where(x => !Native.IsWindow(x.H)).ToList()) Drop(r);
                if (_rolled.Count == 0) _prune.Stop();
            };
        }

        public bool IsRolled(IntPtr h) => _rolled.ContainsKey(h);

        public void Toggle(IntPtr h)
        {
            if (_rolled.TryGetValue(h, out var r))
            {
                Unroll(r, false);
                return;
            }
            if (!WinList.IsApp(h))
            {
                Say?.Invoke("click on a window first");
                return;
            }
            if (Native.IsZoomed(h) || CursorLock.IsFullscreen(h, out _))
            {
                Say?.Invoke("un-maximize it first");
                return;
            }
            if (!Native.GetWindowRect(h, out var wr)) return;
            var frame = Native.FrameBounds(h);
            double scale = Native.DpiScaleAt(frame.Left + 10, frame.Top + 10);
            int bottomPad = Math.Max(0, wr.Bottom - frame.Bottom);
            int topPad = Math.Max(0, frame.Top - wr.Top);
            int cap = (int)Math.Round(34 * scale);
            if (Native.DwmGetWindowAttribute(h, 5, out Native.RECT buttons, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) == 0 && buttons.Bottom > topPad && buttons.Bottom < 140 * scale)
                cap = buttons.Bottom - topPad + (int)Math.Round(2 * scale);
            int small = topPad + cap + bottomPad;
            if (small >= wr.Height - 10) return;
            var rolled = new Rolled { H = h, Full = wr.Height, Small = small, Tab = new ShadeTab(_cat) };
            rolled.Tab.Clicked += () => Unroll(rolled, true);
            _rolled[h] = rolled;
            WinEvents.Watch(h);
            _prune.Start();
            Sfx.Play("minimize");
            Animate(rolled, wr.Height, small, 200, () => rolled.Tab.ShowOn(h));
        }

        void Animate(Rolled r, int from, int to, int ms, Action? done)
        {
            r.Busy = true;
            Frames.Run(Math.Max(90, ms * Theme.Speed), t =>
            {
                if (!Native.IsWindow(r.H)) return;
                double e = to < from ? 1 - Math.Pow(1 - t, 3) : 1 - Math.Pow(1 - t, 4);
                int h = (int)Math.Round(from + (to - from) * e);
                Native.GetWindowRect(r.H, out var wr);
                Native.SetWindowPos(r.H, IntPtr.Zero, 0, 0, wr.Width, h, Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }, () =>
            {
                r.Busy = false;
                done?.Invoke();
            }, r);
        }

        void Unroll(Rolled r, bool fromTab)
        {
            if (!_rolled.Remove(r.H)) return;
            WinEvents.Unwatch(r.H);
            void Go()
            {
                if (!Native.IsWindow(r.H)) return;
                Native.GetWindowRect(r.H, out var wr);
                Animate(r, wr.Height, r.Full, 150, null);
                Native.Focus(r.H);
            }
            if (fromTab) r.Tab.TakeOut(Go);
            else
            {
                r.Tab.TakeOut(null);
                Go();
            }
        }

        void Drop(Rolled r)
        {
            _rolled.Remove(r.H);
            WinEvents.Unwatch(r.H);
            r.Tab.TakeOut(null);
        }

        public void Dispose()
        {
            foreach (var r in _rolled.Values.ToList())
            {
                if (Native.IsWindow(r.H) && Native.GetWindowRect(r.H, out var wr))
                    Native.SetWindowPos(r.H, IntPtr.Zero, 0, 0, wr.Width, r.Full, Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                try
                {
                    r.Tab.Close();
                }
                catch
                {
                }
            }
            _rolled.Clear();
        }
    }

    internal sealed class ShadeTab : Window
    {
        readonly Border _tab;
        readonly Image _icon = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly ScaleTransform _scale = new(1, 1);
        readonly TranslateTransform _shift = new();
        readonly AppCatalog _cat;
        IntPtr _target;
        bool _leaving;

        public event Action? Clicked;

        public ShadeTab(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 0, true);
            Title = "elysium shade";
            SizeToContent = SizeToContent.WidthAndHeight;
            _tab = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Padding = new Thickness(10, 3, 10, 5),
                BorderThickness = new Thickness(1, 0, 1, 1),
                Cursor = Cursors.Hand,
                ToolTip = "roll it back down",
                RenderTransformOrigin = new Point(0.5, 0)
            };
            _tab.SetResourceReference(Border.BackgroundProperty, "PillBg");
            _tab.SetResourceReference(Border.BorderBrushProperty, "PillStroke");
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_shift);
            _tab.RenderTransform = group;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_icon);
            row.Children.Add(Kit.Icon("\uE70D", 10));
            _tab.Child = row;
            var host = new Grid { Margin = new Thickness(6, 0, 6, 8) };
            host.Children.Add(_tab);
            Content = host;
            _tab.MouseEnter += (_, _) => Hover(true);
            _tab.MouseLeave += (_, _) => Hover(false);
            _tab.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!_leaving) Clicked?.Invoke();
            };
        }

        void Hover(bool on)
        {
            if (_leaving) return;
            double to = on ? 4 : 0;
            Frames.Run(140, t => _shift.Y = _shift.Y + (to - _shift.Y) * Frames.Out(t), null, _shift);
        }

        public void ShowOn(IntPtr target)
        {
            _target = target;
            _leaving = false;
            var path = Native.ProcessPath(target);
            if (path != null) _cat.Icons.Get(path, 32, true, img => _icon.Source = img);
            var me = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowLongPtr(me, Native.GWLP_HWNDPARENT, target);
            _scale.ScaleX = 1;
            _scale.ScaleY = 0.2;
            _tab.Opacity = 0;
            Show();
            Follow(target);
            Frames.Run(Math.Max(100, 220 * Theme.Speed), t =>
            {
                double e = Frames.Spring(t);
                _scale.ScaleY = 0.2 + 0.8 * e;
                _tab.Opacity = Math.Min(1, t * 3);
            }, null, _scale);
        }

        public void Follow(IntPtr target)
        {
            if (_leaving || !IsVisible) return;
            if (!Native.IsWindow(target) || Native.IsIconic(target))
            {
                Opacity = 0;
                return;
            }
            Opacity = 1;
            var f = Native.FrameBounds(target);
            double scale = Native.DpiScaleAt(f.Left + 10, f.Top + 10);
            UpdateLayout();
            int w = (int)Math.Ceiling(ActualWidth * scale);
            int x = f.Right - w - (int)(70 * scale);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, Math.Max(f.Left, x), f.Bottom - 1, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void TakeOut(Action? then)
        {
            if (_leaving) return;
            _leaving = true;
            if (!IsVisible)
            {
                then?.Invoke();
                Close();
                return;
            }
            bool fired = false;
            Frames.Run(Math.Max(90, 170 * Theme.Speed), t =>
            {
                double e = Frames.Out(t);
                _scale.ScaleX = 1 + 0.35 * e;
                _scale.ScaleY = 1 + 0.35 * e;
                _shift.Y = 14 * e;
                _tab.Opacity = 1 - e;
                if (!fired && t > 0.45)
                {
                    fired = true;
                    then?.Invoke();
                }
            }, () =>
            {
                if (!fired) then?.Invoke();
                Close();
            }, _scale);
        }
    }
}
