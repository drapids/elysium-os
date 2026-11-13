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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class PinShot : Window
    {
        readonly BitmapSource _img;
        readonly Settings _s;
        readonly Border _frame;
        double _zoom = 1;

        public Action<BitmapSource>? Markup;
        public Action<string>? Say;

        public PinShot(Settings s, BitmapSource img)
        {
            _s = s;
            _img = img;
            Glass.Attach(this, 10, true);
            Glass.NoBlur(this);
            Title = "heft pin";
            SizeToContent = SizeToContent.WidthAndHeight;
            var image = new Image { Source = img, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _frame = new Border
            {
                Child = image,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)),
                Margin = new Thickness(14),
                Cursor = Cursors.SizeAll,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            };
            Content = _frame;
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    Fade();
                    return;
                }
                try
                {
                    DragMove();
                }
                catch
                {
                }
            };
            MouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    _frame.Opacity = Math.Clamp(_frame.Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.25, 1);
                    return;
                }
                _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.15, 4);
                Rezoom();
            };
            MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var menu = new ContextMenu();
                void Item(string label, Action run)
                {
                    var mi = new MenuItem { Header = label };
                    mi.Click += (_, _) => run();
                    menu.Items.Add(mi);
                }
                Item("Copy", () =>
                {
                    Capture.CopyImage(_img);
                    Say?.Invoke("copied");
                });
                Item("Save", () => Say?.Invoke("saved to " + System.IO.Path.GetFileName(Capture.Save(_s, _img))));
                Item("Mark up", () =>
                {
                    Markup?.Invoke(_img);
                    Fade();
                });
                Item("Actual size", () =>
                {
                    _zoom = 1;
                    Rezoom();
                });
                Item("Close", Fade);
                menu.IsOpen = true;
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Fade();
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    Capture.CopyImage(_img);
                    Say?.Invoke("copied");
                }
            };
        }

        void Rezoom()
        {
            if (_frame.Child is Image im)
            {
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                im.Width = _img.PixelWidth / dpi * _zoom;
                im.Height = _img.PixelHeight / dpi * _zoom;
            }
        }

        public void ShowAt(Native.RECT where)
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            double scale = Native.DpiScaleAt(where.Left + 4, where.Top + 4);
            if (_frame.Child is Image im)
            {
                im.Width = _img.PixelWidth / scale;
                im.Height = _img.PixelHeight / scale;
            }
            int pad = (int)(14 * scale);
            Native.SetWindowPos(h, Native.HWND_TOPMOST, where.Left - pad, where.Top - pad, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _frame.Opacity = 0;
            Show();
            var sc = new ScaleTransform(1.04, 1.04);
            _frame.RenderTransformOrigin = new Point(0.5, 0.5);
            _frame.RenderTransform = sc;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            _frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        void Fade()
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            a.Completed += (_, _) => Close();
            _frame.BeginAnimation(OpacityProperty, a);
        }
    }

    internal sealed class MarkupWindow : Window
    {
        static readonly Color[] Inks = { Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x0A, 0x84, 0xFF), Colors.White, Colors.Black };

        readonly Settings _s;
        readonly BitmapSource _src;
        readonly Grid _paper = new();
        readonly Canvas _ink = new() { Background = Brushes.Transparent, ClipToBounds = true };
        readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal };
        readonly List<UIElement> _undo = new();
        string _tool = "arrow";
        Color _color = Inks[0];
        Point _a;
        UIElement? _live;
        Polyline? _pen;
        bool _down;
        double _thick;

        public Action<string>? Say;
        public Action<BitmapSource>? Pin;

        public MarkupWindow(Settings s, BitmapSource src)
        {
            _s = s;
            _src = src;
            _thick = Math.Max(3, Math.Max(src.PixelWidth, src.PixelHeight) / 260.0);
            Glass.Attach(this, 16);
            Title = "heft markup";

            var surface = Glass.Surface(16);
            var root = new DockPanel { Margin = new Thickness(14) };
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Kit.Push("Copy", () => Out("copy"), true));
            var save = Kit.Push("Save", () => Out("save"));
            save.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(save);
            var pin = Kit.Push("Pin", () => Out("pin"));
            pin.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(pin);
            var close = Kit.Plain("\uE8BB", Close, "close (esc)", 10);
            close.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(close);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            bar.Children.Add(_tools);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            var img = new Image { Source = src, Stretch = Stretch.None };
            _paper.Width = src.PixelWidth;
            _paper.Height = src.PixelHeight;
            _paper.Children.Add(img);
            _paper.Children.Add(_ink);
            var view = new Viewbox { Child = _paper, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            var frame = new Border { Child = view, CornerRadius = new CornerRadius(8), ClipToBounds = true, Cursor = Cursors.Pen };
            root.Children.Add(frame);
            surface.Child = root;
            Content = surface;
            BuildTools();

            _ink.MouseLeftButtonDown += Down;
            _ink.MouseMove += Move;
            _ink.MouseLeftButtonUp += Up;
            PreviewKeyDown += (_, e) =>
            {
                if (e.OriginalSource is TextBox) return;
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo();
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) Out("copy");
                else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) Out("save");
                else return;
                e.Handled = true;
            };
        }

        public void Open()
        {
            var d = Displays.AtCursor();
            double maxW = d.Work.Width / d.Scale * 0.86, maxH = d.Work.Height / d.Scale * 0.86;
            double w = Math.Min(maxW, _src.PixelWidth / d.Scale + 28);
            double h = Math.Min(maxH, _src.PixelHeight / d.Scale + 80);
            Width = Math.Max(460, w);
            Height = Math.Max(260, h);
            Glass.Present(this, (UIElement)Content, () =>
            {
                var hw = new WindowInteropHelper(this).Handle;
                int pw = (int)(Width * d.Scale), ph = (int)(Height * d.Scale);
                Native.SetWindowPos(hw, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - pw) / 2, d.Work.Top + (d.Work.Height - ph) / 2, pw, ph, Native.SWP_NOACTIVATE);
            }, 8, 0.97, 220);
            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
        }

        void BuildTools()
        {
            _tools.Children.Clear();
            foreach (var (id, glyph, tip) in new[] { ("arrow", "\uE72A", "arrow"), ("box", "\uE739", "box"), ("mark", "\uE7E6", "highlighter"), ("pen", "\uED63", "pen"), ("blur", "\uE7B3", "blur it out"), ("text", "\uE8D2", "text") })
            {
                string tid = id;
                bool on = tid == _tool;
                var b = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = tip };
                b.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                b.Child = Kit.Icon(glyph, 13, on ? "OnAccent" : "Text");
                ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
                ((TextBlock)b.Child).VerticalAlignment = VerticalAlignment.Center;
                b.MouseLeftButtonUp += (_, _) =>
                {
                    _tool = tid;
                    BuildTools();
                };
                _tools.Children.Add(b);
            }
            _tools.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 4, 10, 4), Background = new SolidColorBrush(Color.FromArgb(0x40, 128, 128, 128)) });
            foreach (var c in Inks)
            {
                var col = c;
                bool on = col == _color;
                var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(col), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(on ? 3 : 1) };
                if (on) dot.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
                else dot.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 128, 128, 128));
                dot.MouseLeftButtonUp += (_, _) =>
                {
                    _color = col;
                    BuildTools();
                };
                _tools.Children.Add(dot);
            }
            var undo = Kit.Plain("\uE7A7", Undo, "undo (ctrl+z)", 11);
            undo.Margin = new Thickness(6, 0, 0, 0);
            _tools.Children.Add(undo);
        }

        void Down(object sender, MouseButtonEventArgs e)
        {
            _a = e.GetPosition(_ink);
            if (_tool == "text")
            {
                var box = new TextBox { MinWidth = 60, FontSize = Math.Max(16, _thick * 6), Foreground = new SolidColorBrush(_color), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CaretBrush = new SolidColorBrush(_color), FontWeight = FontWeights.SemiBold };
                Canvas.SetLeft(box, _a.X);
                Canvas.SetTop(box, _a.Y - box.FontSize * 0.7);
                _ink.Children.Add(box);
                _undo.Add(box);
                box.Loaded += (_, _) => box.Focus();
                box.LostKeyboardFocus += (_, _) =>
                {
                    if (box.Text.Trim().Length == 0)
                    {
                        _ink.Children.Remove(box);
                        _undo.Remove(box);
                        return;
                    }
                    var t = new TextBlock { Text = box.Text, FontSize = box.FontSize, Foreground = box.Foreground, FontWeight = FontWeights.SemiBold, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black } };
                    Canvas.SetLeft(t, Canvas.GetLeft(box) + 2);
                    Canvas.SetTop(t, Canvas.GetTop(box) + 1);
                    int i = _ink.Children.IndexOf(box);
                    _ink.Children.Remove(box);
                    _ink.Children.Insert(Math.Max(0, i), t);
                    int u = _undo.IndexOf(box);
                    if (u >= 0) _undo[u] = t;
                };
                e.Handled = true;
                return;
            }
            _down = true;
            _ink.CaptureMouse();
            if (_tool == "pen")
            {
                _pen = new Polyline { Stroke = new SolidColorBrush(_color), StrokeThickness = _thick, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _pen.Points.Add(_a);
                _ink.Children.Add(_pen);
                _live = _pen;
            }
            e.Handled = true;
        }

        void Move(object sender, MouseEventArgs e)
        {
            if (!_down) return;
            var b = e.GetPosition(_ink);
            if (_tool == "pen")
            {
                _pen?.Points.Add(b);
                return;
            }
            if (_live != null) _ink.Children.Remove(_live);
            _live = MakeShape(_a, b);
            if (_live != null) _ink.Children.Add(_live);
        }

        void Up(object sender, MouseButtonEventArgs e)
        {
            if (!_down) return;
            _down = false;
            _ink.ReleaseMouseCapture();
            if (_live != null) _undo.Add(_live);
            _live = null;
            _pen = null;
        }

        UIElement? MakeShape(Point a, Point b)
        {
            var r = new Rect(a, b);
            switch (_tool)
            {
                case "box":
                    {
                        var rect = new Rectangle { Width = r.Width, Height = r.Height, Stroke = new SolidColorBrush(_color), StrokeThickness = _thick, RadiusX = _thick * 1.5, RadiusY = _thick * 1.5 };
                        Canvas.SetLeft(rect, r.X);
                        Canvas.SetTop(rect, r.Y);
                        return rect;
                    }
                case "mark":
                    {
                        var rect = new Rectangle { Width = r.Width, Height = r.Height, Fill = new SolidColorBrush(Color.FromArgb(0x60, _color.R, _color.G, _color.B)) };
                        Canvas.SetLeft(rect, r.X);
                        Canvas.SetTop(rect, r.Y);
                        return rect;
                    }
                case "blur":
                    {
                        if (r.Width < 4 || r.Height < 4) return null;
                        var px = new Int32Rect((int)Math.Max(0, r.X), (int)Math.Max(0, r.Y), (int)Math.Min(r.Width, _src.PixelWidth - Math.Max(0, r.X)), (int)Math.Min(r.Height, _src.PixelHeight - Math.Max(0, r.Y)));
                        if (px.Width < 2 || px.Height < 2) return null;
                        var crop = new CroppedBitmap(_src, px);
                        double k = Math.Clamp(10.0 / Math.Max(px.Width, px.Height) * 1.6, 0.02, 1);
                        var small = new TransformedBitmap(crop, new ScaleTransform(k, k));
                        var im = new Image { Source = small, Width = px.Width, Height = px.Height, Stretch = Stretch.Fill };
                        RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.NearestNeighbor);
                        Canvas.SetLeft(im, px.X);
                        Canvas.SetTop(im, px.Y);
                        return im;
                    }
                default:
                    {
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy);
                        if (len < 2) return null;
                        double ux = dx / len, uy = dy / len;
                        double head = Math.Min(len * 0.45, _thick * 5);
                        var tip = b;
                        var baseP = new Point(b.X - ux * head, b.Y - uy * head);
                        var left = new Point(baseP.X - uy * head * 0.55, baseP.Y + ux * head * 0.55);
                        var rightP = new Point(baseP.X + uy * head * 0.55, baseP.Y - ux * head * 0.55);
                        var g = new GeometryGroup();
                        g.Children.Add(new LineGeometry(a, baseP));
                        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
                        fig.Segments.Add(new LineSegment(left, true));
                        fig.Segments.Add(new LineSegment(rightP, true));
                        g.Children.Add(new PathGeometry(new[] { fig }));
                        return new System.Windows.Shapes.Path { Data = g, Stroke = new SolidColorBrush(_color), Fill = new SolidColorBrush(_color), StrokeThickness = _thick, StrokeStartLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black } };
                    }
            }
        }

        void Undo()
        {
            if (_undo.Count == 0) return;
            var last = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _ink.Children.Remove(last);
        }

        BitmapSource Render()
        {
            Keyboard.ClearFocus();
            _paper.Measure(new Size(_src.PixelWidth, _src.PixelHeight));
            _paper.Arrange(new Rect(0, 0, _src.PixelWidth, _src.PixelHeight));
            var rtb = new RenderTargetBitmap(_src.PixelWidth, _src.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_paper);
            rtb.Freeze();
            InvalidateMeasure();
            return rtb;
        }

        void Out(string what)
        {
            var bmp = Render();
            switch (what)
            {
                case "copy":
                    Capture.CopyImage(bmp);
                    Say?.Invoke("copied");
                    break;
                case "save":
                    Say?.Invoke("saved to " + System.IO.Path.GetFileName(Capture.Save(_s, bmp)));
                    Capture.CopyImage(bmp);
                    break;
                case "pin":
                    Pin?.Invoke(bmp);
                    break;
            }
            Close();
        }
    }

    internal sealed class RecChip : Window
    {
        readonly TextBlock _time;
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DateTime _start = DateTime.Now;
        readonly Border _surface;

        public event Action? StopRequested;

        public RecChip()
        {
            Glass.Attach(this, 14, true);
            Title = "heft recording";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(14);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 6, 6) };
            var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var pulse = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            dot.BeginAnimation(OpacityProperty, pulse);
            row.Children.Add(dot);
            _time = Kit.Text("0:00", 12.5, true);
            _time.VerticalAlignment = VerticalAlignment.Center;
            _time.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(_time);
            row.Children.Add(Kit.Push("Stop", () => StopRequested?.Invoke(), true));
            _surface.Child = row;
            Content = _surface;
            _tick.Tick += (_, _) =>
            {
                var t = DateTime.Now - _start;
                _time.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
            };
        }

        public void ShowNear(Native.RECT r)
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            Show();
            UpdateLayout();
            Native.GetWindowRect(h, out var me);
            var work = Native.WorkAreaAt(r.Left + r.Width / 2, r.Top + r.Height / 2);
            int x = r.Left + (r.Width - me.Width) / 2;
            int y = r.Bottom + 10;
            if (y + me.Height > work.Bottom) y = r.Top - me.Height - 10;
            if (y < work.Top) y = work.Top + 10;
            x = Math.Clamp(x, work.Left + 6, Math.Max(work.Left + 6, work.Right - me.Width - 6));
            Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Glass.SetBlur(this, true);
            _tick.Start();
        }

        public void Done()
        {
            _tick.Stop();
            Close();
        }
    }
}
