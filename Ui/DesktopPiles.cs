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

namespace Elysium.Ui
{
    internal sealed class DesktopPiles : Window
    {
        static readonly (string id, string label, string glyph)[] Kinds =
        {
            ("folder", "folders", "\uE8B7"),
            ("app", "apps", "\uE71D"),
            ("image", "images", "\uEB9F"),
            ("video", "videos", "\uE714"),
            ("audio", "music", "\uE8D6"),
            ("doc", "documents", "\uE8A5"),
            ("code", "code", "\uE943"),
            ("archive", "zips", "\uF012"),
            ("other", "other stuff", "\uE7C3")
        };

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly StackPanel _col = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 26, 24) };
        readonly List<FileSystemWatcher> _watchers = new();
        readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(700) };
        PileFan? _fan;
        Display? _display;
        IntPtr _hwnd;

        public Action<string, FrameworkElement>? ItemMenu;

        public DesktopPiles(Settings s, AppCatalog cat)
        {
            _s = s;
            _cat = cat;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "elysium piles";
            Content = _col;
            _later.Tick += (_, _) =>
            {
                _later.Stop();
                Build();
            };
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd, true);
                HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
            };
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0046)
            {
                var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.hwndInsertAfter = new IntPtr(1);
                pos.flags &= ~0x0004u;
                System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        static IEnumerable<string> Dirs()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        }

        public void Sync()
        {
            bool on = _s.DesktopPiles;
            if (on && !IsVisible) Start();
            else if (!on && IsVisible) Stop();
        }

        void Start()
        {
            _display = Displays.Primary(Displays.All());
            Show();
            var w = _display.Work;
            Native.SetWindowPos(_hwnd, new IntPtr(1), w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE);
            Width = w.Width / _display.Scale;
            Height = w.Height / _display.Scale;
            foreach (var dir in Dirs().Where(Directory.Exists))
            {
                try
                {
                    var fw = new FileSystemWatcher(dir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
                    FileSystemEventHandler poke = (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _later.Stop();
                        _later.Start();
                    }));
                    fw.Created += poke;
                    fw.Deleted += poke;
                    fw.Renamed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _later.Stop();
                        _later.Start();
                    }));
                    _watchers.Add(fw);
                }
                catch
                {
                }
            }
            Build();
            DesktopIcons.Set(false);
        }

        void Stop()
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _fan?.Close2();
            Hide();
            DesktopIcons.Set(!_s.HideDesktop);
        }

        static string Kind(string path)
        {
            if (Directory.Exists(path)) return "folder";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".lnk" or ".url" or ".exe" or ".appref-ms") return "app";
            string cat = Files.Category(path);
            return cat switch
            {
                "image" => "image",
                "video" => "video",
                "audio" => "audio",
                "doc" => "doc",
                "app" => "app",
                "code" => "code",
                "archive" => "archive",
                _ => "other"
            };
        }

        Dictionary<string, List<string>> Groups()
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var dir in Dirs().Where(Directory.Exists))
            {
                try
                {
                    foreach (var p in Directory.EnumerateFileSystemEntries(dir))
                    {
                        var name = Path.GetFileName(p);
                        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            if ((File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        }
                        catch
                        {
                            continue;
                        }
                        string k = Kind(p);
                        if (!map.TryGetValue(k, out var list)) map[k] = list = new List<string>();
                        list.Add(p);
                    }
                }
                catch
                {
                }
            }
            foreach (var list in map.Values) list.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            return map;
        }

        void Build()
        {
            _col.Children.Clear();
            var groups = Groups();
            int i = 0;
            foreach (var (id, label, glyph) in Kinds)
            {
                if (!groups.TryGetValue(id, out var items) || items.Count == 0) continue;
                var pile = Pile(label, glyph, items);
                _col.Children.Add(pile);
                Glass.FadeIn(pile, -6, 160 + i * 30);
                i++;
            }
        }

        FrameworkElement Pile(string label, string glyph, List<string> items)
        {
            var stack = new Grid { Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Center };
            var top = items.Take(3).Reverse().ToList();
            for (int i = 0; i < top.Count; i++)
            {
                double angle = (top.Count - 1 - i) * (i % 2 == 0 ? -7 : 7);
                var img = new Image { Width = 60, Height = 60, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(angle) };
                var path = top[i];
                _cat.Icons.Get(path, 96, false, src => img.Source = src);
                stack.Children.Add(img);
            }
            var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 1, 6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Kit.Text(items.Count.ToString(), 10.5, true, "OnAccent") };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            stack.Children.Add(badge);
            var name = new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            name.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black };
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            box.Children.Add(stack);
            box.Children.Add(name);
            var hit = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = box, Cursor = Cursors.Hand, Padding = new Thickness(6), CornerRadius = Kit.Round(12) };
            hit.MouseEnter += (_, _) => hit.Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
            hit.MouseLeave += (_, _) => hit.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            hit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _fan ??= new PileFan(_s, _cat) { ItemMenu = (p, el) => ItemMenu?.Invoke(p, el) };
                var tl = hit.PointToScreen(new Point(0, 0));
                _fan.Open(label, items, new Rect(tl, hit.PointToScreen(new Point(hit.ActualWidth, hit.ActualHeight))));
            };
            return hit;
        }
    }

    internal sealed class PileFan : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Border _surface;
        readonly StackPanel _body = new() { Margin = new Thickness(14, 12, 14, 14) };
        bool _shown;
        Point _down;
        string? _pressed;

        public Action<string, FrameworkElement>? ItemMenu;

        public PileFan(Settings s, AppCatalog cat)
        {
            _s = s;
            _cat = cat;
            Glass.Attach(this, 16);
            Title = "elysium pile";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(16);
            _surface.Child = _body;
            Content = _surface;
            Deactivated += (_, _) =>
            {
                if (!GlassMenu.Showing) Close2();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close2();
            };
        }

        public void Open(string label, List<string> items, Rect pilePx)
        {
            _body.Children.Clear();
            var head = Kit.Text(label + "  ·  " + items.Count, 13.5, true);
            head.Margin = new Thickness(4, 0, 0, 10);
            _body.Children.Add(head);
            var wrap = new WrapPanel { Width = Math.Min(6, Math.Max(3, (int)Math.Ceiling(Math.Sqrt(items.Count)))) * 96 };
            int n = 0;
            foreach (var p in items.Take(60))
            {
                var tile = Tile(p);
                wrap.Children.Add(tile);
                Glass.FadeIn(tile, 6, 120 + Math.Min(n, 20) * 12);
                n++;
            }
            _body.Children.Add(new ScrollViewer { Content = wrap, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var d = Displays.At((int)(pilePx.X + pilePx.Width / 2), (int)(pilePx.Y + pilePx.Height / 2));
            _shown = true;
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                int w = (int)Math.Ceiling(_surface.DesiredSize.Width * d.Scale), h = (int)Math.Ceiling(_surface.DesiredSize.Height * d.Scale);
                int x = Math.Clamp((int)pilePx.Left - w - (int)(12 * d.Scale), d.Work.Left + 8, d.Work.Right - w - 8);
                int y = Math.Clamp((int)(pilePx.Top + pilePx.Height / 2 - h / 2.0), d.Work.Top + 8, Math.Max(d.Work.Top + 8, d.Work.Bottom - h - 8));
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 0, 0.94, 240);
            Activate();
        }

        FrameworkElement Tile(string p)
        {
            var img = new Image { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
            _cat.Icons.Get(p, 96, false, src => img.Source = src);
            var name = new TextBlock { Text = Path.GetFileNameWithoutExtension(p), FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, Margin = new Thickness(0, 5, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var box = new StackPanel { Width = 84 };
            box.Children.Add(img);
            box.Children.Add(name);
            var hit = new Border { Child = box, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 4), CornerRadius = Kit.Round(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(p) };
            hit.MouseEnter += (_, _) => hit.SetResourceReference(Border.BackgroundProperty, "RowHover");
            hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
            hit.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _pressed = p;
                _down = e.GetPosition(this);
                if (e.ClickCount == 2)
                {
                    Files.Open(p, _s);
                    Close2();
                }
            };
            hit.MouseMove += (_, e) =>
            {
                if (_pressed != p || e.LeftButton != MouseButtonState.Pressed) return;
                var at = e.GetPosition(this);
                if (Math.Abs(at.X - _down.X) < 4 && Math.Abs(at.Y - _down.Y) < 4) return;
                _pressed = null;
                try
                {
                    DragDrop.DoDragDrop(hit, new DataObject(DataFormats.FileDrop, new[] { p }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                }
                catch
                {
                }
            };
            hit.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ItemMenu?.Invoke(p, hit);
            };
            return hit;
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            Glass.Dismiss(this, _surface);
        }
    }
}
