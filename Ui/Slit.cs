using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class Slit : Window
    {
        const double W = 320;
        const double H = 190;
        const double PillTop = 6;
        const double PillH = 54;
        const double Groove = PillTop + 41;

        readonly Func<string, ImageSource?> _icon;
        readonly Canvas _stage = new() { Width = W, Height = H };
        readonly Canvas _cards = new() { Width = W, Height = H, IsHitTestVisible = false };
        readonly Border _pill;
        readonly Border _groove;
        readonly Border _glow;
        readonly TextBlock _label;
        readonly TextBlock _count;
        readonly Border _badge;
        readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(200) };
        readonly DispatcherTimer _spring = new() { Interval = TimeSpan.FromMilliseconds(850) };

        Display? _display;
        long _lastDrag;
        int _total;
        bool _shown, _over;

        public event Action<Display>? OpenFull;
        public event Action<List<string>, bool>? Accepted;

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
            AllowDrop = true;
            Width = W;
            Height = H;
            Title = "heft slit";
            UseLayoutRounding = true;
            SetResourceReference(FontFamilyProperty, "UiFont");

            _pill = new Border { Width = W - 20, Height = PillH, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            Canvas.SetLeft(_pill, 10);
            Canvas.SetTop(_pill, PillTop);

            var inner = new Grid();
            var row = new DockPanel { Margin = new Thickness(16, 8, 14, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = true };
            _count = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Text = "0", HorizontalAlignment = HorizontalAlignment.Center };
            _count.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
            _badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 0, 7, 1), MinWidth = 20, Child = _count, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
            _badge.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            DockPanel.SetDock(_badge, Dock.Right);
            row.Children.Add(_badge);
            var glyph = Kit.Icon("\uE7B8", 12, "SubText");
            glyph.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            _label = Kit.Text("drop it in", 12, true);
            row.Children.Add(_label);
            inner.Children.Add(row);

            _glow = new Border { Height = 10, Margin = new Thickness(36, 0, 36, 7), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
            _glow.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _glow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 10 };
            inner.Children.Add(_glow);
            _groove = new Border { Height = 6, Margin = new Thickness(40, 0, 40, 9), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom };
            _groove.Background = new LinearGradientBrush(Color.FromArgb(0xF0, 0, 0, 0), Color.FromArgb(0x90, 0, 0, 0), 90);
            inner.Children.Add(_groove);
            _pill.Child = inner;

            _cards.Clip = new RectangleGeometry(new Rect(0, Groove + 1, W, H - Groove - 1));
            _stage.Children.Add(_cards);
            _stage.Children.Add(_pill);
            Content = _stage;
            Paint();
            Theme.Applied += Paint;

            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);

            _pill.MouseLeftButtonUp += (_, _) =>
            {
                var d = _display;
                HideSlit();
                if (d != null) OpenFull?.Invoke(d);
            };
            DragEnter += (_, e) =>
            {
                Touch();
                _over = true;
                e.Effects = DropFx(e);
                e.Handled = true;
                Highlight(true);
                _spring.Stop();
                _spring.Start();
            };
            DragOver += (_, e) =>
            {
                Touch();
                e.Effects = DropFx(e);
                e.Handled = true;
            };
            DragLeave += (_, _) =>
            {
                _over = false;
                Highlight(false);
                _spring.Stop();
            };
            Drop += (_, e) =>
            {
                e.Handled = true;
                _spring.Stop();
                _over = false;
                Highlight(false);
                Take(e.Data, (e.KeyStates & DragDropKeyStates.ControlKey) != 0);
            };
            _spring.Tick += (_, _) =>
            {
                _spring.Stop();
                if (!_over || _display == null) return;
                var d = _display;
                HideSlit();
                OpenFull?.Invoke(d);
            };
            _watch.Tick += (_, _) =>
            {
                if (!_shown) return;
                long idle = Environment.TickCount64 - _lastDrag;
                if (IsMouseOver || _over) return;
                if (idle > (_total > 0 ? 2600 : 1600)) HideSlit();
            };
        }

        public bool Shown => _shown;

        static DragDropEffects DropFx(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.Copy;
            return (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }

        void Paint()
        {
            var bg = Theme.IsDark ? Color.FromArgb(0xEE, 28, 28, 31) : Color.FromArgb(0xF4, 246, 246, 248);
            var stroke = Theme.IsDark ? Color.FromArgb(0x30, 255, 255, 255) : Color.FromArgb(0x20, 0, 0, 0);
            _pill.Background = new SolidColorBrush(bg);
            _pill.BorderBrush = new SolidColorBrush(stroke);
            _pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = Theme.IsDark ? 0.45 : 0.18, Color = Colors.Black };
        }

        void Touch() => _lastDrag = Environment.TickCount64;

        public void ShowAt(Display d, double bottomPx)
        {
            Touch();
            bool fresh = !_shown || _display == null || _display.Index != d.Index;
            _display = d;
            if (!_shown)
            {
                _total = 0;
                _badge.Visibility = Visibility.Collapsed;
                _label.Text = "drop it in";
            }
            _shown = true;
            var h = new WindowInteropHelper(this).EnsureHandle();
            int wpx = (int)Math.Round(W * d.Scale);
            int x = d.Bounds.Left + (d.Bounds.Width - wpx) / 2;
            int y = (int)bottomPx;
            if (!IsVisible)
            {
                _pill.Opacity = 0;
                Show();
            }
            Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _watch.Start();
            if (!fresh) return;

            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var st = new ScaleTransform(0.6, 0.2);
            _pill.RenderTransformOrigin = new Point(0.5, 0);
            _pill.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        }

        public void HideSlit()
        {
            if (!_shown) return;
            _shown = false;
            _watch.Stop();
            _spring.Stop();
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var st = new ScaleTransform(1, 1);
            _pill.RenderTransformOrigin = new Point(0.5, 0);
            _pill.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) =>
            {
                if (_shown) return;
                _cards.Children.Clear();
                Hide();
            };
            _pill.BeginAnimation(OpacityProperty, fade);
        }

        void Highlight(bool on)
        {
            _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.85 : 0, TimeSpan.FromMilliseconds(160)));
            _label.Text = on ? "let go to shelf it" : _total > 0 ? "on the shelf" : "drop it in";
        }

        public void Take(IDataObject data, bool copy)
        {
            var paths = Shelf.DroppedPaths(data);
            if (paths.Count == 0) return;
            Touch();
            Accepted?.Invoke(paths, copy);
            Swallow(paths);
        }

        public void Swallow(List<string> paths)
        {
            if (!_shown || paths.Count == 0) return;
            Touch();
            Sfx.Play("drop");
            int n = Math.Min(paths.Count, 3);
            for (int i = 0; i < n; i++) Card(paths[i], i, n);
            _total += paths.Count;
            Dispatcher.BeginInvoke(new Action(Bump), DispatcherPriority.Background);
        }

        void Bump()
        {
            _count.Text = _total.ToString();
            _label.Text = "on the shelf";
            _badge.Visibility = Visibility.Visible;
            var st = new ScaleTransform(1.4, 1.4);
            _badge.RenderTransform = st;
            var back = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(520);
            var begin = TimeSpan.FromMilliseconds(360);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.4, 1, dur) { EasingFunction = back, BeginTime = begin });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.4, 1, dur) { EasingFunction = back, BeginTime = begin });
            var flash = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(300) };
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
            _glow.BeginAnimation(OpacityProperty, flash);
        }

        void Card(string path, int index, int count)
        {
            const double cw = 58, ch = 72;
            var face = new Grid();
            var img = new Image { Width = 34, Height = 34, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
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
                Background = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0xF2, 48, 48, 54) : Color.FromArgb(0xF8, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            double spread = (index - (count - 1) / 2.0) * 22;
            double x = W / 2 - cw / 2 + spread;
            double startY = Groove + 46 + index * 6;
            double endY = Groove - ch - 4;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, 0);
            card.Opacity = 0;

            var skew = new SkewTransform(0, 0);
            var scale = new ScaleTransform(1, 1);
            var rot = new RotateTransform(spread * 0.35);
            var move = new TranslateTransform(0, startY);
            var g = new TransformGroup();
            g.Children.Add(scale);
            g.Children.Add(skew);
            g.Children.Add(rot);
            g.Children.Add(move);
            card.RenderTransformOrigin = new Point(0.5, 0);
            card.RenderTransform = g;
            _cards.Children.Add(card);

            var begin = TimeSpan.FromMilliseconds(index * 90);
            var appear = TimeSpan.FromMilliseconds(130);
            var slide = TimeSpan.FromMilliseconds(460);
            var slideBegin = begin + TimeSpan.FromMilliseconds(110);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var easeOut = new QuinticEase { EasingMode = EasingMode.EaseOut };

            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, appear) { BeginTime = begin });

            var tilt = new DoubleAnimationUsingKeyFrames { BeginTime = slideBegin };
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0.42, KeyTime.FromTimeSpan(slide), easeIn));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, tilt);
            var narrow = new DoubleAnimationUsingKeyFrames { BeginTime = slideBegin };
            narrow.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(slide), easeIn));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, narrow);
            skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, -14 + spread * 0.2, slide) { BeginTime = slideBegin, EasingFunction = easeIn });
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(spread * 0.35, 0, slide) { BeginTime = slideBegin, EasingFunction = easeOut });
            var up = new DoubleAnimation(startY, endY, slide) { BeginTime = slideBegin, EasingFunction = easeIn };
            up.Completed += (_, _) => _cards.Children.Remove(card);
            move.BeginAnimation(TranslateTransform.YProperty, up);
        }
    }
}
