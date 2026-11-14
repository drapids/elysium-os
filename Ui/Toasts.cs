using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class Toasts
    {
        sealed class ToastWindow : Window
        {
            public readonly Note Note;
            public readonly DispatcherTimer Life = new();
            public bool Dying;
            public bool Placed;
            public double TargetX, TargetY, CurX, CurY;
            public int LastX = int.MinValue, LastY = int.MinValue;

            public ToastWindow(Note n, double seconds)
            {
                Note = n;
                Glass.Attach(this, 14, true);
                ShowActivated = false;
                Width = 360;
                SizeToContent = SizeToContent.Height;
                Life.Interval = TimeSpan.FromSeconds(seconds);
            }
        }

        readonly Settings _s;
        readonly Func<Display, double> _top;
        readonly List<ToastWindow> _open = new();
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        double _lastFrame;
        bool _hooked;
        Display? _display;

        public event Action<Note>? Clicked;

        public Toasts(Settings settings, Func<Display, double> topPx)
        {
            _s = settings;
            _top = topPx;
        }

        public void Show(Note n)
        {
            var here = Displays.AtCursor();
            if (_display == null || _display.Index != here.Index || _display.Key != here.Key)
            {
                _display = here;
                foreach (var old in _open)
                {
                    old.Placed = false;
                }
            }
            if (_open.Count(t => !t.Dying) >= 4) Close(_open.First(t => !t.Dying));

            var w = new ToastWindow(n, Math.Clamp(_s.ToastSeconds, 2, 30));
            var surface = Glass.Surface(14);
            var grid = new Grid { Margin = new Thickness(14, 12, 10, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            badge.Child = Kit.Icon(n.Glyph, 15, "AccentFill");
            grid.Children.Add(badge);

            var text = new StackPanel();
            var top = new DockPanel();
            var time = Kit.Text("now", 11, false, "Faint");
            DockPanel.SetDock(time, Dock.Right);
            top.Children.Add(time);
            top.Children.Add(Kit.Text(n.App, 11.5, false, "SubText"));
            text.Children.Add(top);
            if (!string.IsNullOrWhiteSpace(n.Title)) text.Children.Add(Kit.Text(n.Title, 13.5, true));
            if (!string.IsNullOrWhiteSpace(n.Body))
            {
                var body = Kit.Text(n.Body, 12.5, false, "SubText");
                body.TextWrapping = TextWrapping.Wrap;
                body.TextTrimming = TextTrimming.CharacterEllipsis;
                body.MaxHeight = 54;
                text.Children.Add(body);
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var close = Kit.Plain("\uE8BB", () => Close(w), "dismiss", 9);
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(6, -4, 0, 0);
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);

            surface.Child = grid;
            w.Content = surface;
            surface.Cursor = Cursors.Hand;
            surface.MouseLeftButtonUp += (_, _) =>
            {
                Clicked?.Invoke(n);
                Close(w);
            };
            w.MouseEnter += (_, _) => w.Life.Stop();
            w.MouseLeave += (_, _) =>
            {
                if (!w.Dying) w.Life.Start();
            };
            w.Life.Tick += (_, _) => Close(w);

            var d = _display;
            w.Left = -20000;
            w.Top = -20000;
            surface.Opacity = 0;
            w.Show();
            var h = new WindowInteropHelper(w).Handle;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Work.Right - (int)(380 * d.Scale), (int)_top(d) + (int)(10 * d.Scale), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            w.UpdateLayout();
            _open.Add(w);
            Layout();
            Step(1);
            w.Life.Start();

            var slide = new TranslateTransform(40, 0);
            surface.RenderTransform = slide;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            fadeIn.Completed += (_, _) =>
            {
                if (!w.Dying) Glass.SetBlur(w, true);
            };
            surface.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        void Layout()
        {
            var d = _display;
            if (d == null) return;
            double y = _top(d) + 10 * d.Scale;
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                var w = _open[i];
                if (w.Dying) continue;
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) continue;
                Native.GetWindowRect(h, out var r);
                int width = r.Width > 0 ? r.Width : (int)(360 * d.Scale);
                int height = r.Height > 0 ? r.Height : (int)(64 * d.Scale);
                w.TargetX = d.Work.Right - width - (int)(14 * d.Scale);
                w.TargetY = y;
                if (!w.Placed)
                {
                    w.Placed = true;
                    w.CurX = w.TargetX;
                    w.CurY = y;
                }
                y += height + 8 * d.Scale;
            }
            if (_hooked) return;
            _hooked = true;
            _lastFrame = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - _lastFrame, 0, 0.05);
            _lastFrame = now;
            if (Step(1 - Math.Exp(-dt * 17))) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        bool Step(double k)
        {
            bool moving = false;
            foreach (var w in _open)
            {
                if (!w.Placed) continue;
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) continue;
                double dx = w.TargetX - w.CurX, dy = w.TargetY - w.CurY;
                if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
                {
                    w.CurX += dx * k;
                    w.CurY += dy * k;
                    moving = true;
                }
                else
                {
                    w.CurX = w.TargetX;
                    w.CurY = w.TargetY;
                }
                int x = (int)Math.Round(w.CurX), y = (int)Math.Round(w.CurY);
                if (x == w.LastX && y == w.LastY) continue;
                w.LastX = x;
                w.LastY = y;
                Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            return moving;
        }

        void Close(ToastWindow w)
        {
            if (w.Dying) return;
            w.Dying = true;
            w.Life.Stop();
            Glass.SetBlur(w, false);
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) =>
            {
                _open.Remove(w);
                w.Close();
                Layout();
            };
            if (w.Content is UIElement surface) surface.BeginAnimation(UIElement.OpacityProperty, fade);
            else w.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public static void OpenTarget(Note n)
        {
            try
            {
                if (!string.IsNullOrEmpty(n.Open))
                {
                    Process.Start(new ProcessStartInfo(n.Open) { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(n.AppId))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{n.AppId}") { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }
    }
}
