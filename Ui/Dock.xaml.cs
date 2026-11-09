using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Heft.Ui
{
    public partial class Dock : Window
    {
        sealed class Tile
        {
            public WheelItem Item = null!;
            public int Index;
            public Grid Root = null!;
            public Image Img = null!;
            public ScaleTransform Scale = null!;
            public Border? Label;
            public double LabelW, LabelH;
            public bool Loaded;
            public double Mag = 1, X, Y, Size;
            public bool Shown;
        }

        sealed class Layer
        {
            public string Kind = "apps";
            public string Title = "";
            public string? Path;
            public Canvas Canvas = null!;
            public ScaleTransform Zoom = null!;
            public List<Tile> Tiles = new();
            public string Key => Kind + "|" + (Path ?? "");
        }

        const double R = 380;
        const double Across = 360;

        static readonly DropShadowEffect IconShadow = MakeShadow();

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Stack<string> _trail = new();
        readonly Dictionary<string, double> _memo = new();

        Layer? _cur;
        string _side = "right";
        double _size = 56, _along = 760, _apex, _d, _v, _step;
        double _phi, _target, _vel, _reveal, _revealTarget, _last, _snapAt, _zoomLock, _outsideSince = -1, _keyAt = -10;
        double _lastPointer, _travel, _lastMoveT;
        bool _running, _closing, _coasting, _dragging, _entered, _hoverOpened, _snapPending;
        Tile? _hover, _press;
        int _focus = -1;

        public event Action? SettingsRequested;

        internal Dock(Settings settings, AppCatalog catalog)
        {
            InitializeComponent();
            _s = settings;
            _cat = catalog;

            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, false);
            Deactivated += (_, _) =>
            {
                if (!_dragging) HideDock();
            };
            PreviewKeyDown += OnKey;

            root.MouseLeftButtonDown += OnDown;
            root.MouseMove += OnMove;
            root.MouseLeftButtonUp += OnUp;
            root.MouseRightButtonUp += OnRight;
            root.MouseWheel += OnWheel;

            back.Click += (_, _) => ZoomOut(true);
            openHere.Click += (_, _) =>
            {
                if (_cur?.Path != null) Reveal(_cur.Path, false);
                HideDock();
            };
        }

        static DropShadowEffect MakeShadow()
        {
            var e = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            e.Freeze();
            return e;
        }

        double Now => _clock.Elapsed.TotalSeconds;
        bool Vertical => _side == "left" || _side == "right";
        bool FanLabels => Vertical && _cur != null && _cur.Kind != "apps";

        Point Map(double u, double v)
        {
            return _side switch
            {
                "left" => new Point(v, _along / 2 + u),
                "top" => new Point(_along / 2 + u, v),
                "bottom" => new Point(_along / 2 + u, Across - v),
                _ => new Point(Across - v, _along / 2 + u)
            };
        }

        (double u, double v) Unmap(Point p)
        {
            return _side switch
            {
                "left" => (p.Y - _along / 2, p.X),
                "top" => (p.X - _along / 2, p.Y),
                "bottom" => (p.X - _along / 2, Across - p.Y),
                _ => (p.Y - _along / 2, Across - p.X)
            };
        }

        double PointerAngle(Point p)
        {
            var (u, v) = Unmap(p);
            return Math.Atan2(u, v + _d) * 180 / Math.PI;
        }

        void Geometry()
        {
            _side = _s.DockSide is "left" or "top" or "bottom" ? _s.DockSide : "right";
            _size = Math.Clamp(_s.IconSize, 36, 80);
            _along = Vertical ? 760 : 960;
            _apex = _size * 0.5 + 30;
            _d = R - _apex;
            _v = Math.Acos(Math.Clamp((_d - _size * 0.3) / R, -1, 1)) * 180 / Math.PI;
            _step = _size * 1.25 / R * 180 / Math.PI;

            if (Vertical)
            {
                Width = Across;
                Height = _along;
            }
            else
            {
                Width = _along;
                Height = Across;
            }
        }

        public void ShowDock(bool byHover, bool toggle)
        {
            if (IsVisible && !_closing)
            {
                if (toggle) HideDock();
                return;
            }

            _closing = false;
            _hoverOpened = byHover;
            _entered = false;
            _outsideSince = -1;
            _dragging = false;
            _focus = -1;
            _hover = null;
            Geometry();

            _trail.Clear();
            foreach (var c in layers.Children.OfType<Canvas>().ToList()) layers.Children.Remove(c);
            _cur = null;
            SetLayer(BuildApps(), 0);

            _reveal = 0;
            _revealTarget = 1;

            Native.GetCursorPos(out var p);
            Show();
            var h = new WindowInteropHelper(this).Handle;
            const uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
            Native.SetWindowPos(h, IntPtr.Zero, p.X, p.Y, 0, 0, flags);
            Native.GetWindowRect(h, out var r);
            var wa = Native.WorkAreaAt(p.X, p.Y);
            int x, y;
            switch (_side)
            {
                case "left":
                    x = wa.Left;
                    y = wa.Top + (wa.Height - r.Height) / 2;
                    break;
                case "top":
                    x = wa.Left + (wa.Width - r.Width) / 2;
                    y = wa.Top;
                    break;
                case "bottom":
                    x = wa.Left + (wa.Width - r.Width) / 2;
                    y = wa.Bottom - r.Height;
                    break;
                default:
                    x = wa.Right - r.Width;
                    y = wa.Top + (wa.Height - r.Height) / 2;
                    break;
            }
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, flags);

            Activate();
            Focus();
            Start();
            Tick(null, EventArgs.Empty);
        }

        public void HideDock()
        {
            if (!IsVisible || _closing) return;
            _closing = true;
            _revealTarget = 0;
            _dragging = false;
            if (root.IsMouseCaptured) root.ReleaseMouseCapture();
        }

        Layer BuildApps()
        {
            var items = _cat.WheelItems(30);
            items.Add(new WheelItem { Name = "Settings", Path = "", Kind = "settings" });
            return BuildLayer("apps", "Apps", null, items);
        }

        Layer BuildLayer(string kind, string title, string? path, List<WheelItem> items)
        {
            var canvas = new Canvas { Width = Width, Height = Height };
            var origin = Map(0, -_d);
            var zoom = new ScaleTransform(1, 1, origin.X, origin.Y);
            canvas.RenderTransform = zoom;
            var layer = new Layer { Kind = kind, Title = title, Path = path, Canvas = canvas, Zoom = zoom };

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var scale = new ScaleTransform(1, 1);
                var rootGrid = new Grid
                {
                    Width = _size,
                    Height = _size,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = scale,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                var img = new Image { Stretch = Stretch.Uniform, Effect = IconShadow };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                if (item.Kind == "settings")
                {
                    var tile = new Border { CornerRadius = new CornerRadius(_size * 0.24), Effect = IconShadow };
                    tile.SetResourceReference(Border.BackgroundProperty, "PillBg");
                    tile.SetResourceReference(Border.BorderBrushProperty, "PillStroke");
                    tile.BorderThickness = new Thickness(1);
                    var glyph = new TextBlock
                    {
                        Text = "\uE713",
                        FontSize = _size * 0.42,
                        Style = (Style)FindResource("Icon")
                    };
                    tile.Child = glyph;
                    rootGrid.Children.Add(tile);
                }
                rootGrid.Children.Add(img);

                var t = new Tile { Item = item, Index = i, Root = rootGrid, Img = img, Scale = scale, Size = _size, Loaded = item.Kind == "settings" };
                canvas.Children.Add(rootGrid);

                if (kind != "apps" && Vertical)
                {
                    var label = new Border
                    {
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(8, 2, 8, 3),
                        BorderThickness = new Thickness(1),
                        IsHitTestVisible = false,
                        Visibility = Visibility.Collapsed,
                        Child = new TextBlock
                        {
                            Text = item.Name,
                            FontSize = 12,
                            MaxWidth = 170,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    };
                    label.SetResourceReference(Border.BackgroundProperty, "PillBg");
                    label.SetResourceReference(Border.BorderBrushProperty, "PillStroke");
                    ((TextBlock)label.Child).SetResourceReference(TextBlock.ForegroundProperty, "Text");
                    t.Label = label;
                    canvas.Children.Add(label);
                }

                layer.Tiles.Add(t);
            }
            return layer;
        }

        (double min, double max) Range()
        {
            int n = _cur?.Tiles.Count ?? 0;
            if (n <= 1) return (0, 0);
            double span = (n - 1) * _step;
            double limit = _v * 0.82;
            if (span <= limit * 2) return (-span / 2, -span / 2);
            return (limit - span, -limit);
        }

        double Snap(double a)
        {
            var (min, max) = Range();
            if (min == max) return min;
            double s = Math.Round(a / _step) * _step;
            return Math.Clamp(s, min, max);
        }

        void SetLayer(Layer layer, int dir)
        {
            var old = _cur;
            if (old != null) _memo[old.Key] = _phi;

            _cur = layer;
            layers.Children.Add(layer.Canvas);
            _hover = null;
            _focus = -1;
            _coasting = false;
            _vel = 0;

            var (min, max) = Range();
            _phi = _memo.TryGetValue(layer.Key, out var saved) ? Math.Clamp(saved, min, max) : max;
            _target = _phi;

            crumbTitle.Text = layer.Title;
            crumbHint.Text = layer.Kind switch
            {
                "apps" => "ctrl + scroll to zoom out",
                "places" => "pick a place",
                _ => $"{layer.Tiles.Count} item{(layer.Tiles.Count == 1 ? "" : "s")}"
            };
            back.Visibility = layer.Kind == "apps" ? Visibility.Collapsed : Visibility.Visible;
            openHere.Visibility = layer.Kind == "folder" ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Visibility = layer.Tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Text = layer.Kind == "folder" ? "nothing in here" : "nothing yet";

            if (old == null || dir == 0)
            {
                if (old != null) layers.Children.Remove(old.Canvas);
                return;
            }

            var d = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double outScale = dir < 0 ? 0.55 : 1.7;
            double inScale = dir < 0 ? 1.7 : 0.55;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => layers.Children.Remove(old.Canvas);
            old.Canvas.IsHitTestVisible = false;
            old.Canvas.BeginAnimation(OpacityProperty, fade);
            old.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(outScale, d) { EasingFunction = ease });
            old.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(outScale, d) { EasingFunction = ease });

            layer.Canvas.Opacity = 0;
            layer.Canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
            layer.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(inScale, 1, d) { EasingFunction = ease });
            layer.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(inScale, 1, d) { EasingFunction = ease });
        }

        void Bump()
        {
            if (_cur == null) return;
            var a = new DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _cur.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _cur.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void ShowPlaces(int dir)
        {
            _trail.Clear();
            SetLayer(BuildLayer("places", "Places", null, _cat.PlaceItems()), dir);
        }

        async void ShowFolder(string path, int dir)
        {
            List<WheelItem> items;
            try
            {
                items = await Task.Run(() => AppCatalog.FolderItems(path));
            }
            catch
            {
                items = new List<WheelItem>();
            }
            if (!IsVisible || _closing) return;
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = path;
            SetLayer(BuildLayer("folder", name, path, items), dir);
        }

        void Enter(string path)
        {
            if (!Directory.Exists(path)) return;
            _trail.Push(path);
            ShowFolder(path, 1);
        }

        void ZoomOut(bool force)
        {
            if (_cur == null) return;
            double now = Now;
            if (!force && now < _zoomLock) return;
            _zoomLock = now + 0.35;

            switch (_cur.Kind)
            {
                case "apps":
                    ShowPlaces(-1);
                    break;
                case "places":
                    Bump();
                    break;
                default:
                    if (_trail.Count > 1)
                    {
                        _trail.Pop();
                        ShowFolder(_trail.Peek(), -1);
                    }
                    else
                    {
                        ShowPlaces(-1);
                    }
                    break;
            }
        }

        void ZoomIn()
        {
            if (_cur == null) return;
            double now = Now;
            if (now < _zoomLock) return;
            _zoomLock = now + 0.35;

            if (_hover != null && IsContainer(_hover.Item))
            {
                Enter(_hover.Item.Path);
                return;
            }
            if (_cur.Kind == "places")
            {
                SetLayer(BuildApps(), 1);
                return;
            }
            Bump();
        }

        static bool IsContainer(WheelItem item) => item.Kind is "folder" or "place" or "drive";

        void Open(Tile t)
        {
            var item = t.Item;
            if (item.Kind == "settings")
            {
                HideDock();
                SettingsRequested?.Invoke();
                return;
            }
            if (IsContainer(item))
            {
                Enter(item.Path);
                return;
            }
            HideDock();
            _cat.Launch(item);
        }

        static void Reveal(string path, bool select)
        {
            try
            {
                var args = select ? $"/select,\"{path}\"" : $"\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        void Start()
        {
            if (_running) return;
            _running = true;
            _last = Now;
            CompositionTarget.Rendering += Tick;
        }

        void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= Tick;
        }

        Point? CursorPoint()
        {
            try
            {
                Native.GetCursorPos(out var p);
                return root.PointFromScreen(new Point(p.X, p.Y));
            }
            catch
            {
                return null;
            }
        }

        void Tick(object? sender, EventArgs e)
        {
            double now = Now;
            double dt = Math.Clamp(now - _last, 0, 1.0 / 30.0);
            _last = now;

            _reveal += (_revealTarget - _reveal) * Math.Min(1, dt * (_revealTarget > 0.5 ? 13 : 18));
            if (_closing && _reveal < 0.02)
            {
                _closing = false;
                _reveal = 0;
                Stop();
                Hide();
                return;
            }

            var mouse = CursorPoint();
            bool inside = mouse is Point mp && mp.X >= 0 && mp.Y >= 0 && mp.X <= ActualWidth && mp.Y <= ActualHeight;
            if (inside) _entered = true;

            if (!_closing && !_dragging && (_hoverOpened || _entered))
            {
                if (!inside)
                {
                    if (_outsideSince < 0) _outsideSince = now;
                    else if (now - _outsideSince > 0.45 && now - _keyAt > 1.5) HideDock();
                }
                else
                {
                    _outsideSince = -1;
                }
            }

            StepScroll(dt, now);
            Layout(dt, inside ? mouse : null);
            Chrome();
        }

        void StepScroll(double dt, double now)
        {
            if (_dragging) return;
            var (min, max) = Range();

            if (_coasting)
            {
                _phi += _vel * dt;
                _vel *= Math.Exp(-3.2 * dt);
                if (_phi < min - _step || _phi > max + _step) _vel *= Math.Exp(-12 * dt);
                if (Math.Abs(_vel) < 40)
                {
                    _coasting = false;
                    _target = Snap(_phi);
                }
                return;
            }

            if (_snapPending && now > _snapAt)
            {
                _snapPending = false;
                _target = Snap(_target);
            }
            _target = Math.Clamp(_target, min, max);

            const double k = 260;
            double c = 2 * Math.Sqrt(k);
            double acc = k * (_target - _phi) - c * _vel;
            _vel += acc * dt;
            _phi += _vel * dt;
            if (Math.Abs(_target - _phi) < 0.01 && Math.Abs(_vel) < 0.5)
            {
                _phi = _target;
                _vel = 0;
            }
        }

        void Layout(double dt, Point? mouse)
        {
            if (_cur == null) return;
            var tiles = _cur.Tiles;
            double hide = (1 - _reveal) * (_apex + _size);
            int budget = 3;
            Tile? hover = null;
            double best = double.MaxValue;
            bool fan = FanLabels;

            foreach (var t in tiles)
            {
                double theta = t.Index * _step + _phi;
                if (Math.Abs(theta) > _v + 8)
                {
                    if (t.Shown)
                    {
                        t.Shown = false;
                        t.Root.Visibility = Visibility.Collapsed;
                        if (t.Label != null) t.Label.Visibility = Visibility.Collapsed;
                    }
                    t.Mag = 1;
                    continue;
                }

                if (!t.Shown)
                {
                    t.Shown = true;
                    t.Root.Visibility = Visibility.Visible;
                }

                if (!t.Loaded && budget > 0)
                {
                    budget--;
                    t.Loaded = true;
                    bool iconOnly = t.Item.Kind is "favorite" or "project" or "frequent";
                    t.Img.Source = _cat.Icon(t.Item.Path, 96, iconOnly);
                    if (t.Img.Source == null && t.Item.Kind != "drive") t.Img.Source = _cat.Icon(t.Item.Path, 96, true);
                }

                double rad = theta * Math.PI / 180;
                double u = R * Math.Sin(rad);
                double v = R * Math.Cos(rad) - _d;
                var basePt = Map(u, v);

                double target = 1;
                if (mouse is Point m && _s.Magnify > 0.01)
                {
                    double dx = m.X - basePt.X, dy = m.Y - basePt.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    target = 1 + _s.Magnify * 0.75 * Math.Exp(-Math.Pow(dist / (_size * 1.3), 2));
                    if (dist < _size * 0.75 && dist < best)
                    {
                        best = dist;
                        hover = t;
                    }
                }
                t.Mag += (target - t.Mag) * Math.Min(1, dt * 16);

                double lift = (t.Mag - 1) * _size * 0.4;
                double vv = v + lift - hide;
                var pt = Map(u, vv);
                t.X = pt.X;
                t.Y = pt.Y;

                double edgeFade = Math.Clamp((vv + _size * 0.35) / (_size * 0.9), 0, 1);
                double alpha = edgeFade * Math.Clamp(_reveal * 1.2, 0, 1);

                Canvas.SetLeft(t.Root, pt.X - _size / 2);
                Canvas.SetTop(t.Root, pt.Y - _size / 2);
                t.Scale.ScaleX = t.Scale.ScaleY = t.Mag;
                t.Root.Opacity = alpha;

                if (t.Label != null)
                {
                    if (!fan || alpha < 0.05)
                    {
                        t.Label.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        if (t.Label.Visibility != Visibility.Visible)
                        {
                            t.Label.Visibility = Visibility.Visible;
                            if (t.LabelW <= 0)
                            {
                                t.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                                t.LabelW = t.Label.DesiredSize.Width;
                                t.LabelH = t.Label.DesiredSize.Height;
                            }
                        }
                        double half = _size * t.Mag / 2;
                        double lx = _side == "left" ? pt.X + half + 10 : pt.X - half - 10 - t.LabelW;
                        Canvas.SetLeft(t.Label, lx);
                        Canvas.SetTop(t.Label, pt.Y - t.LabelH / 2);
                        t.Label.Opacity = alpha;
                    }
                }
            }

            if (mouse != null || _hover == null || Now - _keyAt > 1.5) _hover = hover;
            if (_hover != null) _focus = _hover.Index;
        }

        void Chrome()
        {
            double alpha = Math.Clamp(_reveal * 1.2, 0, 1);

            crumb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var cs = crumb.DesiredSize;
            const double m = 14;
            double cx = _side == "right" ? Width - m - cs.Width : m;
            double cy = _side == "bottom" ? Height - m - cs.Height : m;
            Canvas.SetLeft(crumb, cx);
            Canvas.SetTop(crumb, cy);
            crumb.Opacity = alpha;

            if (emptyText.Visibility == Visibility.Visible)
            {
                emptyText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var p = Map(0, _apex);
                Canvas.SetLeft(emptyText, p.X - emptyText.DesiredSize.Width / 2);
                Canvas.SetTop(emptyText, p.Y - emptyText.DesiredSize.Height / 2);
                emptyText.Opacity = alpha;
            }

            Tile? show = _hover;
            if (show == null && Now - _keyAt < 1.5 && _cur != null && _focus >= 0 && _focus < _cur.Tiles.Count) show = _cur.Tiles[_focus];
            bool pill = show != null && show.Shown && !(FanLabels && show.Label != null);

            if (!pill)
            {
                hoverPill.Opacity = Math.Max(0, hoverPill.Opacity - 0.2);
                return;
            }

            if (hoverText.Text != show!.Item.Name) hoverText.Text = show.Item.Name;
            hoverPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ps = hoverPill.DesiredSize;
            double half = _size * show.Mag / 2;
            double x, y;
            switch (_side)
            {
                case "left":
                    x = show.X + half + 12;
                    y = show.Y - ps.Height / 2;
                    break;
                case "top":
                    x = show.X - ps.Width / 2;
                    y = show.Y + half + 12;
                    break;
                case "bottom":
                    x = show.X - ps.Width / 2;
                    y = show.Y - half - 12 - ps.Height;
                    break;
                default:
                    x = show.X - half - 12 - ps.Width;
                    y = show.Y - ps.Height / 2;
                    break;
            }
            x = Math.Clamp(x, 4, Math.Max(4, Width - ps.Width - 4));
            y = Math.Clamp(y, 4, Math.Max(4, Height - ps.Height - 4));
            Canvas.SetLeft(hoverPill, x);
            Canvas.SetTop(hoverPill, y);
            hoverPill.Opacity = Math.Min(alpha, hoverPill.Opacity + 0.25);
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(root);
            _press = _hover;
            _dragging = true;
            _coasting = false;
            _snapPending = false;
            _vel = 0;
            _travel = 0;
            _lastPointer = PointerAngle(p);
            _lastMoveT = Now;
            root.CaptureMouse();
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(root);
            double a = PointerAngle(p);
            double d = a - _lastPointer;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            _lastPointer = a;
            _travel += Math.Abs(d);

            var (min, max) = Range();
            if (_phi + d < min || _phi + d > max) d *= 0.35;
            _phi += d;
            _target = _phi;

            double t = Now;
            double dt = t - _lastMoveT;
            if (dt > 0.001)
            {
                _vel = _vel * 0.5 + d / dt * 0.5;
                _lastMoveT = t;
            }
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            root.ReleaseMouseCapture();
            e.Handled = true;

            if (_travel < 1.5)
            {
                _vel = 0;
                _target = Snap(_phi);
                if (_press != null) Open(_press);
                return;
            }

            if (Now - _lastMoveT > 0.08) _vel = 0;
            _vel = Math.Clamp(_vel, -900, 900);
            if (Math.Abs(_vel) > 60)
            {
                _coasting = true;
            }
            else
            {
                _vel = 0;
                _target = Snap(_phi);
            }
        }

        void OnRight(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var t = _hover;
            if (t == null || string.IsNullOrEmpty(t.Item.Path)) return;
            if (IsContainer(t.Item)) Reveal(t.Item.Path, false);
            else Reveal(t.Item.Path, true);
            HideDock();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Delta < 0) ZoomOut(false);
                else ZoomIn();
                return;
            }
            if (_coasting)
            {
                _coasting = false;
                _target = _phi;
            }
            _target += e.Delta / 120.0 * _step;
            var (min, max) = Range();
            _target = Math.Clamp(_target, min - _step * 0.3, max + _step * 0.3);
            _snapPending = true;
            _snapAt = Now + 0.16;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (_cur == null) return;
            int n = _cur.Tiles.Count;
            bool forward = Vertical ? e.Key == Key.Down : e.Key == Key.Right;
            bool backward = Vertical ? e.Key == Key.Up : e.Key == Key.Left;
            bool outward = _side switch
            {
                "left" => e.Key == Key.Left,
                "top" => e.Key == Key.Up,
                "bottom" => e.Key == Key.Down,
                _ => e.Key == Key.Right
            };

            if (e.Key == Key.Escape)
            {
                HideDock();
            }
            else if (forward || backward || e.Key == Key.Tab)
            {
                if (n == 0) return;
                _keyAt = Now;
                if (_focus < 0) _focus = Nearest();
                else _focus = Math.Clamp(_focus + (backward ? -1 : 1), 0, n - 1);
                _hover = _cur.Tiles[_focus];
                Follow(_focus);
            }
            else if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (_focus < 0) _focus = Nearest();
                if (_focus >= 0 && _focus < n) Open(_cur.Tiles[_focus]);
            }
            else if (e.Key == Key.Back || outward)
            {
                ZoomOut(true);
            }
            else
            {
                return;
            }
            e.Handled = true;
        }

        int Nearest()
        {
            if (_cur == null || _cur.Tiles.Count == 0) return -1;
            int i = (int)Math.Round(-_phi / _step);
            return Math.Clamp(i, 0, _cur.Tiles.Count - 1);
        }

        void Follow(int index)
        {
            double theta = index * _step + _target;
            double limit = _v * 0.6;
            if (theta > limit) _target -= theta - limit;
            if (theta < -limit) _target += -limit - theta;
            _target = Snap(_target);
        }
    }
}
