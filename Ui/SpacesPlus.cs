using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace Elysium
{
    public sealed class SpaceLink
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
}

namespace Elysium.Ui
{
    internal sealed partial class Spaces
    {
        const double GridStep = 28;

        readonly Canvas _links = new() { IsHitTestVisible = false };
        readonly Canvas _mini = new() { Width = 180, Height = 112, ClipToBounds = true, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Border? _miniHost;
        SpaceItem? _linkFrom;
        Shapes.Path? _ghost;
        Rect _miniWorld;
        double _miniScale = 1;
        bool _miniDrag;

        bool GridOn => _s.SpaceGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        static double SnapTo(double v) => Math.Round(v / GridStep) * GridStep;

        static bool IsPicture(string path)
        {
            if (!File.Exists(path)) return false;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff";
        }

        static void SizeFor(SpaceItem it)
        {
            if (it.Kind == "file" && IsPicture(it.Path))
            {
                it.W = 240;
                it.H = 170;
            }
        }

        FrameworkElement Picture(SpaceItem it)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 30, 31, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                ToolTip = it.Path,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            var grid = new Grid();
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            grid.Children.Add(img);
            string path = it.Path;
            Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = 640;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        img.Source = bmp;
                        img.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                    }));
                }
                catch
                {
                    Dispatcher.BeginInvoke(new Action(() => _cat.Icons.Get(path, 96, false, src => img.Source = src)));
                }
            });
            var shade = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 34,
                Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xA0, 0, 0, 0), 90),
                IsHitTestVisible = false
            };
            var name = new TextBlock
            {
                Text = System.IO.Path.GetFileName(path),
                FontSize = 11.5,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 22, 7),
                IsHitTestVisible = false
            };
            grid.Children.Add(shade);
            grid.Children.Add(name);
            _details.Add((shade, 0.45));
            _details.Add((name, 0.45));
            grid.Children.Add(Grip(it));
            b.Child = grid;
            return b;
        }

        static Point Center(SpaceItem it) => new(it.X + it.W / 2, it.Y + it.H / 2);

        static Point Edge(SpaceItem it, Point toward)
        {
            var c = Center(it);
            double dx = toward.X - c.X, dy = toward.Y - c.Y;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return c;
            double hw = it.W / 2 + 6, hh = it.H / 2 + 6;
            double sx = Math.Abs(dx) > 0.01 ? hw / Math.Abs(dx) : double.MaxValue;
            double sy = Math.Abs(dy) > 0.01 ? hh / Math.Abs(dy) : double.MaxValue;
            double s = Math.Min(sx, sy);
            return new Point(c.X + dx * s, c.Y + dy * s);
        }

        static Geometry Arrow(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return Geometry.Empty;
            double nx = -dy / len, ny = dx / len;
            double bend = Math.Min(60, len * 0.12);
            var ctrl = new Point((a.X + b.X) / 2 + nx * bend, (a.Y + b.Y) / 2 + ny * bend);
            var fig = new PathFigure { StartPoint = a, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new QuadraticBezierSegment(ctrl, b, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            double tx = b.X - ctrl.X, ty = b.Y - ctrl.Y;
            double tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl > 0.01)
            {
                tx /= tl;
                ty /= tl;
                const double head = 11, wide = 6;
                var left = new Point(b.X - tx * head + -ty * wide, b.Y - ty * head + tx * wide);
                var right = new Point(b.X - tx * head - -ty * wide, b.Y - ty * head - tx * wide);
                var tip = new PathFigure { StartPoint = left, IsClosed = false, IsFilled = false };
                tip.Segments.Add(new LineSegment(b, true));
                tip.Segments.Add(new LineSegment(right, true));
                g.Figures.Add(tip);
            }
            g.Freeze();
            return g;
        }

        void DrawLinks()
        {
            _links.Children.Clear();
            if (_cur.Links.Count == 0) return;
            var byId = _cur.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var stroke = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255));
            stroke.Freeze();
            foreach (var l in _cur.Links)
            {
                if (!byId.TryGetValue(l.From, out var a) || !byId.TryGetValue(l.To, out var b)) continue;
                var pa = Edge(a, Center(b));
                var pb = Edge(b, Center(a));
                _links.Children.Add(new Shapes.Path { Data = Arrow(pa, pb), Stroke = stroke, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            }
        }

        void AttachLinks()
        {
            if (_links.Parent is Panel p) p.Children.Remove(_links);
            Panel.SetZIndex(_links, 1);
            _world.Children.Add(_links);
            DrawLinks();
        }

        void StartLink(SpaceItem from)
        {
            EndLink();
            _linkFrom = from;
            _ghost = new Shapes.Path { Stroke = new SolidColorBrush(Theme.Accent), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false };
            Panel.SetZIndex(_ghost, 1);
            _world.Children.Add(_ghost);
            Say?.Invoke("click another item to point at it. esc cancels");
        }

        void GhostTo(Point world)
        {
            if (_linkFrom == null || _ghost == null) return;
            var a = Edge(_linkFrom, world);
            _ghost.Data = Arrow(a, world);
        }

        void EndLink()
        {
            _linkFrom = null;
            if (_ghost != null) _world.Children.Remove(_ghost);
            _ghost = null;
        }

        bool Linking(SpaceItem target)
        {
            var from = _linkFrom;
            if (from == null) return false;
            EndLink();
            if (from == target) return true;
            bool exists = _cur.Links.Any(l => (l.From == from.Id && l.To == target.Id) || (l.From == target.Id && l.To == from.Id));
            if (!exists) _cur.Links.Add(new SpaceLink { From = from.Id, To = target.Id });
            DrawLinks();
            UpdateMini();
            Sfx.Play("tick");
            Touch();
            return true;
        }

        void Unlink(SpaceItem it)
        {
            if (_cur.Links.RemoveAll(l => l.From == it.Id || l.To == it.Id) > 0)
            {
                DrawLinks();
                Touch();
            }
        }

        void BuildMini(Grid overGrid)
        {
            _miniHost = new Border
            {
                Child = _mini,
                Width = 182,
                Height = 114,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x70, 12, 13, 16)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 18, 40),
                ClipToBounds = true,
                ToolTip = "click or drag to move around"
            };
            _mini.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _miniDrag = true;
                _mini.CaptureMouse();
                MiniJump(e.GetPosition(_mini));
            };
            _mini.MouseMove += (_, e) =>
            {
                if (_miniDrag && e.LeftButton == MouseButtonState.Pressed) MiniJump(e.GetPosition(_mini));
            };
            _mini.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _miniDrag = false;
                _mini.ReleaseMouseCapture();
                Touch();
            };
            overGrid.Children.Add(_miniHost);
        }

        void MiniJump(Point p)
        {
            if (_miniScale <= 0) return;
            double wx = _miniWorld.X + p.X / _miniScale;
            double wy = _miniWorld.Y + p.Y / _miniScale;
            double z = _scale.ScaleX;
            _pan.X = _viewport.ActualWidth / 2 - wx * z;
            _pan.Y = _viewport.ActualHeight / 2 - wy * z;
            Apply();
        }

        void UpdateMini()
        {
            if (_miniHost == null) return;
            bool show = _s.SpaceMinimap && _cur.Items.Count > 2;
            _miniHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            double z = _scale.ScaleX;
            double vw = Math.Max(1, _viewport.ActualWidth), vh = Math.Max(1, _viewport.ActualHeight);
            var view = new Rect(-_pan.X / z, -_pan.Y / z, vw / z, vh / z);
            var all = view;
            foreach (var it in _cur.Items) all.Union(new Rect(it.X, it.Y, Math.Max(1, it.W), Math.Max(1, it.H)));
            all.Inflate(all.Width * 0.04 + 20, all.Height * 0.04 + 20);
            double s = Math.Min(_mini.Width / all.Width, _mini.Height / all.Height);
            _miniScale = s;
            _miniWorld = new Rect(all.X - (_mini.Width / s - all.Width) / 2, all.Y - (_mini.Height / s - all.Height) / 2, _mini.Width / s, _mini.Height / s);
            _mini.Children.Clear();
            var accent = Theme.Accent;
            foreach (var it in _cur.Items)
            {
                bool group = it.Kind == "group";
                var r = new Shapes.Rectangle
                {
                    Width = Math.Max(2, it.W * s),
                    Height = Math.Max(2, it.H * s),
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = new SolidColorBrush(group ? Color.FromArgb(0x22, 255, 255, 255) : it.Kind == "note" ? Color.FromArgb(0xB0, 255, 214, 10) : IsPicture(it.Path) ? Color.FromArgb(0xC0, accent.R, accent.G, accent.B) : Color.FromArgb(0x90, 255, 255, 255)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(r, (it.X - _miniWorld.X) * s);
                Canvas.SetTop(r, (it.Y - _miniWorld.Y) * s);
                _mini.Children.Add(r);
            }
            var frame = new Shapes.Rectangle
            {
                Width = Math.Max(4, view.Width * s),
                Height = Math.Max(4, view.Height * s),
                Stroke = new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
                RadiusX = 2,
                RadiusY = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, (view.X - _miniWorld.X) * s);
            Canvas.SetTop(frame, (view.Y - _miniWorld.Y) * s);
            _mini.Children.Add(frame);
        }

        void ToggleGrid()
        {
            _s.SpaceGrid = !_s.SpaceGrid;
            _s.Touch();
            Say?.Invoke(_s.SpaceGrid ? "snapping to the grid. hold alt to place freely" : "free placement");
            if (_s.SpaceGrid)
            {
                foreach (var it in _cur.Items)
                {
                    it.X = SnapTo(it.X);
                    it.Y = SnapTo(it.Y);
                    if (_els.TryGetValue(it, out var el))
                    {
                        Canvas.SetLeft(el, it.X);
                        Canvas.SetTop(el, it.Y);
                    }
                }
                DrawLinks();
                UpdateMini();
                Touch();
            }
        }
    }
}
