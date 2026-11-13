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
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class Lens : Window
    {
        static readonly (string id, string label, string glyph, string tip)[] Modes =
        {
            ("shot", "Shot", "\uE722", "drag a box or click a window"),
            ("text", "Text", "\uE8D2", "box some text and it gets copied"),
            ("color", "Color", "\uE790", "click a pixel to copy its hex. shift+click saves it"),
            ("aesthetic", "Palette", "\uE771", "box something to pull its colors"),
            ("qr", "QR", "\uED14", "box a qr code to read it"),
            ("measure", "Measure", "\uED5E", "drag to measure pixels"),
            ("gif", "Record", "\uE714", "box an area to record it")
        };

        readonly Settings _s;
        readonly Grid _root = new();
        readonly Image _img = new() { Stretch = Stretch.Fill };
        readonly System.Windows.Shapes.Path _dim = new() { IsHitTestVisible = false };
        readonly Border _selBox = new() { BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly Border _hoverBox = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly Canvas _layer = new();
        readonly Border _toolbar = new();
        readonly StackPanel _modeRow = new() { Orientation = Orientation.Horizontal };
        readonly TextBlock _hint;
        readonly Border _loupe = new() { Width = 136, CornerRadius = new CornerRadius(14), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly Image _zoom = new() { Width = 120, Height = 120, Stretch = Stretch.Fill };
        readonly TextBlock _hex;
        readonly Border _size = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 2, 7, 3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly TextBlock _sizeText;
        readonly System.Windows.Shapes.Line _ruler = new() { StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Border? _panel;

        Shot? _shot;
        double _scale = 1;
        string _mode = "shot";
        bool _shown, _dragging, _hasSel, _busy;
        Point _start;
        Rect _sel;
        List<Native.RECT> _windows = new();
        Native.RECT? _hoverWin;

        public Action<string>? Say;
        public Action<string>? ToShelf;
        public Action<string>? ToSpace;
        public Action<IList<string>>? Share;
        public Action<BitmapSource, Native.RECT>? Pin;
        public Action<BitmapSource>? Markup;
        public Action<Native.RECT, bool>? Record;

        public Lens(Settings s)
        {
            _s = s;
            Glass.Attach(this, 0);
            Glass.NoBlur(this);
            Title = "heft lens";
            Cursor = Cursors.Cross;

            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.NearestNeighbor);
            _root.Children.Add(_img);
            _dim.Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0));
            _root.Children.Add(_dim);
            _selBox.BorderBrush = Brushes.White;
            _selBox.HorizontalAlignment = HorizontalAlignment.Left;
            _selBox.VerticalAlignment = VerticalAlignment.Top;
            _selBox.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black };
            _root.Children.Add(_selBox);
            _hoverBox.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
            _hoverBox.HorizontalAlignment = HorizontalAlignment.Left;
            _hoverBox.VerticalAlignment = VerticalAlignment.Top;
            _root.Children.Add(_hoverBox);
            _ruler.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFill");
            _layer.Children.Add(_ruler);
            _root.Children.Add(_layer);

            var tb = new StackPanel();
            tb.Children.Add(_modeRow);
            _hint = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            tb.Children.Add(_hint);
            _toolbar.Child = tb;
            _toolbar.Padding = new Thickness(8, 7, 8, 7);
            _toolbar.CornerRadius = new CornerRadius(16);
            _toolbar.Background = new SolidColorBrush(Color.FromArgb(0xEE, 28, 29, 34));
            _toolbar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
            _toolbar.BorderThickness = new Thickness(1);
            _toolbar.HorizontalAlignment = HorizontalAlignment.Center;
            _toolbar.VerticalAlignment = VerticalAlignment.Top;
            _toolbar.Margin = new Thickness(0, 22, 0, 0);
            _toolbar.Cursor = Cursors.Arrow;
            _toolbar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            _toolbar.MouseLeftButtonDown += (_, e) => e.Handled = true;
            _root.Children.Add(_toolbar);

            var lg = new StackPanel { Margin = new Thickness(8) };
            var zoomHost = new Grid { Width = 120, Height = 120 };
            RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
            zoomHost.Children.Add(new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = _zoom });
            zoomHost.Clip = new RectangleGeometry(new Rect(0, 0, 120, 120), 9, 9);
            zoomHost.Children.Add(new Border { Width = 9, Height = 9, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black } });
            lg.Children.Add(zoomHost);
            _hex = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            lg.Children.Add(_hex);
            _loupe.Child = lg;
            _loupe.Background = new SolidColorBrush(Color.FromArgb(0xF2, 24, 25, 30));
            _loupe.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255));
            _loupe.BorderThickness = new Thickness(1);
            _loupe.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            _layer.Children.Add(_loupe);

            _sizeText = new TextBlock { FontSize = 11, Foreground = Brushes.White, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            _size.Child = _sizeText;
            _size.Background = new SolidColorBrush(Color.FromArgb(0xD8, 20, 20, 24));
            _layer.Children.Add(_size);

            Content = _root;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_panel != null || _hasSel) Reset();
                else Close2();
            };
            PreviewKeyDown += OnKey;
            SizeChanged += (_, _) => Dim(_hasSel ? _sel : null);
            Deactivated += (_, _) =>
            {
                if (!_busy) Close2();
            };
        }

        public bool Shown => _shown;

        public void Open(string? mode = null)
        {
            if (_shown)
            {
                if (mode != null) SetMode(mode);
                return;
            }
            var d = Displays.AtCursor();
            _windows = Windows(d);
            var shot = Capture.Grab(d);
            if (shot == null)
            {
                Say?.Invoke("couldn't grab the screen");
                return;
            }
            _shot = shot;
            _scale = d.Scale;
            _img.Source = shot.Image;
            _shown = true;
            _busy = false;
            SetMode(mode ?? (_s.CaptureMode.Length > 0 ? _s.CaptureMode : "shot"));
            Reset();
            var h = new WindowInteropHelper(this).EnsureHandle();
            Width = d.Bounds.Width / d.Scale;
            Height = d.Bounds.Height / d.Scale;
            _root.Opacity = 0;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, 0);
            Show();
            Activate();
            if (!IsActive) Native.Focus(h);
            Focus();
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
            _dim.Opacity = 0;
            _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            Glass.FadeIn(_toolbar, -10, 260);
            Sfx.Play("tick");
            var p = Mouse.GetPosition(_root);
            Track(p);
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            _dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) =>
            {
                if (_shown) return;
                Hide();
                _img.Source = null;
                _shot = null;
                ClearPanel();
            };
            _root.BeginAnimation(OpacityProperty, fade);
        }

        static List<Native.RECT> Windows(Display d)
        {
            var list = new List<Native.RECT>();
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return true;
                if ((Native.Style(h) & Native.WS_CHILD) != 0) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == me) return true;
                string cls = Native.ClassName(h);
                if (cls is "Progman" or "WorkerW") return true;
                var r = Native.FrameBounds(h);
                if (r.Width < 40 || r.Height < 30) return true;
                if (r.Right <= d.Bounds.Left || r.Left >= d.Bounds.Right || r.Bottom <= d.Bounds.Top || r.Top >= d.Bounds.Bottom) return true;
                list.Add(r);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        void SetMode(string mode)
        {
            if (!Modes.Any(m => m.id == mode)) mode = "shot";
            _mode = mode;
            if (_s.CaptureMode != mode)
            {
                _s.CaptureMode = mode;
                _s.Touch();
            }
            _modeRow.Children.Clear();
            int n = 1;
            foreach (var (id, label, glyph, _) in Modes)
            {
                string mid = id;
                bool on = id == _mode;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
                sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                var chip = new Border { Child = sp, CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 6, 12, 7), Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand, ToolTip = $"{n}" };
                if (on) chip.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                else chip.Background = Brushes.Transparent;
                chip.MouseEnter += (_, _) =>
                {
                    if (!on) chip.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
                };
                chip.MouseLeave += (_, _) =>
                {
                    if (!on) chip.Background = Brushes.Transparent;
                };
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Reset();
                    SetMode(mid);
                };
                _modeRow.Children.Add(chip);
                n++;
            }
            if (_mode == "color" && _s.Palette.Count > 0)
            {
                var sep = new Border { Width = 1, Margin = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)) };
                _modeRow.Children.Add(sep);
                foreach (var hex in _s.Palette.TakeLast(8))
                {
                    string hx = hex;
                    Color c;
                    try
                    {
                        c = (Color)ColorConverter.ConvertFromString(hx);
                    }
                    catch
                    {
                        continue;
                    }
                    var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(c), Margin = new Thickness(2, 0, 2, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = hx + "  (right-click removes)", VerticalAlignment = VerticalAlignment.Center };
                    dot.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        Capture.CopyText(hx);
                        Say?.Invoke("copied " + hx);
                    };
                    dot.MouseRightButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        _s.Palette.Remove(hx);
                        _s.Touch();
                        SetMode(_mode);
                    };
                    _modeRow.Children.Add(dot);
                }
            }
            _hint.Text = Modes.First(m => m.id == _mode).tip + "   ·   1-7 switch   ·   esc closes";
            _hoverBox.Visibility = Visibility.Collapsed;
            UpdateLoupe(Mouse.GetPosition(_root));
        }

        void Reset()
        {
            _dragging = false;
            _hasSel = false;
            _selBox.Visibility = Visibility.Collapsed;
            _size.Visibility = Visibility.Collapsed;
            _ruler.Visibility = Visibility.Collapsed;
            ClearPanel();
            Dim(null);
        }

        void ClearPanel()
        {
            if (_panel != null)
            {
                _layer.Children.Remove(_panel);
                _panel = null;
            }
        }

        void Dim(Rect? hole)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
            if (hole == null || hole.Value.Width < 1 || hole.Value.Height < 1)
            {
                _dim.Data = full;
                _dim.Fill = new SolidColorBrush(Color.FromArgb(_mode is "color" or "measure" ? (byte)0x00 : (byte)0x48, 0, 0, 0));
                return;
            }
            _dim.Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0));
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole.Value, 4, 4));
        }

        bool AreaMode => _mode is "shot" or "text" or "aesthetic" or "qr" or "gif";

        Int32Rect ToPx(Rect r)
        {
            int x = (int)Math.Round(r.X * _scale), y = (int)Math.Round(r.Y * _scale);
            int w = (int)Math.Round(r.Width * _scale), h = (int)Math.Round(r.Height * _scale);
            return new Int32Rect(x, y, Math.Max(1, w), Math.Max(1, h));
        }

        Rect FromScreen(Native.RECT r)
        {
            var b = _shot!.Display.Bounds;
            return new Rect((r.Left - b.Left) / _scale, (r.Top - b.Top) / _scale, r.Width / _scale, r.Height / _scale);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            switch (e.Key)
            {
                case Key.Escape:
                    if (_panel != null || _hasSel) Reset();
                    else Close2();
                    break;
                case >= Key.D1 and <= Key.D7:
                    Reset();
                    SetMode(Modes[e.Key - Key.D1].id);
                    break;
                case Key.Enter:
                    if (_hasSel && _mode == "shot") Finish("copy");
                    break;
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                    if (_hasSel && _mode == "shot") Finish("copy");
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_shot == null) return;
            var p = e.GetPosition(_root);
            if (_panel != null && _panel.IsMouseOver) return;
            if (_mode == "color")
            {
                PickColor(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                return;
            }
            ClearPanel();
            _dragging = true;
            _hasSel = false;
            _start = p;
            _sel = new Rect(p, p);
            CaptureMouse();
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (_shot == null) return;
            Track(e.GetPosition(_root));
        }

        void Track(Point p)
        {
            UpdateLoupe(p);
            if (!_dragging)
            {
                if (AreaMode && _panel == null && !_hasSel) HoverWindow(p);
                return;
            }
            if (_mode == "measure")
            {
                _ruler.X1 = _start.X;
                _ruler.Y1 = _start.Y;
                _ruler.X2 = p.X;
                _ruler.Y2 = p.Y;
                _ruler.Visibility = Visibility.Visible;
                double dx = (p.X - _start.X) * _scale, dy = (p.Y - _start.Y) * _scale;
                ShowSize($"{Math.Round(Math.Sqrt(dx * dx + dy * dy))} px   ({Math.Abs(Math.Round(dx))} × {Math.Abs(Math.Round(dy))})", p);
                return;
            }
            _hoverBox.Visibility = Visibility.Collapsed;
            _sel = new Rect(_start, p);
            _selBox.Margin = new Thickness(_sel.X, _sel.Y, 0, 0);
            _selBox.Width = _sel.Width;
            _selBox.Height = _sel.Height;
            _selBox.Visibility = _sel.Width > 2 || _sel.Height > 2 ? Visibility.Visible : Visibility.Collapsed;
            Dim(_sel);
            var px = ToPx(_sel);
            ShowSize($"{px.Width} × {px.Height}", new Point(_sel.Right, _sel.Bottom));
        }

        void HoverWindow(Point p)
        {
            var b = _shot!.Display.Bounds;
            int x = b.Left + (int)(p.X * _scale), y = b.Top + (int)(p.Y * _scale);
            Native.RECT? hit = null;
            foreach (var r in _windows)
            {
                if (r.Contains(x, y))
                {
                    hit = r;
                    break;
                }
            }
            _hoverWin = hit;
            if (hit == null)
            {
                _hoverBox.Visibility = Visibility.Collapsed;
                return;
            }
            var rr = FromScreen(hit.Value);
            rr.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
            if (rr.IsEmpty)
            {
                _hoverBox.Visibility = Visibility.Collapsed;
                return;
            }
            _hoverBox.Margin = new Thickness(rr.X, rr.Y, 0, 0);
            _hoverBox.Width = rr.Width;
            _hoverBox.Height = rr.Height;
            _hoverBox.Visibility = Visibility.Visible;
        }

        void ShowSize(string text, Point at)
        {
            _sizeText.Text = text;
            _size.Visibility = Visibility.Visible;
            _size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = Math.Min(at.X + 10, ActualWidth - _size.DesiredSize.Width - 6);
            double y = Math.Min(at.Y + 10, ActualHeight - _size.DesiredSize.Height - 6);
            Canvas.SetLeft(_size, Math.Max(4, x));
            Canvas.SetTop(_size, Math.Max(4, y));
        }

        void UpdateLoupe(Point p)
        {
            if (_shot == null || !(_mode is "color" or "measure") || (_dragging && _mode == "color"))
            {
                _loupe.Visibility = Visibility.Collapsed;
                return;
            }
            int px = (int)(p.X * _scale), py = (int)(p.Y * _scale);
            const int span = 13;
            var crop = Capture.Clamp(_shot, new Int32Rect(px - span / 2, py - span / 2, span, span));
            try
            {
                _zoom.Source = new CroppedBitmap(_shot.Image, crop);
            }
            catch
            {
            }
            var c = Capture.At(_shot, px, py);
            _hex.Text = Capture.Hex(c);
            _loupe.Visibility = Visibility.Visible;
            double lx = p.X + 24, ly = p.Y + 24;
            if (lx + 140 > ActualWidth) lx = p.X - 160;
            if (ly + 170 > ActualHeight) ly = p.Y - 190;
            Canvas.SetLeft(_loupe, lx);
            Canvas.SetTop(_loupe, ly);
        }

        void PickColor(Point p, bool keep)
        {
            var c = Capture.At(_shot!, (int)(p.X * _scale), (int)(p.Y * _scale));
            string hex = Capture.Hex(c);
            Capture.CopyText(hex);
            if (keep)
            {
                _s.Palette.Remove(hex);
                _s.Palette.Add(hex);
                while (_s.Palette.Count > 40) _s.Palette.RemoveAt(0);
                _s.Touch();
                Sfx.Play("tick");
                SetMode("color");
                return;
            }
            Say?.Invoke("copied " + hex);
            Sfx.Play("drop");
            Close2();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging || _shot == null) return;
            _dragging = false;
            ReleaseMouseCapture();
            var p = e.GetPosition(_root);
            if (_mode == "measure")
            {
                var px = (p.X - _start.X) * _scale;
                var py = (p.Y - _start.Y) * _scale;
                double dist = Math.Round(Math.Sqrt(px * px + py * py));
                if (dist > 1)
                {
                    Capture.CopyText(dist.ToString());
                    ShowSize($"{dist} px   ({Math.Abs(Math.Round(px))} × {Math.Abs(Math.Round(py))})   copied", p);
                }
                return;
            }
            var r = new Rect(_start, p);
            if (r.Width < 4 && r.Height < 4)
            {
                if (_hoverWin == null) return;
                r = FromScreen(_hoverWin.Value);
                r.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
                if (r.IsEmpty) return;
            }
            _sel = r;
            _hasSel = true;
            _hoverBox.Visibility = Visibility.Collapsed;
            _selBox.Margin = new Thickness(r.X, r.Y, 0, 0);
            _selBox.Width = r.Width;
            _selBox.Height = r.Height;
            _selBox.Visibility = Visibility.Visible;
            Dim(r);
            _size.Visibility = Visibility.Collapsed;
            AfterSelect();
        }

        async void AfterSelect()
        {
            var shot = _shot;
            if (shot == null) return;
            var px = Capture.Clamp(shot, ToPx(_sel));
            var region = Capture.Crop(shot, px);
            switch (_mode)
            {
                case "shot":
                    ShotActions(region, px);
                    break;
                case "text":
                    {
                        var card = Card();
                        card.Add(Status("reading..."));
                        Place(card.Host);
                        string text = await Capture.Ocr(region);
                        if (!_shown || _shot != shot) return;
                        card.Clear();
                        if (text.Length == 0)
                        {
                            card.Add(Status("couldn't find any text there"));
                            break;
                        }
                        Capture.CopyText(text);
                        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                        card.Add(Head($"copied {words} word{(words == 1 ? "" : "s")}"));
                        var box = new TextBox { Text = text, IsReadOnly = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, MinWidth = 280, MaxWidth = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(8), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 8) };
                        card.Add(box);
                        card.Add(Buttons(
                            ("Copy", () => Done(() => Capture.CopyText(box.Text), "copied")),
                            ("Search web", () => Done(() => Launch.Url("https://www.google.com/search?q=" + Uri.EscapeDataString(box.Text.Length > 300 ? box.Text[..300] : box.Text)), null)),
                            ("Done", Close2)));
                        Place(card.Host);
                        Sfx.Play("drop");
                        break;
                    }
                case "aesthetic":
                    {
                        var colors = Capture.Palette(region, 6);
                        var card = Card();
                        card.Add(Head("the vibe"));
                        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
                        foreach (var (color, share) in colors)
                        {
                            string hx = Capture.Hex(color);
                            var col = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                            var sw = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(color), Cursor = Cursors.Hand, ToolTip = $"{Math.Round(share * 100)}% of it. click to copy", BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1) };
                            sw.MouseLeftButtonUp += (_, ev) =>
                            {
                                ev.Handled = true;
                                Capture.CopyText(hx);
                                Say?.Invoke("copied " + hx);
                            };
                            col.Children.Add(sw);
                            col.Children.Add(new TextBlock { Text = hx, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas") });
                            strip.Children.Add(col);
                        }
                        card.Add(strip);
                        var list = colors.Select(x => x.color).ToList();
                        string hexes = string.Join(", ", list.Select(Capture.Hex));
                        string css = ":root {\n" + string.Join("\n", list.Select((c, i) => $"  --c{i + 1}: {Capture.Hex(c)};")) + "\n}";
                        card.Add(Buttons(
                            ("Copy hex", () => Done(() => Capture.CopyText(hexes), "copied " + list.Count + " colors")),
                            ("Copy CSS", () => Done(() => Capture.CopyText(css), "copied css vars")),
                            ("Copy image", () => Done(() => Capture.CopyImage(Capture.Swatches(list)), "copied the palette")),
                            ("Save colors", () =>
                            {
                                foreach (var c in list)
                                {
                                    string hx = Capture.Hex(c);
                                    _s.Palette.Remove(hx);
                                    _s.Palette.Add(hx);
                                }
                                while (_s.Palette.Count > 40) _s.Palette.RemoveAt(0);
                                _s.Touch();
                                Done(() => { }, "saved to your colors");
                            })));
                        Place(card.Host);
                        Sfx.Play("drop");
                        break;
                    }
                case "qr":
                    {
                        string? text = Capture.Qr(region);
                        var card = Card();
                        if (text == null)
                        {
                            card.Add(Status("no qr code there. try boxing it tighter"));
                            Place(card.Host);
                            break;
                        }
                        bool link = Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
                        card.Add(Head(link ? "it's a link" : "it says"));
                        var t = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 6, 0, 10) };
                        card.Add(t);
                        if (link) card.Add(Buttons(("Open", () => Done(() => Launch.Url(text), null)), ("Copy", () => Done(() => Capture.CopyText(text), "copied"))));
                        else card.Add(Buttons(("Copy", () => Done(() => Capture.CopyText(text), "copied"))));
                        Place(card.Host);
                        Sfx.Play("drop");
                        break;
                    }
                case "gif":
                    {
                        var b = shot.Display.Bounds;
                        var screen = new Native.RECT { Left = b.Left + px.X, Top = b.Top + px.Y, Right = b.Left + px.X + px.Width, Bottom = b.Top + px.Y + px.Height };
                        var card = Card();
                        if (Capture.Ffmpeg() == null)
                        {
                            card.Add(Head("recording needs ffmpeg"));
                            card.Add(Status("install it once with:  winget install ffmpeg   then try again"));
                            card.Add(Buttons(("Copy command", () => Done(() => Capture.CopyText("winget install ffmpeg"), "copied"))));
                            Place(card.Host);
                            break;
                        }
                        card.Add(Head("record this as"));
                        card.Add(Buttons(
                            ("GIF", () => StartRecord(screen, true)),
                            ("Video (mp4)", () => StartRecord(screen, false))));
                        Place(card.Host);
                        break;
                    }
            }
        }

        void StartRecord(Native.RECT r, bool gif)
        {
            _busy = true;
            Close2();
            Dispatcher.BeginInvoke(new Action(() => Record?.Invoke(r, gif)), DispatcherPriority.Background);
        }

        void ShotActions(BitmapSource region, Int32Rect px)
        {
            var card = Card();
            var b = _shot!.Display.Bounds;
            var screen = new Native.RECT { Left = b.Left + px.X, Top = b.Top + px.Y, Right = b.Left + px.X + px.Width, Bottom = b.Top + px.Y + px.Height };
            var row = new WrapPanel { MaxWidth = 560 };
            void Act(string glyph, string label, Action run)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Brushes.White });
                var btn = new Border { Child = sp, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6, 12, 7), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255)) };
                btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
                btn.MouseLeave += (_, _) => btn.Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255));
                btn.MouseLeftButtonDown += (_, e) => e.Handled = true;
                btn.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    run();
                };
                row.Children.Add(btn);
            }
            Act("\uE8C8", "Copy", () => Finish("copy"));
            Act("\uE74E", "Save", () => Finish("save"));
            Act("\uE718", "Pin", () =>
            {
                Close2();
                Pin?.Invoke(region, screen);
            });
            Act("\uE70F", "Mark up", () =>
            {
                Close2();
                Markup?.Invoke(region);
            });
            if (ToShelf != null) Act("\uE7B8", "Shelf", () => Done(() => ToShelf(Library.Stash(null, region)), "on the shelf"));
            if (ToSpace != null) Act("\uE80A", "Space", () => Done(() => ToSpace(Capture.Save(_s, region)), "added to your space"));
            if (Share != null) Act("\uE8EA", "Phone", () =>
            {
                string path = Capture.Save(_s, region);
                Close2();
                Share(new[] { path });
            });
            Act("\uE8D2", "Text", () =>
            {
                SetMode("text");
                _hasSel = true;
                Dim(_sel);
                _selBox.Visibility = Visibility.Visible;
                AfterSelect();
            });
            Act("\uE771", "Palette", () =>
            {
                SetMode("aesthetic");
                _hasSel = true;
                Dim(_sel);
                _selBox.Visibility = Visibility.Visible;
                AfterSelect();
            });
            card.Add(row);
            card.Add(Status("enter copies  ·  esc redo"));
            Place(card.Host);
        }

        void Finish(string what)
        {
            if (_shot == null || !_hasSel) return;
            var region = Capture.Crop(_shot, Capture.Clamp(_shot, ToPx(_sel)));
            if (what == "copy")
            {
                Capture.CopyImage(region);
                Say?.Invoke("screenshot copied");
            }
            else
            {
                string path = Capture.Save(_s, region);
                Capture.CopyImage(region);
                Say?.Invoke("saved to " + Path.GetFileName(Path.GetDirectoryName(path)) + " + copied");
            }
            Sfx.Play("drop");
            Close2();
        }

        void Done(Action run, string? say)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("lens action failed " + ex.Message);
            }
            if (say != null) Say?.Invoke(say);
            Close2();
        }

        sealed class CardParts
        {
            public Border Host = null!;
            public StackPanel Body = null!;
            public void Add(UIElement e) => Body.Children.Add(e);
            public void Clear() => Body.Children.Clear();
        }

        CardParts Card()
        {
            ClearPanel();
            var body = new StackPanel();
            var host = new Border
            {
                Child = body,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 12, 14, 12),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 26, 27, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Arrow,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.55, Color = Colors.Black }
            };
            host.MouseLeftButtonDown += (_, e) => e.Handled = true;
            _panel = host;
            _layer.Children.Add(host);
            return new CardParts { Host = host, Body = body };
        }

        static UIElement Head(string text) => new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };

        static UIElement Status(string text) => new TextBlock { Text = text, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };

        static UIElement Buttons(params (string label, Action run)[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            bool first = true;
            foreach (var (label, run) in items)
            {
                var act = run;
                var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
                if (first) b.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                else b.Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255));
                b.Child = new TextBlock { Text = label, FontSize = 12.5, Foreground = Brushes.White, FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal };
                b.MouseLeftButtonDown += (_, e) => e.Handled = true;
                b.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    act();
                };
                row.Children.Add(b);
                first = false;
            }
            return row;
        }

        void Place(Border host)
        {
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = host.DesiredSize;
            double x = _sel.Left + (_sel.Width - size.Width) / 2;
            double y = _sel.Bottom + 12;
            if (y + size.Height > ActualHeight - 10) y = _sel.Top - size.Height - 12;
            if (y < 90) y = Math.Min(ActualHeight - size.Height - 10, _sel.Top + 12);
            x = Math.Clamp(x, 10, Math.Max(10, ActualWidth - size.Width - 10));
            y = Math.Clamp(y, 10, Math.Max(10, ActualHeight - size.Height - 10));
            Canvas.SetLeft(host, x);
            Canvas.SetTop(host, y);
            Glass.FadeIn(host, 6, 180);
        }
    }
}
