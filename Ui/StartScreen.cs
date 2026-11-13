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
    internal sealed class StartScreen : Window
    {
        const double Cell = 58;

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly AppIndex _apps;
        readonly StartData _data;
        readonly Commands _cmds;
        readonly Notifier _notes;
        readonly NowPlaying _np;
        readonly Activity _act;
        readonly Library _lib;

        readonly Grid _root = new();
        readonly Border _scrim = new();
        readonly Grid _stage = new();
        readonly TextBlock _clock;
        readonly TextBlock _date;
        readonly TextBox _search;
        readonly StackPanel _main = new();
        readonly StackPanel _side = new() { Width = 300 };
        readonly WrapPanel _dockRow = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(10) };
        readonly Image _backdrop = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
        bool _shown, _allApps, _dirty = true, _warm;
        DateTime _builtAt = DateTime.MinValue;

        public Action<string>? Search;
        public Action? OpenSettings;
        public Action? Overview;
        public Func<IEnumerable<ExtWidget>>? ExtWidgets;

        public bool Shown => _shown;

        public StartScreen(Settings s, AppCatalog cat, AppIndex apps, StartData data, Commands cmds, Notifier notes, NowPlaying np, Activity act, Library lib)
        {
            _s = s;
            _cat = cat;
            _apps = apps;
            _data = data;
            _cmds = cmds;
            _notes = notes;
            _np = np;
            _act = act;
            _lib = lib;
            Glass.Attach(this, 0);
            Glass.NoBlur(this);
            Title = "heft start";

            RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.HighQuality);
            _backdrop.RenderTransformOrigin = new Point(0.5, 0.5);
            _backdrop.RenderTransform = new ScaleTransform(1.04, 1.04);
            _root.Children.Add(_backdrop);
            _scrim.Background = new SolidColorBrush(Color.FromArgb(0x78, 8, 8, 12));
            _scrim.MouseLeftButtonDown += (_, _) => Close2();
            _root.Children.Add(_scrim);

            var layout = new Grid { Margin = new Thickness(60, 46, 60, 36), MaxWidth = 1320 };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(0, 0, 0, 26) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var time = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _clock = new TextBlock { FontSize = 54, FontWeight = FontWeights.Light, Foreground = Brushes.White };
            _date = new TextBlock { FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), Margin = new Thickness(2, -4, 0, 0) };
            time.Children.Add(_clock);
            time.Children.Add(_date);
            head.Children.Add(time);

            var searchHost = new Border
            {
                CornerRadius = new CornerRadius(14),
                Height = 52,
                MaxWidth = 620,
                Margin = new Thickness(40, 0, 0, 0),
                Padding = new Thickness(18, 0, 18, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mag = new TextBlock { Text = "\uE721", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            sg.Children.Add(mag);
            var field = new Grid();
            var ph = new TextBlock { Text = "type to search apps, files, the web, or ask for a reminder", FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            field.Children.Add(ph);
            _search = new TextBox { FontSize = 16, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
            _search.TextChanged += (_, _) =>
            {
                ph.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_search.Text.Length == 0) return;
                string text = _search.Text;
                Close2();
                Search?.Invoke(text);
            };
            field.Children.Add(_search);
            Grid.SetColumn(field, 1);
            sg.Children.Add(field);
            searchHost.Child = sg;
            Grid.SetColumn(searchHost, 1);
            head.Children.Add(searchHost);
            layout.Children.Add(head);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mainScroll = new ScrollViewer { Content = _main, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Margin = new Thickness(0, 0, 30, 0) };
            body.Children.Add(mainScroll);
            var sideScroll = new ScrollViewer { Content = _side, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            Grid.SetColumn(sideScroll, 1);
            body.Children.Add(sideScroll);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            var dockBar = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 22, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = _dockRow
            };
            Grid.SetRow(dockBar, 2);
            layout.Children.Add(dockBar);

            _stage.Children.Add(layout);
            _root.Children.Add(_stage);
            Content = _root;

            PreviewKeyDown += OnKey;
            Deactivated += (_, _) => Close2();
            _tick.Tick += (_, _) => Clock();
            _data.Updated += () => _dirty = true;
            _lib.Updated += () => _dirty = true;
            _refresh.Tick += (_, _) =>
            {
                if (!_shown) Prepare();
            };
        }

        public void Prepare()
        {
            try
            {
                if (_apps.Loaded) _data.Rename();
                _data.Refresh();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("start refresh failed " + ex.Message);
            }
        }

        public void MarkDirty() => _dirty = true;

        public void Hold(bool on)
        {
            if (on) _refresh.Stop();
            else if (_warm) _refresh.Start();
        }

        public void Prewarm()
        {
            if (_warm || _shown) return;
            _warm = true;
            try
            {
                if (!_apps.Loaded) _apps.Load();
                _data.Rename();
                BuildMain();
                BuildSide();
                var d = Displays.Primary();
                var h = new WindowInteropHelper(this).EnsureHandle();
                Width = d.Bounds.Width / d.Scale;
                Height = d.Bounds.Height / d.Scale;
                _root.Opacity = 0;
                ShowActivated = false;
                Native.SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
                Show();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowActivated = true;
                    if (!_shown) Hide();
                }), DispatcherPriority.ContextIdle);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("start prewarm failed " + ex.Message);
            }
            _refresh.Start();
            Prepare();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close2();
                e.Handled = true;
                return;
            }
            if (e.OriginalSource != _search && e.Key >= Key.A && e.Key <= Key.Z && Keyboard.Modifiers == ModifierKeys.None) _search.Focus();
        }

        public void Toggle()
        {
            if (_shown) Close2();
            else Open();
        }

        public void Open()
        {
            var d = Displays.AtCursor();
            _backdrop.Source = Backdrop.Capture(d.Bounds);
            _shown = true;
            bool wasAll = _allApps;
            _allApps = false;
            if (!_apps.Loaded)
            {
                _apps.Load();
                _data.Rename();
                _dirty = true;
            }
            if (_dirty || wasAll || (DateTime.Now - _builtAt).TotalMinutes > 5) BuildMain();
            _side.Children.Clear();
            BuildSide();
            _search.Text = "";
            Clock();
            _tick.Start();

            Glass.Present(this, _root, () =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Width = d.Bounds.Width / d.Scale;
                Height = d.Bounds.Height / d.Scale;
                Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, Native.SWP_NOACTIVATE);
            }, 0, 1, 200);

            var st = new ScaleTransform(1.035, 1.035);
            _stage.RenderTransformOrigin = new Point(0.5, 0.45);
            _stage.RenderTransform = st;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.035, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.035, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });

            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
            _search.Focus();
            Entrance();
        }

        void Entrance()
        {
            int i = 0;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            foreach (var el in _main.Children.OfType<UIElement>().Concat(_side.Children.OfType<UIElement>()))
            {
                var tt = new TranslateTransform(0, 14);
                el.RenderTransform = tt;
                var begin = TimeSpan.FromMilliseconds(30 + Math.Min(i, 12) * 26);
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = begin });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = begin, EasingFunction = ease });
                i++;
            }
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            _tick.Stop();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_shown) Prepare();
            }), DispatcherPriority.ApplicationIdle);
            Glass.Dismiss(this, _root, null, 150);
            var st = new ScaleTransform(1, 1);
            _stage.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(150)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(150)));
        }

        void Clock()
        {
            var now = DateTime.Now;
            _clock.Text = now.ToString(_s.Bar24h ? "HH:mm" : "h:mm");
            string part = now.Hour < 5 ? "up late" : now.Hour < 12 ? "good morning" : now.Hour < 17 ? "good afternoon" : now.Hour < 22 ? "good evening" : "late night";
            _date.Text = $"{now:dddd, MMMM d}   ·   {part}";
        }

        void Build()
        {
            BuildMain();
            _side.Children.Clear();
            BuildSide();
            if (_shown) Entrance();
        }

        void BuildMain()
        {
            _main.Children.Clear();
            _dockRow.Children.Clear();
            if (_allApps) BuildAllApps();
            else BuildPinned();
            BuildDock();
            _dirty = false;
            _builtAt = DateTime.Now;
        }

        void RefreshSide()
        {
            _side.Children.Clear();
            BuildSide();
        }

        UIElement Header(string text, string? linkText = null, Action? link = null)
        {
            var dp = new DockPanel { Margin = new Thickness(4, 6, 8, 10) };
            if (linkText != null && link != null)
            {
                var l = new TextBlock { Text = linkText, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                l.MouseEnter += (_, _) => l.Foreground = Brushes.White;
                l.MouseLeave += (_, _) => l.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255));
                l.MouseLeftButtonUp += (_, _) => link();
                DockPanel.SetDock(l, Dock.Right);
                dp.Children.Add(l);
            }
            dp.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 255, 255, 255)) });
            return dp;
        }

        Border Tile(string name, string? iconPath, bool iconOnly, string glyph, double w, double h, Action click, Action? alt = null)
        {
            bool big = h >= Cell * 2 - 1;
            var g = new Grid();
            var img = new Image { Width = big ? 40 : 30, Height = big ? 40 : 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, big ? 16 : 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var fallback = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = big ? 26 : 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = img.Margin };
            g.Children.Add(fallback);
            g.Children.Add(img);
            if (iconPath != null)
            {
                _cat.Icons.Get(iconPath, 96, iconOnly, src =>
                {
                    img.Source = src;
                    if (src != null) fallback.Visibility = Visibility.Collapsed;
                });
            }
            if (big || w >= Cell * 2 - 1)
            {
                var label = new TextBlock { Text = name, FontSize = 12, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 8) };
                g.Children.Add(label);
            }

            var tile = new Border
            {
                Width = w,
                Height = h,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = g,
                Cursor = Cursors.Hand,
                ToolTip = name,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var sc = new ScaleTransform(1, 1);
            tile.RenderTransform = sc;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tile.MouseEnter += (_, _) =>
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(0x34, 255, 255, 255));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
            };
            tile.MouseLeave += (_, _) =>
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            };
            tile.MouseLeftButtonUp += (_, _) =>
            {
                Close2();
                click();
            };
            if (alt != null)
                tile.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    alt();
                };
            return tile;
        }

        void BuildPinned()
        {
            var groups = _data.Groups;
            if (groups.Count == 0)
            {
                _main.Children.Add(Header("Pinned", "All apps  ›", () =>
                {
                    _allApps = true;
                    Build();
                }));
                var hint = new TextBlock { Text = "loading your Start menu pins...", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), Margin = new Thickness(6, 0, 0, 18) };
                _main.Children.Add(hint);
            }
            else
            {
                _main.Children.Add(Header("Pinned", "All apps  ›", () =>
                {
                    _allApps = true;
                    Build();
                }));
                var wrap = new WrapPanel();
                foreach (var g in groups)
                {
                    var col = new StackPanel { Margin = new Thickness(0, 0, 26, 18), VerticalAlignment = VerticalAlignment.Top };
                    if (g.Name.Length > 0) col.Children.Add(new TextBlock { Text = g.Name, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), Margin = new Thickness(6, 0, 0, 6) });
                    var canvas = new Canvas();
                    int maxCol = 0, maxRow = 0;
                    foreach (var p in g.Pins)
                    {
                        var pin = p;
                        double w = Math.Max(1, p.W) * Cell + (p.W - 1) * 8;
                        double h = Math.Max(1, p.H) * Cell + (p.H - 1) * 8;
                        var tile = Tile(p.Name, p.IconPath, true, "\uE71D", w, h, pin.Launch);
                        tile.Margin = new Thickness(0);
                        Canvas.SetLeft(tile, p.Col * (Cell + 8));
                        Canvas.SetTop(tile, p.Row * (Cell + 8));
                        canvas.Children.Add(tile);
                        maxCol = Math.Max(maxCol, p.Col + p.W);
                        maxRow = Math.Max(maxRow, p.Row + p.H);
                    }
                    canvas.Width = maxCol * (Cell + 8);
                    canvas.Height = maxRow * (Cell + 8);
                    col.Children.Add(canvas);
                    wrap.Children.Add(col);
                }
                _main.Children.Add(wrap);
            }

            var recent = _s.CommandHistory.Where(hh => hh.Key.StartsWith("app:"))
                .OrderByDescending(hh => hh.Score * Math.Pow(0.5, (DateTime.Now - hh.Last).TotalDays / 14))
                .Select(hh => _apps.Apps.FirstOrDefault(a => "app:" + a.Id == hh.Key))
                .Where(a => a != null).Take(8).ToList();
            var used = _s.Usage.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Where(File.Exists).Take(10).ToList();
            if (recent.Count + used.Count > 0)
            {
                _main.Children.Add(Header("You use these a lot"));
                var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                foreach (var a in recent)
                {
                    var app = a!;
                    row.Children.Add(Tile(app.Name, AppIndex.IconPath(app), true, "\uE71D", Cell * 2 + 8, Cell, () => AppIndex.Launch(app)));
                }
                foreach (var path in used)
                {
                    if (row.Children.Count >= 12) break;
                    string p = path;
                    row.Children.Add(Tile(_cat.DisplayName(p), p, true, "\uE71D", Cell * 2 + 8, Cell, () => Launch.Run(p)));
                }
                _main.Children.Add(row);
            }

            var files = _lib.Get("recent").Take(10).ToList();
            if (files.Count > 0)
            {
                _main.Children.Add(Header("Recent files"));
                var row = new WrapPanel();
                foreach (var f in files)
                {
                    string p = f.Path;
                    row.Children.Add(Tile(f.Name, p, f.Dir, f.Dir ? "\uE8B7" : "\uE8A5", Cell * 2 + 8, Cell * 2 + 8, () => Files.Open(p, _s), () =>
                    {
                        Close2();
                        Files.Reveal(p);
                    }));
                }
                _main.Children.Add(row);
            }
        }

        void BuildAllApps()
        {
            _main.Children.Add(Header("All apps", "‹  Pinned", () =>
            {
                _allApps = false;
                Build();
            }));
            var letters = new WrapPanel { Margin = new Thickness(2, 0, 0, 10) };
            _main.Children.Add(letters);
            var sorted = _apps.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var group in sorted.GroupBy(a => char.IsLetter(a.Name.FirstOrDefault()) ? char.ToUpperInvariant(a.Name[0]) : '#'))
            {
                var head = new TextBlock { Text = group.Key.ToString(), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 10, 0, 4) };
                _main.Children.Add(head);
                var jump = new TextBlock { Text = group.Key.ToString(), FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), Margin = new Thickness(0, 0, 10, 4), Cursor = Cursors.Hand };
                jump.MouseLeftButtonUp += (_, _) => head.BringIntoView(new Rect(0, 0, 10, 400));
                letters.Children.Add(jump);
                var row = new WrapPanel();
                foreach (var a in group)
                {
                    var app = a;
                    row.Children.Add(Tile(app.Name, AppIndex.IconPath(app), true, "\uE71D", Cell * 3 + 16, Cell, () => AppIndex.Launch(app)));
                }
                _main.Children.Add(row);
            }
        }

        Border Card(UIElement child)
        {
            return new Border
            {
                Child = child,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
        }

        static TextBlock W(string text, double size, bool bold = false, byte alpha = 0xFF)
        {
            return new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
        }

        static TextBlock G(string glyph, double size, byte alpha = 0xFF)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        }

        void BuildSide()
        {
            if (_data.Weather.Length > 0)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = G(_data.WeatherGlyph, 30);
                icon.Margin = new Thickness(0, 0, 14, 0);
                g.Children.Add(icon);
                var sp = new StackPanel();
                sp.Children.Add(W(_data.Weather, 17, true));
                if (_data.WeatherPlace.Length > 0) sp.Children.Add(W(_data.WeatherPlace, 12, false, 0xA0));
                Grid.SetColumn(sp, 1);
                g.Children.Add(sp);
                _side.Children.Add(Card(g));
            }

            if (_np.HasSession && _np.Title.Length > 0)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var art = new Image { Width = 58, Height = 58, Source = _np.Art, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 12, 0) };
                art.Clip = new RectangleGeometry(new Rect(0, 0, 58, 58), 10, 10);
                g.Children.Add(art);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(W(_np.Title, 14, true));
                sp.Children.Add(W(_np.Artist, 12, false, 0xA8));
                var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 4, 0, 0) };
                ctl.Children.Add(Btn("\uE892", () => _ = _np.Previous()));
                ctl.Children.Add(Btn(_np.Playing ? "\uE769" : "\uE768", () => _ = _np.Toggle()));
                ctl.Children.Add(Btn("\uE893", () => _ = _np.Next()));
                sp.Children.Add(ctl);
                Grid.SetColumn(sp, 1);
                g.Children.Add(sp);
                _side.Children.Add(Card(g));
            }

            var toggles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            void AddToggle(string glyph, string label, string id)
            {
                var c = _cmds.Get(id);
                if (c == null) return;
                string? state = c.State?.Invoke();
                bool on = id == "desktop" ? state == "showing" : state == "on";
                var b = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Padding = new Thickness(6, 10, 6, 8), Cursor = Cursors.Hand, Background = new SolidColorBrush(on ? Color.FromArgb(0xE0, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x1C, 255, 255, 255)) };
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var gl = G(glyph, 16);
                gl.HorizontalAlignment = HorizontalAlignment.Center;
                sp.Children.Add(gl);
                var t = W(label, 11);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, 5, 0, 0);
                sp.Children.Add(t);
                b.Child = sp;
                b.MouseLeftButtonUp += (_, _) =>
                {
                    c.Run();
                    Dispatcher.BeginInvoke(new Action(RefreshSide), DispatcherPriority.Background);
                };
                toggles.Children.Add(b);
            }
            AddToggle("\uE708", "Focus", "focus");
            AddToggle("\uE72E", "Cursor lock", "lock");
            AddToggle("\uE7F4", "Desktop", "desktop");
            AddToggle("\uE945", "Physics", "physics");
            AddToggle("\uE77F", "Paste stack", "stack");
            var themeCmd = _cmds.Get("theme");
            if (themeCmd != null)
            {
                var b = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Padding = new Thickness(6, 10, 6, 8), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)) };
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var gl = G("\uE793", 16);
                gl.HorizontalAlignment = HorizontalAlignment.Center;
                sp.Children.Add(gl);
                var t = W("Light/dark", 11);
                t.Margin = new Thickness(0, 5, 0, 0);
                sp.Children.Add(t);
                b.Child = sp;
                b.MouseLeftButtonUp += (_, _) => themeCmd.Run();
                toggles.Children.Add(b);
            }
            _side.Children.Add(Card(toggles));

            var sys = new StackPanel();
            foreach (var (label, value) in new[] { ("CPU", _act.Cpu), ("RAM", _act.Ram), ("GPU", _act.Gpu) })
            {
                if (value < 0) continue;
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.Children.Add(W(label, 12, false, 0xA8));
                var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
                var fill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x84, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                track.Child = fill;
                track.SizeChanged += (_, e) => fill.BeginAnimation(WidthProperty, new DoubleAnimation(e.NewSize.Width * value / 100.0, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                Grid.SetColumn(track, 1);
                row.Children.Add(track);
                var v = W(value + "%", 12);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(v, 2);
                row.Children.Add(v);
                sys.Children.Add(row);
            }
            _side.Children.Add(Card(sys));

            if (ExtWidgets != null)
            {
                foreach (var wdg in ExtWidgets())
                {
                    var sp = new StackPanel();
                    if (wdg.Title.Length > 0) sp.Children.Add(W(wdg.Title, 13, true, 0xC8));
                    if (wdg.Body.Length > 0)
                    {
                        var body = W(wdg.Body, 12.5);
                        body.TextWrapping = TextWrapping.Wrap;
                        body.Margin = new Thickness(0, wdg.Title.Length > 0 ? 6 : 0, 0, 0);
                        sp.Children.Add(body);
                    }
                    _side.Children.Add(Card(sp));
                }
            }

            if (_s.Reminders.Count > 0)
            {
                var sp = new StackPanel();
                sp.Children.Add(W("Coming up", 13, true, 0xC8));
                foreach (var r in _s.Reminders.OrderBy(r => r.Due).Take(4))
                {
                    var t = W($"{r.Due:h:mm tt}  ·  {(r.Timer ? "timer" : r.Text)}", 12.5);
                    t.Margin = new Thickness(0, 6, 0, 0);
                    sp.Children.Add(t);
                }
                _side.Children.Add(Card(sp));
            }

            var power = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var (glyph, id, tip) in new[] { ("\uE72E", "pclock", "lock"), ("\uE708", "sleep", "sleep"), ("\uE777", "restart", "restart"), ("\uE7E8", "shutdown", "shut down"), ("\uE713", "", "heft settings") })
            {
                string cid = id;
                var b = Btn(glyph, () =>
                {
                    Close2();
                    if (cid.Length == 0) OpenSettings?.Invoke();
                    else _cmds.Run(cid);
                }, 17);
                b.ToolTip = tip;
                b.Width = 44;
                b.Height = 40;
                power.Children.Add(b);
            }
            _side.Children.Add(Card(power));
        }

        Border Btn(string glyph, Action click, double size = 13)
        {
            var b = new Border { Width = 32, Height = 30, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = G(glyph, size), Cursor = Cursors.Hand };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return b;
        }

        void BuildDock()
        {
            foreach (var p in _data.Taskbar)
            {
                var pin = p;
                var t = Tile(p.Name, p.Target, true, "\uE71D", 52, 52, () => Launch.Run(pin.Target));
                t.Margin = new Thickness(3);
                t.CornerRadius = new CornerRadius(12);
                _dockRow.Children.Add(t);
            }
            var overview = Btn("\uE8A9", () =>
            {
                Close2();
                Overview?.Invoke();
            }, 18);
            overview.Width = 52;
            overview.Height = 52;
            overview.ToolTip = "all your windows";
            overview.Margin = new Thickness(10, 3, 3, 3);
            _dockRow.Children.Add(overview);
        }
    }
}
