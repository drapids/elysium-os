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
using System.Windows.Media.Imaging;

namespace Elysium.Ui
{
    internal sealed class Inspector : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly ObjectActions _actions;
        readonly Border _surface;
        readonly Grid _layout = new();
        readonly Image _icon = new() { Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly ContentControl _title = new() { VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        readonly TextBlock _sub = Kit.Text("", 11.5, false, "SubText");
        readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _previewHost = new() { Margin = new Thickness(14, 0, 14, 0), ClipToBounds = true };
        readonly Grid _preview = new();
        readonly TextBlock _foot = Kit.Text("", 11.5, false, "SubText");
        readonly TextBlock _hint = Kit.Text("", 11, false, "Faint");
        readonly Border _infoPanel;
        readonly StackPanel _info = new();
        readonly TranslateTransform _infoShift = new(320, 0);

        Obj? _obj;
        bool _closing, _infoOpen, _renaming;
        Display? _display;
        MediaElement? _media;
        List<string>? _siblings;
        int _load;

        public Action<string>? Say;

        public Inspector(Settings s, AppCatalog cat, ObjectActions actions)
        {
            _s = s;
            _cat = cat;
            _actions = actions;
            Glass.Attach(this, 18);
            Title = "elysium quick look";
            SizeToContent = SizeToContent.WidthAndHeight;

            _surface = Glass.Surface(18);
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

            var bar = new DockPanel { Margin = new Thickness(16, 0, 10, 0) };
            DockPanel.SetDock(_tools, Dock.Right);
            bar.Children.Add(_tools);
            bar.Children.Add(_icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(_title);
            names.Children.Add(_sub);
            bar.Children.Add(names);
            _layout.Children.Add(bar);

            _previewHost.CornerRadius = Kit.Round(12);
            _previewHost.SetResourceReference(Border.BackgroundProperty, "TileBg");
            _previewHost.Child = _preview;
            _previewHost.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && _obj != null)
                {
                    Files.Open(_obj.Path, _s);
                    Close();
                }
            };
            Grid.SetRow(_previewHost, 1);
            _layout.Children.Add(_previewHost);

            _infoPanel = Glass.Surface(12);
            _infoPanel.Width = 300;
            _infoPanel.HorizontalAlignment = HorizontalAlignment.Right;
            _infoPanel.Margin = new Thickness(0, 0, 14, 0);
            _infoPanel.RenderTransform = _infoShift;
            _infoPanel.Visibility = Visibility.Collapsed;
            _infoPanel.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _info, Margin = new Thickness(16, 12, 14, 12), Focusable = false };
            Grid.SetRow(_infoPanel, 1);
            _layout.Children.Add(_infoPanel);

            var foot = new DockPanel { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_hint, Dock.Right);
            foot.Children.Add(_hint);
            _foot.TextTrimming = TextTrimming.CharacterEllipsis;
            foot.Children.Add(_foot);
            Grid.SetRow(foot, 2);
            _layout.Children.Add(foot);

            _surface.Child = _layout;
            Content = _surface;

            PreviewKeyDown += OnKey;
            Deactivated += (_, _) =>
            {
                if (GlassMenu.Showing) return;
                Close();
            };
        }

        public bool IsShowing => IsVisible && !_closing;

