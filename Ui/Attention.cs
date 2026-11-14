using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal sealed class AttentionBubble : Window
    {
        readonly Border _bubble;
        readonly Image _icon = new() { Width = 30, Height = 30 };
        readonly TranslateTransform _shift = new();
        readonly ScaleTransform _scale = new(0, 0);
        readonly AppCatalog _cat;
        readonly Settings _s;
        IntPtr _target;
        string _side = "right";

        public event Action? Clicked;

        public AttentionBubble(Settings s, AppCatalog cat)
        {
            _s = s;
            _cat = cat;
            Glass.Attach(this, 0, true);
            Title = "elysium attention";
            SizeToContent = SizeToContent.WidthAndHeight;
            _bubble = new Border
            {
                Width = 50,
                Height = 50,
                CornerRadius = new CornerRadius(25),
                BorderThickness = new Thickness(1),
                Child = _icon,
                Cursor = Cursors.Hand,
                Margin = new Thickness(14),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _bubble.SetResourceReference(Border.BackgroundProperty, "PillBg");
            _bubble.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_shift);
            _bubble.RenderTransform = group;
            _bubble.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };
            Content = _bubble;
            _bubble.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Clicked?.Invoke();
            };
        }

        public void Ping(IntPtr target, string? path)
        {
            bool same = target == _target && IsVisible;
            _target = target;
            if (path != null) _cat.Icons.Get(path, 64, true, img => _icon.Source = img);
            var f = Native.FrameBounds(target);
            var all = Displays.All();
            var d = Native.IsIconic(target) ? Displays.AtCursor(all) : Displays.At(f.Left + f.Width / 2, f.Top + f.Height / 2, all);
            _side = "right";
            foreach (var (display, side) in Displays.DockEdges(_s, all))
                if (display.Index == d.Index) _side = side;
            if (!IsVisible)
            {
                Show();
                UpdateLayout();
            }
            int w = (int)Math.Ceiling(ActualWidth * d.Scale), h = (int)Math.Ceiling(ActualHeight * d.Scale);
            int x = _side switch
            {
                "left" => d.Bounds.Left,
                "top" or "bottom" => d.Bounds.Left + (d.Bounds.Width - w) / 2,
                _ => d.Bounds.Right - w
            };
            int y = _side switch
            {
                "top" => d.Bounds.Top,
                "bottom" => d.Bounds.Bottom - h,
                _ => d.Bounds.Top + (d.Bounds.Height - h) / 2
            };
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!same)
            {
                Frames.Run(Math.Max(120, 380 * Theme.Speed), t =>
                {
                    double e = Frames.Spring(t);
                    _scale.ScaleX = e;
                    _scale.ScaleY = e;
                }, Bounce, _scale);
            }
            else Bounce();
        }

        void Bounce()
        {
            double dx = _side == "left" ? 1 : _side == "right" ? -1 : 0;
            double dy = _side == "top" ? 1 : _side == "bottom" ? -1 : 0;
            Frames.Run(Math.Max(300, 1300 * Theme.Speed), t =>
            {
                double k = Math.Abs(Math.Sin(t * Math.PI * 3)) * (1 - t);
                _shift.X = dx * 12 * k;
                _shift.Y = dy * 12 * k;
            }, null, _shift);
        }

        public void Clear()
        {
            _target = IntPtr.Zero;
            if (!IsVisible) return;
            Frames.Run(Math.Max(80, 180 * Theme.Speed), t =>
            {
                double e = 1 - Frames.Out(t);
                _scale.ScaleX = e;
                _scale.ScaleY = e;
            }, Hide, _scale);
        }
    }
}
