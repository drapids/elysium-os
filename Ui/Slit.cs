using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    internal sealed class Slit : Window
    {
        const double W = 340;
        const double H = 200;

        readonly Func<string, ImageSource?> _icon;
        readonly Canvas _cards = new() { Width = W, Height = H, IsHitTestVisible = false };
        int _flying;

        public Slit(Func<string, ImageSource?> icon)
        {
            _icon = icon;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Width = W;
            Height = H;
            Title = "heft slit";
            _cards.Clip = new RectangleGeometry(new Rect(0, 1, W, H - 1));
            Content = _cards;
            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(h, true);
                long ex = Native.ExStyle(h);
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | 0x20 | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE));
            };
        }

        public void Swallow(Bar bar, List<string> paths)
        {
            if (paths.Count == 0) return;
            var slot = bar.SlotPx;
            var d = bar.Display;
            var h = new WindowInteropHelper(this).EnsureHandle();
            int wpx = (int)Math.Round(W * d.Scale);
            if (!IsVisible) Show();
            Native.SetWindowPos(h, Native.HWND_TOPMOST, slot.X - wpx / 2, slot.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Sfx.Play("drop");
            int n = Math.Min(paths.Count, 3);
            for (int i = 0; i < n; i++) Card(paths[i], i, n);
        }

        void Card(string path, int index, int count)
        {
            const double cw = 60, ch = 76;
            var face = new Grid();
            var img = new Image { Width = 36, Height = 36, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 11, 0, 0) };
            try
            {
                img.Source = _icon(path);
            }
            catch
            {
            }
            face.Children.Add(img);
            var name = new TextBlock
            {
                Text = Path.GetFileName(path.TrimEnd('\\')),
                FontSize = 8.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 4, 7)
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            face.Children.Add(name);

            var card = new Border
            {
                Width = cw,
                Height = ch,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = face,
                Background = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0xF4, 46, 46, 52) : Color.FromArgb(0xFA, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.4, Color = Colors.Black }
            };
            double spread = (index - (count - 1) / 2.0) * 24;
            double startY = 86 + index * 8;
            double endY = -ch - 6;
            Canvas.SetLeft(card, W / 2 - cw / 2 + spread);
            Canvas.SetTop(card, 0);
            card.Opacity = 0;

            var scale = new ScaleTransform(1, 1);
            var skew = new SkewTransform(0, 0);
            var rot = new RotateTransform(spread * 0.4);
            var move = new TranslateTransform(0, startY);
            var g = new TransformGroup();
            g.Children.Add(scale);
            g.Children.Add(skew);
            g.Children.Add(rot);
            g.Children.Add(move);
            card.RenderTransformOrigin = new Point(0.5, 0);
            card.RenderTransform = g;
            _cards.Children.Add(card);
            _flying++;

            var begin = TimeSpan.FromMilliseconds(index * 90);
            var slide = TimeSpan.FromMilliseconds(480);
            var slideBegin = begin + TimeSpan.FromMilliseconds(120);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var easeOut = new QuinticEase { EasingMode = EasingMode.EaseOut };

            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { BeginTime = begin });
            var tilt = new DoubleAnimationUsingKeyFrames { BeginTime = slideBegin };
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0.38, KeyTime.FromTimeSpan(slide), easeIn));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, tilt);
            var narrow = new DoubleAnimationUsingKeyFrames { BeginTime = slideBegin };
            narrow.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromTimeSpan(slide), easeIn));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, narrow);
            skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, -16 + spread * 0.2, slide) { BeginTime = slideBegin, EasingFunction = easeIn });
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(spread * 0.4, 0, slide) { BeginTime = slideBegin, EasingFunction = easeOut });
            var up = new DoubleAnimation(startY, endY, slide) { BeginTime = slideBegin, EasingFunction = easeIn };
            up.Completed += (_, _) =>
            {
                _cards.Children.Remove(card);
                _flying--;
                if (_flying <= 0)
                {
                    _flying = 0;
                    Hide();
                }
            };
            move.BeginAnimation(TranslateTransform.YProperty, up);
        }
    }
}
