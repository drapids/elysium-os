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

namespace Heft.Ui
{
    internal sealed class Inspector : Window
    {
        const double CardW = 780;
        const double CardH = 480;
        const double PreviewW = 340;

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly ObjectActions _actions;
        readonly Grid _root = new();
        readonly Border _scrim = new();
        readonly Canvas _fly = new() { IsHitTestVisible = false };
        readonly Border _card;
        readonly Grid _preview = new();
        readonly StackPanel _info = new();
        readonly Image _flyer = new() { Stretch = Stretch.Uniform };

        Obj? _obj;
        Rect _from;
        Rect _to;
        bool _closing;
        Display? _display;
        MediaElement? _media;

        public Inspector(Settings s, AppCatalog cat, ObjectActions actions)
        {
            _s = s;
            _cat = cat;
            _actions = actions;
            Glass.Attach(this, 0);
            Title = "heft inspector";

            _scrim.SetResourceReference(Border.BackgroundProperty, "Scrim");
            _scrim.MouseLeftButtonDown += (_, _) => Close();
            _root.Children.Add(_scrim);

            _card = Glass.Surface(18);
            _card.Width = CardW;
            _card.Height = CardH;
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Center;
            _card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.35, Color = Colors.Black };

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewW) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var previewHost = new Border { Margin = new Thickness(16), CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = _preview };
            previewHost.SetResourceReference(Border.BackgroundProperty, "TileBg");
            layout.Children.Add(previewHost);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _info, Margin = new Thickness(4, 18, 18, 16), Focusable = false };
            Grid.SetColumn(scroll, 1);
            layout.Children.Add(scroll);
            _card.Child = layout;
            _root.Children.Add(_card);
            _root.Children.Add(_fly);
            _fly.Children.Add(_flyer);
            Content = _root;

            PreviewKeyDown += OnKey;
            Deactivated += (_, _) => Close();
        }

        public bool IsShowing => IsVisible && !_closing;

        public void Show(Obj obj, Rect? sourcePx = null, ImageSource? icon = null, bool rename = false)
        {
            _obj = obj;
            _closing = false;
            _display = sourcePx is Rect r0 ? Displays.At((int)(r0.X + r0.Width / 2), (int)(r0.Y + r0.Height / 2)) : Displays.AtCursor();
            var d = _display;
            double scale = d.Scale;

            if (!IsVisible)
            {
                Left = -20000;
                Top = -20000;
                Width = d.Bounds.Width / scale;
                Height = d.Bounds.Height / scale;
                Show();
            }
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, Native.SWP_NOACTIVATE);
            Width = d.Bounds.Width / scale;
            Height = d.Bounds.Height / scale;
            Activate();
            Focus();

            double w = Width, hgt = Height;
            double cardLeft = (w - CardW) / 2, cardTop = (hgt - CardH) / 2;
            _to = new Rect(cardLeft + 16 + 40, cardTop + 16 + 60, PreviewW - 32 - 80, CardH - 32 - 120);
            if (sourcePx is Rect src)
                _from = new Rect((src.X - d.Bounds.Left) / scale, (src.Y - d.Bounds.Top) / scale, src.Width / scale, src.Height / scale);
            else
                _from = new Rect(w / 2 - 32, hgt / 2 - 32, 64, 64);

            Build(obj, rename);

            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(380);
            Glass.SetBlur(this, false);
            var scrimIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            scrimIn.Completed += (_, _) =>
            {
                if (!_closing) Glass.SetBlur(this, true);
            };
            _scrim.BeginAnimation(OpacityProperty, scrimIn);

            _flyer.Source = icon ?? _cat.Icon(obj.Path, 256, obj.Kind is "app" or "uwp");
            _flyer.Visibility = _flyer.Source != null ? Visibility.Visible : Visibility.Collapsed;
            _flyer.Opacity = 1;
            Animate(_flyer, _from, _to, dur, ease);

            _card.Opacity = 0;
            var st = new ScaleTransform(0.92, 0.92);
            _card.RenderTransformOrigin = new Point(0.5, 0.5);
            _card.RenderTransform = st;
            var cardIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(120) };
            cardIn.Completed += (_, _) =>
            {
                if (_closing) return;
                _flyer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            };
            _card.BeginAnimation(OpacityProperty, cardIn);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(80) });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(80) });
        }

        static void Animate(FrameworkElement e, Rect from, Rect to, TimeSpan dur, IEasingFunction ease)
        {
            e.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.X, to.X, dur) { EasingFunction = ease });
            e.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from.Y, to.Y, dur) { EasingFunction = ease });
            e.BeginAnimation(WidthProperty, new DoubleAnimation(from.Width, to.Width, dur) { EasingFunction = ease });
            e.BeginAnimation(HeightProperty, new DoubleAnimation(from.Height, to.Height, dur) { EasingFunction = ease });
        }

        public new void Close()
        {
            if (_closing || !IsVisible) return;
            _closing = true;
            Glass.SetBlur(this, false);
            StopMedia();
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(220);
            if (_flyer.Source != null)
            {
                _flyer.BeginAnimation(OpacityProperty, null);
                _flyer.Opacity = 1;
                Animate(_flyer, _to, _from, dur, ease);
            }
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (_, _) =>
            {
                if (!_closing) return;
                Hide();
            };
            _scrim.BeginAnimation(OpacityProperty, fade);
            _flyer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { BeginTime = TimeSpan.FromMilliseconds(120) });
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
            if (e.OriginalSource is TextBox) return;
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Space:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_obj != null) Files.Open(_obj.Path, _s);
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        void Build(Obj o, bool rename)
        {
            StopMedia();
            _preview.Children.Clear();
            _info.Children.Clear();
            string p = o.Path;
            bool isDir = Directory.Exists(p);
            string cat = Files.Category(p);
            var color = Files.ColorFor(_s, cat);

            var stripe = new Border { Height = 3, Width = 46, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), Background = new SolidColorBrush(color) };
            _info.Children.Add(stripe);

            if (rename && (File.Exists(p) || isDir))
            {
                var box = new TextBox { Style = (Style)FindResource("Field"), Text = o.Name, FontSize = 18 };
                box.Loaded += (_, _) =>
                {
                    box.Focus();
                    int dot = isDir ? -1 : box.Text.LastIndexOf('.');
                    box.Select(0, dot > 0 ? dot : box.Text.Length);
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        Build(o, false);
                        e.Handled = true;
                    }
                    if (e.Key != Key.Enter) return;
                    e.Handled = true;
                    string name = box.Text.Trim();
                    if (name.Length == 0 || name == o.Name)
                    {
                        Build(o, false);
                        return;
                    }
                    try
                    {
                        string target = Path.Combine(Path.GetDirectoryName(p.TrimEnd('\\'))!, name);
                        if (isDir) Directory.Move(p, target);
                        else File.Move(p, target);
                        var n = Obj.From(target);
                        _obj = n;
                        Build(n, false);
                    }
                    catch (Exception ex)
                    {
                        box.ToolTip = ex.Message;
                        box.SetResourceReference(Control.BorderBrushProperty, "AccentFill");
                    }
                };
                _info.Children.Add(box);
            }
            else
            {
                var title = Kit.Text(o.Name, 20, true);
                title.TextWrapping = TextWrapping.Wrap;
                title.MaxHeight = 56;
                _info.Children.Add(title);
            }

            var sub = Kit.Text(Kind(o, cat), 12.5, false, "SubText");
            sub.Margin = new Thickness(0, 2, 0, 12);
            _info.Children.Add(sub);

            BuildActions(o);
            BuildFacts(o, isDir);
            BuildRelations(o, isDir);
            BuildPreview(o, isDir, cat);

            foreach (UIElement child in _info.Children)
                if (child is FrameworkElement fe) Glass.FadeIn(fe, 6, 220);
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

        void BuildActions(Obj o)
        {
            var list = _actions.For(o).Where(a => a.Name != "Inspect").ToList();
            if (list.Count == 0) return;
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var a in list.Take(14))
            {
                var act = a;
                var b = new Button { Style = (Style)FindResource(a == list[0] ? "AccentBtn" : "Push"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Kit.Icon(a.Glyph, 11, a == list[0] ? "OnAccent" : "Text");
                g.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(g);
                sp.Children.Add(new TextBlock { Text = a.Name, FontSize = 12 });
                b.Content = sp;
                b.Click += (_, _) =>
                {
                    bool keep = act.Name is "Rename" or "Color tag (cycle)" or "Copy path" or "Copy";
                    if (act.Name == "Rename")
                    {
                        Build(o, true);
                        return;
                    }
                    act.Run();
                    if (act.Name == "Color tag (cycle)") Build(o, false);
                    else if (!keep) Close();
                };
                wrap.Children.Add(b);
            }
            _info.Children.Add(wrap);
        }

        void Fact(Grid g, string label, string value)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Kit.Text(label, 12, false, "SubText");
            l.Margin = new Thickness(0, 3, 14, 3);
            Grid.SetRow(l, row);
            g.Children.Add(l);
            var v = Kit.Text(value, 12);
            v.Margin = new Thickness(0, 3, 0, 3);
            v.ToolTip = value;
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
        }

        void BuildFacts(Obj o, bool isDir)
        {
            string p = o.Path;
            var g = new Grid { Margin = new Thickness(0, 6, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _info.Children.Add(Kit.Header("Info"));
            ((FrameworkElement)_info.Children[^1]).Margin = new Thickness(0, 8, 0, 2);

            try
            {
                if (o.Kind == "uwp")
                {
                    Fact(g, "App id", p);
                    _info.Children.Add(g);
                    return;
                }

                FileSystemInfo fi = isDir ? new DirectoryInfo(p) : new FileInfo(p);
                if (!isDir)
                {
                    Fact(g, "Size", Kit.Size(((FileInfo)fi).Length));
                }
                else
                {
                    var sizeRow = g.RowDefinitions.Count;
                    Fact(g, "Size", "counting...");
                    var cell = g.Children.OfType<TextBlock>().Last();
                    _ = Task.Run(() => FolderSize(p)).ContinueWith(t =>
                    {
                        Dispatcher.BeginInvoke(new Action(() => cell.Text = t.Result.bytes < 0 ? "too big to count quickly" : $"{Kit.Size(t.Result.bytes)} · {t.Result.files} files"));
                    });
                }
                Fact(g, "Where", Path.GetDirectoryName(p.TrimEnd('\\')) ?? p);
                Fact(g, "Modified", $"{fi.LastWriteTime:MMM d yyyy, h:mm tt}  ({Kit.Ago(fi.LastWriteTime)})");
                Fact(g, "Created", $"{fi.CreationTime:MMM d yyyy, h:mm tt}");
                Fact(g, "Last opened", $"{fi.LastAccessTime:MMM d yyyy, h:mm tt}");
                if (_s.FileOpens.TryGetValue(p, out double n) && n > 0) Fact(g, "Opened via heft", $"{n:0} time{(n == 1 ? "" : "s")}");
                if (_s.Bookmarks.TryGetValue(p, out int tag) && tag > 0) Fact(g, "Tag", new[] { "red", "orange", "yellow", "green", "blue", "purple" }[Math.Clamp(tag - 1, 0, 5)]);

                if (!isDir)
                {
                    string ext = Path.GetExtension(p);
                    if (o.Kind == "app" && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = FileVersionInfo.GetVersionInfo(p);
                        if (!string.IsNullOrWhiteSpace(v.ProductVersion)) Fact(g, "Version", v.ProductVersion);
                        if (!string.IsNullOrWhiteSpace(v.CompanyName)) Fact(g, "Made by", v.CompanyName);
                        int running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Length;
                        if (running > 0) Fact(g, "Running", $"{running} process{(running == 1 ? "" : "es")}");
                    }
                    else if (ext.Length > 0)
                    {
                        var app = Native.AssocApp(ext);
                        if (app != null) Fact(g, "Opens with", app);
                    }
                }
            }
            catch
            {
            }
            _info.Children.Add(g);
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
                    if (sw.ElapsedMilliseconds > 2500) return (-1, count);
                }
            }
            catch
            {
            }
            return (total, count);
        }

        void BuildRelations(Obj o, bool isDir)
        {
            string p = o.Path;
            var root = Files.ProjectRoot(p);
            var related = Files.Related(p, 8);
            if (root == null && related.Count == 0) return;

            _info.Children.Add(Kit.Header("Connected"));
            ((FrameworkElement)_info.Children[^1]).Margin = new Thickness(0, 10, 0, 4);
            var wrap = new WrapPanel();

            if (root != null && !root.Equals(p, StringComparison.OrdinalIgnoreCase))
            {
                wrap.Children.Add(Chip("\uE943", "project: " + Path.GetFileName(root), () => Swap(Obj.From(root))));
                try
                {
                    var counts = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 })
                        .Where(f => !f.Contains("\\node_modules\\") && !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                        .Take(3000)
                        .GroupBy(Files.Category)
                        .Where(gp => gp.Key != "other")
                        .OrderByDescending(gp => gp.Count())
                        .Take(4);
                    foreach (var gp in counts)
                    {
                        var label = Files.Categories.FirstOrDefault(c => c.cat == gp.Key).label ?? gp.Key;
                        wrap.Children.Add(Chip("\uE8B7", $"{gp.Count()} {label.ToLowerInvariant()}", null));
                    }
                }
                catch
                {
                }
                var readme = Directory.EnumerateFiles(root, "README*").FirstOrDefault();
                if (readme != null && !readme.Equals(p, StringComparison.OrdinalIgnoreCase))
                    wrap.Children.Add(Chip("\uE8A5", Path.GetFileName(readme), () => Swap(Obj.From(readme))));
            }

            foreach (var r in related)
            {
                var rp = r;
                wrap.Children.Add(Chip("\uE8A5", Path.GetFileName(r), () => Swap(Obj.From(rp))));
            }
            _info.Children.Add(wrap);
        }

        Border Chip(string glyph, string text, Action? click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Kit.Icon(glyph, 10, "SubText");
            g.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(g);
            var t = Kit.Text(text, 11.5);
            t.MaxWidth = 200;
            sp.Children.Add(t);
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), Child = sp };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            if (click != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseLeftButtonUp += (_, _) => click();
            }
            return b;
        }

        void Swap(Obj o)
        {
            _obj = o;
            _flyer.BeginAnimation(OpacityProperty, null);
            _flyer.Opacity = 0;
            Build(o, false);
        }

        void BuildPreview(Obj o, bool isDir, string cat)
        {
            string p = o.Path;
            var fallback = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(40) };
            _preview.Children.Add(fallback);
            _cat.Icons.Get(p, 256, o.Kind is "app" or "uwp" or "drive", img =>
            {
                if (fallback.Source == null) fallback.Source = img;
            });

            try
            {
                if (isDir)
                {
                    var list = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14) };
                    foreach (var item in AppCatalog.FolderItems(p, "recent", false, false).Take(6))
                    {
                        var t = Kit.Text(item.Name, 11.5, false, "SubText");
                        list.Children.Add(t);
                    }
                    fallback.Margin = new Thickness(60, 30, 60, 130);
                    _preview.Children.Add(list);
                }
                else if (Files.IsImage(p))
                {
                    var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8), Opacity = 0 };
                    _preview.Children.Add(img);
                    _ = Task.Run(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 700;
                        bmp.UriSource = new Uri(p);
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }).ContinueWith(t =>
                    {
                        if (t.IsFaulted) return;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            img.Source = t.Result;
                            fallback.Visibility = Visibility.Collapsed;
                            img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
                        }));
                    });
                }
                else if (Files.IsText(p) && new FileInfo(p).Length < 4_000_000)
                {
                    string text;
                    using (var r = new StreamReader(p))
                    {
                        var buf = new char[6000];
                        int n = r.Read(buf, 0, buf.Length);
                        text = new string(buf, 0, n);
                    }
                    fallback.Visibility = Visibility.Collapsed;
                    var tb = new TextBlock
                    {
                        Text = text,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap,
                        Margin = new Thickness(12)
                    };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                    _preview.Children.Add(new ScrollViewer { Content = tb, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                }
                else if (cat == "video" || cat == "audio")
                {
                    _media = new MediaElement { Source = new Uri(p), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, IsMuted = cat == "video", Stretch = Stretch.Uniform };
                    _media.MediaOpened += (_, _) =>
                    {
                        if (cat == "video") fallback.Visibility = Visibility.Collapsed;
                    };
                    _media.MediaEnded += (_, _) =>
                    {
                        if (_media == null) return;
                        _media.Position = TimeSpan.Zero;
                        _media.Play();
                    };
                    _preview.Children.Add(_media);
                    _media.Play();
                }
                else if (o.Kind == "file")
                {
                    var thumb = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(10) };
                    _preview.Children.Add(thumb);
                    _cat.Icons.Get(p, 512, false, img =>
                    {
                        thumb.Source = img;
                        if (img != null) fallback.Visibility = Visibility.Collapsed;
                    });
                }
            }
            catch
            {
            }
        }
    }
}
