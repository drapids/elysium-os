using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    internal static class Glass
    {
        public static void Attach(Window w, double radius, bool noActivate = false)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.ResizeMode = ResizeMode.NoResize;
            w.UseLayoutRounding = true;
            w.SetResourceReference(Control.FontFamilyProperty, "UiFont");

            w.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                Native.MakeToolWindow(h, noActivate);
                Apply(w, radius);
            };
            w.SizeChanged += (_, _) => Region(w, radius);
            w.LocationChanged += (_, _) => Region(w, radius);
            Theme.Applied += () => Apply(w, radius);
        }

        static void Apply(Window w, double radius)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            Native.SetAccent(h, 3, 0);
            Region(w, radius);
        }

        public static void Region(Window w, double radius)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            if (!Native.GetWindowRect(h, out var r) || r.Width <= 0) return;
            double scale = Native.DpiScaleAt(r.Left + r.Width / 2, r.Top + r.Height / 2);
            int d = (int)Math.Round(radius * 2 * scale);
            IntPtr rgn = d > 0 ? Native.CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, d, d) : IntPtr.Zero;
            Native.SetWindowRgn(h, rgn, true);
        }

        public static Border Surface(double radius)
        {
            var b = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), ClipToBounds = true };
            b.SetResourceReference(Border.BackgroundProperty, "GlassTint");
            b.SetResourceReference(Border.BorderBrushProperty, "PillStroke");
            return b;
        }

        public static void MovePx(Window w, int x, int y)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public static void FadeIn(UIElement e, double fromY = -8, int ms = 180)
        {
            var tt = new TranslateTransform(0, fromY);
            e.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(ms + 60)) { EasingFunction = ease });
            e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));
        }
    }

    internal static class Kit
    {
        static Style S(string key) => (Style)Application.Current.FindResource(key);

        public static TextBlock Text(string text, double size = 13, bool bold = false, string brush = "Text")
        {
            var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        public static TextBlock Caption(string text)
        {
            var t = new TextBlock { Text = text, Style = S("Caption") };
            return t;
        }

        public static TextBlock Header(string text) => new() { Text = text, Style = S("H") };

        public static TextBlock Icon(string glyph, double size = 13, string brush = "Text")
        {
            var t = new TextBlock { Text = glyph, Style = S("Icon"), FontSize = size };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        public static Border Group(params UIElement[] rows)
        {
            var stack = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) stack.Children.Add(new Border { Style = S("Sep") });
                stack.Children.Add(rows[i]);
            }
            return new Border { Style = S("Group"), Child = stack, Margin = new Thickness(0, 0, 0, 2) };
        }

        public static Grid Row(string label, string? caption, UIElement? right)
        {
            var g = new Grid { Style = S("Row") };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
            left.Children.Add(Text(label));
            if (!string.IsNullOrEmpty(caption)) left.Children.Add(Caption(caption));
            g.Children.Add(left);
            if (right != null)
            {
                Grid.SetColumn(right, 1);
                if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(right);
            }
            return g;
        }

        public static CheckBox Switch(bool value, Action<bool> set)
        {
            var c = new CheckBox { Style = S("Switch"), IsChecked = value };
            c.Checked += (_, _) => set(true);
            c.Unchecked += (_, _) => set(false);
            return c;
        }

        public static Grid SliderRow(string label, double min, double max, double value, Func<double, string> fmt, Action<double> set)
        {
            var g = new Grid { Style = S("Row") };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            g.Children.Add(Text(label));
            var slider = new Slider { Style = S("Mac"), Minimum = min, Maximum = max, Value = value };
            Grid.SetColumn(slider, 1);
            g.Children.Add(slider);
            var val = new TextBlock { Style = S("Value"), Text = fmt(value) };
            Grid.SetColumn(val, 2);
            g.Children.Add(val);
            slider.ValueChanged += (_, e) =>
            {
                val.Text = fmt(e.NewValue);
                set(e.NewValue);
            };
            return g;
        }

        public static Border Segments(string[] labels, string[] values, string current, Action<string> set)
        {
            var grid = new UniformGrid { Rows = 1 };
            string group = "seg" + Guid.NewGuid().ToString("N");
            for (int i = 0; i < labels.Length; i++)
            {
                var rb = new RadioButton { Style = S("Seg"), Content = labels[i], GroupName = group, Tag = values[i], IsChecked = values[i] == current };
                string v = values[i];
                rb.Checked += (_, _) => set(v);
                grid.Children.Add(rb);
            }
            return new Border { Style = S("SegGroup"), Child = grid };
        }

        public static Button Push(string text, Action click, bool accent = false)
        {
            var b = new Button { Style = S(accent ? "AccentBtn" : "Push"), Content = text };
            b.Click += (_, _) => click();
            return b;
        }

        public static Button Plain(string glyph, Action click, string? tip = null, double size = 12)
        {
            var b = new Button { Style = S("Plain"), ToolTip = tip };
            var t = Icon(glyph, size);
            t.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
            b.Content = t;
            b.Click += (_, _) => click();
            return b;
        }

        public static TextBox Field(string value, string placeholder, Action<string> set, bool multi = false)
        {
            var t = new TextBox { Style = S("Field"), Text = value, Tag = placeholder };
            if (multi)
            {
                t.AcceptsReturn = true;
                t.TextWrapping = TextWrapping.Wrap;
                t.MinHeight = 70;
                t.VerticalContentAlignment = VerticalAlignment.Top;
            }
            t.LostKeyboardFocus += (_, _) => set(t.Text);
            return t;
        }

        public static StackPanel Block(string label, string? caption, UIElement content)
        {
            var p = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            p.Children.Add(Text(label));
            if (caption != null)
            {
                var c = Caption(caption);
                c.Margin = new Thickness(0, 1, 0, 8);
                p.Children.Add(c);
            }
            else if (content is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 8, 0, 0);
            }
            p.Children.Add(content);
            return p;
        }

        public static Border Hover(UIElement child, Action? click = null, double radius = 8)
        {
            var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), Background = Brushes.Transparent, Padding = new Thickness(8, 6, 8, 6) };
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "RowHover");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            if (click != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    click();
                };
            }
            return b;
        }

        public static Border Meter(double value, double width = 120)
        {
            var track = new Border { Height = 4, Width = width, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            track.SetResourceReference(Border.BackgroundProperty, "SliderTrack");
            var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Clamp(value, 0, 1) * width };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            track.Child = fill;
            return track;
        }

        public static void SetMeter(Border meter, double value)
        {
            if (meter.Child is Border fill) fill.Width = Math.Clamp(value, 0, 1) * meter.Width;
        }

        public static string Size(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return i == 0 ? $"{bytes} B" : $"{v:0.#} {u[i]}";
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "just now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
            if (d.TotalDays < 7) return $"{(int)d.TotalDays}d ago";
            return t.ToString("MMM d");
        }
    }
}
