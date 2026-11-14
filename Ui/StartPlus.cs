using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Heft
{
    public sealed class StartPin
    {
        public string Kind { get; set; } = "app";
        public string Name { get; set; } = "";
        public string Target { get; set; } = "";
        public bool Uwp { get; set; }
        public List<StartPin> Kids { get; set; } = new();

        public bool Folder => Kind == "folder";

        public string IconPath => Uwp ? "shell:AppsFolder\\" + Target : Target;

        public void Launch()
        {
            if (Uwp) Heft.Launch.Uwp(Target);
            else Heft.Launch.Run(Target);
        }
    }
}

namespace Heft.Ui
{
    internal sealed partial class StartScreen
    {
        sealed class OpenCard
        {
            public AppWin Win = null!;
            public Border Host = null!;
            public Border Frame = null!;
            public Thumb? Thumb;
        }

        const double PinW = 100, PinH = 92;

        readonly StackPanel _openRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        readonly List<OpenCard> _openCards = new();
        readonly Grid _folderLayer = new() { Visibility = Visibility.Collapsed };
        List<AppWin> _wins = new();
        Border? _mediaCard;
        WrapPanel? _pinWrap;
        StackPanel? _folderBody;
        StartPin? _folder;
        byte _thumbAlpha;

        StartPin? _drag;
        StartPin? _dragFrom;
        FrameworkElement? _dragTile;
        Point _dragStart;
        bool _dragging;
        Border? _dropMark;
        int _dropIndex = -1;
        StartPin? _dropOn;

        public Func<IReadOnlyDictionary<string, double>>? ScreenTime;
        public Func<string, string>? AppName;

        List<StartPin> ListOf(StartPin? folder) => folder?.Kids ?? _s.StartPins;

        void SeedPins()
        {
            if (_s.StartPinsSeeded || _data.Groups.Count == 0) return;
            _s.StartPins = _data.Groups.SelectMany(g => g.Pins).Where(p => p.Target.Length > 0)
                .Select(p => new StartPin { Name = p.Name, Target = p.Target, Uwp = p.Uwp }).ToList();
            _s.StartPinsSeeded = true;
            _s.Touch();
        }

        bool Pinned(string target) => _s.StartPins.Any(p => p.Target.Equals(target, StringComparison.OrdinalIgnoreCase) || p.Kids.Any(k => k.Target.Equals(target, StringComparison.OrdinalIgnoreCase)));

        void PinApp(string name, string target, bool uwp)
        {
            if (Pinned(target)) return;
            _s.StartPins.Add(new StartPin { Name = name, Target = target, Uwp = uwp });
            _s.Touch();
            _dirty = true;
        }

