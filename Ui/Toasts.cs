using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class Toasts
    {
        sealed class ToastWindow : Window
        {
            public readonly Note Note;
            public readonly DispatcherTimer Life = new();
            public bool Closing;
            public double TargetY;

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
        readonly Func<double> _top;
        readonly List<ToastWindow> _open = new();

        public event Action<Note>? Clicked;

        public Toasts(Settings settings, Func<double> top)
        {
            _s = settings;
            _top = top;
        }

        public void Show(Note n)
        {
            if (_open.Count >= 4) Close(_open[0]);

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
            w.MouseLeave += (_, _) => w.Life.Start();
            w.Life.Tick += (_, _) => Close(w);

            var d = Displays.Primary();
            double scale = d.Scale;
            w.Left = d.Work.Right / scale - w.Width - 14;
            w.Top = _top() + 10;
            w.Opacity = 0;
            w.Show();
            _open.Add(w);
            Layout(true);
            w.Life.Start();

            var slide = new TranslateTransform(40, 0);
            surface.RenderTransform = slide;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        }

        void Layout(bool instantForNewest)
        {
            double y = _top() + 10;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                var w = _open[i];
                if (w.Closing) continue;
                w.TargetY = y;
                if (instantForNewest && i == _open.Count - 1)
                {
                    w.BeginAnimation(Window.TopProperty, null);
                    w.Top = y;
                }
                else
                {
                    w.BeginAnimation(Window.TopProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                }
                y += Math.Max(w.ActualHeight, 64) + 8;
            }
        }

        void Close(ToastWindow w)
        {
            if (w.Closing) return;
            w.Closing = true;
            w.Life.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) =>
            {
                _open.Remove(w);
                w.Close();
                Layout(false);
            };
            w.BeginAnimation(UIElement.OpacityProperty, fade);
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
