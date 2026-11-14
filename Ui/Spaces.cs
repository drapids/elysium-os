using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Elysium
{
    public sealed class SpaceItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string Kind { get; set; } = "file";
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; } = 104;
        public double H { get; set; } = 112;
        public string Path { get; set; } = "";
        public string Text { get; set; } = "";
        public string Exe { get; set; } = "";
    }

    public sealed class SpaceWin
    {
        public string Exe { get; set; } = "";
        public string Title { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Max { get; set; }
    }

    public sealed class SpaceDoc
    {
        public string Name { get; set; } = "home";
        public string Desktop { get; set; } = "";
        public List<SpaceItem> Items { get; set; } = new();
        public double PanX { get; set; } = 40;
        public double PanY { get; set; } = 90;
        public double Zoom { get; set; } = 1;
        public List<SpaceWin> Windows { get; set; } = new();
        public List<SpaceLink> Links { get; set; } = new();
    }

    public sealed class SpacesFile
    {
        public List<SpaceDoc> Spaces { get; set; } = new();
        public int Current { get; set; }
    }
}

namespace Elysium.Ui
{
    internal sealed partial class Spaces : Window
    {
        static readonly string FilePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium", "spaces.json");
        static readonly HashSet<string> Resizable = new() { "note", "group", "portal", "smart" };

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Library _lib;
        readonly NowPlaying _np;
        readonly StartData _data;

        readonly Grid _root = new();
        readonly Canvas _viewport = new() { ClipToBounds = true, AllowDrop = true };
        readonly Canvas _world = new();
        readonly Canvas _over = new();
        readonly ScaleTransform _scale = new(1, 1);
        readonly TranslateTransform _pan = new(0, 0);
        readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
        readonly TextBlock _zoomText;
        readonly Border _dots = new() { IsHitTestVisible = false };
        readonly DrawingBrush _dotBrush;
        readonly Dictionary<SpaceItem, FrameworkElement> _els = new();
        readonly Dictionary<SpaceItem, (FrameworkElement host, Thumb thumb)> _thumbs = new();
        readonly List<Action> _tickers = new();
        readonly List<(FrameworkElement el, double minZoom)> _details = new();
        readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        SpacesFile _file = new();
        SpaceDoc _cur = new();
        Display? _display;
        bool _shown;
        Border? _menu;

        SpaceItem? _dragItem;
        bool _resizing, _moved, _panning;
        Point _dragStart;
        double _x0, _y0, _w0, _h0, _panX0, _panY0;
        List<(SpaceItem item, double x, double y)> _carry = new();
        SpaceItem? _hover;
        int _z = 10;
        int _tick;

        public Action<Obj, Rect?>? Inspect;
        public Action<string>? Say;

        void PickDesktop()
        {
            if (!_s.SpacePerDesktop) return;
            var (ids, cur) = VirtualDesktops.Read();
            if (ids.Count < 2) return;
            string id = ids[cur].ToString("N");
            var doc = _file.Spaces.FirstOrDefault(x => x.Desktop == id);
            if (doc == null)
            {
                bool anyBound = _file.Spaces.Any(x => x.Desktop.Length > 0);
                if (!anyBound && cur == 0)
                {
                    doc = _cur;
                }
                else
                {
                    doc = new SpaceDoc { Name = "desktop " + (cur + 1) };
                    _file.Spaces.Add(doc);
                }
                doc.Desktop = id;
                Touch();
            }
            if (doc == _cur) return;
            _cur = doc;
            _file.Current = _file.Spaces.IndexOf(doc);
        }

