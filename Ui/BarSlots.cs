using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    internal sealed partial class Bar
    {
        public static readonly (string id, string label, string side)[] SlotDefaults =
        {
            ("desktops", "Desktop dots", "left"),
            ("ext", "Extension stuff", "right"),
            ("stats", "CPU / GPU / RAM", "right"),
            ("clip", "Clipboard", "right"),
            ("focus", "Focus mode", "right"),
            ("lock", "Cursor lock", "right"),
            ("mic", "Mic muted", "right"),
            ("scratch", "Scratchpad", "right"),
            ("download", "Last download", "right"),
            ("volume", "Volume", "right"),
            ("battery", "Battery", "right"),
            ("bell", "Notifications", "right"),
            ("clock", "Clock", "right"),
            ("shelf", "Shelf button", "right")
        };

        readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _desk = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        readonly Dictionary<string, FrameworkElement> _slots = new();
        Border? _dlChip;
        int _deskCount = -1, _deskCur = -1;
        bool _selfDrag;

        public event Action<int>? DeskClicked;
        public event Action<Bar, double>? ScratchClicked;
        public event Action? LastDownloadClicked;
        public event Action? LastDownloadDrag;

        public static List<(string id, string side)> SlotLayout(Settings s)
        {
            var list = new List<(string id, string side)>();
            foreach (var entry in s.BarLayout)
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                string id = parts[0], side = parts[1];
                if (!SlotDefaults.Any(d => d.id == id) || list.Any(x => x.id == id)) continue;
                if (side is not ("left" or "right" or "off")) continue;
                list.Add((id, side));
            }
            foreach (var d in SlotDefaults)
                if (!list.Any(x => x.id == d.id)) list.Add((d.id, d.side));
            return list;
        }

        FrameworkElement SlotIcon(string glyph, string tip, Action<FrameworkElement> click)
        {
            var t = Kit.Icon(glyph, 12.5);
            var host = new Border { Child = t, Background = Brushes.Transparent, Padding = new Thickness(5, 2, 5, 2), Cursor = Cursors.Hand, ToolTip = tip };
            host.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click(host);
            };
            return host;
        }

        void BuildSlots(FrameworkElement ext, FrameworkElement stats, FrameworkElement clip, FrameworkElement focus, FrameworkElement lockIcon, FrameworkElement volume, FrameworkElement battery, FrameworkElement bell, FrameworkElement clock, FrameworkElement shelf)
        {
            _slots["desktops"] = _desk;
            _slots["ext"] = ext;
            _slots["stats"] = stats;
            _slots["clip"] = clip;
            _slots["focus"] = focus;
            _slots["lock"] = lockIcon;
            _slots["mic"] = MicSlot();
            _slots["scratch"] = SlotIcon("\uE70B", "scratchpad", host => ScratchClicked?.Invoke(this, CenterX(host)));
            _slots["download"] = DownloadChip();
            _slots["volume"] = volume;
            _slots["battery"] = battery;
            _slots["bell"] = bell;
            _slots["clock"] = clock;
            _slots["shelf"] = shelf;
            Arrange();
        }

        public void Arrange()
        {
            foreach (var el in _slots.Values)
                if (el.Parent is Panel p) p.Children.Remove(el);
            foreach (var (id, side) in SlotLayout(_s))
            {
                if (side == "off" || !_slots.TryGetValue(id, out var el)) continue;
                (side == "left" ? _left : _right).Children.Add(el);
            }
        }

        public void SetDesks(int count, int current)
        {
            if (count == _deskCount && current == _deskCur) return;
            bool grew = count != _deskCount;
            _deskCount = count;
            _deskCur = current;
            _desk.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (grew)
            {
                _desk.Children.Clear();
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    var dot = new Border { Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var hit = new Border { Child = dot, Padding = new Thickness(2.5, 6, 2.5, 6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "desktop " + (i + 1) };
                    hit.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        DeskClicked?.Invoke(index);
                    };
                    _desk.Children.Add(hit);
                }
            }
            for (int i = 0; i < _desk.Children.Count; i++)
            {
                if (_desk.Children[i] is not Border hit || hit.Child is not Border dot) continue;
                bool on = i == current;
                dot.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "Faint");
                double to = on ? 16 : 6;
                if (double.IsNaN(dot.Width) || grew) dot.Width = to;
                else dot.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(260 * Theme.Speed)) { EasingFunction = new SpringEase() });
            }
        }

        Border? _mic;
        public event Action? MicClicked;

        FrameworkElement MicSlot()
        {
            var t = Kit.Icon("\uE720", 12.5);
            t.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x4E));
            _mic = new Border { Child = t, Background = Brushes.Transparent, Padding = new Thickness(5, 2, 5, 2), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, ToolTip = "mic is muted. click to unmute" };
            _mic.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                MicClicked?.Invoke();
            };
            return _mic;
        }

        public void SetMic(bool muted)
        {
            if (_mic != null) _mic.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement DownloadChip()
        {
            var t = Kit.Icon("\uE896", 12.5);
            _dlChip = new Border { Child = t, Background = Brushes.Transparent, Padding = new Thickness(5, 2, 5, 2), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
            Point down = default;
            bool pressed = false;
            _dlChip.PreviewMouseLeftButtonDown += (_, e) =>
            {
                pressed = true;
                down = e.GetPosition(this);
            };
            _dlChip.MouseLeftButtonDown += (_, e) => e.Handled = true;
            _dlChip.MouseMove += (_, e) =>
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - down.X) < 3 && Math.Abs(p.Y - down.Y) < 3) return;
                pressed = false;
                _selfDrag = true;
                try
                {
                    LastDownloadDrag?.Invoke();
                }
                finally
                {
                    _selfDrag = false;
                }
            };
            _dlChip.MouseLeftButtonUp += (_, e) =>
            {
                if (!pressed) return;
                pressed = false;
                e.Handled = true;
                LastDownloadClicked?.Invoke();
            };
            return _dlChip;
        }

        public void SetLastDownload(string name)
        {
            if (_dlChip == null) return;
            _dlChip.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dlChip.ToolTip = name.Length > 0 ? name + "\nclick to open, drag it anywhere" : null;
        }
    }
}