        void Unpin(string target)
        {
            _s.StartPins.RemoveAll(p => !p.Folder && p.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
            foreach (var f in _s.StartPins.Where(p => p.Folder).ToList())
            {
                f.Kids.RemoveAll(k => k.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
                Tidy(f);
            }
            _s.Touch();
        }

        void Tidy(StartPin folder)
        {
            if (!folder.Folder) return;
            int at = _s.StartPins.IndexOf(folder);
            if (at < 0) return;
            if (folder.Kids.Count == 0) _s.StartPins.RemoveAt(at);
            else if (folder.Kids.Count == 1) _s.StartPins[at] = folder.Kids[0];
        }

        void BuildGrid()
        {
            SeedPins();
            _main.Children.Add(Header("Pinned", "All apps  ›", () =>
            {
                _allApps = true;
                Build();
            }));
            if (_s.StartPins.Count == 0)
            {
                string msg = !_s.StartPinsSeeded && _data.Groups.Count == 0 ? "loading your Start menu pins..." : "nothing pinned yet. right-click an app in all apps and pin it";
                _main.Children.Add(new TextBlock { Text = msg, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), Margin = new Thickness(6, 0, 0, 18) });
                _pinWrap = null;
                return;
            }
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            foreach (var p in _s.StartPins) wrap.Children.Add(PinTile(p, null));
            _pinWrap = wrap;
            _main.Children.Add(wrap);
        }

        FrameworkElement PinTile(StartPin pin, StartPin? folder)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (pin.Folder)
            {
                var mini = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 2, Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                foreach (var k in pin.Kids.Take(4))
                {
                    var im = new Image { Width = 16, Height = 16, Margin = new Thickness(1.5) };
                    RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                    _cat.Icons.Get(k.IconPath, 32, true, src => im.Source = src);
                    mini.Children.Add(im);
                }
                var plate = new Border { Width = 46, Height = 46, CornerRadius = Kit.Round(12), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), Child = mini, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(plate);
            }
            else
            {
                var img = new Image { Width = 34, Height = 34, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var fallback = new TextBlock { Text = "\uE71D", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(fallback);
                g.Children.Add(img);
                _cat.Icons.Get(pin.IconPath, 96, true, src =>
                {
                    img.Source = src;
                    if (src != null) fallback.Visibility = Visibility.Collapsed;
                });
            }
            var label = new TextBlock { Text = pin.Name, FontSize = 11.5, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(6, 0, 6, 9), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = PinW - 12 };
            Grid.SetRow(label, 1);
            g.Children.Add(label);

            var tile = new Border
            {
                Width = PinW,
                Height = PinH,
                Margin = new Thickness(3),
                CornerRadius = Kit.Round(12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = g,
                Cursor = Cursors.Hand,
                ToolTip = pin.Folder ? pin.Name + " (" + pin.Kids.Count + ")" : pin.Name,
                Tag = pin
            };
            tile.MouseEnter += (_, _) =>
            {
                if (!_dragging) tile.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
            };
            tile.MouseLeave += (_, _) =>
            {
                if (_drag != pin) tile.Background = Brushes.Transparent;
            };
            tile.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _drag = pin;
                _dragFrom = folder;
                _dragTile = tile;
                _dragging = false;
                _dragStart = e.GetPosition(this);
                tile.CaptureMouse();
                e.Handled = true;
            };
            tile.MouseMove += (_, e) =>
            {
                if (_drag != pin || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (!_dragging)
                {
                    if (Math.Abs(p.X - _dragStart.X) < 6 && Math.Abs(p.Y - _dragStart.Y) < 6) return;
                    _dragging = true;
                    Panel.SetZIndex(tile, 50);
                    tile.Opacity = 0.85;
                }
                tile.RenderTransform = new TranslateTransform(p.X - _dragStart.X, p.Y - _dragStart.Y);
                Aim(p);
            };
            tile.MouseLeftButtonUp += (_, e) =>
            {
                if (_drag != pin) return;
                e.Handled = true;
                bool wasDrag = _dragging;
                Drop(pin, folder, wasDrag);
                tile.ReleaseMouseCapture();
                if (wasDrag) return;
                if (pin.Folder)
                {
                    OpenFolder(pin);
                    return;
                }
                Close2();
                pin.Launch();
            };
            tile.LostMouseCapture += (_, _) =>
            {
                if (_drag == pin && _dragging) Drop(pin, folder, true);
            };
            tile.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                GlassMenu.Show(this, PinMenu(pin, folder));
            };
            return tile;
        }

        Panel? DropPanel() => _folder != null && _folderBody != null && _folderBody.Children.Count > 1 ? _folderBody.Children[1] as Panel : _pinWrap;

        void Aim(Point inWindow)
        {
            ClearMark();
            _dropOn = null;
            _dropIndex = -1;
            var panel = DropPanel();
            if (panel == null || _drag == null) return;
            if (_folder != null && _folderBody != null)
            {
                var local = _folderBody.TranslatePoint(new Point(0, 0), this);
                var r = new Rect(local, new Size(_folderBody.ActualWidth, _folderBody.ActualHeight));
                if (!r.Contains(inWindow)) return;
            }
            var list = panel.Children.OfType<FrameworkElement>().ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var el = list[i];
                if (el == _dragTile || el.Tag is not StartPin target) continue;
                var tl = el.TranslatePoint(new Point(0, 0), this);
                var rect = new Rect(tl, new Size(el.ActualWidth, el.ActualHeight));
                if (!rect.Contains(inWindow)) continue;
                double fx = (inWindow.X - rect.Left) / rect.Width;
                bool center = fx > 0.28 && fx < 0.72;
                if (center && !_drag.Folder && _folder == null && target != _drag)
                {
                    _dropOn = target;
                    Mark(el, true);
                }
                else
                {
                    _dropIndex = ListOf(_folder).IndexOf(target) + (fx >= 0.5 ? 1 : 0);
                    Mark(el, false, fx >= 0.5);
                }
                return;
            }
        }

        void Mark(FrameworkElement el, bool merge, bool after = false)
        {
            if (el is not Border b) return;
            _dropMark = b;
            var accent = Theme.Accent;
            if (merge)
            {
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B));
                b.Background = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            }
            else
            {
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B));
                b.BorderThickness = after ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            }
        }

        void ClearMark()
        {
            if (_dropMark == null) return;
            _dropMark.BorderBrush = Brushes.Transparent;
            _dropMark.BorderThickness = new Thickness(1);
            _dropMark.Background = Brushes.Transparent;
            _dropMark = null;
        }

        void Drop(StartPin pin, StartPin? from, bool dragged)
        {
            var tile = _dragTile;
            _drag = null;
            _dragTile = null;
            _dragging = false;
            ClearMark();
            if (tile != null)
            {
                tile.RenderTransform = null;
                tile.Opacity = 1;
                Panel.SetZIndex(tile, 0);
            }
            if (!dragged) return;
            var src = ListOf(from);
            bool changed = false;
            if (_dropOn != null && !pin.Folder)
            {
                var target = _dropOn;
                src.Remove(pin);
                if (target.Folder) target.Kids.Add(pin);
                else
                {
                    int at = _s.StartPins.IndexOf(target);
                    if (at >= 0) _s.StartPins[at] = new StartPin { Kind = "folder", Name = "Folder", Kids = new List<StartPin> { target, pin } };
                }
                changed = true;
            }
            else if (_dropIndex >= 0)
            {
                var dest = ListOf(_folder);
                int old = dest.IndexOf(pin);
                int at = _dropIndex;
                if (old >= 0)
                {
                    dest.RemoveAt(old);
                    if (old < at) at--;
                }
                else src.Remove(pin);
                dest.Insert(Math.Clamp(at, 0, dest.Count), pin);
                changed = true;
            }
            else if (from != null && _folder != null && _folderBody != null)
            {
                Native.GetCursorPos(out var cur);
                var local = PointFromScreen(new Point(cur.X, cur.Y));
                var tl = _folderBody.TranslatePoint(new Point(0, 0), this);
                if (!new Rect(tl, new Size(_folderBody.ActualWidth, _folderBody.ActualHeight)).Contains(local))
                {
                    from.Kids.Remove(pin);
                    int at = _s.StartPins.IndexOf(from);
                    _s.StartPins.Insert(at < 0 ? _s.StartPins.Count : at + 1, pin);
                    Tidy(from);
                    CloseFolder();
                    changed = true;
                }
            }
            _dropOn = null;
            _dropIndex = -1;
            if (!changed) return;
            _s.Touch();
            Sfx.Play("tick");
            if (_folder != null) FillFolder();
            RebuildPinned();
        }

        void RebuildPinned()
        {
            if (_allApps) return;
            BuildMain();
        }

        List<MenuEntry> PinMenu(StartPin pin, StartPin? folder)
        {
            var list = new List<MenuEntry>();
            if (pin.Folder)
            {
                list.Add(MenuEntry.Item("\uE8DA", "Open", () => OpenFolder(pin)));
                list.Add(MenuEntry.Item("\uE8AC", "Rename", () => OpenFolder(pin, true)));
                list.Add(MenuEntry.Item("\uE8C6", "Ungroup", () =>
                {
                    int at = _s.StartPins.IndexOf(pin);
                    if (at < 0) return;
                    _s.StartPins.RemoveAt(at);
                    _s.StartPins.InsertRange(at, pin.Kids);
                    _s.Touch();
                    RebuildPinned();
                }));
                list.Add(MenuEntry.Sep());
                list.Add(MenuEntry.Item("\uE77A", "Unpin the whole folder", () =>
                {
                    _s.StartPins.Remove(pin);
                    _s.Touch();
                    RebuildPinned();
                }, true));
                return list;
            }
            list.Add(MenuEntry.Item("\uE768", "Open", () =>
            {
                Close2();
                pin.Launch();
            }));
            list.AddRange(AppMenu(pin.Name, pin.Target, pin.Uwp));
            if (folder != null)
            {
                list.Add(MenuEntry.Item("\uE8C6", "Take out of folder", () =>
                {
                    folder.Kids.Remove(pin);
                    int at = _s.StartPins.IndexOf(folder);
                    _s.StartPins.Insert(at < 0 ? _s.StartPins.Count : at + 1, pin);
                    Tidy(folder);
                    _s.Touch();
                    if (_s.StartPins.Contains(folder)) FillFolder();
                    else CloseFolder();
                    RebuildPinned();
                }));
            }
            return list;
        }

        List<MenuEntry> AppMenu(string name, string target, bool uwp)
        {
            var list = new List<MenuEntry>();
            if (!uwp)
            {
                list.Add(MenuEntry.Item("\uE7EF", "Run as admin", () =>
                {
                    Close2();
                    Launch.Admin(target);
                }));
                list.Add(MenuEntry.Item("\uE838", "Open file location", () =>
                {
                    Close2();
                    Files.Reveal(target);
                }));
            }
            bool pinned = Pinned(target);
            list.Add(pinned
                ? MenuEntry.Item("\uE77A", "Unpin from Start", () =>
                {
                    Unpin(target);
                    RebuildPinned();
                })
                : MenuEntry.Item("\uE718", "Pin to Start", () =>
                {
                    PinApp(name, target, uwp);
                    if (_allApps) return;
                    RebuildPinned();
                }));
            list.Add(MenuEntry.Sep());
            list.Add(MenuEntry.Item("\uE74D", "Uninstall", () =>
            {
                Close2();
                Launch.Run("ms-settings:appsfeatures");
            }, true));
            return list;
        }

        void OpenFolder(StartPin folder, bool rename = false)
        {
            _folder = folder;
            _folderLayer.Children.Clear();
            var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) };
            dim.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                CloseFolder();
            };
            _folderLayer.Children.Add(dim);
            _folderBody = new StackPanel { Width = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var card = new Border
            {
                CornerRadius = Kit.Round(22),
                Padding = new Thickness(22, 18, 22, 20),
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 28, 28, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = _folderBody,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _folderLayer.Children.Add(card);
            FillFolder();
            _folderLayer.Visibility = Visibility.Visible;
            Glass.FadeIn(card, 10, 200);
            if (rename && _folderBody.Children.Count > 0 && _folderBody.Children[0] is TextBox box)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    box.Focus();
                    box.SelectAll();
                }), DispatcherPriority.Input);
        }

        void FillFolder()
        {
            var folder = _folder;
            if (folder == null || _folderBody == null) return;
            _folderBody.Children.Clear();
            var name = new TextBox
            {
                Text = folder.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            name.LostKeyboardFocus += (_, _) =>
            {
                string n = name.Text.Trim();
                if (n.Length == 0 || n == folder.Name) return;
                folder.Name = n;
                _s.Touch();
                RebuildPinned();
            };
            name.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Keyboard.ClearFocus();
                Focus();
            };
            _folderBody.Children.Add(name);
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var k in folder.Kids) wrap.Children.Add(PinTile(k, folder));
            _folderBody.Children.Add(wrap);
            var hint = new TextBlock { Text = "drag something out of here to take it out of the folder", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            _folderBody.Children.Add(hint);
        }

        void CloseFolder()
        {
            if (_folder == null) return;
            _folder = null;
            _folderBody = null;
            _folderLayer.Visibility = Visibility.Collapsed;
            _folderLayer.Children.Clear();
        }

        void BuildOpenRow()
        {
            DropThumbs();
            _openCards.Clear();
            _openRow.Children.Clear();
            double width = ActualWidth > 0 ? ActualWidth : 1600;
            int max = Math.Max(3, (int)((width - 260) / 178));
            var wins = _wins.Take(max).ToList();
            foreach (var w in wins) _openRow.Children.Add(OpenTile(w));
            if (_wins.Count > max)
            {
                var more = new Border { Width = 92, Height = 118, CornerRadius = Kit.Round(12), Margin = new Thickness(4), Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)), Cursor = Cursors.Hand };
                var t = new TextBlock { Text = "+" + (_wins.Count - max) + "\nmore", FontSize = 13, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                more.Child = t;
                more.MouseLeftButtonUp += (_, _) =>
                {
                    Close2();
                    Overview?.Invoke();
                };
                _openRow.Children.Add(more);
            }
            _openRow.Visibility = _wins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        FrameworkElement OpenTile(AppWin w)
        {
            var card = new OpenCard { Win = w };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var frame = new Border { CornerRadius = Kit.Round(8), Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), Margin = new Thickness(6, 6, 6, 0) };
            if (w.Min || w.Path == null)
            {
                var icon = new Image { Width = 36, Height = 36, Opacity = 0.85 };
                if (w.Path != null) _cat.Icons.Get(w.Path, 64, true, src => icon.Source = src);
                frame.Child = icon;
            }
            g.Children.Add(frame);
            var label = new DockPanel { Margin = new Thickness(8, 6, 8, 7) };
            var small = new Image { Width = 15, Height = 15, Margin = new Thickness(0, 0, 6, 0) };
            if (w.Path != null) _cat.Icons.Get(w.Path, 32, true, src => small.Source = src);
            DockPanel.SetDock(small, Dock.Left);
            label.Children.Add(small);
            var title = new TextBlock { Text = w.Hung ? "not responding" : w.Title, FontSize = 11.5, Foreground = w.Hung ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)) : Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(title);
            Grid.SetRow(label, 1);
            g.Children.Add(label);
            var x = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(0xD0, 30, 30, 34)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Cursor = Cursors.Hand, Visibility = Visibility.Hidden, ToolTip = "close" };
            x.Child = new TextBlock { Text = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(x);
            var host = new Border
            {
                Width = 170,
                CornerRadius = Kit.Round(12),
                Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = g,
                Cursor = Cursors.Hand,
                ToolTip = w.Title
            };
            card.Host = host;
            card.Frame = frame;
            _openCards.Add(card);
            host.MouseEnter += (_, _) =>
            {
                host.Background = new SolidColorBrush(Color.FromArgb(0x34, 255, 255, 255));
                x.Visibility = Visibility.Visible;
            };
            host.MouseLeave += (_, _) =>
            {
                host.Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255));
                x.Visibility = Visibility.Hidden;
            };
            x.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                CloseCard(card);
            };
            host.MouseLeftButtonUp += (_, _) =>
            {
                Close2();
                WinList.Switch(w.Hwnd);
            };
            host.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                CloseCard(card);
            };
            host.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var items = new List<MenuEntry>
                {
                    MenuEntry.Item("\uE8A7", "Switch to it", () =>
                    {
                        Close2();
                        WinList.Switch(w.Hwnd);
                    }),
                    MenuEntry.Item(w.Min ? "\uE740" : "\uE921", w.Min ? "Restore" : "Minimize", () =>
                    {
                        Native.ShowWindow(w.Hwnd, w.Min ? Native.SW_RESTORE : Native.SW_MINIMIZE);
                        Refresh();
                    }),
                    MenuEntry.Item("\uE8BB", "Close", () => CloseCard(card)),
                    MenuEntry.Item("\uE711", "End task", () =>
                    {
                        WinList.Kill(w.Hwnd);
                        RemoveCard(card);
                    }, true)
                };
                GlassMenu.Show(this, items);
            };
            return host;
        }

        void CloseCard(OpenCard card)
        {
            WinList.Close(card.Win.Hwnd);
            RemoveCard(card);
        }

        void RemoveCard(OpenCard card)
        {
            card.Thumb?.Dispose();
            card.Thumb = null;
            _openCards.Remove(card);
            _openRow.Children.Remove(card.Host);
            _wins.Remove(card.Win);
            Sfx.Play("tick");
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void Refresh()
        {
            if (!_shown) return;
            _wins = WinList.All();
            BuildOpenRow();
            _dockRow.Children.Clear();
            BuildDock();
            Dispatcher.BeginInvoke(new Action(PlaceThumbs), DispatcherPriority.Loaded);
        }

        void PlaceThumbs()
        {
            if (!_shown) return;
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            Native.GetWindowRect(h, out var wr);
            foreach (var c in _openCards)
            {
                if (c.Win.Min || c.Win.Path == null) continue;
                if (c.Thumb == null)
                {
                    var t = new Thumb(h, c.Win.Hwnd);
                    if (!t.Valid) continue;
                    c.Thumb = t;
                }
                try
                {
                    if (!c.Frame.IsVisible || c.Frame.ActualWidth < 4) continue;
                    var tl = c.Frame.PointToScreen(new Point(3, 3));
                    var br = c.Frame.PointToScreen(new Point(c.Frame.ActualWidth - 3, c.Frame.ActualHeight - 3));
                    c.Thumb.Place(new Native.RECT { Left = (int)tl.X - wr.Left, Top = (int)tl.Y - wr.Top, Right = (int)br.X - wr.Left, Bottom = (int)br.Y - wr.Top }, _thumbAlpha);
                }
                catch
                {
                }
            }
        }

        void FadeThumbs(bool show)
        {
            Frames.Run((show ? 220 : 120) * Theme.Speed, t =>
            {
                _thumbAlpha = (byte)Math.Round(255 * (show ? t : 1 - t));
                PlaceThumbs();
            }, show ? null : DropThumbs, _openCards);
        }

        void DropThumbs()
        {
            foreach (var c in _openCards)
            {
                c.Thumb?.Dispose();
                c.Thumb = null;
            }
            if (!_shown) _openCards.Clear();
        }

        FrameworkElement MediaBody()
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
            ctl.Children.Add(Btn("\uE892", () => Media(_np.Previous)));
            ctl.Children.Add(Btn(_np.Playing ? "\uE769" : "\uE768", () => Media(_np.Toggle)));
            ctl.Children.Add(Btn("\uE893", () => Media(_np.Next)));
            sp.Children.Add(ctl);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            return g;
        }

        async void Media(Func<System.Threading.Tasks.Task> act)
        {
            try
            {
                await act();
                for (int i = 0; i < 4; i++)
                {
                    await System.Threading.Tasks.Task.Delay(i == 0 ? 180 : 350);
                    await _np.Refresh();
                    SongChanged();
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("start media failed " + ex.Message);
            }
        }

        string _songSig = "";

        public void SongChanged()
        {
            if (!_shown) return;
            bool has = _np.HasSession && _np.Title.Length > 0;
            string sig = has ? _np.Title + "|" + _np.Artist + "|" + _np.Playing + "|" + (_np.Art?.GetHashCode() ?? 0) : "";
            if (sig == _songSig) return;
            _songSig = sig;
            if (has && _mediaCard != null)
            {
                _mediaCard.Child = MediaBody();
                return;
            }
            if (has != (_mediaCard != null)) RefreshSide();
        }

        UIElement? ToolsCard()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var (glyph, tip, run) in new (string, string, Action)[]
                     {
                         ("\uE9D9", "task manager", () => Launch.Run("taskmgr.exe")),
                         ("\uE838", "file explorer", () => Launch.Run("explorer.exe")),
                         ("\uE713", "windows settings", () => Launch.Run("ms-settings:")),
                         ("\uE756", "terminal", () => Launch.Run(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe")) ? "wt.exe" : "cmd.exe")),
                         ("\uE7C3", "run", () => Launch.Run("explorer.exe", "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}")),
                         ("\uE8A9", "every window", () => Overview?.Invoke())
                     })
            {
                var act = run;
                var b = Btn(glyph, () =>
                {
                    Close2();
                    act();
                }, 16);
                b.ToolTip = tip;
                b.Width = 44;
                b.Height = 38;
                row.Children.Add(b);
            }
            return Card(row);
        }

        UIElement? ScreenTimeCard()
        {
            if (!_s.ScreenTimeCard || ScreenTime == null) return null;
            var data = ScreenTime();
            if (data.Count == 0) return null;
            var top = data.OrderByDescending(kv => kv.Value).Take(5).ToList();
            double total = data.Values.Sum();
            double max = Math.Max(1, top[0].Value);
            var sp = new StackPanel();
            var head = new DockPanel();
            var sum = W(Span(total), 12, false, 0xA8);
            DockPanel.SetDock(sum, Dock.Right);
            head.Children.Add(sum);
            head.Children.Add(W("Screen time today", 13, true, 0xC8));
            sp.Children.Add(head);
            var accent = Theme.Accent;
            foreach (var (exe, secs) in top)
            {
                var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.Children.Add(W(AppName?.Invoke(exe) ?? exe, 12));
                var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0xE0, accent.R, accent.G, accent.B)), HorizontalAlignment = HorizontalAlignment.Left };
                double frac = secs / max;
                track.SizeChanged += (_, e) => fill.Width = Math.Max(4, e.NewSize.Width * frac);
                track.Child = fill;
                Grid.SetColumn(track, 1);
                row.Children.Add(track);
                var v = W(Span(secs), 11.5, false, 0xA8);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(v, 2);
                row.Children.Add(v);
                sp.Children.Add(row);
            }
            return Card(sp);
        }

        static string Span(double secs)
        {
            var t = TimeSpan.FromSeconds(secs);
            if (t.TotalMinutes < 1) return "<1m";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + "m";
            return (int)t.TotalHours + "h " + t.Minutes + "m";
        }

        void Running(Pin pin, out List<AppWin> mine)
        {
            string exe = WinList.ExeOf(pin.Target);
            mine = exe.Length == 0 ? new List<AppWin>() : _wins.Where(w => w.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