        public Spaces(Settings s, AppCatalog cat, Library lib, NowPlaying np, StartData data)
        {
            _s = s;
            _cat = cat;
            _lib = lib;
            _np = np;
            _data = data;
            Glass.Attach(this, 0);
            Topmost = false;
            Title = "elysium spaces";

            var scrim = new Border();
            scrim.Background = new SolidColorBrush(Color.FromArgb(0xB0, 10, 11, 14));
            _root.Children.Add(scrim);

            var dots = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), null, new EllipseGeometry(new Point(1, 1), 1, 1));
            var cell = new DrawingGroup();
            cell.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
            cell.Children.Add(dots);
            _dotBrush = new DrawingBrush(cell) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 28, 28), Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            _dots.Background = _dotBrush;
            _root.Children.Add(_dots);

            _viewport.Background = Brushes.Transparent;
            var g = new TransformGroup();
            g.Children.Add(_scale);
            g.Children.Add(_pan);
            _world.RenderTransform = g;
            _viewport.Children.Add(_world);
            _root.Children.Add(_viewport);

            var top = new DockPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 14, 18, 0) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(Tool("\uE8CB", "tidy up (t)", Tidy));
            tools.Children.Add(Tool("\uE9A6", "fit everything (f)", Fit));
            tools.Children.Add(Tool("\uE80A", "snap to grid (g)", ToggleGrid));
            tools.Children.Add(Tool("\uE74E", "remember the windows on screen for this space", SaveWindows));
            tools.Children.Add(Tool("\uE8A7", "put this space's windows back", RestoreWindows));
            tools.Children.Add(Tool("\uE8BB", "close (esc)", Close2));
            DockPanel.SetDock(tools, Dock.Right);
            top.Children.Add(tools);
            var tabScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tabs, Focusable = false };
            top.Children.Add(tabScroll);
            _over.Children.Clear();
            var overGrid = new Grid();
            overGrid.Children.Add(top);

            var hint = new TextBlock
            {
                Text = "drag files in  ·  double-click for a note  ·  right-click for widgets and arrows  ·  scroll to zoom  ·  g snaps to the grid  ·  ctrl + drag a file to take it out",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(22, 0, 0, 16),
                IsHitTestVisible = false
            };
            overGrid.Children.Add(hint);
            _zoomText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 22, 16), Cursor = Cursors.Hand, ToolTip = "back to 100%" };
            _zoomText.MouseLeftButtonUp += (_, _) => ZoomTo(1, new Point(ActualWidth / 2, ActualHeight / 2));
            overGrid.Children.Add(_zoomText);
            BuildMini(overGrid);
            _root.Children.Add(overGrid);
            _root.Children.Add(_over);
            Content = _root;

            _viewport.MouseWheel += OnWheel;
            _viewport.MouseDown += OnDown;
            _viewport.MouseMove += OnMove;
            _viewport.MouseUp += OnUp;
            _viewport.Drop += OnDrop;
            _viewport.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
                e.Handled = true;
            };
            PreviewKeyDown += OnKey;
            SizeChanged += (_, _) => Apply();
            _clock.Tick += (_, _) =>
            {
                _tick++;
                foreach (var t in _tickers.ToList()) t();
                if (_tick % 3 == 0) _ = _np.Refresh();
                if (_tick % 2 == 0) PlaceThumbs();
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveFile();
            };
            Load();
        }

        public bool Shown => _shown;

        public void AddFile(string path)
        {
            double z = _cur.Zoom <= 0 ? 1 : _cur.Zoom;
            double cx = ((_shown ? ActualWidth : 1200) / 2 - _cur.PanX) / z;
            double cy = ((_shown ? ActualHeight : 700) / 2 - _cur.PanY) / z;
            if (_shown)
            {
                cx = (ActualWidth / 2 - _pan.X) / _scale.ScaleX;
                cy = (ActualHeight / 2 - _pan.Y) / _scale.ScaleX;
            }
            var it = new SpaceItem { Kind = Directory.Exists(path) ? "folder" : "file", Path = path, W = 104, H = 112, X = cx - 52, Y = cy - 56 };
            SizeFor(it);
            if (_s.SpaceGrid)
            {
                it.X = SnapTo(it.X);
                it.Y = SnapTo(it.Y);
            }
            _cur.Items.Add(it);
            if (_shown)
            {
                var el = Add(it);
                Glass.FadeIn(el, 8, 200);
            }
            SaveFile();
        }

        Border Tool(string glyph, string tip, Action click)
        {
            var b = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(9), Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip, Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) };
            b.Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255));
            b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return b;
        }

        void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var f = JsonSerializer.Deserialize<SpacesFile>(File.ReadAllText(FilePath));
                    if (f != null) _file = f;
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("spaces load failed " + ex.Message);
            }
            _file.Spaces ??= new List<SpaceDoc>();
            if (_file.Spaces.Count == 0) _file.Spaces.Add(Starter());
            foreach (var sp in _file.Spaces)
            {
                sp.Items ??= new List<SpaceItem>();
                sp.Windows ??= new List<SpaceWin>();
                sp.Desktop ??= "";
                sp.Links ??= new List<SpaceLink>();
                if (sp.Zoom <= 0.05 || double.IsNaN(sp.Zoom)) sp.Zoom = 1;
            }
            _file.Current = Math.Clamp(_file.Current, 0, _file.Spaces.Count - 1);
            _cur = _file.Spaces[_file.Current];
        }

        static SpaceDoc Starter()
        {
            var d = new SpaceDoc { Name = "home" };
            d.Items.Add(new SpaceItem { Kind = "clock", X = 0, Y = 0, W = 240, H = 120 });
            d.Items.Add(new SpaceItem { Kind = "music", X = 260, Y = 0, W = 300, H = 120 });
            d.Items.Add(new SpaceItem { Kind = "note", X = 0, Y = 150, W = 240, H = 170, Text = "this is your space. drag files in, group stuff, zoom out to see it all" });
            d.Items.Add(new SpaceItem { Kind = "smart", X = 260, Y = 150, W = 300, H = 270, Path = "recent" });
            return d;
        }

        void Touch()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        void SaveFile()
        {
            try
            {
                _cur.PanX = _pan.X;
                _cur.PanY = _pan.Y;
                _cur.Zoom = _scale.ScaleX;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("spaces save failed " + ex.Message);
            }
        }

        public void Toggle()
        {
            if (_shown && IsActive) Close2();
            else Open();
        }

        public void Open()
        {
            if (_shown)
            {
                Activate();
                if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
                return;
            }
            _shown = true;
            var d = Displays.AtCursor();
            _display = d;
            PickDesktop();
            BuildTabs();
            Build();
            _clock.Start();
            Glass.Present(this, _root, () =>
            {
                Width = d.Work.Width / d.Scale;
                Height = d.Work.Height / d.Scale;
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(h, IntPtr.Zero, d.Work.Left, d.Work.Top, d.Work.Width, d.Work.Height, Native.SWP_NOACTIVATE);
            }, 0, 1, 200);
            var st = new ScaleTransform(1.03, 1.03);
            _viewport.RenderTransformOrigin = new Point(0.5, 0.5);
            _viewport.RenderTransform = st;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.03, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.03, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
            Focus();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            HideMenu();
            _clock.Stop();
            ClearThumbs();
            SaveFile();
            Glass.Dismiss(this, _root, null, 150);
        }

        void BuildTabs()
        {
            _tabs.Children.Clear();
            for (int i = 0; i < _file.Spaces.Count; i++)
            {
                var sp = _file.Spaces[i];
                bool on = sp == _cur;
                var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
                chip.Background = new SolidColorBrush(on ? Color.FromArgb(0xE6, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x26, 255, 255, 255));
                chip.Child = new TextBlock { Text = sp.Name, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.White };
                var target = sp;
                chip.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    if (e.ClickCount == 2)
                    {
                        RenameTab(chip, target);
                        return;
                    }
                    Switch(target);
                };
                chip.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    var p = e.GetPosition(_over);
                    var items = new List<(string, string, Action)>
                    {
                        ("\uE8AC", "Rename", () => RenameTab(chip, target))
                    };
                    if (_file.Spaces.Count > 1) items.Add(("\uE74D", "Delete this space", () => DeleteSpace(target)));
                    ShowMenu(p, items);
                };
                _tabs.Children.Add(chip);
            }
            var add = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 5, 10, 6), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)), ToolTip = "new space" };
            add.Child = new TextBlock { Text = "\uE710", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            add.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var sp = new SpaceDoc { Name = "space " + (_file.Spaces.Count + 1) };
                _file.Spaces.Add(sp);
                Switch(sp);
            };
            _tabs.Children.Add(add);
        }

        void RenameTab(Border chip, SpaceDoc sp)
        {
            var box = new TextBox { Text = sp.Name, MinWidth = 90, FontSize = 12.5, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White };
            chip.Child = box;
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            void Done()
            {
                string name = box.Text.Trim();
                if (name.Length > 0) sp.Name = name;
                Touch();
                BuildTabs();
            }
            box.LostKeyboardFocus += (_, _) => Done();
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    e.Handled = true;
                    Keyboard.ClearFocus();
                    Focus();
                }
            };
        }

        void DeleteSpace(SpaceDoc sp)
        {
            if (_file.Spaces.Count <= 1) return;
            int i = _file.Spaces.IndexOf(sp);
            _file.Spaces.Remove(sp);
            if (sp == _cur) Switch(_file.Spaces[Math.Clamp(i - 1, 0, _file.Spaces.Count - 1)]);
            else BuildTabs();
            Touch();
        }

        void Switch(SpaceDoc sp)
        {
            HideMenu();
            _cur.PanX = _pan.X;
            _cur.PanY = _pan.Y;
            _cur.Zoom = _scale.ScaleX;
            _cur = sp;
            _file.Current = _file.Spaces.IndexOf(sp);
            BuildTabs();
            Build();
            Sfx.Play("tick");
            Touch();
            _world.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        void Build()
        {
            ClearThumbs();
            _world.Children.Clear();
            _els.Clear();
            _tickers.Clear();
            _details.Clear();
            _scale.ScaleX = _scale.ScaleY = _cur.Zoom;
            _pan.X = _cur.PanX;
            _pan.Y = _cur.PanY;
            foreach (var it in _cur.Items.Where(i => i.Kind == "group").ToList()) Add(it);
            foreach (var it in _cur.Items.Where(i => i.Kind != "group").ToList()) Add(it);
            AttachLinks();
            Apply();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void ClearThumbs()
        {
            foreach (var t in _thumbs.Values) t.thumb.Dispose();
            _thumbs.Clear();
        }

        void Apply()
        {
            double z = _scale.ScaleX;
            double step = 28 * z;
            while (step < 14) step *= 2;
            _dotBrush.Viewport = new Rect(Mod(_pan.X, step), Mod(_pan.Y, step), step, step);
            _zoomText.Text = $"{Math.Round(z * 100)}%";
            foreach (var (el, minZoom) in _details) el.Visibility = z >= minZoom ? Visibility.Visible : Visibility.Collapsed;
            PlaceThumbs();
            UpdateMini();
        }

        static double Mod(double a, double m) => ((a % m) + m) % m;

        Point ToWorld(Point screen) => new((screen.X - _pan.X) / _scale.ScaleX, (screen.Y - _pan.Y) / _scale.ScaleX);

        void ZoomTo(double z, Point at)
        {
            z = Math.Clamp(z, 0.15, 3);
            var w = ToWorld(at);
            _scale.ScaleX = _scale.ScaleY = z;
            _pan.X = at.X - w.X * z;
            _pan.Y = at.Y - w.Y * z;
            Apply();
            Touch();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            HideMenu();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _pan.X += e.Delta * 0.6;
                Apply();
                Touch();
                return;
            }
            double k = e.Delta > 0 ? 1.12 : 1 / 1.12;
            ZoomTo(_scale.ScaleX * k, e.GetPosition(_viewport));
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_menu != null && !_menu.IsMouseOver) HideMenu();
            if (_linkFrom != null)
            {
                EndLink();
                e.Handled = true;
                return;
            }
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                var w = ToWorld(e.GetPosition(_viewport));
                var note = new SpaceItem { Kind = "note", X = w.X - 110, Y = w.Y - 20, W = 220, H = 160 };
                _cur.Items.Add(note);
                var el = Add(note);
                Glass.FadeIn(el, 6, 180);
                Touch();
                e.Handled = true;
                return;
            }
            if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
            {
                _panning = true;
                _dragStart = e.GetPosition(_viewport);
                _panX0 = _pan.X;
                _panY0 = _pan.Y;
                _viewport.CaptureMouse();
                _viewport.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (_linkFrom != null) GhostTo(ToWorld(e.GetPosition(_viewport)));
            if (!_panning) return;
            var p = e.GetPosition(_viewport);
            _pan.X = _panX0 + (p.X - _dragStart.X);
            _pan.Y = _panY0 + (p.Y - _dragStart.Y);
            Apply();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                _viewport.ReleaseMouseCapture();
                _viewport.Cursor = null;
                Touch();
                e.Handled = true;
                return;
            }
            if (e.ChangedButton == MouseButton.Right)
            {
                e.Handled = true;
                var p = e.GetPosition(_over);
                var w = ToWorld(e.GetPosition(_viewport));
                ShowMenu(p, new List<(string, string, Action)>
                {
                    ("\uE70B", "Note", () => AddAt(new SpaceItem { Kind = "note", W = 220, H = 160 }, w)),
                    ("\uE8F1", "Group", () => AddAt(new SpaceItem { Kind = "group", W = 440, H = 300, Text = "group" }, w)),
                    ("\uE823", "Clock", () => AddAt(new SpaceItem { Kind = "clock", W = 240, H = 120 }, w)),
                    ("\uE8D6", "Now playing", () => AddAt(new SpaceItem { Kind = "music", W = 300, H = 120 }, w)),
                    ("\uE706", "Weather", () => AddAt(new SpaceItem { Kind = "weather", W = 220, H = 120 }, w)),
                    ("\uE8A9", "Portal to a window...", () => PickWindow(p, w)),
                    ("\uE81C", "Smart folder...", () => PickSmart(p, w)),
                    ("\uE8CB", "Tidy up", Tidy),
                    ("\uE9A6", "Fit everything", Fit)
                });
            }
        }

        void AddAt(SpaceItem it, Point world)
        {
            it.X = world.X - it.W / 2;
            it.Y = world.Y - 16;
            if (it.Kind == "group")
            {
                _cur.Items.Insert(0, it);
                Build();
            }
            else
            {
                _cur.Items.Add(it);
                var el = Add(it);
                Glass.FadeIn(el, 6, 180);
            }
            Touch();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void PickSmart(Point at, Point world)
        {
            var items = new List<(string, string, Action)>();
            foreach (var (id, label, glyph) in Library.Smart)
            {
                string tab = id;
                items.Add((glyph, label, () => AddAt(new SpaceItem { Kind = "smart", W = 300, H = 270, Path = tab }, world)));
            }
            foreach (var name in _s.Collections.Keys.Take(8))
            {
                string col = name;
                items.Add(("\uE8B7", col, () => AddAt(new SpaceItem { Kind = "smart", W = 300, H = 270, Path = col }, world)));
            }
            Dispatcher.BeginInvoke(new Action(() => ShowMenu(at, items)), DispatcherPriority.Background);
        }

        void PickWindow(Point at, Point world)
        {
            var items = new List<(string, string, Action)>();
            foreach (var (h, title, exe) in AppWindows().Take(14))
            {
                var hw = h;
                string t = title, x = exe;
                items.Add(("\uE8A7", t.Length > 46 ? t[..46] + "..." : t, () => AddAt(new SpaceItem { Kind = "portal", W = 380, H = 250, Text = t, Exe = x }, world)));
            }
            if (items.Count == 0) items.Add(("\uE946", "no windows open", () => { }));
            Dispatcher.BeginInvoke(new Action(() => ShowMenu(at, items)), DispatcherPriority.Background);
        }

        List<(IntPtr h, string title, string exe)> AppWindows()
        {
            var list = new List<(IntPtr, string, string)>();
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsCloaked(h)) return true;
                if ((Native.ExStyle(h) & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if ((Native.Style(h) & Native.WS_CHILD) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == me) return true;
                string cls = Native.ClassName(h);
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return true;
                list.Add((h, Native.Title(h), Native.ProcessPath(pid) ?? ""));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        void ShowMenu(Point at, List<(string glyph, string label, Action act)> items)
        {
            HideMenu();
            var stack = new StackPanel { Margin = new Thickness(5) };
            foreach (var (glyph, label, act) in items)
            {
                var a = act;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), Width = 22, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                var b = new Border { Child = row, Padding = new Thickness(10, 6, 14, 7), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
                b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
                b.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    HideMenu();
                    a();
                };
                stack.Children.Add(b);
            }
            _menu = new Border
            {
                Child = stack,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 32, 33, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                MinWidth = 190,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            };
            _menu.MouseDown += (_, e) => e.Handled = true;
            _over.Children.Add(_menu);
            _menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = Math.Min(at.X, Math.Max(0, ActualWidth - _menu.DesiredSize.Width - 8));
            double y = Math.Min(at.Y, Math.Max(0, ActualHeight - _menu.DesiredSize.Height - 8));
            Canvas.SetLeft(_menu, x);
            Canvas.SetTop(_menu, y);
            Glass.FadeIn(_menu, -4, 140);
            HideThumbsUnder(new Rect(x, y, _menu.DesiredSize.Width, _menu.DesiredSize.Height));
        }

        void HideMenu()
        {
            if (_menu == null) return;
            _over.Children.Remove(_menu);
            _menu = null;
            PlaceThumbs();
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var paths = Shelf.DroppedPaths(e.Data);
            if (paths.Count == 0) return;
            var w = ToWorld(e.GetPosition(_viewport));
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(paths.Count)));
            int i = 0;
            foreach (var p in paths.Take(60))
            {
                var it = new SpaceItem { Kind = Directory.Exists(p) ? "folder" : Files.IsApp(p) ? "app" : "file", Path = p, W = 104, H = 112 };
                it.X = w.X - 52 + (i % cols) * 118;
                it.Y = w.Y - 40 + (i / cols) * 126;
                if (IsPicture(p) && it.Kind == "file")
                {
                    SizeFor(it);
                    it.X = w.X - 120 + (i % cols) * 254;
                    it.Y = w.Y - 85 + (i / cols) * 184;
                }
                if (_s.SpaceGrid)
                {
                    it.X = SnapTo(it.X);
                    it.Y = SnapTo(it.Y);
                }
                _cur.Items.Add(it);
                var el = Add(it);
                Glass.FadeIn(el, 8, 200);
                i++;
            }
            Sfx.Play("drop");
            Touch();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            switch (e.Key)
            {
                case Key.Escape:
                    if (_linkFrom != null) EndLink();
                    else if (_menu != null) HideMenu();
                    else Close2();
                    break;
                case Key.G:
                    ToggleGrid();
                    break;
                case Key.Space:
                    if (_hover != null && _hover.Path.Length > 0 && (File.Exists(_hover.Path) || Directory.Exists(_hover.Path))) Inspect?.Invoke(Obj.From(_hover.Path), null);
                    break;
                case Key.Delete:
                    if (_hover != null) Remove(_hover);
                    break;
                case Key.T:
                    Tidy();
                    break;
                case Key.F:
                    Fit();
                    break;
                case Key.N:
                    {
                        var w = ToWorld(Mouse.GetPosition(_viewport));
                        AddAt(new SpaceItem { Kind = "note", W = 220, H = 160 }, w);
                    }
                    break;
                case Key.D0:
                    ZoomTo(1, new Point(ActualWidth / 2, ActualHeight / 2));
                    break;
                case Key.Tab:
                    {
                        int i = _file.Spaces.IndexOf(_cur);
                        int dir = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                        Switch(_file.Spaces[((i + dir) % _file.Spaces.Count + _file.Spaces.Count) % _file.Spaces.Count]);
                    }
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        void Remove(SpaceItem it)
        {
            _cur.Items.Remove(it);
            if (_cur.Links.RemoveAll(l => l.From == it.Id || l.To == it.Id) > 0) DrawLinks();
            UpdateMini();
            if (_thumbs.TryGetValue(it, out var t))
            {
                t.thumb.Dispose();
                _thumbs.Remove(it);
            }
            if (_els.TryGetValue(it, out var el))
            {
                _els.Remove(it);
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                fade.Completed += (_, _) => _world.Children.Remove(el);
                el.BeginAnimation(OpacityProperty, fade);
            }
            if (_hover == it) _hover = null;
            Touch();
        }

        FrameworkElement Add(SpaceItem it)
        {
            var el = Make(it);
            el.Width = it.W;
            el.Height = it.H;
            Canvas.SetLeft(el, it.X);
            Canvas.SetTop(el, it.Y);
            Panel.SetZIndex(el, it.Kind == "group" ? 0 : ++_z);
            Wire(it, el);
            _world.Children.Add(el);
            _els[it] = el;
            return el;
        }

        static Border Card(double radius = 14, byte alpha = 0xE6)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = new SolidColorBrush(Color.FromArgb(alpha, 34, 35, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
        }

        static TextBlock T(string text, double size, bool bold = false, byte alpha = 0xFF)
        {
            return new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
        }

        static TextBlock G(string glyph, double size, byte alpha = 0xFF)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)) };
        }

        FrameworkElement Make(SpaceItem it)
        {
            switch (it.Kind)
            {
                case "group":
                    {
                        var b = new Border { CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)), BorderThickness = new Thickness(1.5) };
                        var grid = new Grid();
                        var title = T(it.Text.Length > 0 ? it.Text : "group", 15, true, 0xE0);
                        title.Margin = new Thickness(16, 10, 16, 0);
                        title.VerticalAlignment = VerticalAlignment.Top;
                        grid.Children.Add(title);
                        grid.Children.Add(Grip(it));
                        b.Child = grid;
                        return b;
                    }
                case "note":
                    {
                        var b = Card(12, 0xF0);
                        b.Background = new SolidColorBrush(Color.FromArgb(0xF0, 58, 52, 30));
                        var grid = new Grid();
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        var bar = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 214, 10)), CornerRadius = new CornerRadius(11, 11, 0, 0), Cursor = Cursors.SizeAll };
                        grid.Children.Add(bar);
                        var box = new TextBox
                        {
                            Text = it.Text,
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 255, 248, 220)),
                            CaretBrush = Brushes.White,
                            FontSize = 13,
                            Padding = new Thickness(10, 4, 10, 10),
                            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
                        };
                        box.TextChanged += (_, _) =>
                        {
                            it.Text = box.Text;
                            Touch();
                        };
                        Grid.SetRow(box, 1);
                        grid.Children.Add(box);
                        var grip = Grip(it);
                        Grid.SetRow(grip, 1);
                        grid.Children.Add(grip);
                        b.Child = grid;
                        return b;
                    }
                case "clock":
                    {
                        var b = Card();
                        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 18, 0) };
                        var time = T("", 38, true);
                        var date = T("", 13, false, 0xA0);
                        sp.Children.Add(time);
                        sp.Children.Add(date);
                        b.Child = sp;
                        void Upd()
                        {
                            var n = DateTime.Now;
                            time.Text = _s.Bar24h ? n.ToString("HH:mm") : n.ToString("h:mm");
                            date.Text = n.ToString("dddd, MMMM d").ToLowerInvariant();
                        }
                        Upd();
                        _tickers.Add(Upd);
                        return b;
                    }
                case "music":
                    {
                        var b = Card();
                        var grid = new Grid { Margin = new Thickness(12) };
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        var art = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true };
                        var img = new Image { Stretch = Stretch.UniformToFill };
                        art.Child = img;
                        grid.Children.Add(art);
                        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                        var title = T("nothing playing", 14, true);
                        var artist = T("", 12, false, 0xA0);
                        info.Children.Add(title);
                        info.Children.Add(artist);
                        var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                        var playGlyph = G("\uE768", 14);
                        ctl.Children.Add(Btn(G("\uE892", 13), () => _ = _np.Previous()));
                        ctl.Children.Add(Btn(playGlyph, () => _ = _np.Toggle()));
                        ctl.Children.Add(Btn(G("\uE893", 13), () => _ = _np.Next()));
                        info.Children.Add(ctl);
                        Grid.SetColumn(info, 1);
                        grid.Children.Add(info);
                        b.Child = grid;
                        void Upd()
                        {
                            bool any = _np.HasSession && _np.Title.Length > 0;
                            title.Text = any ? _np.Title : "nothing playing";
                            artist.Text = any ? _np.Artist : "";
                            img.Source = any ? _np.Art : null;
                            playGlyph.Text = _np.Playing ? "\uE769" : "\uE768";
                        }
                        Upd();
                        _tickers.Add(Upd);
                        return b;
                    }
                case "weather":
                    {
                        var b = Card();
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 18, 0) };
                        var icon = G(_data.WeatherGlyph, 34);
                        icon.Margin = new Thickness(0, 0, 14, 0);
                        icon.VerticalAlignment = VerticalAlignment.Center;
                        sp.Children.Add(icon);
                        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                        var temp = T("", 24, true);
                        var place = T("", 12, false, 0xA0);
                        col.Children.Add(temp);
                        col.Children.Add(place);
                        sp.Children.Add(col);
                        b.Child = sp;
                        void Upd()
                        {
                            icon.Text = _data.WeatherGlyph;
                            temp.Text = _data.Weather.Length > 0 ? _data.Weather : "--";
                            place.Text = _data.WeatherPlace.Length > 0 ? _data.WeatherPlace : "weather";
                        }
                        Upd();
                        _tickers.Add(Upd);
                        return b;
                    }
                case "portal":
                    {
                        var b = Card(14, 0xF2);
                        var grid = new Grid();
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                        var small = new Image { Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0) };
                        if (it.Exe.Length > 0) _cat.Icons.Get(it.Exe, 32, true, src => small.Source = src);
                        head.Children.Add(small);
                        head.Children.Add(T(it.Text, 11.5, false, 0xC0));
                        grid.Children.Add(head);
                        var host = new Border { Margin = new Thickness(6, 0, 6, 6), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) };
                        var missing = T("window's closed. double-click to look again", 11.5, false, 0x90);
                        missing.HorizontalAlignment = HorizontalAlignment.Center;
                        missing.VerticalAlignment = VerticalAlignment.Center;
                        host.Child = missing;
                        Grid.SetRow(host, 1);
                        grid.Children.Add(host);
                        var grip = Grip(it);
                        Grid.SetRow(grip, 1);
                        grid.Children.Add(grip);
                        b.Child = grid;
                        b.Loaded += (_, _) => AttachPortal(it, host, missing);
                        return b;
                    }
                case "smart":
                    {
                        var b = Card();
                        var dock = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
                        string label = Library.Smart.FirstOrDefault(x => x.id == it.Path).label ?? it.Path;
                        string glyph = Library.Smart.FirstOrDefault(x => x.id == it.Path).glyph ?? "\uE8B7";
                        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
                        var hg = G(glyph, 12, 0xC0);
                        hg.Margin = new Thickness(0, 0, 8, 0);
                        hg.VerticalAlignment = VerticalAlignment.Center;
                        head.Children.Add(hg);
                        head.Children.Add(T(label, 13.5, true));
                        DockPanel.SetDock(head, Dock.Top);
                        dock.Children.Add(head);
                        var list = new StackPanel();
                        dock.Children.Add(list);
                        var grid = new Grid();
                        grid.Children.Add(dock);
                        grid.Children.Add(Grip(it));
                        b.Child = grid;
                        void Fill()
                        {
                            list.Children.Clear();
                            int max = Math.Max(1, (int)((it.H - 50) / 30));
                            var entries = _lib.Get(it.Path);
                            if (it.Path != "dupes") entries = _lib.Sort(entries, it.Path == "recent" ? "accessed" : "modified", false);
                            foreach (var en in entries.Take(max))
                            {
                                var ep = en.Path;
                                var row = new StackPanel { Orientation = Orientation.Horizontal };
                                var ic = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 9, 0) };
                                _cat.Icons.Get(ep, 32, false, src => ic.Source = src);
                                row.Children.Add(ic);
                                var name = T(en.Name, 12.5);
                                name.MaxWidth = Math.Max(60, it.W - 70);
                                row.Children.Add(name);
                                var hostRow = new Border { Child = row, Padding = new Thickness(6, 5, 6, 5), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = ep };
                                hostRow.MouseEnter += (_, _) => hostRow.Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255));
                                hostRow.MouseLeave += (_, _) => hostRow.Background = Brushes.Transparent;
                                hostRow.MouseLeftButtonDown += (_, e) =>
                                {
                                    e.Handled = true;
                                    Files.Open(ep, _s);
                                };
                                list.Children.Add(hostRow);
                            }
                            if (list.Children.Count == 0) list.Children.Add(T(_lib.Ready ? "nothing here" : "looking...", 12, false, 0x90));
                        }
                        Fill();
                        int last = 0;
                        _tickers.Add(() =>
                        {
                            last++;
                            if (last % 20 == 0) Fill();
                        });
                        return b;
                    }
                default:
                    {
                        if (it.Kind == "file" && IsPicture(it.Path)) return Picture(it);
                        var b = new Border { CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, ToolTip = it.Path };
                        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 8, 6, 6) };
                        var img = new Image { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Center };
                        _cat.Icons.Get(it.Path, 96, it.Kind == "app", src => img.Source = src);
                        sp.Children.Add(img);
                        string name = System.IO.Path.GetFileName(it.Path.TrimEnd('\\'));
                        if (name.Length == 0) name = it.Path;
                        if (it.Kind == "app" && name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                        var label = new TextBlock { Text = name, FontSize = 11.5, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxHeight = 32, Margin = new Thickness(0, 6, 0, 0) };
                        label.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black };
                        sp.Children.Add(label);
                        _details.Add((label, 0.5));
                        b.Child = sp;
                        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
                        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
                        if (!File.Exists(it.Path) && !Directory.Exists(it.Path)) b.Opacity = 0.45;
                        return b;
                    }
            }
        }

        static Border Btn(TextBlock glyph, Action click)
        {
            var b = new Border { Child = glyph, Padding = new Thickness(7, 4, 7, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return b;
        }

        Border Grip(SpaceItem it)
        {
            var grip = new Border { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 0, 2, 2) };
            grip.Child = new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 8, Foreground = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), RenderTransform = new RotateTransform(45, 4, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grip.MouseLeftButtonDown += (_, e) =>
            {
                if (!_els.TryGetValue(it, out var el)) return;
                e.Handled = true;
                _dragItem = it;
                _resizing = true;
                _moved = false;
                _dragStart = e.GetPosition(_viewport);
                _w0 = it.W;
                _h0 = it.H;
                el.CaptureMouse();
            };
            return grip;
        }

        static bool InTextBox(object? src)
        {
            var d = src as DependencyObject;
            while (d != null)
            {
                if (d is TextBoxBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void Wire(SpaceItem it, FrameworkElement el)
        {
            el.MouseEnter += (_, _) => _hover = it;
            el.MouseLeave += (_, _) =>
            {
                if (_hover == it) _hover = null;
            };
            el.MouseLeftButtonDown += (_, e) =>
            {
                if (_menu != null) HideMenu();
                if (InTextBox(e.OriginalSource)) return;
                e.Handled = true;
                if (Linking(it)) return;
                if (e.ClickCount == 2)
                {
                    Open(it);
                    return;
                }
                bool isFile = it.Kind is "file" or "folder" or "app";
                if (isFile && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (File.Exists(it.Path) || Directory.Exists(it.Path)))
                {
                    try
                    {
                        var data = new DataObject(DataFormats.FileDrop, new[] { it.Path });
                        DragDrop.DoDragDrop(el, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                    }
                    catch
                    {
                    }
                    return;
                }
                _dragItem = it;
                _resizing = false;
                _moved = false;
                _dragStart = e.GetPosition(_viewport);
                _x0 = it.X;
                _y0 = it.Y;
                _carry = it.Kind == "group" ? Members(it).Select(m => (item: m, x: m.X, y: m.Y)).ToList() : new List<(SpaceItem item, double x, double y)>();
                if (it.Kind != "group") Panel.SetZIndex(el, ++_z);
                el.CaptureMouse();
            };
            el.MouseMove += (_, e) =>
            {
                if (_dragItem != it || !el.IsMouseCaptured) return;
                var p = e.GetPosition(_viewport);
                double z = _scale.ScaleX;
                double dx = (p.X - _dragStart.X) / z, dy = (p.Y - _dragStart.Y) / z;
                if (!_moved && Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) < 4) return;
                _moved = true;
                if (_resizing)
                {
                    it.W = Math.Max(it.Kind == "group" ? 200 : 150, GridOn ? SnapTo(_w0 + dx) : _w0 + dx);
                    it.H = Math.Max(it.Kind == "group" ? 140 : 90, GridOn ? SnapTo(_h0 + dy) : _h0 + dy);
                    el.Width = it.W;
                    el.Height = it.H;
                }
                else
                {
                    if (GridOn)
                    {
                        dx = SnapTo(_x0 + dx) - _x0;
                        dy = SnapTo(_y0 + dy) - _y0;
                    }
                    it.X = _x0 + dx;
                    it.Y = _y0 + dy;
                    Canvas.SetLeft(el, it.X);
                    Canvas.SetTop(el, it.Y);
                    foreach (var (m, mx, my) in _carry)
                    {
                        m.X = mx + dx;
                        m.Y = my + dy;
                        if (_els.TryGetValue(m, out var mel))
                        {
                            Canvas.SetLeft(mel, m.X);
                            Canvas.SetTop(mel, m.Y);
                        }
                    }
                }
                PlaceThumbs();
                if (_cur.Links.Count > 0) DrawLinks();
            };
            el.MouseLeftButtonUp += (_, e) =>
            {
                if (_dragItem != it) return;
                e.Handled = true;
                el.ReleaseMouseCapture();
                _dragItem = null;
                bool resized = _resizing;
                _resizing = false;
                _carry.Clear();
                if (_moved)
                {
                    UpdateMini();
                    Touch();
                }
                if (resized && it.Kind == "smart") Build();
            };
            el.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var p = e.GetPosition(_over);
                var items = new List<(string, string, Action)>();
                bool exists = it.Path.Length > 0 && (File.Exists(it.Path) || Directory.Exists(it.Path));
                if (exists)
                {
                    items.Add(("\uE8A7", "Open", () => Open(it)));
                    items.Add(("\uE946", "Inspect", () => Inspect?.Invoke(Obj.From(it.Path), null)));
                    items.Add(("\uE838", "Show in folder", () => Files.Reveal(it.Path)));
                }
                if (it.Kind == "group")
                {
                    items.Add(("\uE8AC", "Rename", () => RenameGroup(it)));
                    items.Add(("\uE8CB", "Tidy inside", () => TidyGroup(it, true)));
                }
                if (it.Kind == "portal") items.Add(("\uE72C", "Look for the window again", () => Build()));
                items.Add(("\uE72A", "Draw an arrow to...", () => StartLink(it)));
                if (_cur.Links.Any(l => l.From == it.Id || l.To == it.Id)) items.Add(("\uE894", "Remove its arrows", () => Unlink(it)));
                items.Add(("\uE711", "Remove from this space", () => Remove(it)));
                ShowMenu(p, items);
            };
        }

        void RenameGroup(SpaceItem it)
        {
            if (!_els.TryGetValue(it, out var el) || el is not Border b || b.Child is not Grid grid) return;
            var old = grid.Children.OfType<TextBlock>().FirstOrDefault();
            var box = new TextBox { Text = it.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(14, 8, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            if (old != null) grid.Children.Remove(old);
            grid.Children.Add(box);
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                it.Text = box.Text.Trim();
                Touch();
                Build();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Escape)) return;
                e.Handled = true;
                Keyboard.ClearFocus();
                Focus();
            };
        }

        void Open(SpaceItem it)
        {
            switch (it.Kind)
            {
                case "portal":
                    if (_thumbs.ContainsKey(it))
                    {
                        var h = FindWindow(it);
                        if (h != IntPtr.Zero) Native.ForceForeground(h);
                    }
                    else
                    {
                        Build();
                    }
                    break;
                case "group":
                    RenameGroup(it);
                    break;
                case "note":
                    break;
                default:
                    if (File.Exists(it.Path) || Directory.Exists(it.Path)) Files.Open(it.Path, _s);
                    break;
            }
        }

        IEnumerable<SpaceItem> Members(SpaceItem group)
        {
            var r = new Rect(group.X, group.Y, group.W, group.H);
            return _cur.Items.Where(i => i != group && i.Kind != "group" && r.Contains(new Point(i.X + i.W / 2, i.Y + i.H / 2))).ToList();
        }

        IntPtr FindWindow(SpaceItem it)
        {
            var wins = AppWindows();
            foreach (var (h, title, exe) in wins)
                if (title == it.Text && exe.Equals(it.Exe, StringComparison.OrdinalIgnoreCase)) return h;
            foreach (var (h, title, exe) in wins)
                if (it.Exe.Length > 0 && exe.Equals(it.Exe, StringComparison.OrdinalIgnoreCase)) return h;
            foreach (var (h, title, exe) in wins)
                if (title == it.Text) return h;
            return IntPtr.Zero;
        }

        void AttachPortal(SpaceItem it, Border host, TextBlock missing)
        {
            if (_thumbs.TryGetValue(it, out var old))
            {
                old.thumb.Dispose();
                _thumbs.Remove(it);
            }
            var target = FindWindow(it);
            if (target == IntPtr.Zero) return;
            var me = new WindowInteropHelper(this).Handle;
            if (me == IntPtr.Zero) return;
            var t = new Thumb(me, target);
            if (!t.Valid)
            {
                t.Dispose();
                return;
            }
            missing.Visibility = Visibility.Collapsed;
            _thumbs[it] = (host, t);
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void PlaceThumbs()
        {
            if (!_shown || _thumbs.Count == 0) return;
            var me = new WindowInteropHelper(this).Handle;
            if (me == IntPtr.Zero) return;
            Native.GetWindowRect(me, out var wr);
            double scale = _display?.Scale ?? 1;
            int topBand = (int)(60 * scale);
            foreach (var (host, thumb) in _thumbs.Values)
            {
                try
                {
                    if (!host.IsLoaded || _scale.ScaleX < 0.2)
                    {
                        thumb.Hide();
                        continue;
                    }
                    var tl = host.PointToScreen(new Point(0, 0));
                    var br = host.PointToScreen(new Point(host.ActualWidth, host.ActualHeight));
                    var r = new Native.RECT { Left = (int)tl.X - wr.Left, Top = (int)tl.Y - wr.Top, Right = (int)br.X - wr.Left, Bottom = (int)br.Y - wr.Top };
                    bool off = r.Right <= 0 || r.Bottom <= 0 || r.Left >= wr.Width || r.Top >= wr.Height || r.Top < topBand || r.Width < 24 || r.Height < 16;
                    if (off) thumb.Hide();
                    else thumb.Place(r, 255);
                }
                catch
                {
                    thumb.Hide();
                }
            }
        }

        void HideThumbsUnder(Rect area)
        {
            foreach (var (host, thumb) in _thumbs.Values)
            {
                try
                {
                    var tl = host.TranslatePoint(new Point(0, 0), _over);
                    var r = new Rect(tl, new Size(host.ActualWidth * _scale.ScaleX, host.ActualHeight * _scale.ScaleX));
                    if (r.IntersectsWith(area)) thumb.Hide();
                }
                catch
                {
                }
            }
        }

        void Tidy()
        {
            HideMenu();
            double z = _scale.ScaleX;
            double left = (40 - _pan.X) / z, top = (90 - _pan.Y) / z;
            double width = Math.Max(400, (ActualWidth - 80) / z);
            var groups = _cur.Items.Where(i => i.Kind == "group").ToList();
            var inGroups = new HashSet<SpaceItem>(groups.SelectMany(Members));
            var loose = _cur.Items.Where(i => i.Kind != "group" && !inGroups.Contains(i))
                .OrderBy(i => Rank(i))
                .ThenBy(i => Files.Category(i.Path))
                .ThenBy(i => System.IO.Path.GetFileName(i.Path), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            double x = left, y = top, rowH = 0;
            string lastRank = "";
            foreach (var it in loose)
            {
                string rk = Rank(it) < 2 ? "w" : "f";
                if (lastRank.Length > 0 && rk != lastRank && x > left)
                {
                    x = left;
                    y += rowH + 30;
                    rowH = 0;
                }
                lastRank = rk;
                if (x + it.W > left + width && x > left)
                {
                    x = left;
                    y += rowH + 24;
                    rowH = 0;
                }
                MoveTo(it, x, y);
                x += it.W + 20;
                rowH = Math.Max(rowH, it.H);
            }
            double gy = y + rowH + 40, gx = left;
            foreach (var g in groups)
            {
                var members = Members(g).ToList();
                if (gx + g.W > left + width && gx > left)
                {
                    gx = left;
                    gy += 40;
                }
                double dx = gx - g.X, dy = gy - g.Y;
                MoveTo(g, gx, gy);
                foreach (var m in members) MoveTo(m, m.X + dx, m.Y + dy);
                TidyGroup(g, false);
                gx += g.W + 30;
            }
            Sfx.Play("tick");
            Touch();
        }

        static int Rank(SpaceItem i) => i.Kind switch
        {
            "clock" or "music" or "weather" => 0,
            "smart" or "portal" or "note" => 1,
            "folder" => 2,
            "app" => 3,
            _ => 4
        };

        void TidyGroup(SpaceItem g, bool sound)
        {
            var members = Members(g).OrderBy(Rank).ThenBy(i => System.IO.Path.GetFileName(i.Path), StringComparer.CurrentCultureIgnoreCase).ToList();
            double x = g.X + 16, y = g.Y + 44, rowH = 0;
            double right = g.X + g.W - 16;
            foreach (var m in members)
            {
                if (x + m.W > right && x > g.X + 16)
                {
                    x = g.X + 16;
                    y += rowH + 12;
                    rowH = 0;
                }
                MoveTo(m, x, y);
                x += m.W + 12;
                rowH = Math.Max(rowH, m.H);
            }
            double need = y + rowH + 16 - g.Y;
            if (members.Count > 0 && need > g.H)
            {
                g.H = need;
                if (_els.TryGetValue(g, out var gel)) gel.Height = g.H;
            }
            if (sound)
            {
                Sfx.Play("tick");
                Touch();
            }
        }

        void MoveTo(SpaceItem it, double x, double y)
        {
            it.X = x;
            it.Y = y;
            if (!_els.TryGetValue(it, out var el)) return;
            double fromX = Canvas.GetLeft(el), fromY = Canvas.GetTop(el);
            if (double.IsNaN(fromX)) fromX = x;
            if (double.IsNaN(fromY)) fromY = y;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var ax = new DoubleAnimation(fromX, x, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var ay = new DoubleAnimation(fromY, y, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            ax.Completed += (_, _) => PlaceThumbs();
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.BeginAnimation(Canvas.LeftProperty, ax);
            el.BeginAnimation(Canvas.TopProperty, ay);
        }

        void Fit()
        {
            HideMenu();
            if (_cur.Items.Count == 0)
            {
                ZoomTo(1, new Point(ActualWidth / 2, ActualHeight / 2));
                return;
            }
            double minX = _cur.Items.Min(i => i.X), minY = _cur.Items.Min(i => i.Y);
            double maxX = _cur.Items.Max(i => i.X + i.W), maxY = _cur.Items.Max(i => i.Y + i.H);
            double w = Math.Max(1, maxX - minX), h = Math.Max(1, maxY - minY);
            double availW = Math.Max(200, ActualWidth - 120), availH = Math.Max(200, ActualHeight - 180);
            double z = Math.Clamp(Math.Min(availW / w, availH / h), 0.15, 1.4);
            _scale.ScaleX = _scale.ScaleY = z;
            _pan.X = (ActualWidth - w * z) / 2 - minX * z;
            _pan.Y = 80 + (availH - h * z) / 2 - minY * z;
            Apply();
            Touch();
        }

        void SaveWindows()
        {
            var list = new List<SpaceWin>();
            foreach (var (h, title, exe) in AppWindows())
            {
                if (exe.Length == 0 || Native.IsIconic(h)) continue;
                Native.GetWindowRect(h, out var r);
                if (r.Left <= -10000) continue;
                list.Add(new SpaceWin { Exe = exe, Title = title, X = r.Left, Y = r.Top, W = r.Width, H = r.Height, Max = Native.IsZoomed(h) });
            }
            _cur.Windows = list;
            Touch();
            Sfx.Play("tick");
            Say?.Invoke($"remembered {list.Count} window{(list.Count == 1 ? "" : "s")} for {_cur.Name}");
        }

        void RestoreWindows()
        {
            if (_cur.Windows.Count == 0)
            {
                Say?.Invoke("nothing saved for this space yet. hit the save button first");
                return;
            }
            var open = AppWindows();
            var used = new HashSet<IntPtr>();
            int moved = 0, launched = 0;
            foreach (var sw in _cur.Windows)
            {
                var match = open.FirstOrDefault(w => !used.Contains(w.h) && w.exe.Equals(sw.Exe, StringComparison.OrdinalIgnoreCase) && w.title == sw.Title);
                if (match.h == IntPtr.Zero) match = open.FirstOrDefault(w => !used.Contains(w.h) && w.exe.Equals(sw.Exe, StringComparison.OrdinalIgnoreCase));
                if (match.h != IntPtr.Zero)
                {
                    used.Add(match.h);
                    var h = match.h;
                    if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                    Native.SetWindowPos(h, IntPtr.Zero, sw.X, sw.Y, sw.W, sw.H, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    if (sw.Max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                    moved++;
                }
                else if (File.Exists(sw.Exe) && !System.IO.Path.GetFileName(sw.Exe).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Launch.Run(sw.Exe);
                    launched++;
                }
            }
            Sfx.Play("tick");
            Say?.Invoke(launched > 0 ? $"put back {moved}, opened {launched}" : $"put back {moved} window{(moved == 1 ? "" : "s")}");
        }
    }
}
