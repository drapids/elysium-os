using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal static class ThemePreview
    {
        static Brush Hex(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        static Brush Pick(ThemePack? p, string key, string dark, string light, bool isDark)
        {
            if (p != null)
            {
                if (p.Gradients != null && p.Gradients.TryGetValue(key, out var stops) && Theme.Paint(stops) is Brush g) return g;
                if (p.Colors != null && p.Colors.TryGetValue(key, out var hex) && Theme.Paint(hex) is Brush c) return c;
            }
            return Hex(isDark ? dark : light);
        }

        static Brush Wall(ThemePack? p, bool dark)
        {
            var stops = p?.WallpaperColors;
            if (stops is not { Length: > 0 }) stops = dark ? new[] { "#1F2B4A", "#0B0D16", "#2B1F4A" } : new[] { "#DCE7F7", "#F4F6FA", "#E3DCF7" };
            var layers = new DrawingGroup();
            var colors = new System.Collections.Generic.List<Color>();
            foreach (var s in stops)
            {
                try
                {
                    colors.Add((Color)ColorConverter.ConvertFromString(s));
                }
                catch
                {
                }
            }
            if (colors.Count == 0) colors.Add(dark ? Color.FromRgb(0x10, 0x12, 0x18) : Color.FromRgb(0xF0, 0xF2, 0xF6));
            if (colors.Count == 1) colors.Add(colors[0]);
            var lin = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
            for (int i = 0; i < colors.Count; i++) lin.GradientStops.Add(new GradientStop(colors[i], i / (double)(colors.Count - 1)));
            layers.Children.Add(new GeometryDrawing(lin, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            var spots = new[] { new Point(0.78, 0.3), new Point(0.2, 0.75), new Point(0.6, 0.85) };
            for (int i = 0; i < spots.Length && i < colors.Count; i++)
            {
                var c = colors[(i + 1) % colors.Count];
                var blob = new RadialGradientBrush { Center = spots[i], GradientOrigin = spots[i], RadiusX = 0.5, RadiusY = 0.6 };
                blob.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, c.R, c.G, c.B), 0));
                blob.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                layers.Children.Add(new GeometryDrawing(blob, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            }
            var brush = new DrawingBrush(layers) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }

        public static FrameworkElement Build(ThemePack? p, double w, double h)
        {
            bool dark = p?.Mode switch
            {
                "light" => false,
                "dark" => true,
                _ => Theme.IsDark
            };
            double corner = Math.Clamp(p?.Corner ?? 1, 0.3, 1.8);
            Brush accent = Pick(p, "AccentFill", "#FF0A84FF", "#FF0A84FF", dark);
            if (p == null || !(p.Gradients?.ContainsKey("AccentFill") ?? false))
            {
                try
                {
                    if (p != null && p.Accent.StartsWith('#')) accent = Hex(p.Accent);
                    else if (p == null) accent = new SolidColorBrush(Theme.Accent);
                }
                catch
                {
                }
            }
            var text = Pick(p, "Text", "#F2FFFFFF", "#E6000000", dark);
            var sub = Pick(p, "SubText", "#A6FFFFFF", "#8C000000", dark);
            var faint = Pick(p, "Faint", "#66FFFFFF", "#59000000", dark);
            var group = Pick(p, "GroupBg", "#12FFFFFF", "#8CFFFFFF", dark);
            var stroke = Pick(p, "GroupStroke", "#17FFFFFF", "#0F000000", dark);
            var glass = Pick(p, "GlassTint", "#EB1C1C1F", "#EBF3F3F5", dark);
            var pill = Pick(p, "PillBg", "#D92C2C2E", "#E6F7F7F9", dark);
            var pillStroke = Pick(p, "PillStroke", "#29FFFFFF", "#26000000", dark);
            var onAccent = Pick(p, "OnAccent", "#FFFFFFFF", "#FFFFFFFF", dark);

            var stage = new Grid { Width = w, Height = h, ClipToBounds = true };
            stage.Children.Add(new Border { Background = Wall(p, dark), CornerRadius = new CornerRadius(9) });

            var barFill = Theme.Paint(p?.Bar?.Fill) ?? Hex(dark ? "#C0161619" : "#D9F7F7F9");
            var bar = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Top };
            bar.Children.Add(new Border { Background = barFill, CornerRadius = new CornerRadius(9, 9, 0, 0), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Theme.Paint(p?.Bar?.Line) ?? stroke });
            if (p?.Bar?.Gloss == true)
                bar.Children.Add(new Border { Height = 5, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(9, 9, 0, 0), Background = Hex("#66FFFFFF") });
            var barInk = Theme.Paint(p?.Bar?.Text) ?? text;
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            left.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = barInk, Margin = new Thickness(0, 0, 4, 0) });
            left.Children.Add(new Border { Width = 14, Height = 2.5, CornerRadius = new CornerRadius(1.25), Background = barInk, Opacity = 0.7 });
            bar.Children.Add(left);
            bar.Children.Add(new Border { Width = 26, Height = 6, CornerRadius = new CornerRadius(3), Background = Hex("#E6000000"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            right.Children.Add(new Border { Width = 10, Height = 2.5, CornerRadius = new CornerRadius(1.25), Background = barInk, Opacity = 0.7, Margin = new Thickness(0, 0, 4, 0) });
            right.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = accent });
            bar.Children.Add(right);
            stage.Children.Add(bar);

            double ww = w * 0.62, wh = h * 0.52;
            var win = new Border
            {
                Width = ww,
                Height = wh,
                Background = glass,
                BorderBrush = pillStroke,
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(6 * corner),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(w * 0.1, h * 0.2, 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = dark ? 0.5 : 0.18, Direction = 270 }
            };
            var inner = new StackPanel { Margin = new Thickness(6, 5, 6, 5) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            title.Children.Add(new Border { Width = 18, Height = 3, CornerRadius = new CornerRadius(1.5), Background = text });
            inner.Children.Add(title);
            var card = new Border { Background = group, BorderBrush = stroke, BorderThickness = new Thickness(0.6), CornerRadius = new CornerRadius(4 * corner), Padding = new Thickness(5, 4, 5, 4) };
            var rows = new StackPanel();
            var r1 = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var sw = new Border { Width = 10, Height = 6, CornerRadius = new CornerRadius(3), Background = accent };
            DockPanel.SetDock(sw, Dock.Right);
            r1.Children.Add(sw);
            r1.Children.Add(new Border { Height = 2.5, Width = 26, CornerRadius = new CornerRadius(1.25), Background = text, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            rows.Children.Add(r1);
            rows.Children.Add(new Border { Height = 2, Width = 38, CornerRadius = new CornerRadius(1), Background = sub, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 3) });
            rows.Children.Add(new Border { Height = 2, Width = 22, CornerRadius = new CornerRadius(1), Background = faint, HorizontalAlignment = HorizontalAlignment.Left });
            card.Child = rows;
            inner.Children.Add(card);
            var btn = new Border { Width = 22, Height = 7, CornerRadius = new CornerRadius(3.5 * Math.Min(1.4, corner)), Background = accent, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            btn.Child = new Border { Width = 10, Height = 1.6, CornerRadius = new CornerRadius(0.8), Background = onAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(btn);
            win.Child = inner;
            stage.Children.Add(win);

            var dock = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var tones = new[] { "#FF5AC8FA", "#FFFF9F0A", "#FF30D158", "#FFBF5AF2" };
            bool plates = p?.Dock?.Tile != null;
            foreach (var tone in tones)
            {
                var cell = new Grid { Width = 11, Height = 11, Margin = new Thickness(1.5, 0, 1.5, 0) };
                if (plates)
                {
                    double round = Math.Clamp(p!.Dock!.Round, 0, 0.5);
                    cell.Children.Add(new Border { CornerRadius = new CornerRadius(11 * round), Background = Theme.Paint(p.Dock.Tile) ?? pill, BorderBrush = Theme.Paint(p.Dock.Stroke) ?? pillStroke, BorderThickness = new Thickness(0.6) });
                    cell.Children.Add(new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(1.5), Background = Hex(tone) });
                }
                else cell.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Hex(tone) });
                dock.Children.Add(cell);
            }
            var shelf = new Border
            {
                Child = dock,
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(6 * corner),
                Background = pill,
                BorderBrush = pillStroke,
                BorderThickness = new Thickness(0.6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stage.Children.Add(shelf);
            stage.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderBrush = Hex(dark ? "#1FFFFFFF" : "#1A000000"), BorderThickness = new Thickness(1), IsHitTestVisible = false });
            return stage;
        }
    }
}
