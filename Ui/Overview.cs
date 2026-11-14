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
    internal sealed class OverviewHooks
    {
        public Action<IntPtr>? Pip;
        public Action<IntPtr>? Pocket;
        public Action<IntPtr>? Shot;
        public Action<IntPtr>? Record;
        public Action<IntPtr>? Remember;
        public Action<IntPtr>? Note;
        public Action<IntPtr, IntPtr>? Stack;
        public Action<string>? Inspect;
        public Func<string, (bool playing, bool muted)?>? Audio;
        public Action<string, bool>? Mute;
        public Func<IReadOnlyList<ClosedApp>>? Closed;
        public Action<ClosedApp>? Reopen;
        public Action<string>? Say;
    }

    internal sealed class Overview : Window
    {
        sealed class Card
        {
            public AppWin Win = null!;
            public string App = "";
            public Border Host = null!;
            public Border Frame = null!;
            public Thumb? Thumb;
            public bool Picked;
            public double X, Y, W, H;
        }

        readonly AppCatalog _cat;
        readonly Settings _s;
        readonly Grid _root = new();
        readonly Canvas _canvas = new();
        readonly TextBlock _hint;
        readonly TextBlock _filterText;
        readonly StackPanel _screens = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        readonly StackPanel _closedRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 22) };
        readonly Image _blur = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Margin = new Thickness(-6) };
        readonly Border _zoom = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) };
        readonly List<Card> _cards = new();
        readonly List<(Border tile, Display d)> _screenTiles = new();
        Display? _display;
        bool _shown;
        string _filter = "";
        int _sel;
        byte _alpha = 255;
        int _cols = 1;
        Card? _press;
        Point _pressAt;
        bool _dragging;
        Border? _ghost;
        Thumb? _zoomThumb;

        public OverviewHooks Hooks { get; } = new();

        public bool Shown => _shown;

        public Overview(AppCatalog cat, Settings s)
        {
            _cat = cat;
            _s = s;
            Glass.Attach(this, 0);
            Glass.Opaque(this);
            Title = "heft overview";
            RenderOptions.SetBitmapScalingMode(_blur, BitmapScalingMode.HighQuality);
            _root.ClipToBounds = true;
            _root.Children.Add(_blur);

            var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 6, 6, 10)) };
            scrim.MouseLeftButtonDown += (_, _) => Close2();
            _root.Children.Add(scrim);
            _root.Children.Add(_canvas);

            var top = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0) };
            top.Children.Add(_screens);
            _filterText = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            _hint = new TextBlock { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false };
            top.Children.Add(_filterText);
            top.Children.Add(_hint);
            _root.Children.Add(top);

            var group = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 22, 0), Padding = new Thickness(10, 5, 12, 6), CornerRadius = Kit.Round(9), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            var groupText = new TextBlock { FontSize = 12, Foreground = Brushes.White };
            group.Child = groupText;
            void GroupLabel() => groupText.Text = _s.OverviewGroup ? "✓ grouped by app" : "group by app";
            GroupLabel();
            group.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _s.OverviewGroup = !_s.OverviewGroup;
                _s.Touch();
                GroupLabel();
                Order();
                Arrange(true);
            };
            _root.Children.Add(group);
            _root.Children.Add(_closedRow);
            _zoom.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Unzoom();
            };
            _root.Children.Add(_zoom);
            Content = _root;

            PreviewKeyDown += OnKey;
            PreviewTextInput += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
                if (_filter.Length == 0 && (e.Text == " " || char.IsDigit(e.Text[0]) || (e.Text is "t" or "T" && _cards.Count(c => c.Picked) >= 2))) return;
                _filter += e.Text;
                Arrange(true);
                e.Handled = true;
            };
            Deactivated += (_, _) =>
            {
                if (!GlassMenu.Showing) Close2();
            };
            SizeChanged += (_, _) =>
            {
                if (_shown) Arrange(false);
            };
            PreviewMouseMove += OnDragMove;
            PreviewMouseLeftButtonUp += OnDragUp;
        }

        public void Toggle()
        {
            if (_shown) Close2();
            else Open();
        }

        public void Open(Display? on = null)
        {
            _display = on ?? Displays.AtCursor();
            var d = _display;
            _shown = true;
            _filter = "";
            _sel = 0;
            Collect();
            var (sharp, soft) = Backdrop.Grab(d.Bounds, 6, 2);
            _blur.Source = soft;
            Glass.Behind(this, sharp);
            Glass.Present(this, _root, () => Glass.Fill(this, d.Bounds), 0, 1, 180);
            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
            Focus();
            Build();
            BuildScreens();
            BuildClosed();
            Arrange(true);
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            GlassMenu.CloseAll();
            EndDrag();
            Unzoom();
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
            var all = Displays.All();
            foreach (var w in WinList.All())
            {
                if (!w.Min)
                {
                    var center = Displays.At(w.Frame.Left + w.Frame.Width / 2, w.Frame.Top + w.Frame.Height / 2, all);
                    if (center.Key != d.Key) continue;
                }
                _cards.Add(new Card { Win = w, App = w.Path != null ? _cat.DisplayName(w.Path) : "" });
            }
            Order();
        }

        void Order()
        {
            var list = _cards.ToList();
            _cards.Clear();
            IEnumerable<Card> ordered = list.OrderBy(c => c.Win.Min);
            if (_s.OverviewGroup)
            {
                var firstSeen = new Dictionary<string, int>();
                for (int i = 0; i < list.Count; i++)
                    if (!firstSeen.ContainsKey(list[i].App)) firstSeen[list[i].App] = i;
                ordered = list.OrderBy(c => c.Win.Min).ThenBy(c => firstSeen[c.App]);
            }
            _cards.AddRange(ordered);
        }

        void Build()
        {
            _canvas.Children.Clear();
            var h = new WindowInteropHelper(this).Handle;
            foreach (var c in _cards)
            {
                var card = c;
                var frame = new Border { CornerRadius = Kit.Round(10), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
                if (c.Win.Min)
                {
                    var icon = new Image { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    if (c.Win.Path != null) _cat.Icons.Get(c.Win.Path, 96, true, src => icon.Source = src);
                    frame.Child = icon;
                }
                var label = new DockPanel { Margin = new Thickness(2, 8, 0, 0) };
                var small = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
                if (c.Win.Path != null) _cat.Icons.Get(c.Win.Path, 64, true, src => small.Source = src);
                DockPanel.SetDock(small, Dock.Left);
                label.Children.Add(small);
                var badges = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(badges, Dock.Right);
                label.Children.Add(badges);
                if (c.Win.Hung) badges.Children.Add(Badge("not responding", Color.FromRgb(0xFF, 0x6B, 0x5E), null));
                if (WinOps.IsTopmost(c.Win.Hwnd)) badges.Children.Add(Badge("\uE718", Color.FromArgb(0xC0, 255, 255, 255), null, true));
                var audio = Hooks.Audio?.Invoke(c.Win.Exe);
                if (audio is { } au && (au.playing || au.muted))
                {
                    bool muted = au.muted;
                    var exe = c.Win.Exe;
                    badges.Children.Add(Badge(muted ? "\uE74F" : "\uE767", muted ? Color.FromRgb(0xFF, 0x6B, 0x5E) : Color.FromArgb(0xE6, 255, 255, 255), () =>
                    {
                        Hooks.Mute?.Invoke(exe, !muted);
                        Rebuild();
                    }, true));
                }
                label.Children.Add(new TextBlock { Text = c.Win.Title, FontSize = 12.5, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

                var stack = new DockPanel();
                DockPanel.SetDock(label, Dock.Bottom);
                stack.Children.Add(label);
                stack.Children.Add(frame);
                var host = new Border { Child = stack, Cursor = Cursors.Hand, Background = Brushes.Transparent, Opacity = c.Win.Min ? 0.6 : 1, ToolTip = c.Win.Min ? "minimized" : null };
                host.MouseEnter += (_, _) =>
                {
                    if (_dragging) return;
                    _sel = Visible().IndexOf(card);
                    Highlight();
                };
                host.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        e.Handled = true;
                        card.Picked = !card.Picked;
                        Highlight();
                        Hint();
                        return;
                    }
                    _press = card;
                    _pressAt = e.GetPosition(this);
                };
                host.MouseLeftButtonUp += (_, e) =>
                {
                    if (_dragging || _press != card) return;
                    _press = null;
                    e.Handled = true;
                    Pick(card);
                };
                host.MouseDown += (_, e) =>
                {
                    if (e.ChangedButton != MouseButton.Middle) return;
                    e.Handled = true;
                    CloseWin(card);
                };
                host.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _sel = Visible().IndexOf(card);
                    Highlight();
                    GlassMenu.Show(this, Menu(card));
                };
                c.Host = host;
                c.Frame = frame;
                _canvas.Children.Add(host);
                if (!c.Win.Min)
                {
                    var t = new Thumb(h, c.Win.Hwnd);
                    if (t.Valid) c.Thumb = t;
                }
            }
        }

        static Border Badge(string text, Color color, Action? click, bool icon = false)
        {
            var t = new TextBlock { Text = text, FontSize = icon ? 12 : 11, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            if (icon) t.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            var b = new Border { Child = t, Padding = new Thickness(6, 2, 6, 3), Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center };
            if (click != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    click();
                };
                b.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
            }
            return b;
        }

        void Rebuild()
        {
            if (!_shown) return;
            Collect();
            Build();
            Arrange(false);
        }

        void BuildScreens()
        {
            _screens.Children.Clear();
            _screenTiles.Clear();
            var all = Displays.All();
            if (all.Count < 2)
            {
                _screens.Visibility = Visibility.Collapsed;
                return;
            }
            _screens.Visibility = Visibility.Visible;
            double maxH = 46;
            double k = maxH / all.Max(d => (double)d.Bounds.Height);
            foreach (var d in all)
            {
                var disp = d;
                bool here = d.Key == _display?.Key;
                var t = new TextBlock { Text = (d.Index + 1).ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tile = new Border
                {
                    Width = Math.Max(40, d.Bounds.Width * k),
                    Height = Math.Max(26, d.Bounds.Height * k),
                    Margin = new Thickness(5, 0, 5, 10),
                    CornerRadius = Kit.Round(6),
                    BorderThickness = new Thickness(here ? 2 : 1),
                    BorderBrush = new SolidColorBrush(here ? Theme.Accent : Color.FromArgb(0x60, 255, 255, 255)),
                    Background = new SolidColorBrush(Color.FromArgb(here ? (byte)0x40 : (byte)0x22, 255, 255, 255)),
                    Child = t,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = here ? "this screen" : "click to see this screen, or drag a window here to send it"
                };
                tile.MouseLeftButtonUp += (_, e) =>
                {
                    if (_dragging) return;
                    e.Handled = true;
                    if (disp.Key == _display?.Key) return;
                    Switch(disp);
                };
                _screens.Children.Add(tile);
                _screenTiles.Add((tile, d));
            }
        }

        void Switch(Display d)
        {
            foreach (var c in _cards) c.Thumb?.Dispose();
            _cards.Clear();
            _display = d;
            var (sharp, soft) = Backdrop.Grab(d.Bounds, 6, 2);
            _blur.Source = soft;
            Glass.Behind(this, sharp);
            Glass.Fill(this, d.Bounds);
            _filter = "";
            _sel = 0;
            Collect();
            Build();
            BuildScreens();
            Arrange(true);
        }

        void BuildClosed()
        {
            _closedRow.Children.Clear();
            var closed = Hooks.Closed?.Invoke();
            if (closed == null || closed.Count == 0)
            {
                _closedRow.Visibility = Visibility.Collapsed;
                return;
            }
            _closedRow.Visibility = Visibility.Visible;
            _closedRow.Children.Add(new TextBlock { Text = "recently closed", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var app in closed.Take(6))
            {
                var a = app;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0) };
                _cat.Icons.Get(a.Path, 32, true, src => ic.Source = src);
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = a.Name, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis });
                var chip = new Border { Child = sp, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(4, 0, 4, 0), CornerRadius = Kit.Round(10), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), Cursor = Cursors.Hand, ToolTip = a.Title };
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Close2();
                    Hooks.Reopen?.Invoke(a);
                };
                _closedRow.Children.Add(chip);
            }
        }

        List<Card> Visible() => _filter.Length == 0
            ? _cards
            : _cards.Where(c => Fuzzy.Score(_filter, c.Win.Title) > 0 || Fuzzy.Score(_filter, c.App) > 0).OrderByDescending(c => Math.Max(Fuzzy.Score(_filter, c.Win.Title), Fuzzy.Score(_filter, c.App))).ToList();

        void Hint()
        {
            int picked = _cards.Count(c => c.Picked);
            _hint.Text = picked >= 2 ? $"{picked} picked  ·  t tiles them  ·  ctrl-click to pick more"
                : _filter.Length > 0 ? "enter to switch  ·  esc to clear"
                : "click to switch  ·  right-click for more  ·  type to filter  ·  space zooms  ·  shift + arrows send it to another screen";
        }

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
            Hint();
            if (list.Count == 0) return;
            if (_sel >= list.Count || _sel < 0) _sel = 0;

            double W = ActualWidth > 0 ? ActualWidth : 1600, H = ActualHeight > 0 ? ActualHeight : 900;
            double top = _screens.Visibility == Visibility.Visible ? 150 : 96;
            double bottom = _closedRow.Visibility == Visibility.Visible ? 80 : 40;
            double pad = 40, gap = 26;
            double areaW = W - pad * 2, areaH = H - top - bottom;
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
            _cols = bestCols;
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
                c.X = x;
                c.Y = y;
                c.W = cardWidth;
                c.H = cardHeight;
                Canvas.SetLeft(c.Host, x);
                Canvas.SetTop(c.Host, y);
                if (animate)
                {
                    var st = new ScaleTransform(0.94, 0.94);
                    c.Host.RenderTransformOrigin = new Point(0.5, 0.5);
                    c.Host.RenderTransform = st;
                    var dur = TimeSpan.FromMilliseconds(280 * Theme.Speed);
                    var begin = TimeSpan.FromMilliseconds(i * 14 * Theme.Speed);
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = ease, BeginTime = begin });
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = ease, BeginTime = begin });
                }
            }
            Highlight();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), System.Windows.Threading.DispatcherPriority.Loaded);
            if (animate)
            {
                _alpha = 0;
                Frames.Run(Math.Max(120, (280 + n * 14) * Theme.Speed), t =>
                {
                    _alpha = (byte)Math.Round(255 * Frames.Out(t));
                    PlaceThumbs();
                }, null, this);
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
                    c.Thumb.Place(new Native.RECT { Left = (int)tl.X - wr.Left, Top = (int)tl.Y - wr.Top, Right = (int)br.X - wr.Left, Bottom = (int)br.Y - wr.Top }, _zoom.Visibility == Visibility.Visible ? (byte)0 : _alpha);
                }
                catch
                {
                }
            }
        }

        void Highlight()
        {
            var list = Visible();
            var accent = Theme.Accent;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                c.Frame.BorderBrush = c.Picked ? new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58))
                    : i == _sel ? new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B))
                    : Brushes.Transparent;
            }
        }

        void Pick(Card c)
        {
            var h = c.Win.Hwnd;
            Close2();
            Native.Focus(h);
        }

        void CloseWin(Card card)
        {
            WinList.Close(card.Win.Hwnd);
            card.Thumb?.Dispose();
            _cards.Remove(card);
            _canvas.Children.Remove(card.Host);
            Arrange(true);
        }

        List<MenuEntry> Menu(Card card)
        {
            var h = card.Win.Hwnd;
            var all = Displays.All();
            var list = new List<MenuEntry>
            {
                MenuEntry.Item("\uE8A7", "Switch to it", () => Pick(card))
            };
            var others = all.Where(d => d.Key != _display?.Key).ToList();
            if (others.Count == 1)
            {
                var other = others[0];
                list.Add(MenuEntry.Item("\uE7F4", "Move to the other screen", () => Send(card, other)));
            }
            else if (others.Count > 1)
            {
                list.Add(MenuEntry.Sub("\uE7F4", "Move to", others.Select(d => MenuEntry.Item("\uE7F4", $"Screen {d.Index + 1}{(d.Primary ? " (main)" : "")}", () => Send(card, d))).ToList()));
            }
            bool borderless = WinOps.IsBorderless(h);
            list.Add(MenuEntry.Toggle("\uE740", "Borderless fullscreen", borderless, () =>
            {
                WinOps.Borderless(h, _display);
                Rebuild();
            }));
            var d0 = _display ?? Displays.AtCursor();
            string layout = _s.SnapLayouts.TryGetValue(d0.Key, out var l) && l.Length > 0 ? l : "auto";
            var zones = SnapZones.Zones(layout, d0);
            list.Add(MenuEntry.Sub("\uE80A", "Snap to", zones.Select((z, i) => MenuEntry.Item("\uE80A", WinOps.ZoneName(z, i, zones.Count), () =>
            {
                int gap = (int)Math.Round(Math.Clamp(_s.SnapGap, 0, 40) * d0.Scale);
                SnapZones.Place(h, SnapZones.ToPx(z, d0, gap));
                Rebuild();
            })).ToList()));
            list.Add(MenuEntry.Sep());
            bool max = Native.IsZoomed(h);
            list.Add(MenuEntry.Item(max ? "\uE923" : "\uE922", max ? "Restore" : "Maximize", () =>
            {
                Native.ShowWindow(h, max ? Native.SW_RESTORE : Native.SW_MAXIMIZE);
                Rebuild();
            }));
            list.Add(MenuEntry.Item(card.Win.Min ? "\uE740" : "\uE921", card.Win.Min ? "Restore" : "Minimize", () =>
            {
                Native.ShowWindow(h, card.Win.Min ? Native.SW_RESTORE : Native.SW_MINIMIZE);
                Rebuild();
            }));
            list.Add(MenuEntry.Toggle("\uE718", "Always on top", WinOps.IsTopmost(h), () =>
            {
                WinOps.ToggleTopmost(h);
                Rebuild();
            }));
            if (Hooks.Pip != null) list.Add(MenuEntry.Item("\uE8A0", "Picture in picture", () =>
            {
                Close2();
                Hooks.Pip(h);
            }));
            if (Hooks.Pocket != null) list.Add(MenuEntry.Item("\uE7B8", "Pocket it on the shelf", () =>
            {
                Close2();
                Hooks.Pocket(h);
            }));
            var stackable = _cards.Where(c => c != card && !c.Win.Min).Take(8).ToList();
            if (Hooks.Stack != null && stackable.Count > 0)
            {
                list.Add(MenuEntry.Sub("\uE8A9", "Tab it with", stackable.Select(c => MenuEntry.Item("\uE8A9", c.Win.Title.Length > 40 ? c.Win.Title[..40] + "..." : c.Win.Title, () =>
                {
                    Close2();
                    Hooks.Stack(h, c.Win.Hwnd);
                })).ToList()));
            }
            var audio = Hooks.Audio?.Invoke(card.Win.Exe);
            if (audio is { } au && Hooks.Mute != null)
            {
                bool muted = au.muted;
                string exe = card.Win.Exe;
                list.Add(MenuEntry.Item(muted ? "\uE767" : "\uE74F", muted ? "Unmute it" : "Mute it", () =>
                {
                    Hooks.Mute(exe, !muted);
                    Rebuild();
                }));
            }
            list.Add(MenuEntry.Sep());
            if (Hooks.Shot != null) list.Add(MenuEntry.Item("\uE722", "Screenshot it", () =>
            {
                Close2();
                Hooks.Shot(h);
            }));
            if (Hooks.Record != null) list.Add(MenuEntry.Item("\uE7C8", "Record it", () =>
            {
                Close2();
                Hooks.Record(h);
            }));
            if (Hooks.Remember != null) list.Add(MenuEntry.Item("\uE8A7", "Remember where this goes", () => Hooks.Remember(h)));
            if (Hooks.Note != null) list.Add(MenuEntry.Item("\uE70B", "Add a note to this app", () =>
            {
                Close2();
                Hooks.Note(h);
            }));
            if (card.Win.Path != null)
            {
                string path = card.Win.Path;
                list.Add(MenuEntry.Item("\uE838", "Show the app's file", () =>
                {
                    Close2();
                    Files.Reveal(path);
                }));
                if (Hooks.Inspect != null) list.Add(MenuEntry.Item("\uE946", "Inspect", () =>
                {
                    Close2();
                    Hooks.Inspect(path);
                }));
            }
            list.Add(MenuEntry.Sep());
            list.Add(MenuEntry.Item("\uE8BB", "Close", () => CloseWin(card)));
            list.Add(MenuEntry.Item("\uE711", card.Win.Hung ? "End task (it's frozen)" : "End task", () =>
            {
                if (WinList.Kill(h)) CloseWin(card);
            }, true));
            return list;
        }

        void Send(Card card, Display d)
        {
            WinOps.MoveTo(card.Win.Hwnd, d, Hooks.Say);
            card.Thumb?.Dispose();
            _cards.Remove(card);
            _canvas.Children.Remove(card.Host);
            Sfx.Play("throw");
            Arrange(false);
        }

        void OnDragMove(object sender, MouseEventArgs e)
        {
            if (_press == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (!_dragging)
            {
                if (Math.Abs(p.X - _pressAt.X) < 8 && Math.Abs(p.Y - _pressAt.Y) < 8) return;
                if (_screenTiles.Count == 0) return;
                _dragging = true;
                CaptureMouse();
                var icon = new Image { Width = 40, Height = 40 };
                if (_press.Win.Path != null) _cat.Icons.Get(_press.Win.Path, 64, true, src => icon.Source = src);
                _ghost = new Border { Child = icon, Padding = new Thickness(10), CornerRadius = Kit.Round(14), Background = new SolidColorBrush(Color.FromArgb(0xD0, 30, 30, 36)), IsHitTestVisible = false };
                _canvas.Children.Add(_ghost);
                _press.Host.Opacity = 0.4;
            }
            if (_ghost != null)
            {
                Canvas.SetLeft(_ghost, p.X - 30);
                Canvas.SetTop(_ghost, p.Y - 30);
            }
            foreach (var (tile, d) in _screenTiles)
            {
                bool over = Over(tile, p) && d.Key != _display?.Key;
                tile.Background = new SolidColorBrush(over ? Color.FromArgb(0x80, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B) : Color.FromArgb(d.Key == _display?.Key ? (byte)0x40 : (byte)0x22, 255, 255, 255));
            }
        }

        bool Over(FrameworkElement el, Point p)
        {
            var tl = el.TranslatePoint(new Point(0, 0), this);
            return new Rect(tl, new Size(el.ActualWidth, el.ActualHeight)).Contains(p);
        }

        void OnDragUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
            {
                if (_press != null && !_press.Host.IsMouseOver) _press = null;
                return;
            }
            e.Handled = true;
            var card = _press;
            var p = e.GetPosition(this);
            Display? target = null;
            foreach (var (tile, d) in _screenTiles)
                if (Over(tile, p) && d.Key != _display?.Key) target = d;
            EndDrag();
            if (card != null && target != null) Send(card, target);
            BuildScreens();
        }

        void EndDrag()
        {
            if (_press != null) _press.Host.Opacity = _press.Win.Min ? 0.6 : 1;
            _press = null;
            if (_ghost != null) _canvas.Children.Remove(_ghost);
            _ghost = null;
            if (_dragging) ReleaseMouseCapture();
            _dragging = false;
        }

        void Zoom()
        {
            var list = Visible();
            if (list.Count == 0) return;
            var c = list[Math.Clamp(_sel, 0, list.Count - 1)];
            if (c.Win.Min) return;
            _zoom.Visibility = Visibility.Visible;
            var h = new WindowInteropHelper(this).Handle;
            _zoomThumb?.Dispose();
            _zoomThumb = new Thumb(h, c.Win.Hwnd);
            PlaceThumbs();
            Native.GetWindowRect(h, out var wr);
            int padX = (int)(wr.Width * 0.08), padY = (int)(wr.Height * 0.08);
            var rect = new Native.RECT { Left = padX, Top = padY, Right = wr.Width - padX, Bottom = wr.Height - padY };
            Frames.Run(200 * Theme.Speed, t => _zoomThumb?.Place(rect, (byte)Math.Round(255 * Frames.Out(t))), null, _zoom);
        }

        void Unzoom()
        {
            if (_zoom.Visibility != Visibility.Visible) return;
            _zoom.Visibility = Visibility.Collapsed;
            _zoomThumb?.Dispose();
            _zoomThumb = null;
            PlaceThumbs();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var list = Visible();
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            Card? cur = list.Count > 0 ? list[Math.Clamp(_sel, 0, list.Count - 1)] : null;
            if (_zoom.Visibility == Visibility.Visible && e.Key is Key.Space or Key.Escape)
            {
                Unzoom();
                e.Handled = true;
                return;
            }
            switch (e.Key)
            {
                case Key.Escape:
                    if (GlassMenu.Showing) GlassMenu.CloseAll();
                    else if (_filter.Length > 0)
                    {
                        _filter = "";
                        Arrange(true);
                    }
                    else if (_cards.Any(c => c.Picked))
                    {
                        foreach (var c in _cards) c.Picked = false;
                        Highlight();
                        Hint();
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
                    if (cur != null) Pick(cur);
                    break;
                case Key.Space:
                    if (_filter.Length > 0) return;
                    Zoom();
                    break;
                case Key.Delete:
                    if (cur != null) CloseWin(cur);
                    break;
                case Key.T:
                    {
                        var picked = _cards.Where(c => c.Picked).Select(c => c.Win.Hwnd).ToList();
                        if (_filter.Length > 0 || picked.Count < 2) return;
                        var d = _display ?? Displays.AtCursor();
                        Close2();
                        WinOps.Tile(picked, d);
                    }
                    break;
                case Key.Right:
                case Key.Left:
                    if (cur != null && shift)
                    {
                        var all = Displays.All();
                        if (all.Count < 2) break;
                        int i = all.FindIndex(x => x.Key == _display?.Key);
                        Send(cur, all[((i + (e.Key == Key.Right ? 1 : -1)) % all.Count + all.Count) % all.Count]);
                        break;
                    }
                    if (list.Count > 0) _sel = ((_sel + (e.Key == Key.Right ? 1 : -1)) % list.Count + list.Count) % list.Count;
                    Highlight();
                    break;
                case Key.Down:
                    if (list.Count > 0) _sel = Math.Min(list.Count - 1, _sel + _cols);
                    Highlight();
                    break;
                case Key.Up:
                    if (list.Count > 0) _sel = Math.Max(0, _sel - _cols);
                    Highlight();
                    break;
                case Key.Tab:
                    if (list.Count > 0) _sel = ((_sel + (shift ? -1 : 1)) % list.Count + list.Count) % list.Count;
                    Highlight();
                    break;
                default:
                    if (_filter.Length == 0 && e.Key >= Key.D1 && e.Key <= Key.D9)
                    {
                        int n = e.Key - Key.D1;
                        if (n < list.Count) Pick(list[n]);
                        break;
                    }
                    return;
            }
            e.Handled = true;
        }
    }
}
