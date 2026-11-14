using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class Osd : Window
    {
        readonly Border _surface;
        readonly TextBlock _glyph = Kit.Icon("\uE767", 18);
        readonly TextBlock _text = Kit.Text("", 13, true);
        readonly TextBlock _value = Kit.Text("", 12, false, "SubText");
        readonly Border _track = new() { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 9, 0, 0) };
        readonly Border _fill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        string _key = "";

        public Osd()
        {
            Glass.Attach(this, 18, true);
            Title = "elysium osd";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(18);
            var grid = new Grid { Width = 250, Margin = new Thickness(18, 14, 20, 15) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _glyph.Margin = new Thickness(0, 0, 14, 0);
            _glyph.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(_glyph);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel();
            DockPanel.SetDock(_value, Dock.Right);
            head.Children.Add(_value);
            head.Children.Add(_text);
            right.Children.Add(head);
            _track.SetResourceReference(Border.BackgroundProperty, "SliderTrack");
            _fill.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _track.Child = _fill;
            right.Children.Add(_track);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            _surface.Child = grid;
            Content = _surface;
            _hide.Tick += (_, _) =>
            {
                _hide.Stop();
                Glass.Dismiss(this, _surface, null, 200);
            };
        }

        public void Show(string key, string glyph, string text, double level)
        {
            var d = Displays.AtCursor();
            bool fresh = !IsVisible || key != _key;
            _key = key;
            _glyph.Text = glyph;
            _text.Text = text;
            _track.Visibility = level >= 0 ? Visibility.Visible : Visibility.Collapsed;
            _value.Text = level >= 0 ? Math.Round(level * 100) + "%" : "";
            if (level >= 0)
            {
                double to = Math.Clamp(level, 0, 1) * 250;
                double from = _fill.ActualWidth > 0 && !fresh ? _fill.ActualWidth : to;
                Frames.Run(Math.Max(60, 150 * Theme.Speed), t => _fill.Width = Math.Max(0, from + (to - from) * Frames.Out(t)), null, _fill);
            }
            _hide.Stop();
            _hide.Start();
            if (IsVisible) return;
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _surface.DesiredSize;
                int w = (int)(size.Width * d.Scale), h = (int)(size.Height * d.Scale);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - w) / 2, d.Work.Bottom - h - (int)(70 * d.Scale), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 12, 0.96, 220);
        }

        public static void HideWindowsFlyout()
        {
            try
            {
                IntPtr host = IntPtr.Zero;
                while ((host = Native.FindWindowEx(IntPtr.Zero, host, "NativeHWNDHost", "")) != IntPtr.Zero)
                {
                    if (Native.FindWindowEx(host, IntPtr.Zero, "DirectUIHWND", "") == IntPtr.Zero) continue;
                    Native.ShowWindow(host, 6);
                    return;
                }
            }
            catch
            {
            }
        }
    }
}
