using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class DockPreview : Window
    {
        const double CardW = 220, ThumbH = 132;

        readonly Border _surface;
        readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        readonly DispatcherTimer _wait = new() { Interval = TimeSpan.FromMilliseconds(380) };
        readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(320) };
        readonly List<(Border slot, Thumb thumb)> _thumbs = new();
        string? _path;
        Rect _tile;
        string _side = "right";
        Display? _display;

        public event Action? Picked;

        public DockPreview()
        {
            Glass.Attach(this, 14, true);
            Title = "elysium dock preview";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(14);
            _surface.Child = _row;
            Content = _surface;
            _wait.Tick += (_, _) =>
            {
                _wait.Stop();
                Open();
            };
            _leave.Tick += (_, _) =>
            {
                _leave.Stop();
                if (!IsMouseOver) Close2();
            };
            MouseEnter += (_, _) => _leave.Stop();
            MouseLeave += (_, _) => _leave.Start();
        }

        public void Hover(string? path, Rect tile, string side)
        {
            if (path != null && path == _path && IsVisible) return;
            _wait.Stop();
            if (path == null)
            {
                _path = null;
                if (IsVisible) _leave.Start();
                return;
            }
            _leave.Stop();
            _path = path;
            _tile = tile;
            _side = side;
            if (IsVisible) Open();
            else _wait.Start();
        }

        List<AppWin> Windows()
        {
            string exe = WinList.ExeOf(_path);
            if (exe.Length == 0) return new List<AppWin>();
            return WinList.All().Where(w => w.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        }

        void Open()
        {
            var wins = _path == null ? new List<AppWin>() : Windows();
            if (wins.Count == 0)
            {
                Close2();
                return;
            }
            DropThumbs();
            _row.Children.Clear();
            foreach (var w in wins) _row.Children.Add(Card(w));
            _display = Displays.At((int)(_tile.X + _tile.Width / 2), (int)(_tile.Y + _tile.Height / 2));
            var d = _display;
            Glass.Present(this, _surface, () => Place(d), 0, 0.97, 180);
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void Place(Display d)
        {
            _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _surface.DesiredSize;
            int w = (int)Math.Ceiling(size.Width * d.Scale), h = (int)Math.Ceiling(size.Height * d.Scale);
            int gap = (int)(14 * d.Scale);
            int x, y;
            switch (_side)
            {
                case "left":
                    x = (int)_tile.Right + gap;
                    y = (int)(_tile.Y + _tile.Height / 2 - h / 2.0);
                    break;
                case "top":
                    x = (int)(_tile.X + _tile.Width / 2 - w / 2.0);
                    y = (int)_tile.Bottom + gap;
                    break;
                case "bottom":
                    x = (int)(_tile.X + _tile.Width / 2 - w / 2.0);
                    y = (int)_tile.Top - h - gap;
                    break;
                default:
                    x = (int)_tile.Left - w - gap;
                    y = (int)(_tile.Y + _tile.Height / 2 - h / 2.0);
                    break;
            }
            x = Math.Clamp(x, d.Work.Left + 6, Math.Max(d.Work.Left + 6, d.Work.Right - w - 6));
            y = Math.Clamp(y, d.Work.Top + 6, Math.Max(d.Work.Top + 6, d.Work.Bottom - h - 6));
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        FrameworkElement Card(AppWin w)
        {
            var box = new StackPanel { Width = CardW, Margin = new Thickness(5) };
            var slot = new Border { Height = ThumbH, CornerRadius = Kit.Round(8), Tag = w.Hwnd };
            slot.SetResourceReference(Border.BackgroundProperty, "TileBg");
            if (w.Min) slot.Opacity = 0.6;
            box.Children.Add(slot);
            var head = new DockPanel { Margin = new Thickness(2, 6, 0, 0) };
            var close = Kit.Plain("\uE8BB", () =>
            {
                WinList.Close(w.Hwnd);
                Dispatcher.BeginInvoke(new Action(Open), DispatcherPriority.Background);
            }, "close", 9);
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var title = Kit.Text(w.Title, 11.5);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(title);
            box.Children.Add(head);
            var card = new Border { Child = box, CornerRadius = Kit.Round(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(3) };
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "RowHover");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                WinList.Switch(w.Hwnd);
                Picked?.Invoke();
                Close2();
            };
            card.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                WinList.Close(w.Hwnd);
                Dispatcher.BeginInvoke(new Action(Open), DispatcherPriority.Background);
            };
            return card;
        }

        void PlaceThumbs()
        {
            if (!IsVisible) return;
            var me = new WindowInteropHelper(this).Handle;
            double scale = _display?.Scale ?? 1;
            foreach (var child in _row.Children.OfType<Border>())
            {
                if (child.Child is not StackPanel sp || sp.Children[0] is not Border slot || slot.Tag is not IntPtr src) continue;
                var thumb = new Thumb(me, src);
                if (!thumb.Valid)
                {
                    thumb.Dispose();
                    continue;
                }
                var p = slot.TranslatePoint(new Point(0, 0), _surface);
                var r = new Native.RECT
                {
                    Left = (int)Math.Round(p.X * scale) + 4,
                    Top = (int)Math.Round(p.Y * scale) + 4,
                    Right = (int)Math.Round((p.X + slot.ActualWidth) * scale) - 4,
                    Bottom = (int)Math.Round((p.Y + slot.ActualHeight) * scale) - 4
                };
                thumb.Place(r, 255);
                _thumbs.Add((slot, thumb));
            }
        }

        void DropThumbs()
        {
            foreach (var (_, t) in _thumbs) t.Dispose();
            _thumbs.Clear();
        }

        public void Close2()
        {
            _wait.Stop();
            _leave.Stop();
            _path = null;
            if (!IsVisible) return;
            DropThumbs();
            Glass.Dismiss(this, _surface, null, 120);
        }
    }
}
