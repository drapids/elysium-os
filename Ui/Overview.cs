using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    internal sealed class Overview : Window
    {
        sealed class Card
        {
            public IntPtr Hwnd;
            public string Title = "";
            public string App = "";
            public string? Path;
            public Border Host = null!;
            public Border Frame = null!;
            public Thumb? Thumb;
            public bool Min;
        }

        readonly AppCatalog _cat;
        readonly Grid _root = new();
        readonly Canvas _canvas = new();
        readonly TextBlock _hint;
        readonly TextBlock _filterText;
        readonly List<Card> _cards = new();
        readonly uint _pid = (uint)Environment.ProcessId;
        Display? _display;
        bool _shown;
        string _filter = "";
        int _sel;
        byte _alpha = 255;

        public bool Shown => _shown;

        public Overview(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 0);
            Title = "heft overview";

            var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 6, 6, 10)) };
            scrim.MouseLeftButtonDown += (_, _) => Close2();
            _root.Children.Add(scrim);
            _root.Children.Add(_canvas);

            var top = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 0, 0), IsHitTestVisible = false };
            _filterText = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            _hint = new TextBlock { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            top.Children.Add(_filterText);
            top.Children.Add(_hint);
            _root.Children.Add(top);
            Content = _root;

            PreviewKeyDown += OnKey;
            PreviewTextInput += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
                _filter += e.Text;
                Arrange(true);
                e.Handled = true;
            };
            Deactivated += (_, _) => Close2();
            SizeChanged += (_, _) =>
            {
                if (_shown) Arrange(false);
            };
        }

        public void Toggle()
        {
            if (_shown) Close2();
            else Open();
        }

        public void Open()
        {
            _display = Displays.AtCursor();
            var d = _display;
            _shown = true;
            _filter = "";
            _sel = 0;
            Collect();

            Glass.Present(this, _root, () =>
            {
                Width = d.Bounds.Width / d.Scale;
                Height = d.Bounds.Height / d.Scale;
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, Native.SWP_NOACTIVATE);
            }, 0, 1, 180);
            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
            Focus();
            Build();
            Arrange(true);
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            foreach (var c in _cards) c.Thumb?.Dispose();
            Glass.Dismiss(this, _root, () =>
            {
                _canvas.Children.Clear();
                _cards.Clear();
            }, 140);
        }

        void Collect()
        {
            foreach (var c in _cards) c.Thumb?.Dispose();
            _cards.Clear();
            var d = _display!;
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsCloaked(h)) return true;
                long ex = Native.ExStyle(h);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if ((Native.Style(h) & Native.WS_CHILD) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == _pid) return true;
                string cls = Native.ClassName(h);
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow") return true;
                var r = Native.FrameBounds(h);
                bool min = Native.IsIconic(h);
                if (!min)
                {
                    if (r.Left <= -10000) return true;
                    var center = Displays.At(r.Left + r.Width / 2, r.Top + r.Height / 2);
                    if (center.Index != d.Index) return true;
                }
                var path = Native.ProcessPath(pid);
                _cards.Add(new Card { Hwnd = h, Title = Native.Title(h), Path = path, App = path != null ? _cat.DisplayName(path) : "", Min = min });
                return true;
            }, IntPtr.Zero);
            _cards.Sort((a, b) => a.Min.CompareTo(b.Min));
        }

        void Build()
        {
            _canvas.Children.Clear();
            var h = new WindowInteropHelper(this).Handle;
            foreach (var c in _cards)
            {
                var card = c;
                var frame = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
                if (c.Min)
                {
                    var icon = new Image { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    if (c.Path != null) _cat.Icons.Get(c.Path, 96, true, src => icon.Source = src);
                    frame.Child = icon;
                }
                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 0, 0) };
                var small = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
                if (c.Path != null) _cat.Icons.Get(c.Path, 64, true, src => small.Source = src);
                label.Children.Add(small);
                label.Children.Add(new TextBlock { Text = c.Title, FontSize = 12.5, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

                var stack = new DockPanel();
                DockPanel.SetDock(label, Dock.Bottom);
                stack.Children.Add(label);
                stack.Children.Add(frame);
                var host = new Border { Child = stack, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = c.Min ? "minimized" : null };
                host.MouseEnter += (_, _) =>
                {
                    _sel = _cards.IndexOf(card);
                    Highlight();
                };
                host.MouseLeftButtonUp += (_, _) => Pick(card);
                host.MouseDown += (_, e) =>
                {
                    if (e.ChangedButton != MouseButton.Middle) return;
                    e.Handled = true;
                    Native.PostMessage(card.Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    card.Thumb?.Dispose();
                    _cards.Remove(card);
                    _canvas.Children.Remove(host);
                    Arrange(true);
                };
                c.Host = host;
                c.Frame = frame;
                _canvas.Children.Add(host);
                if (!c.Min)
                {
                    var t = new Thumb(h, c.Hwnd);
                    if (t.Valid) c.Thumb = t;
                }
            }
        }

        List<Card> Visible() => _filter.Length == 0
            ? _cards
            : _cards.Where(c => Fuzzy.Score(_filter, c.Title) > 0 || Fuzzy.Score(_filter, c.App) > 0).OrderByDescending(c => Math.Max(Fuzzy.Score(_filter, c.Title), Fuzzy.Score(_filter, c.App))).ToList();

        void Arrange(bool animate)
        {
            var list = Visible();
            foreach (var c in _cards)
            {
                bool on = list.Contains(c);
                c.Host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (!on) c.Thumb?.Hide();
            }
            _filterText.Text = _filter.Length > 0 ? _filter : list.Count == 0 ? "no windows here" : "";
            _hint.Text = _filter.Length > 0 ? $"{list.Count} match{(list.Count == 1 ? "" : "es")}  ·  enter to switch  ·  esc to clear" : "click to switch  ·  type to filter  ·  middle-click to close";
            if (list.Count == 0) return;
            if (_sel >= list.Count) _sel = 0;

            double W = ActualWidth > 0 ? ActualWidth : Width, H = ActualHeight > 0 ? ActualHeight : Height;
            double top = 96, pad = 40, gap = 26;
            double areaW = W - pad * 2, areaH = H - top - pad;
            int n = list.Count;
            int bestCols = 1;
            double bestSize = 0;
            for (int cols = 1; cols <= n; cols++)
            {
                int rows = (int)Math.Ceiling(n / (double)cols);
                double cw = (areaW - gap * (cols - 1)) / cols;
                double ch = (areaH - gap * (rows - 1)) / rows - 28;
                double cardW = Math.Min(cw, ch * 16 / 10);
                if (cardW > bestSize)
                {
                    bestSize = cardW;
                    bestCols = cols;
                }
            }
            double cardWidth = Math.Min(bestSize, 520);
            double cardHeight = cardWidth * 10 / 16;
            int rowsN = (int)Math.Ceiling(n / (double)bestCols);
            double totalH = rowsN * (cardHeight + 28) + (rowsN - 1) * gap;
            double y0 = top + Math.Max(0, (areaH - totalH) / 2);

            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < n; i++)
            {
                var c = list[i];
                int row = i / bestCols, col = i % bestCols;
                int inRow = Math.Min(bestCols, n - row * bestCols);
                double rowW = inRow * cardWidth + (inRow - 1) * gap;
                double x = (W - rowW) / 2 + col * (cardWidth + gap);
                double y = y0 + row * (cardHeight + 28 + gap);
                c.Host.Width = cardWidth;
                c.Frame.Height = cardHeight;
                Canvas.SetLeft(c.Host, x);
                Canvas.SetTop(c.Host, y);
                if (animate)
                {
                    var st = new ScaleTransform(0.92, 0.92);
                    c.Host.RenderTransformOrigin = new Point(0.5, 0.5);
                    c.Host.RenderTransform = st;
                    var dur = TimeSpan.FromMilliseconds(300);
                    var begin = TimeSpan.FromMilliseconds(i * 18);
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease, BeginTime = begin });
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease, BeginTime = begin });
                }
            }
            Highlight();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), System.Windows.Threading.DispatcherPriority.Loaded);
            if (animate)
            {
                _alpha = 0;
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                int ticks = 0;
                t.Tick += (_, _) =>
                {
                    ticks++;
                    _alpha = (byte)Math.Min(255, ticks * 22);
                    PlaceThumbs();
                    if (ticks > 30) t.Stop();
                };
                t.Start();
            }
        }

        void PlaceThumbs()
        {
            if (!_shown) return;
            var h = new WindowInteropHelper(this).Handle;
            Native.GetWindowRect(h, out var wr);
            foreach (var c in _cards)
            {
                if (c.Thumb == null || c.Host.Visibility != Visibility.Visible) continue;
                try
                {
                    var tl = c.Frame.PointToScreen(new Point(6, 6));
                    var br = c.Frame.PointToScreen(new Point(c.Frame.ActualWidth - 6, c.Frame.ActualHeight - 6));
                    c.Thumb.Place(new Native.RECT { Left = (int)tl.X - wr.Left, Top = (int)tl.Y - wr.Top, Right = (int)br.X - wr.Left, Bottom = (int)br.Y - wr.Top }, _alpha);
                }
                catch
                {
                }
            }
        }

        void Highlight()
        {
            var list = Visible();
            for (int i = 0; i < list.Count; i++)
                list[i].Frame.BorderBrush = i == _sel ? new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x84, 0xFF)) : Brushes.Transparent;
        }

        void Pick(Card c)
        {
            var h = c.Hwnd;
            Close2();
            Native.Focus(h);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var list = Visible();
            switch (e.Key)
            {
                case Key.Escape:
                    if (_filter.Length > 0)
                    {
                        _filter = "";
                        Arrange(true);
                    }
                    else Close2();
                    break;
                case Key.Back:
                    if (_filter.Length > 0)
                    {
                        _filter = _filter[..^1];
                        Arrange(true);
                    }
                    break;
                case Key.Enter:
                    if (list.Count > 0) Pick(list[Math.Clamp(_sel, 0, list.Count - 1)]);
                    break;
                case Key.Right:
                case Key.Tab:
                    if (list.Count > 0) _sel = (_sel + 1) % list.Count;
                    Highlight();
                    break;
                case Key.Left:
                    if (list.Count > 0) _sel = (_sel - 1 + list.Count) % list.Count;
                    Highlight();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }
    }
}
