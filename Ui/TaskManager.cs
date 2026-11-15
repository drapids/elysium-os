using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class TaskManager : Window
    {
        sealed class PRow
        {
            public string Key = "";
            public Border Root = null!;
            public TextBlock Name = null!;
            public TextBlock Tag = null!;
            public TextBlock Chevron = null!;
            public Image Icon = null!;
            public FrameworkElement Fallback = null!;
            public Border CpuCell = null!, MemCell = null!, DiskCell = null!;
            public TextBlock CpuText = null!, MemText = null!, DiskText = null!;
            public ProcGroup? Group;
            public ProcRow? Proc;
            public string? IconPath;
        }

        sealed class Graph
        {
            public readonly Canvas Host = new() { Height = 74, ClipToBounds = true };
            readonly System.Windows.Shapes.Path _area = new() { StrokeThickness = 0 };
            readonly System.Windows.Shapes.Path _line = new() { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            readonly System.Windows.Shapes.Path _line2 = new() { StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.55 };
            readonly List<double> _a = new(), _b = new();
            readonly double _fixedMax;
            const int Points = 60;

            public Graph(double fixedMax)
            {
                _fixedMax = fixedMax;
                _area.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentSoft");
                _line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFill");
                _line2.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Text");
                Host.Children.Add(_area);
                Host.Children.Add(_line2);
                Host.Children.Add(_line);
                Host.SizeChanged += (_, _) => Draw();
            }

            public void Push(double a, double b = double.NaN)
            {
                _a.Add(a);
                if (!double.IsNaN(b)) _b.Add(b);
                while (_a.Count > Points) _a.RemoveAt(0);
                while (_b.Count > Points) _b.RemoveAt(0);
                Draw();
            }

            void Draw()
            {
                double w = Host.ActualWidth, h = Host.ActualHeight;
                if (w <= 0 || h <= 0 || _a.Count == 0) return;
                double max = _fixedMax > 0 ? _fixedMax : Math.Max(1, Math.Max(_a.DefaultIfEmpty(0).Max(), _b.DefaultIfEmpty(0).Max()) * 1.15);
                _line.Data = Curve(_a, w, h, max, false);
                _area.Data = Curve(_a, w, h, max, true);
                _line2.Data = _b.Count > 1 ? Curve(_b, w, h, max, false) : null;
            }

            static Geometry Curve(List<double> v, double w, double h, double max, bool fill)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    double step = w / (Points - 1);
                    double x0 = w - (v.Count - 1) * step;
                    Point P(int i) => new(x0 + i * step, h - 1 - Math.Clamp(v[i] / max, 0, 1) * (h - 3));
                    if (fill)
                    {
                        c.BeginFigure(new Point(x0, h), true, true);
                        c.LineTo(P(0), false, false);
                    }
                    else c.BeginFigure(P(0), false, false);
                    for (int i = 1; i < v.Count; i++) c.LineTo(P(i), true, true);
                    if (fill) c.LineTo(new Point(x0 + (v.Count - 1) * step, h), false, false);
                }
                g.Freeze();
                return g;
            }
        }

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Func<int> _gpu;
        readonly Func<(double down, double up)> _net;
        readonly Procs _procs = new();
        readonly Border _surface;
        readonly Grid _body = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly StackPanel _list = new();
        readonly ScrollViewer _scroll;
        readonly Dictionary<string, PRow> _rows = new();
        readonly HashSet<string> _open = new();
        readonly TextBox _search;
        readonly TextBlock _note = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Button _end;
        readonly Grid _columns = new() { Margin = new Thickness(10, 4, 18, 4) };
        readonly TextBlock[] _totals = new TextBlock[4];
        readonly TextBlock[] _labels = new TextBlock[4];
        readonly Border _tabs;
        readonly StackPanel _appsPage = new();
        readonly Grid _appsGrid = new();
        Grid? _perfPage;
        StackPanel? _startPage;
        ScrollViewer? _startScroll;
        ProcSnapshot? _last;
        string _tab = "apps";
        string _sort = "cpu";
        bool _desc = true;
        string _selected = "";
        string _filter = "";
        bool _busy;
        bool _shown;
        long _noteUntil;
        readonly Graph _cpuG = new(100), _memG = new(100), _gpuG = new(100), _diskG = new(0), _netG = new(0);
        readonly TextBlock _cpuV = Big(), _memV = Big(), _gpuV = Big(), _diskV = Big(), _netV = Big(), _sysV = Big();
        readonly TextBlock _cpuS = Small(), _memS = Small(), _gpuS = Small(), _diskS = Small(), _netS = Small(), _sysS = Small();

        public Func<bool>? KeysOn;
        public Action<bool>? SetKeys;

        static TextBlock Big()
        {
            var t = Kit.Text("", 22, false);
            t.FontWeight = FontWeights.Light;
            return t;
        }

        static TextBlock Small()
        {
            var t = Kit.Text("", 11.5, false, "SubText");
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        public TaskManager(Settings s, AppCatalog cat, Func<int> gpu, Func<(double down, double up)> net)
        {
            _s = s;
            _cat = cat;
            _gpu = gpu;
            _net = net;
            Glass.Attach(this, 14);
            Topmost = false;
            ShowInTaskbar = true;
            Title = "task manager";
            Width = 900;
            Height = 640;
            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                long ex = Native.ExStyle(h);
                ex &= ~Native.WS_EX_TOOLWINDOW;
                ex &= ~Native.WS_EX_NOACTIVATE;
                ex |= 0x00040000;
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));
            };

            _surface = Glass.Surface(14);
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new DockPanel { Margin = new Thickness(16, 12, 12, 8), Background = Brushes.Transparent };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button? pin = null;
            pin = Kit.Plain("\uE718", () =>
            {
                Topmost = !Topmost;
                pin!.Opacity = Topmost ? 1 : 0.55;
                Say(Topmost ? "staying on top" : "not on top anymore");
            }, "keep on top", 12);
            pin.Opacity = 0.55;
            right.Children.Add(pin);
            right.Children.Add(Kit.Plain("\uE921", () => WindowState = WindowState.Minimized, "minimize", 11));
            right.Children.Add(Kit.Plain("\uE711", Shut, "close", 11));
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var glyph = Kit.Icon("\uE9D9", 15, "AccentFill");
            glyph.Margin = new Thickness(0, 1, 9, 0);
            brand.Children.Add(glyph);
            brand.Children.Add(Kit.Text("task manager", 14, true));
            DockPanel.SetDock(brand, Dock.Left);
            head.Children.Add(brand);
            _tabs = Kit.Segments(new[] { "Processes", "Performance", "Startup" }, new[] { "apps", "perf", "startup" }, _tab, v => Tab(v));
            _tabs.Width = 300;
            _tabs.HorizontalAlignment = HorizontalAlignment.Center;
            head.Children.Add(_tabs);
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && Up<ButtonBase>(d) == null && e.ButtonState == MouseButtonState.Pressed)
                {
                    try
                    {
                        DragMove();
                    }
                    catch
                    {
                    }
                }
            };
            root.Children.Add(head);

            _search = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Tag = "search by name, exe or pid", Width = 260 };
            _search.TextChanged += (_, _) =>
            {
                _filter = _search.Text.Trim();
                if (_last != null) Fill(_last);
            };
            BuildApps();
            Grid.SetRow(_body, 1);
            _body.Children.Add(_appsGrid);
            root.Children.Add(_body);

            var foot = new DockPanel { Margin = new Thickness(16, 6, 12, 12) };
            _end = Kit.Push("End task", EndSelected);
            _end.IsEnabled = false;
            DockPanel.SetDock(_end, Dock.Right);
            foot.Children.Add(_end);
            var grip = new Border { Width = 14, Height = 14, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, -6, -6) };
            grip.Child = Kit.Icon("\uE740", 9, "Faint");
            Resizer(grip);
            DockPanel.SetDock(grip, Dock.Right);
            foot.Children.Insert(0, grip);
            _note.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            foot.Children.Add(_note);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            _surface.Child = root;
            _scroll = (ScrollViewer)_appsGrid.Children[2];
            Content = _surface;

            _timer.Tick += (_, _) => Pull();
            PreviewKeyDown += OnKey;
            PreviewTextInput += (_, e) =>
            {
                if (_tab != "apps" || _search.IsKeyboardFocusWithin || e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
                _search.Focus();
                _search.Text += e.Text;
                _search.CaretIndex = _search.Text.Length;
                e.Handled = true;
            };
            Closing += (_, e) =>
            {
                if (!_quitting)
                {
                    e.Cancel = true;
                    Shut();
                }
            };
        }

        bool _quitting;

        public void Quit()
        {
            _quitting = true;
            _timer.Stop();
            _procs.Dispose();
            try
            {
                Close();
            }
            catch
            {
            }
        }

        static T? Up<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void Resizer(Border grip)
        {
            Point start = default;
            Size from = default;
            grip.MouseLeftButtonDown += (_, e) =>
            {
                start = PointToScreen(e.GetPosition(this));
                from = new Size(ActualWidth, ActualHeight);
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (_, e) =>
            {
                if (!grip.IsMouseCaptured) return;
                var now = PointToScreen(e.GetPosition(this));
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Width = Math.Max(680, from.Width + (now.X - start.X) / scale);
                Height = Math.Max(460, from.Height + (now.Y - start.Y) / scale);
            };
            grip.MouseLeftButtonUp += (_, _) => grip.ReleaseMouseCapture();
        }

        public bool Shown => _shown;

        public void Toggle()
        {
            if (_shown && IsActive) Shut();
            else Open();
        }

        public void Open(string tab = "")
        {
            if (tab.Length > 0 && tab != _tab) SelectTab(tab);
            if (_shown)
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                return;
            }
            _shown = true;
            var d = Displays.AtCursor();
            double scale = d.Scale;
            double wDip = Math.Min(Width, d.Work.Width / scale - 40), hDip = Math.Min(Height, d.Work.Height / scale - 40);
            Width = wDip;
            Height = hDip;
            Glass.Present(this, _surface, () =>
            {
                int w = (int)(wDip * scale), h = (int)(hDip * scale);
                int x = d.Work.Left + (d.Work.Width - w) / 2, y = d.Work.Top + (d.Work.Height - h) / 2;
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
            }, 8, 0.97, 240);
            Activate();
            Pull();
            _timer.Start();
            if (_tab == "startup") LoadStartup();
        }

        public void Shut()
        {
            if (!_shown) return;
            _shown = false;
            _timer.Stop();
            GlassMenu.CloseAll();
            Glass.Dismiss(this, _surface);
        }

        void SelectTab(string tab)
        {
            if (_tabs.Child is UniformGrid g)
                foreach (var rb in g.Children.OfType<RadioButton>())
                    if ((rb.Tag as string) == tab) rb.IsChecked = true;
            Tab(tab);
        }

        void Tab(string tab)
        {
            if (_tab == tab && _body.Children.Count > 0) return;
            _tab = tab;
            _body.Children.Clear();
            _end.Visibility = tab == "apps" ? Visibility.Visible : Visibility.Collapsed;
            if (tab == "apps") _body.Children.Add(_appsGrid);
            else if (tab == "perf")
            {
                _perfPage ??= BuildPerf();
                _body.Children.Add(_perfPage);
            }
            else
            {
                if (_startPage == null)
                {
                    _startPage = new StackPanel { Margin = new Thickness(16, 4, 16, 4) };
                    _startScroll = new ScrollViewer { Content = _startPage, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                }
                _body.Children.Add(_startScroll!);
                LoadStartup();
            }
            if (_last != null) Fill(_last);
        }

        void BuildApps()
        {
            _appsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _appsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _appsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var bar = new DockPanel { Margin = new Thickness(16, 2, 16, 6) };
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var cap = Kit.Text("ctrl + shift + esc opens this", 11.5, false, "SubText");
            cap.VerticalAlignment = VerticalAlignment.Center;
            cap.Margin = new Thickness(0, 0, 8, 0);
            keys.Children.Add(cap);
            keys.Children.Add(Kit.Switch(_s.TaskManagerKeys, v =>
            {
                _s.TaskManagerKeys = v;
                _s.Touch();
                SetKeys?.Invoke(v);
            }));
            DockPanel.SetDock(keys, Dock.Right);
            bar.Children.Add(keys);
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
            searchRow.Children.Add(_search);
            bar.Children.Add(searchRow);
            _appsGrid.Children.Add(bar);

            Cols(_columns);
            string[] ids = { "name", "cpu", "mem", "disk" };
            string[] names = { "name", "cpu", "memory", "disk" };
            for (int i = 0; i < 4; i++)
            {
                int col = i == 0 ? 2 : i + 2;
                var stack = new StackPanel { HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(i == 0 ? 0 : 0, 0, i == 0 ? 0 : 8, 0) };
                _totals[i] = Kit.Text(i == 0 ? "" : "–", 13, true);
                _totals[i].HorizontalAlignment = stack.HorizontalAlignment;
                _labels[i] = Kit.Text(names[i], 11, false, "SubText");
                _labels[i].HorizontalAlignment = stack.HorizontalAlignment;
                stack.Children.Add(_totals[i]);
                stack.Children.Add(_labels[i]);
                string id = ids[i];
                var hit = new Border { Background = Brushes.Transparent, Child = stack, Cursor = Cursors.Hand, Padding = new Thickness(4, 2, 0, 2) };
                hit.MouseLeftButtonUp += (_, _) =>
                {
                    if (_sort == id) _desc = !_desc;
                    else
                    {
                        _sort = id;
                        _desc = id != "name";
                    }
                    SortLabels();
                    if (_last != null) Fill(_last, true);
                };
                Grid.SetColumn(hit, col);
                if (i == 0) Grid.SetColumnSpan(hit, 1);
                _columns.Children.Add(hit);
            }
            SortLabels();
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -4) };
            line.SetResourceReference(Border.BackgroundProperty, "Separator");
            Grid.SetColumnSpan(line, 6);
            _columns.Children.Add(line);
            Grid.SetRow(_columns, 1);
            _appsGrid.Children.Add(_columns);

            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 4, 10, 4) };
            Grid.SetRow(scroll, 2);
            _appsGrid.Children.Add(scroll);
        }

        static void Cols(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        }

        void SortLabels()
        {
            string[] ids = { "name", "cpu", "mem", "disk" };
            string[] names = { "name", "cpu", "memory", "disk" };
            for (int i = 0; i < 4; i++)
            {
                bool on = _sort == ids[i];
                _labels[i].Text = names[i] + (on ? (_desc ? "  ▾" : "  ▴") : "");
                _labels[i].SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentFill" : "SubText");
            }
        }

        void Pull()
        {
            if (_busy || !_shown) return;
            _busy = true;
            Task.Run(() =>
            {
                ProcSnapshot? snap = null;
                try
                {
                    snap = _procs.Sample();
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("task manager sample failed " + ex.Message);
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    if (snap == null || !_shown) return;
                    _last = snap;
                    Fill(snap);
                }));
            });
        }

        void Fill(ProcSnapshot snap, bool force = false)
        {
            if (_tab == "apps") FillApps(snap, force);
            UpdatePerf(snap);
            if (Environment.TickCount64 > _noteUntil)
            {
                _note.Text = $"{snap.Processes} processes  ·  {snap.Threads:N0} threads  ·  {snap.Handles:N0} handles";
                _note.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            }
        }

        static string Mem(long b)
        {
            double mb = b / 1048576.0;
            return mb >= 1024 ? $"{mb / 1024:0.00} GB" : $"{mb:0.0} MB";
        }

        static string Rate(double bps)
        {
            double mb = bps / 1048576.0;
            if (mb < 0.05) return "0 MB/s";
            return mb >= 100 ? $"{mb:0} MB/s" : $"{mb:0.0} MB/s";
        }

        bool Match(ProcGroup g)
        {
            if (_filter.Length == 0) return true;
            var f = _filter;
            if (g.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || g.Image.Contains(f, StringComparison.OrdinalIgnoreCase) || g.Title.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
            return g.Rows.Any(r => r.Pid.ToString() == f);
        }

        IEnumerable<ProcGroup> Sorted(IEnumerable<ProcGroup> list)
        {
            return _sort switch
            {
                "name" => _desc ? list.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase) : list.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "mem" => _desc ? list.OrderByDescending(g => g.Memory) : list.OrderBy(g => g.Memory),
                "disk" => _desc ? list.OrderByDescending(g => g.Disk).ThenByDescending(g => g.Cpu) : list.OrderBy(g => g.Disk),
                _ => _desc ? list.OrderByDescending(g => Math.Round(g.Cpu, 1)).ThenByDescending(g => g.Memory) : list.OrderBy(g => g.Cpu)
            };
        }

        readonly Dictionary<string, TextBlock> _sections = new();

        TextBlock Section(string key)
        {
            if (_sections.TryGetValue(key, out var t)) return t;
            t = Kit.Text("", 12, true, "SubText");
            t.Margin = new Thickness(8, 12, 0, 4);
            _sections[key] = t;
            return t;
        }

        void FillApps(ProcSnapshot snap, bool force)
        {
            double total = snap.MemTotal > 0 ? snap.MemTotal : 16L * 1024 * 1024 * 1024;
            _totals[1].Text = $"{snap.Cpu:0}%";
            _totals[2].Text = snap.MemTotal > 0 ? $"{snap.MemUsed * 100.0 / snap.MemTotal:0}%" : "–";
            _totals[3].Text = Rate(snap.DiskRate);
            var shown = snap.Groups.Where(g => g.Image != "System Idle Process" && Match(g)).ToList();
            var apps = Sorted(shown.Where(g => g.App)).ToList();
            var bg = Sorted(shown.Where(g => !g.App)).ToList();
            var order = new List<UIElement>();
            var live = new HashSet<string>();
            void Add(ProcGroup g)
            {
                var row = Row(g.Key);
                row.Group = g;
                row.Proc = null;
                Paint(row, g.Name + (g.Rows.Count > 1 ? $" ({g.Rows.Count})" : ""), g.Path, g.Cpu, g.Memory, g.Disk, total, false);
                row.Tag.Text = g.Hung ? "not responding" : "";
                row.Chevron.Text = g.Rows.Count > 1 ? (_open.Contains(g.Key) ? "\uE70D" : "\uE76C") : "";
                row.Root.ToolTip = (g.Title.Length > 0 ? g.Title + "\n" : "") + (g.Path ?? g.Image);
                order.Add(row.Root);
                live.Add(g.Key);
                if (g.Rows.Count > 1 && _open.Contains(g.Key))
                {
                    var kids = _sort switch
                    {
                        "mem" => g.Rows.OrderByDescending(r => r.Memory),
                        "disk" => g.Rows.OrderByDescending(r => r.Disk),
                        "name" => g.Rows.OrderBy(r => r.Pid),
                        _ => g.Rows.OrderByDescending(r => r.Cpu)
                    };
                    foreach (var r in kids)
                    {
                        string key = g.Key + "#" + r.Pid;
                        var kid = Row(key, true);
                        kid.Group = g;
                        kid.Proc = r;
                        Paint(kid, $"{System.IO.Path.GetFileNameWithoutExtension(r.Image)}  ·  pid {r.Pid}  ·  {r.Threads} threads", null, r.Cpu, r.Memory, r.Disk, total, true);
                        kid.Root.ToolTip = r.Started > DateTime.MinValue ? "started " + Kit.Ago(r.Started) : null;
                        order.Add(kid.Root);
                        live.Add(key);
                    }
                }
            }
            var appsHead = Section("apps");
            appsHead.Text = $"apps ({apps.Count})";
            if (apps.Count > 0) order.Add(appsHead);
            foreach (var g in apps) Add(g);
            var bgHead = Section("bg");
            bgHead.Text = $"background processes ({bg.Count})";
            if (bg.Count > 0) order.Add(bgHead);
            foreach (var g in bg) Add(g);
            if (order.Count == 0)
            {
                var none = Section("none");
                none.Text = _filter.Length > 0 ? "nothing called \"" + _filter + "\" is running" : "loading...";
                order.Add(none);
            }
            foreach (var k in _rows.Keys.Where(k => !live.Contains(k)).ToList()) _rows.Remove(k);
            if (_selected.Length > 0 && !live.Contains(_selected)) Select("");
            bool same = order.Count == _list.Children.Count && new HashSet<UIElement>(order).SetEquals(_list.Children.Cast<UIElement>());
            bool hold = !force && same && _list.IsMouseOver;
            if (!hold && !order.SequenceEqual(_list.Children.Cast<UIElement>()))
            {
                _list.Children.Clear();
                foreach (var e in order) _list.Children.Add(e);
            }
            UpdateEnd();
        }

        PRow Row(string key, bool child = false)
        {
            if (_rows.TryGetValue(key, out var r)) return r;
            r = new PRow { Key = key };
            var grid = new Grid { Height = child ? 26 : 30 };
            Cols(grid);
            r.Chevron = Kit.Icon("", 9, "SubText");
            r.Chevron.HorizontalAlignment = HorizontalAlignment.Center;
            r.Chevron.VerticalAlignment = VerticalAlignment.Center;
            var chevHit = new Border { Background = Brushes.Transparent, Child = r.Chevron, Cursor = Cursors.Hand };
            chevHit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (r.Group == null || r.Group.Rows.Count < 2) return;
                if (!_open.Remove(r.Group.Key)) _open.Add(r.Group.Key);
                if (_last != null) Fill(_last, true);
            };
            grid.Children.Add(chevHit);
            var iconBox = new Grid { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            r.Icon = new Image { Width = 16, Height = 16 };
            RenderOptions.SetBitmapScalingMode(r.Icon, BitmapScalingMode.HighQuality);
            r.Fallback = Kit.Icon("\uE770", 12, "SubText");
            ((FrameworkElement)r.Fallback).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)r.Fallback).VerticalAlignment = VerticalAlignment.Center;
            iconBox.Children.Add(r.Fallback);
            iconBox.Children.Add(r.Icon);
            Grid.SetColumn(iconBox, 1);
            if (child) iconBox.Visibility = Visibility.Hidden;
            grid.Children.Add(iconBox);
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(child ? 4 : 0, 0, 8, 0) };
            r.Name = Kit.Text("", child ? 11.5 : 12.5, false, child ? "SubText" : "Text");
            r.Name.TextTrimming = TextTrimming.CharacterEllipsis;
            r.Tag = Kit.Text("", 11, false, "Danger");
            r.Tag.Margin = new Thickness(8, 1, 0, 0);
            nameRow.Children.Add(r.Name);
            nameRow.Children.Add(r.Tag);
            Grid.SetColumn(nameRow, 2);
            grid.Children.Add(nameRow);
            (r.CpuCell, r.CpuText) = Cell(grid, 3);
            (r.MemCell, r.MemText) = Cell(grid, 4);
            (r.DiskCell, r.DiskText) = Cell(grid, 5);
            r.Root = new Border { Child = grid, CornerRadius = Kit.Round(7), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 1) };
            r.Root.MouseEnter += (_, _) => Shade(r);
            r.Root.MouseLeave += (_, _) => Shade(r);
            r.Root.MouseLeftButtonDown += (_, e) =>
            {
                Select(r.Key);
                if (e.ClickCount == 2 && r.Group?.App == true && r.Group.Window != IntPtr.Zero)
                {
                    WinList.Switch(r.Group.Window);
                    e.Handled = true;
                }
            };
            r.Root.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                Select(r.Key);
                Menu(r);
            };
            _rows[key] = r;
            return r;
        }

        static (Border, TextBlock) Cell(Grid grid, int col)
        {
            var t = Kit.Text("", 12, false);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 8, 0);
            var b = new Border { Child = t, Margin = new Thickness(2, 2, 0, 2), CornerRadius = new CornerRadius(5) };
            Grid.SetColumn(b, col);
            grid.Children.Add(b);
            return (b, t);
        }

        void Paint(PRow r, string name, string? path, double cpu, long mem, double disk, double total, bool child)
        {
            if (r.Name.Text != name) r.Name.Text = name;
            r.CpuText.Text = $"{cpu:0.0}%";
            r.MemText.Text = Mem(mem);
            r.DiskText.Text = Rate(disk);
            r.CpuCell.Background = Heat(cpu / 40);
            r.MemCell.Background = Heat(mem / (total * 0.2));
            r.DiskCell.Background = Heat(disk / (40.0 * 1048576));
            if (!child && path != r.IconPath)
            {
                r.IconPath = path;
                r.Icon.Source = null;
                r.Fallback.Visibility = Visibility.Visible;
                if (path != null)
                    _cat.Icons.Get(path, 32, true, img =>
                    {
                        if (r.IconPath != path || img == null) return;
                        r.Icon.Source = img;
                        r.Fallback.Visibility = Visibility.Collapsed;
                    });
            }
        }

        static Brush Heat(double v)
        {
            v = Math.Clamp(v, 0, 1);
            if (v < 0.02) return Brushes.Transparent;
            var a = Theme.Accent;
            var b = new SolidColorBrush(Color.FromArgb((byte)(18 + v * 110), a.R, a.G, a.B));
            b.Freeze();
            return b;
        }

        void Shade(PRow r)
        {
            if (r.Key == _selected) r.Root.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            else if (r.Root.IsMouseOver) r.Root.SetResourceReference(Border.BackgroundProperty, "RowHover");
            else r.Root.Background = Brushes.Transparent;
        }

        void Select(string key)
        {
            string was = _selected;
            _selected = key;
            if (_rows.TryGetValue(was, out var a)) Shade(a);
            if (_rows.TryGetValue(key, out var b)) Shade(b);
            UpdateEnd();
        }

        PRow? Picked() => _selected.Length > 0 && _rows.TryGetValue(_selected, out var r) ? r : null;

        void UpdateEnd()
        {
            var r = Picked();
            _end.IsEnabled = r != null;
            bool explorer = r?.Proc == null && r?.Group?.Image.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) == true;
            _end.Content = explorer ? "Restart" : "End task";
        }

        IEnumerable<uint> Pids(PRow r) => r.Proc != null ? new[] { r.Proc.Pid } : r.Group?.Rows.Select(x => x.Pid) ?? Enumerable.Empty<uint>();

        void EndSelected()
        {
            var r = Picked();
            if (r?.Group == null) return;
            if (r.Proc == null && r.Group.Image.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                Say("restarting windows explorer...");
                Task.Run(Procs.RestartExplorer);
                return;
            }
            End(r);
        }

        void End(PRow r)
        {
            if (r.Group == null) return;
            string label = r.Proc != null ? "pid " + r.Proc.Pid : r.Group.Name;
            var pids = Pids(r).ToList();
            Task.Run(() =>
            {
                string? err = Procs.End(pids);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err != null) Say(err, true);
                    else
                    {
                        Sfx.Play("trash");
                        Say("ended " + label);
                    }
                    Pull();
                }));
            });
        }

        void Say(string text, bool bad = false)
        {
            _note.Text = text;
            _note.SetResourceReference(TextBlock.ForegroundProperty, bad ? "Danger" : "AccentFill");
            _noteUntil = Environment.TickCount64 + 4000;
        }

        void Menu(PRow r)
        {
            var g = r.Group;
            if (g == null) return;
            var items = new List<MenuEntry>();
            string? path = r.Proc?.Path ?? g.Path;
            if (r.Proc == null && g.App && g.Window != IntPtr.Zero) items.Add(MenuEntry.Item("\uE8A7", "Switch to", () => WinList.Switch(g.Window)));
            bool explorer = r.Proc == null && g.Image.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            if (explorer) items.Add(MenuEntry.Item("\uE72C", "Restart", () =>
            {
                Say("restarting windows explorer...");
                Task.Run(Procs.RestartExplorer);
            }));
            items.Add(MenuEntry.Item("\uE711", r.Proc != null ? "End process" : g.Rows.Count > 1 ? $"End task ({g.Rows.Count})" : "End task", () => End(r), true));
            items.Add(MenuEntry.Sep());
            var pids = Pids(r).ToList();
            void Pri(ProcessPriorityClass c, string label)
            {
                string? err = Procs.Priority(pids, c);
                Say(err ?? "priority set to " + label, err != null);
            }
            items.Add(MenuEntry.Sub("\uE9D9", "Priority", new List<MenuEntry>
            {
                MenuEntry.Item("", "High", () => Pri(ProcessPriorityClass.High, "high")),
                MenuEntry.Item("", "Above normal", () => Pri(ProcessPriorityClass.AboveNormal, "above normal")),
                MenuEntry.Item("", "Normal", () => Pri(ProcessPriorityClass.Normal, "normal")),
                MenuEntry.Item("", "Below normal", () => Pri(ProcessPriorityClass.BelowNormal, "below normal")),
                MenuEntry.Item("", "Low", () => Pri(ProcessPriorityClass.Idle, "low"))
            }));
            if (path != null) items.Add(MenuEntry.Item("\uE838", "Open file location", () => Files.Reveal(path)));
            items.Add(MenuEntry.Item("\uE721", "Search online", () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(g.Image + " process")) { UseShellExecute = true });
                }
                catch
                {
                }
            }));
            items.Add(MenuEntry.Item("\uE8C8", "Copy name", () =>
            {
                try
                {
                    Clipboard.SetText(r.Proc != null ? r.Proc.Pid.ToString() : g.Image);
                    Say("copied");
                }
                catch
                {
                }
            }));
            GlassMenu.Show(r.Root, items);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_search.Text.Length > 0)
                {
                    _search.Text = "";
                    Keyboard.Focus(_scroll);
                }
                else Shut();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_tab != "apps") SelectTab("apps");
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
                return;
            }
            if (_tab != "apps") return;
            if (e.Key == Key.Delete && !_search.IsKeyboardFocusWithin)
            {
                EndSelected();
                e.Handled = true;
            }
            else if (e.Key is Key.Down or Key.Up)
            {
                var keys = _list.Children.OfType<Border>().Select(b => _rows.Values.FirstOrDefault(r => r.Root == b)).Where(r => r != null).Select(r => r!.Key).ToList();
                if (keys.Count == 0) return;
                int i = keys.IndexOf(_selected);
                i = e.Key == Key.Down ? Math.Min(keys.Count - 1, i + 1) : Math.Max(0, i < 0 ? 0 : i - 1);
                Select(keys[i]);
                if (_rows.TryGetValue(keys[i], out var row)) row.Root.BringIntoView();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Picked() is { Group: { App: true } g } && g.Window != IntPtr.Zero)
            {
                WinList.Switch(g.Window);
                e.Handled = true;
            }
        }

        Grid BuildPerf()
        {
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(12, 4, 12, 4) };
            grid.Children.Add(Card("cpu", "\uE9D9", _cpuV, _cpuS, _cpuG));
            grid.Children.Add(Card("memory", "\uE964", _memV, _memS, _memG));
            grid.Children.Add(Card("gpu", "\uE7F4", _gpuV, _gpuS, _gpuG));
            grid.Children.Add(Card("disk", "\uEDA2", _diskV, _diskS, _diskG));
            grid.Children.Add(Card("network", "\uE968", _netV, _netS, _netG));
            grid.Children.Add(Card("system", "\uE770", _sysV, _sysS, null));
            var host = new Grid();
            host.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return host;
        }

        static Border Card(string name, string glyph, TextBlock value, TextBlock sub, Graph? graph)
        {
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Kit.Icon(glyph, 12, "SubText");
            icon.Margin = new Thickness(0, 1, 7, 0);
            head.Children.Add(icon);
            head.Children.Add(Kit.Text(name, 12, true, "SubText"));
            stack.Children.Add(head);
            value.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(value);
            sub.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(sub);
            if (graph != null) stack.Children.Add(graph.Host);
            var b = new Border { Child = stack, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(4), CornerRadius = Kit.Round(10), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            return b;
        }

        static string Uptime(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m" : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m";

        void UpdatePerf(ProcSnapshot snap)
        {
            _cpuG.Push(snap.Cpu);
            double memPct = snap.MemTotal > 0 ? snap.MemUsed * 100.0 / snap.MemTotal : 0;
            _memG.Push(memPct);
            int gpu = -1;
            try
            {
                gpu = _gpu();
            }
            catch
            {
            }
            _gpuG.Push(Math.Max(0, gpu));
            _diskG.Push(snap.DiskRate / 1048576.0);
            var (down, up) = (0.0, 0.0);
            try
            {
                (down, up) = _net();
            }
            catch
            {
            }
            _netG.Push(down / 1048576.0, up / 1048576.0);
            if (_tab != "perf") return;
            _cpuV.Text = $"{snap.Cpu:0}%";
            _cpuS.Text = $"{Environment.ProcessorCount} logical cores  ·  {snap.Processes} processes";
            _memV.Text = snap.MemTotal > 0 ? $"{snap.MemUsed / 1073741824.0:0.0} / {snap.MemTotal / 1073741824.0:0.0} GB" : "–";
            _memS.Text = snap.CommitLimit > 0 ? $"{memPct:0}% in use  ·  committed {snap.Commit / 1073741824.0:0.0} of {snap.CommitLimit / 1073741824.0:0.0} GB" : "";
            _gpuV.Text = gpu >= 0 ? $"{gpu}%" : "–";
            _gpuS.Text = gpu >= 0 ? "3d engine, all apps" : "windows isn't sharing gpu numbers right now";
            _diskV.Text = Rate(snap.DiskRate);
            _diskS.Text = "reads + writes from every app";
            _netV.Text = "↓ " + Rate(down);
            _netS.Text = "↑ " + Rate(up) + " up  ·  the faint line is upload";
            _sysV.Text = Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
            _sysS.Text = $"up since boot\n{snap.Threads:N0} threads  ·  {snap.Handles:N0} handles\n{Environment.MachineName.ToLowerInvariant()}  ·  {Environment.OSVersion.Version}";
        }

        void LoadStartup()
        {
            if (_startPage == null) return;
            _startPage.Children.Clear();
            _startPage.Children.Add(Kit.Caption("loading..."));
            Task.Run(() =>
            {
                List<StartupItem> list;
                try
                {
                    list = Startups.All();
                }
                catch
                {
                    list = new List<StartupItem>();
                }
                Dispatcher.BeginInvoke(new Action(() => ShowStartup(list)));
            });
        }

        void ShowStartup(List<StartupItem> list)
        {
            if (_startPage == null) return;
            _startPage.Children.Clear();
            var top = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
            var folder = Kit.Push("Open startup folder", () => Files.Open(Environment.GetFolderPath(Environment.SpecialFolder.Startup)));
            DockPanel.SetDock(folder, Dock.Right);
            top.Children.Add(folder);
            var cap = Kit.Caption(list.Count == 0 ? "nothing starts with windows. nice" : $"{list.Count(i => i.On)} of {list.Count} start with windows. flip one off and it won't open next time you sign in");
            cap.TextWrapping = TextWrapping.Wrap;
            cap.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(cap);
            _startPage.Children.Add(top);
            if (list.Count == 0) return;
            var rows = new List<UIElement>();
            foreach (var it in list)
            {
                var item = it;
                var row = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
                var sw = Kit.Switch(item.On, v =>
                {
                    string? err = Startups.Set(item, v);
                    if (err != null)
                    {
                        Say(err, true);
                        LoadStartup();
                    }
                    else Say(item.Name + (v ? " starts with windows again" : " won't start with windows anymore"));
                });
                sw.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(sw, Dock.Right);
                row.Children.Add(sw);
                var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                if (item.Exe != null) _cat.Icons.Get(item.Exe, 32, true, img => icon.Source = img);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var n = Kit.Text(item.Name, 12.5, true);
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(n);
                var sub = Kit.Text((item.Publisher.Length > 0 ? item.Publisher + "  ·  " : "") + item.Where, 11, false, "SubText");
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(sub);
                row.Children.Add(text);
                row.ToolTip = item.Command;
                rows.Add(row);
            }
            _startPage.Children.Add(Kit.Group(rows.ToArray()));
        }
    }
}
