using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Elysium.Ui
{
    internal sealed class Curtain : Window
    {
        public readonly Grid Stage = new();
        readonly Display _d;

        public Curtain(Display d, bool front, bool fromScreen)
        {
            _d = d;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = front;
            AllowsTransparency = false;
            Background = Brushes.Black;
            if (fromScreen)
            {
                var shot = Elysium.Backdrop.Shot(d.Bounds);
                if (shot != null) Background = new ImageBrush(shot) { Stretch = Stretch.Fill };
            }
            Title = "elysium curtain";
            Stage.Opacity = 0;
            Content = Stage;
            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);
        }

        public void Cover()
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            Native.Cloak(h, true);
            Show();
            Width = double.NaN;
            Height = double.NaN;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, _d.Bounds.Left, _d.Bounds.Top, _d.Bounds.Width, _d.Bounds.Height, Native.SWP_NOACTIVATE);
            Glass.Unveil(this);
        }

        public static Brush Backdrop()
        {
            var pack = Themes.Active;
            var wash = Theme.Paint(pack?.Start?.Wash);
            if (wash != null) return wash;
            var a = Theme.Accent;
            bool dark = Theme.IsDark;
            var g = new RadialGradientBrush { Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42), RadiusX = 0.85, RadiusY = 0.85 };
            g.GradientStops.Add(new GradientStop(dark ? Color.FromRgb((byte)(a.R / 5), (byte)(a.G / 5), (byte)(a.B / 5 + 10)) : Color.FromRgb(0xF6, 0xF6, 0xF9), 0));
            g.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(0x05, 0x05, 0x08) : Color.FromRgb(0xDD, 0xDF, 0xE6), 1));
            g.Freeze();
            return g;
        }
    }

    internal static class Moments
    {
        static readonly List<Curtain> Open = new();

        static FrameworkElement Logo(double size, out Ellipse ring, out TextBlock word, out Border glow)
        {
            var box = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var mark = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            glow = new Border { Width = size * 0.9, Height = size * 0.9, CornerRadius = new CornerRadius(size), Opacity = 0, Effect = new BlurEffect { Radius = size * 0.45 } };
            glow.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            mark.Children.Add(glow);
            ring = new Ellipse { Width = size * 0.78, Height = size * 0.78, StrokeThickness = Math.Max(2, size * 0.05), StrokeDashCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.SetResourceReference(Shape.StrokeProperty, "Text");
            mark.Children.Add(ring);
            var dot = new Ellipse { Width = size * 0.16, Height = size * 0.16 };
            dot.SetResourceReference(Shape.FillProperty, "AccentFill");
            mark.Children.Add(dot);
            stack.Children.Add(mark);
            word = new TextBlock { Text = "elysium", FontSize = size * 0.42, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, size * 0.22, 0, 0), Opacity = 0 };
            word.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            word.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            stack.Children.Add(word);
            box.Children.Add(stack);
            return box;
        }

        static void Ring(Ellipse ring, double t)
        {
            double len = Math.PI * (ring.Width - ring.StrokeThickness) / Math.Max(1, ring.StrokeThickness);
            ring.StrokeDashArray = new DoubleCollection { len * t, len * 2 };
            ring.RenderTransformOrigin = new Point(0.5, 0.5);
            ring.RenderTransform = new RotateTransform(-90 + 200 * Frames.Out(t));
        }

        public static void Boot(Action done)
        {
            Close();
            var all = Displays.All();
            var primary = Displays.Primary(all);
            Ellipse? ring = null;
            TextBlock? word = null;
            Border? glow = null;
            foreach (var d in all)
            {
                var c = new Curtain(d, false, false);
                c.Stage.Background = Curtain.Backdrop();
                if (d.Index == primary.Index) c.Stage.Children.Add(Logo(120, out ring, out word, out glow));
                Open.Add(c);
                c.Cover();
            }
            Sfx.Chime(new[] { 523.25, 659.25, 783.99, 1046.5 }, 120, 0.22);
            Frames.Run(Math.Max(300, 2000 * Theme.Speed), t =>
            {
                foreach (var c in Open) c.Stage.Opacity = Math.Min(1, t * 8);
                if (ring != null) Ring(ring, Math.Min(1, t / 0.45));
                if (word != null)
                {
                    double w = Math.Clamp((t - 0.3) / 0.3, 0, 1);
                    word.Opacity = Frames.Out(w);
                    word.RenderTransform = new TranslateTransform(0, 10 * (1 - Frames.Out(w)));
                }
                if (glow != null) glow.Opacity = 0.55 * Math.Sin(Math.Clamp((t - 0.25) / 0.75, 0, 1) * Math.PI);
            }, () =>
            {
                Frames.Run(Math.Max(150, 520 * Theme.Speed), t =>
                {
                    foreach (var c in Open) c.Stage.Opacity = 1 - Frames.Out(t);
                }, () =>
                {
                    Close();
                    done();
                });
            });
        }

        public static void Power(string kind, Action run)
        {
            Close();
            string text = kind switch
            {
                "restart" => "restarting",
                "signout" => "signing out",
                "sleep" => "going to sleep",
                _ => "shutting down"
            };
            TextBlock? label = null;
            Ellipse? ring = null;
            foreach (var d in Displays.All())
            {
                var c = new Curtain(d, true, true);
                c.Stage.Background = Curtain.Backdrop();
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var r = new Ellipse { Width = 46, Height = 46, StrokeThickness = 3, StrokeDashCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center };
                r.SetResourceReference(Shape.StrokeProperty, "Text");
                stack.Children.Add(r);
                var l = new TextBlock { Text = text, FontSize = 26, FontWeight = FontWeights.Light, Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                l.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                l.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
                stack.Children.Add(l);
                c.Stage.Children.Add(stack);
                ring ??= r;
                label ??= l;
                Open.Add(c);
                c.Cover();
            }
            var spin = new List<Ellipse>();
            foreach (var c in Open)
                if (c.Stage.Children.Count > 0 && c.Stage.Children[0] is StackPanel sp && sp.Children[0] is Ellipse e) spin.Add(e);
            double start = Environment.TickCount64;
            bool fired = false;
            Frames.Run(Math.Max(600, 1500 * Theme.Speed), t =>
            {
                foreach (var c in Open) c.Stage.Opacity = Frames.Out(Math.Min(1, t * 3));
                double secs = (Environment.TickCount64 - start) / 1000.0;
                foreach (var e in spin)
                {
                    e.StrokeDashArray = new DoubleCollection { 4, 8 };
                    e.RenderTransformOrigin = new Point(0.5, 0.5);
                    e.RenderTransform = new RotateTransform(secs * 300);
                }
            }, () =>
            {
                if (fired) return;
                fired = true;
                run();
                if (kind == "sleep") Close();
            });
        }

        public static void LockFlash(Action run)
        {
            Close();
            var d = Displays.AtCursor();
            var c = new Curtain(d, true, true);
            c.Stage.Background = Curtain.Backdrop();
            var g = Kit.Icon("\uE72E", 54);
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.VerticalAlignment = VerticalAlignment.Center;
            g.RenderTransformOrigin = new Point(0.5, 0.5);
            c.Stage.Children.Add(g);
            Open.Add(c);
            c.Cover();
            Frames.Run(Math.Max(200, 520 * Theme.Speed), t =>
            {
                c.Stage.Opacity = Frames.Out(Math.Min(1, t * 2));
                double s = 0.7 + 0.3 * Frames.Spring(t);
                g.RenderTransform = new ScaleTransform(s, s);
            }, () =>
            {
                run();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Close();
                };
                timer.Start();
            });
        }

        public static void Close()
        {
            foreach (var c in Open)
            {
                try
                {
                    c.Close();
                }
                catch
                {
                }
            }
            Open.Clear();
        }
    }
}
