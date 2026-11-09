using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    public partial class Wheel : Window
    {
        sealed class Slot
        {
            public WheelItem Item = null!;
            public Border Face = null!;
            public ScaleTransform Scale = null!;
            public RotateTransform Counter = null!;
            public int Index;
        }

        const double Radius = 160;
        const double Center = 220;
        const double HubRadius = 86;

        readonly AppCatalog _cat;
        readonly List<Slot> _slots = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();

        double _angle, _vel, _target, _last, _lastPointer, _lastMoveT, _travel;
        bool _dragging, _snapping, _running, _closing, _pressHub;
        int _selected = -1;
        Slot? _pressSlot;

        internal Wheel(AppCatalog catalog)
        {
            InitializeComponent();
            _cat = catalog;

            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, false);
            Deactivated += (_, _) => Dismiss();
            PreviewKeyDown += OnKey;

            root.MouseLeftButtonDown += OnDown;
            root.MouseMove += OnMove;
            root.MouseLeftButtonUp += OnUp;
            root.MouseWheel += OnWheel;
            root.MouseRightButtonUp += (_, _) => Dismiss();
        }

        double Now => _clock.Elapsed.TotalSeconds;
        double Step => _slots.Count == 0 ? 360 : 360.0 / _slots.Count;
        double Snap(double a) => Math.Round(a / Step) * Step;

        public void ShowAtCursor()
        {
            if (IsVisible && !_closing)
            {
                Dismiss();
                return;
            }
            _closing = false;

            Build();
            _selected = -1;
            _angle = _slots.Count > 1 ? Step * 0.75 : 0;
            _vel = 0;
            _target = 0;
            _snapping = true;
            _dragging = false;

            root.Opacity = 0;
            Native.GetCursorPos(out var p);
            Show();

            var h = new WindowInteropHelper(this).Handle;
            const uint flags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
            Native.SetWindowPos(h, IntPtr.Zero, p.X, p.Y, 0, 0, flags);
            Native.GetWindowRect(h, out var r);
            var wa = Native.WorkAreaAt(p.X, p.Y);
            int x = Math.Clamp(p.X - r.Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - r.Width));
            int y = Math.Clamp(p.Y - r.Height / 2, wa.Top, Math.Max(wa.Top, wa.Bottom - r.Height));
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, flags);

            Activate();
            Focus();

            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

            Start();
            Render();
        }

        public void Dismiss()
        {
            if (!IsVisible || _closing) return;
            _closing = true;
            _dragging = false;
            if (root.IsMouseCaptured) root.ReleaseMouseCapture();

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) =>
            {
                if (!_closing) return;
                _closing = false;
                Stop();
                Hide();
            };
            root.BeginAnimation(OpacityProperty, fade);
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)));
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)));
        }

        void Build()
        {
            spin.Children.Clear();
            _slots.Clear();

            var items = _cat.WheelItems(12);
            double step = items.Count == 0 ? 360 : 360.0 / items.Count;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                double a = i * step * Math.PI / 180;
                double cx = Center + Radius * Math.Sin(a);
                double cy = Center - Radius * Math.Cos(a);

                var scale = new ScaleTransform(1, 1);
                var counter = new RotateTransform(0);
                var tg = new TransformGroup();
                tg.Children.Add(scale);
                tg.Children.Add(counter);

                var inner = new Grid();
                var icon = _cat.Icon(item.Path);
                if (icon != null)
                {
                    inner.Children.Add(new Image { Source = icon, Width = 34, Height = 34 });
                }
                else
                {
                    var t = new TextBlock { Text = "", FontSize = 20 };
                    t.Style = (Style)FindResource("Icon");
                    inner.Children.Add(t);
                }
                if (item.Kind == "favorite")
                {
                    var star = new TextBlock
                    {
                        Text = "",
                        FontSize = 9,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 8, 8),
                        FontFamily = (FontFamily)FindResource("IconFont")
                    };
                    star.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                    inner.Children.Add(star);
                }

                var face = new Border
                {
                    Width = 62,
                    Height = 62,
                    CornerRadius = new CornerRadius(31),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = Brushes.Transparent,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = tg,
                    Child = inner,
                    ToolTip = item.Name
                };

                var slot = new Slot { Item = item, Face = face, Scale = scale, Counter = counter, Index = i };
                face.Tag = slot;
                Canvas.SetLeft(face, cx - 31);
                Canvas.SetTop(face, cy - 31);
                spin.Children.Add(face);
                _slots.Add(slot);
            }

            if (items.Count == 0)
            {
                hubIcon.Source = null;
                hubIcon.Visibility = Visibility.Collapsed;
                hubName.Text = "nothing here yet";
                hubKind.Text = "add apps from the panel";
                hubHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                hubIcon.Visibility = Visibility.Visible;
                hubHint.Visibility = Visibility.Visible;
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

        void Tick(object? sender, EventArgs e)
        {
            double now = Now;
            double dt = Math.Min(now - _last, 1.0 / 30.0);
            _last = now;
            if (dt <= 0) return;

            if (!_dragging)
            {
                if (_snapping)
                {
                    const double k = 210;
                    double c = 2 * Math.Sqrt(k) * 0.9;
                    double acc = k * (_target - _angle) - c * _vel;
                    _vel += acc * dt;
                    _angle += _vel * dt;
                    if (Math.Abs(_target - _angle) < 0.02 && Math.Abs(_vel) < 1)
                    {
                        _angle = _target;
                        _vel = 0;
                    }
                }
                else
                {
                    _angle += _vel * dt;
                    _vel *= Math.Exp(-2.4 * dt);
                    if (Math.Abs(_vel) < 80)
                    {
                        _snapping = true;
                        _target = Snap(_angle + _vel * 0.15);
                    }
                }
            }

            Render();
        }

        void Render()
        {
            rot.Angle = _angle;
            foreach (var s in _slots) s.Counter.Angle = -_angle;

            int n = _slots.Count;
            if (n == 0) return;
            int sel = ((int)Math.Round(-_angle / Step) % n + n) % n;
            if (sel == _selected) return;
            _selected = sel;

            foreach (var s in _slots)
            {
                bool on = s.Index == sel;
                if (on)
                {
                    s.Face.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
                    s.Face.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                }
                else
                {
                    s.Face.BorderBrush = Brushes.Transparent;
                    s.Face.Background = Brushes.Transparent;
                }
                var to = on ? 1.16 : 1.0;
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                s.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                s.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }

            var item = _slots[sel].Item;
            hubIcon.Source = _cat.Icon(item.Path);
            hubName.Text = item.Name;
            hubKind.Text = item.Kind switch
            {
                "favorite" => "favorite",
                "project" => "one of yours",
                _ => "you use this a lot"
            };
        }

        static double PointerAngle(Point p) => Math.Atan2(p.X - 240, -(p.Y - 240)) * 180 / Math.PI;

        static double Dist(Point p) => Math.Sqrt((p.X - 240) * (p.X - 240) + (p.Y - 240) * (p.Y - 240));

        static Slot? SlotFrom(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is Border b && b.Tag is Slot s) return s;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(root);
            double dist = Dist(p);
            _pressSlot = SlotFrom(e.OriginalSource);
            _pressHub = dist < HubRadius;

            if (dist >= HubRadius && dist <= 222)
            {
                _dragging = true;
                _snapping = false;
                _vel = 0;
                _travel = 0;
                _lastPointer = PointerAngle(p);
                _lastMoveT = Now;
                root.CaptureMouse();
            }
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
            _angle += d;
            _travel += Math.Abs(d);

            double t = Now;
            double dt = t - _lastMoveT;
            if (dt > 0.001)
            {
                _vel = _vel * 0.6 + (d / dt) * 0.4;
                _lastMoveT = t;
            }
            Render();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(root);
            if (_dragging)
            {
                _dragging = false;
                root.ReleaseMouseCapture();

                if (_travel < 3)
                {
                    _vel = 0;
                    _snapping = true;
                    _target = Snap(_angle);
                    if (_pressSlot != null) Launch(_pressSlot.Item);
                    return;
                }

                if (Now - _lastMoveT > 0.08) _vel = 0;
                _vel = Math.Clamp(_vel, -1800, 1800);
                _snapping = Math.Abs(_vel) < 80;
                if (_snapping) _target = Snap(_angle);
            }
            else if (_pressHub && Dist(p) < HubRadius)
            {
                LaunchSelected();
            }
            e.Handled = true;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            Nudge(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        void Nudge(int dir)
        {
            if (_slots.Count == 0) return;
            double baseAngle = _snapping ? _target : Snap(_angle);
            _snapping = true;
            _target = baseAngle + dir * Step;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Dismiss();
                    break;
                case Key.Left:
                case Key.Up:
                    Nudge(1);
                    break;
                case Key.Right:
                case Key.Down:
                case Key.Tab:
                    Nudge(-1);
                    break;
                case Key.Enter:
                case Key.Space:
                    LaunchSelected();
                    break;
                default:
                    if (e.Key >= Key.D1 && e.Key <= Key.D9)
                    {
                        int i = e.Key - Key.D1;
                        if (i < _slots.Count) Launch(_slots[i].Item);
                    }
                    else
                    {
                        return;
                    }
                    break;
            }
            e.Handled = true;
        }

        void LaunchSelected()
        {
            if (_selected >= 0 && _selected < _slots.Count) Launch(_slots[_selected].Item);
        }

        void Launch(WheelItem item)
        {
            Dismiss();
            _cat.Launch(item);
        }
    }
}