        public void Show(Obj obj, Rect? sourcePx = null, ImageSource? icon = null, bool rename = false)
        {
            _closing = false;
            _siblings = null;
            var d = sourcePx is Rect r0 ? Displays.At((int)(r0.X + r0.Width / 2), (int)(r0.Y + r0.Height / 2)) : Displays.AtCursor();
            bool moved = _display == null || _display.Key != d.Key || !IsVisible;
            _display = d;
            double w = Math.Clamp(d.Work.Width / d.Scale * 0.6, 620, 1000);
            double h = Math.Clamp(d.Work.Height / d.Scale * 0.68, 420, 720);
            _surface.Width = w;
            _surface.Height = h;
            Load(obj, rename, icon);
            if (IsVisible && !moved)
            {
                Activate();
                return;
            }
            Glass.Present(this, _surface, () => Place(d), 10, 0.96, 230);
            Activate();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!IsActive) Native.Focus(hwnd);
            Focus();
        }

        void Place(Display d)
        {
            int w = (int)Math.Round(_surface.Width * d.Scale), h = (int)Math.Round(_surface.Height * d.Scale);
            int x = d.Work.Left + (d.Work.Width - w) / 2, y = d.Work.Top + (d.Work.Height - h) / 2;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public new void Close()
        {
            if (_closing || !IsVisible) return;
            _closing = true;
            _renaming = false;
            StopMedia();
            GlassMenu.CloseAll();
            Glass.Dismiss(this, _surface, () =>
            {
                _preview.Children.Clear();
                SetInfo(false, false);
            }, 140);
        }

        void StopMedia()
        {
            if (_media == null) return;
            try
            {
                _media.Stop();
                _media.Close();
            }
            catch
            {
            }
            _media = null;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || _obj == null) return;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Space:
                    Close();
                    break;
                case Key.Enter:
                    Files.Open(_obj.Path, _s);
                    Close();
                    break;
                case Key.Delete:
                    Delete();
                    break;
                case Key.Left:
                case Key.Up:
                    Flip(-1);
                    break;
                case Key.Right:
                case Key.Down:
                    Flip(1);
                    break;
                case Key.I:
                    SetInfo(!_infoOpen, true);
                    break;
                case Key.F2:
                    StartRename();
                    break;
                case Key.C when ctrl:
                    Files.CopyFiles(new[] { _obj.Path });
                    Say?.Invoke("copied");
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        void Load(Obj o, bool rename, ImageSource? icon = null)
        {
            _obj = o;
            int token = ++_load;
            StopMedia();
            _renaming = false;
            string p = o.Path;
            bool isDir = Directory.Exists(p);
            string cat = Files.Category(p);

            _icon.Source = icon;
            _cat.Icons.Get(p, 64, o.Kind is "app" or "uwp" or "drive" or "folder", img =>
            {
                if (token == _load && img != null) _icon.Source = img;
            });
            if (rename && (File.Exists(p) || isDir)) StartRename();
            else ShowName(o.Name);
            _sub.Text = Kind(o, cat);

            BuildTools(o, isDir);
            BuildPreview(o, isDir, cat, token);
            Foot(o, isDir, token);
            if (_infoOpen) BuildInfo(o, isDir, token);
            Glass.FadeIn(_previewHost, 0, 160);
        }

        void ShowName(string name)
        {
            var t = Kit.Text(name, 15, true);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.Content = t;
        }

        void StartRename()
        {
            var o = _obj;
            if (o == null || !(File.Exists(o.Path) || Directory.Exists(o.Path))) return;
            bool isDir = Directory.Exists(o.Path);
            _renaming = true;
            var box = new TextBox { Style = (Style)FindResource("Field"), Text = o.Name, FontSize = 14, MinWidth = 260 };
            box.Loaded += (_, _) =>
            {
                box.Focus();
                int dot = isDir ? -1 : box.Text.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : box.Text.Length);
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _renaming = false;
                    ShowName(o.Name);
                    Focus();
                    return;
                }
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                string name = box.Text.Trim();
                if (name.Length == 0 || name == o.Name)
                {
                    _renaming = false;
                    ShowName(o.Name);
                    Focus();
                    return;
                }
                try
                {
                    string target = Path.Combine(Path.GetDirectoryName(o.Path.TrimEnd('\\'))!, name);
                    if (isDir) Directory.Move(o.Path, target);
                    else File.Move(o.Path, target);
                    _siblings = null;
                    Load(Obj.From(target), false);
                    Focus();
                }
                catch (Exception ex)
                {
                    box.ToolTip = ex.Message;
                    box.SetResourceReference(Control.BorderBrushProperty, "Danger");
                }
            };
            _title.Content = box;
        }

        static string Kind(Obj o, string cat)
        {
            if (o.Kind == "drive") return "drive";
            if (o.Kind == "folder") return "folder";
            if (o.Kind == "uwp") return "store app";
            string ext = Path.GetExtension(o.Path).TrimStart('.').ToUpperInvariant();
            foreach (var c in Files.Categories)
                if (c.cat == cat) return $"{ext} · {c.label.ToLowerInvariant()}";
            return ext.Length > 0 ? $"{ext} file" : "file";
        }

        void BuildTools(Obj o, bool isDir)
        {
            _tools.Children.Clear();
            var all = _actions.For(o).Where(a => a.Name != "Inspect").ToList();
            bool real = File.Exists(o.Path) || isDir;
            void Tool(string glyph, string tip, Action run, string brush = "Text")
            {
                var b = Kit.Plain(glyph, run, tip, 13);
                b.Width = 34;
                b.Height = 34;
                b.Margin = new Thickness(2, 0, 2, 0);
                if (brush != "Text") b.SetResourceReference(Control.ForegroundProperty, brush);
                _tools.Children.Add(b);
            }
            var open = all.FirstOrDefault(a => a.Group == "open");
            if (open != null) Tool(open.Glyph, open.Name.ToLowerInvariant(), () =>
            {
                open.Run();
                Close();
            });
            if (real && o.Kind != "drive") Tool("\uE838", "show in folder", () =>
            {
                Files.Reveal(o.Path);
                Close();
            });
            if (real) Tool("\uE8C8", "copy (ctrl + c)", () =>
            {
                Files.CopyFiles(new[] { o.Path });
                Say?.Invoke("copied");
            });
            if (real && o.Kind is "file" or "folder") Tool("\uE8AC", "rename (f2)", StartRename);
            var phone = all.FirstOrDefault(a => a.Name == "Send to phone");
            if (phone != null) Tool(phone.Glyph, "send to phone", () =>
            {
                phone.Run();
                Close();
            });
            if (real && o.Kind is "file" or "folder") Tool("\uE74D", "move to the recycle bin (delete)", Delete, "Danger");
            if (all.Count > 0)
            {
                Tool("\uE712", "more", () => { });
                var more = (Button)_tools.Children[^1];
                more.Click += (_, _) => GlassMenu.Show(more, all.Select(a =>
                {
                    var act = a;
                    return MenuEntry.Item(act.Glyph, act.Name.ToLowerInvariant(), () =>
                    {
                        if (act.Name == "Rename")
                        {
                            StartRename();
                            return;
                        }
                        if (act.Name == "Move to recycle bin")
                        {
                            Delete();
                            return;
                        }
                        act.Run();
                        if (act.Name is "Color tag (cycle)" or "Copy path" or "Copy") return;
                        Close();
                    }, act.Danger);
                }).ToList());
            }
            Tool("\uE946", "info (i)", () => SetInfo(!_infoOpen, true));
            Tool("\uE8BB", "close (space)", Close);
        }

        void Delete()
        {
            var o = _obj;
            if (o == null || !(File.Exists(o.Path) || Directory.Exists(o.Path))) return;
            var next = Neighbor(1) ?? Neighbor(-1);
            if (!Files.Recycle(o.Path))
            {
                Say?.Invoke("couldn't delete that, it might be open somewhere");
                return;
            }
            Say?.Invoke("moved " + o.Name + " to the recycle bin");
            _siblings?.RemoveAll(s => s.Equals(o.Path, StringComparison.OrdinalIgnoreCase));
            if (next != null && File.Exists(next)) Load(Obj.From(next), false);
            else Close();
        }

        List<string> Siblings()
        {
            if (_siblings != null) return _siblings;
            var list = new List<string>();
            try
            {
                var o = _obj;
                if (o != null && File.Exists(o.Path))
                {
                    var dir = Path.GetDirectoryName(o.Path);
                    if (dir != null)
                        list = Directory.EnumerateFiles(dir)
                            .Where(f => (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                            .Take(5000)
                            .ToList();
                }
            }
            catch
            {
            }
            _siblings = list;
            return list;
        }

        string? Neighbor(int dir)
        {
            if (_obj == null) return null;
            var list = Siblings();
            int i = list.FindIndex(f => f.Equals(_obj.Path, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || list.Count < 2) return null;
            int j = i + dir;
            return j >= 0 && j < list.Count ? list[j] : null;
        }

        void Flip(int dir)
        {
            var n = Neighbor(dir);
            if (n == null) return;
            Load(Obj.From(n), false);
        }

        void Foot(Obj o, bool isDir, int token)
        {
            _foot.Text = "";
            _hint.Text = "";
            string p = o.Path;
            Task.Run(() =>
            {
                var parts = new List<string>();
                try
                {
                    if (File.Exists(p))
                    {
                        var fi = new FileInfo(p);
                        parts.Add(Kit.Size(fi.Length));
                        parts.Add("changed " + Kit.Ago(fi.LastWriteTime));
                    }
                    else if (isDir) parts.Add("changed " + Kit.Ago(Directory.GetLastWriteTime(p)));
                    var dir = Path.GetDirectoryName(p.TrimEnd('\\'));
                    if (!string.IsNullOrEmpty(dir)) parts.Add("in " + Path.GetFileName(dir.TrimEnd('\\')));
                }
                catch
                {
                }
                return string.Join("  ·  ", parts);
            }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token != _load) return;
                _foot.Text = t.Result;
                _hint.Text = File.Exists(p) ? "← → flip through   ·   i info   ·   del delete" : "i info";
            })));
        }

        void SetInfo(bool open, bool animate)
        {
            if (_infoOpen == open && animate) return;
            _infoOpen = open;
            double to = open ? 0 : 320;
            if (open)
            {
                _infoPanel.Visibility = Visibility.Visible;
                if (_obj != null) BuildInfo(_obj, Directory.Exists(_obj.Path), _load);
            }
            if (!animate)
            {
                _infoShift.BeginAnimation(TranslateTransform.XProperty, null);
                _infoShift.X = to;
                if (!open) _infoPanel.Visibility = Visibility.Collapsed;
                return;
            }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(open ? 260 : 180)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
            if (!open)
                a.Completed += (_, _) =>
                {
                    if (!_infoOpen) _infoPanel.Visibility = Visibility.Collapsed;
                };
            _infoShift.BeginAnimation(TranslateTransform.XProperty, a);
        }

        void BuildInfo(Obj o, bool isDir, int token)
        {
            _info.Children.Clear();
            var head = Kit.Header("Info");
            head.Margin = new Thickness(0, 0, 0, 4);
            _info.Children.Add(head);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _info.Children.Add(grid);
            var loading = Kit.Caption("loading...");
            _info.Children.Add(loading);
            string p = o.Path;
            Task.Run(() => Facts(o, isDir)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token != _load || t.IsFaulted) return;
                _info.Children.Remove(loading);
                foreach (var (label, value) in t.Result.facts) Fact(grid, label, value);
                if (t.Result.related.Count > 0)
                {
                    var h = Kit.Header("Connected");
                    h.Margin = new Thickness(0, 12, 0, 4);
                    _info.Children.Add(h);
                    var wrap = new WrapPanel();
                    foreach (var r in t.Result.related)
                    {
                        var rp = r;
                        wrap.Children.Add(Chip("\uE8A5", Path.GetFileName(rp.TrimEnd('\\')), () => Load(Obj.From(rp), false)));
                    }
                    _info.Children.Add(wrap);
                }
                if (t.Result.commits.Count > 0)
                {
                    var h = Kit.Header("Project");
                    h.Margin = new Thickness(0, 12, 0, 4);
                    _info.Children.Add(h);
                    foreach (var (subject, ago) in t.Result.commits)
                    {
                        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                        var w = Kit.Text(ago, 11, false, "Faint");
                        w.Margin = new Thickness(10, 0, 0, 0);
                        DockPanel.SetDock(w, Dock.Right);
                        row.Children.Add(w);
                        var s = Kit.Text(subject, 12);
                        s.TextTrimming = TextTrimming.CharacterEllipsis;
                        row.Children.Add(s);
                        _info.Children.Add(row);
                    }
                }
                if (!isDir && File.Exists(p)) BuildVersions(p);
                foreach (UIElement child in _info.Children)
                    if (child is FrameworkElement fe) Glass.FadeIn(fe, 4, 160);
            })));
        }

        (List<(string, string)> facts, List<string> related, List<(string subject, string ago)> commits) Facts(Obj o, bool isDir)
        {
            var facts = new List<(string, string)>();
            var related = new List<string>();
            var commits = new List<(string subject, string ago)>();
            string p = o.Path;
            try
            {
                if (o.Kind == "uwp")
                {
                    facts.Add(("App id", p));
                    return (facts, related, commits);
                }
                FileSystemInfo fi = isDir ? new DirectoryInfo(p) : new FileInfo(p);
                if (!isDir) facts.Add(("Size", Kit.Size(((FileInfo)fi).Length)));
                else
                {
                    var (bytes, files) = FolderSize(p);
                    facts.Add(("Size", bytes < 0 ? "too big to count quickly" : $"{Kit.Size(bytes)} · {files} files"));
                }
                facts.Add(("Where", Path.GetDirectoryName(p.TrimEnd('\\')) ?? p));
                facts.Add(("Modified", $"{fi.LastWriteTime:MMM d yyyy, h:mm tt}"));
                facts.Add(("Created", $"{fi.CreationTime:MMM d yyyy, h:mm tt}"));
                facts.Add(("Opened", $"{fi.LastAccessTime:MMM d yyyy, h:mm tt}"));
                if (_s.FileOpens.TryGetValue(p, out double n) && n > 0) facts.Add(("Via elysium", $"{n:0} time{(n == 1 ? "" : "s")}"));
                if (!isDir)
                {
                    string ext = Path.GetExtension(p);
                    if (o.Kind == "app" && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = FileVersionInfo.GetVersionInfo(p);
                        if (!string.IsNullOrWhiteSpace(v.ProductVersion)) facts.Add(("Version", v.ProductVersion));
                        if (!string.IsNullOrWhiteSpace(v.CompanyName)) facts.Add(("Made by", v.CompanyName));
                    }
                    else if (ext.Length > 0)
                    {
                        var app = Native.AssocApp(ext);
                        if (app != null) facts.Add(("Opens with", app));
                    }
                }
                related.AddRange(Files.Related(p, 8));
                var root = Files.ProjectRoot(p);
                if (root != null && !root.Equals(p, StringComparison.OrdinalIgnoreCase)) related.Insert(0, root);
                if (isDir && Projects.IsProject(p)) commits.AddRange(Projects.Commits(p, 4));
            }
            catch
            {
            }
            return (facts, related, commits);
        }

        static (long bytes, int files) FolderSize(string dir)
        {
            long total = 0;
            int count = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    total += f.Length;
                    count++;
                    if (sw.ElapsedMilliseconds > 1500) return (-1, count);
                }
            }
            catch
            {
            }
            return (total, count);
        }

        void BuildVersions(string p)
        {
            var list = VersionShelf.For(p);
            if (list.Count == 0) return;
            var head = Kit.Header($"Older versions ({list.Count})");
            head.Margin = new Thickness(0, 12, 0, 4);
            _info.Children.Add(head);
            foreach (var v in list.Take(6))
            {
                var ver = v;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 3) };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(Kit.Plain("\uE8A7", () => VersionShelf.OpenCopy(ver), "open this version as a copy", 10));
                buttons.Children.Add(Kit.Plain("\uE7A7", () =>
                {
                    if (VersionShelf.Restore(ver) != null) Load(Obj.From(p), false);
                }, "put this version back (your current one gets saved next to it)", 10));
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                row.Children.Add(Kit.Text($"{Kit.Ago(ver.When)}  ·  {Kit.Size(ver.Size)}", 12, false, "SubText"));
                _info.Children.Add(row);
            }
        }

        void Fact(Grid g, string label, string value)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Kit.Text(label, 11.5, false, "SubText");
            l.Margin = new Thickness(0, 3, 12, 3);
            Grid.SetRow(l, row);
            g.Children.Add(l);
            var v = Kit.Text(value, 11.5);
            v.Margin = new Thickness(0, 3, 0, 3);
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            v.ToolTip = value;
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
        }

        Border Chip(string glyph, string text, Action? click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Kit.Icon(glyph, 10, "SubText");
            g.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(g);
            var t = Kit.Text(text, 11.5);
            t.MaxWidth = 220;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(t);
            var b = new Border { CornerRadius = Kit.Round(8), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), Child = sp };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            if (click != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += (_, _) => click();
            }
            return b;
        }

        void BuildPreview(Obj o, bool isDir, string cat, int token)
        {
            _preview.Children.Clear();
            string p = o.Path;
            var fallback = new Image { Stretch = Stretch.Uniform, MaxWidth = 160, MaxHeight = 160, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _preview.Children.Add(fallback);
            _cat.Icons.Get(p, 256, o.Kind is "app" or "uwp" or "drive" or "folder", img =>
            {
                if (token == _load && fallback.Source == null) fallback.Source = img;
            });
            double scale = _display?.Scale ?? 1;
            int px = (int)Math.Round((_surface.Width - 28) * scale);

            try
            {
                if (isDir)
                {
                    var list = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 0, 14, 18) };
                    fallback.VerticalAlignment = VerticalAlignment.Center;
                    fallback.Margin = new Thickness(0, 0, 0, 80);
                    _preview.Children.Add(list);
                    Task.Run(() => AppCatalog.FolderItems(p, "recent", false, false).Take(6).Select(i => i.Name).ToList()).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (token != _load || t.IsFaulted) return;
                        foreach (var name in t.Result)
                        {
                            var line = Kit.Text(name, 12, false, "SubText");
                            line.HorizontalAlignment = HorizontalAlignment.Center;
                            list.Children.Add(line);
                        }
                    })));
                }
                else if (Files.IsImage(p))
                {
                    var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(10), Opacity = 0 };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    _preview.Children.Add(img);
                    Task.Run(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = Math.Clamp(px, 400, 2400);
                        bmp.UriSource = new Uri(p);
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (token != _load || t.IsFaulted) return;
                        img.Source = t.Result;
                        fallback.Visibility = Visibility.Collapsed;
                        img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                    })));
                }
                else if (Files.IsText(p))
                {
                    var tb = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(16, 14, 16, 14) };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                    var sv = new ScrollViewer { Content = tb, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
                    _preview.Children.Add(sv);
                    Task.Run(() =>
                    {
                        using var r = new StreamReader(p);
                        var buf = new char[20000];
                        int n = r.Read(buf, 0, buf.Length);
                        return new string(buf, 0, n);
                    }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (token != _load || t.IsFaulted) return;
                        tb.Text = t.Result;
                        fallback.Visibility = Visibility.Collapsed;
                        sv.Visibility = Visibility.Visible;
                    })));
                }
                else if (cat == "video" || cat == "audio")
                {
                    var media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, IsMuted = cat == "video", Stretch = Stretch.Uniform, Margin = new Thickness(6) };
                    _media = media;
                    media.MediaOpened += (_, _) =>
                    {
                        if (cat == "video" && token == _load) fallback.Visibility = Visibility.Collapsed;
                    };
                    media.MediaEnded += (_, _) =>
                    {
                        if (_media != media) return;
                        media.Position = TimeSpan.Zero;
                        media.Play();
                    };
                    _preview.Children.Add(media);
                    var go = token;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (go != _load || _media != media) return;
                        media.Source = new Uri(p);
                        media.Play();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else if (o.Kind == "file")
                {
                    var thumb = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
                    _preview.Children.Add(thumb);
                    _cat.Icons.Get(p, 512, false, img =>
                    {
                        if (token != _load || img == null) return;
                        thumb.Source = img;
                        fallback.Visibility = Visibility.Collapsed;
                    });
                }
            }
            catch
            {
            }
        }
    }
}
