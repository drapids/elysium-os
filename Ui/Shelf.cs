using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal sealed class Shelf : Window
    {
        static readonly string[] Sorts = { "modified", "name", "accessed", "size", "type", "color", "frequency", "relevance" };
        static readonly string[] SpineColors = { "#3A4A5C", "#4C3F5E", "#2F5246", "#5C4433", "#3D4F66", "#5A3A44", "#45523A", "#33475B" };

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Library _lib;
        readonly ObjectActions _actions;
        readonly Pocket _pocket;
        readonly Notifier _notes;

        readonly Border _surface;
        readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
        readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBox _filter;
        readonly Button _sort;
        readonly StackPanel _drops = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8), Visibility = Visibility.Collapsed };
        readonly StackPanel _banner = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8), Visibility = Visibility.Collapsed };
        readonly StackPanel _pocketRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        readonly WrapPanel _content = new() { Margin = new Thickness(12, 0, 12, 12) };
        readonly ScrollViewer _scroll;
        readonly TextBlock _empty;
        readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(60) };
        readonly DispatcherTimer _spring = new() { Interval = TimeSpan.FromMilliseconds(750) };
        readonly DispatcherTimer _dragEnd = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly List<(FrameworkElement host, Thumb thumb)> _thumbs = new();
        readonly List<string> _trail = new();

        string _tab = "shelf";
        FrameworkElement? _hovered;
        Entry? _hoveredEntry;
        string? _springPath;
        bool _open, _suppress, _dragging;
        Point _down;
        FrameworkElement? _pressed;
        List<string>? _pendingCollect;

        public Action<Obj, Rect?, ImageSource?>? Inspect;
        public bool IsOpen => _open;

        public Shelf(Settings s, AppCatalog cat, Library lib, ObjectActions actions, Pocket pocket, Notifier notes)
        {
            _s = s;
            _cat = cat;
            _lib = lib;
            _actions = actions;
            _pocket = pocket;
            _notes = notes;
            Glass.Attach(this, 16);
            Title = "heft shelf";
            AllowDrop = true;
            Width = 1100;
            Height = 460;

            _surface = Glass.Surface(16);
            var root = new DockPanel();

            var header = new Grid { Margin = new Thickness(14, 12, 10, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tabScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tabs, Focusable = false };
            tabScroll.PreviewMouseWheel += (_, e) =>
            {
                tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset - e.Delta / 2.0);
                e.Handled = true;
            };
            header.Children.Add(tabScroll);
            var close = Kit.Plain("\uE8BB", HideShelf, "close", 10);
            close.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var tools = new Grid { Margin = new Thickness(18, 0, 16, 8) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tools.Children.Add(_crumbs);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            _filter = Kit.Field("", "filter (or just start typing)", _ => { });
            _filter.Width = 200;
            _filter.TextChanged += (_, _) => Render();
            right.Children.Add(_filter);
            _sort = Kit.Push("", CycleSort);
            _sort.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(_sort);
            Grid.SetColumn(right, 1);
            tools.Children.Add(right);
            DockPanel.SetDock(tools, Dock.Top);
            root.Children.Add(tools);

            DockPanel.SetDock(_banner, Dock.Top);
            root.Children.Add(_banner);
            DockPanel.SetDock(_drops, Dock.Top);
            root.Children.Add(_drops);
            DockPanel.SetDock(_pocketRow, Dock.Top);
            root.Children.Add(_pocketRow);

            var body = new Grid();
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _content, Focusable = false };
            body.Children.Add(_scroll);
            _empty = Kit.Text("", 13, false, "SubText");
            _empty.HorizontalAlignment = HorizontalAlignment.Center;
            _empty.VerticalAlignment = VerticalAlignment.Center;
            _empty.TextAlignment = TextAlignment.Center;
            _empty.IsHitTestVisible = false;
            body.Children.Add(_empty);
            root.Children.Add(body);

            _surface.Child = root;
            Content = _surface;

            BuildDrops();

            Deactivated += (_, _) =>
            {
                if (!_suppress && !_dragging) HideShelf();
            };
            PreviewKeyDown += OnKey;
            _outside.Tick += (_, _) => CheckOutside();
            _spring.Tick += (_, _) =>
            {
                _spring.Stop();
                if (_springPath != null) Portal(_springPath);
            };
            _dragEnd.Tick += (_, _) =>
            {
                _dragEnd.Stop();
                _drops.Visibility = Visibility.Collapsed;
            };

            DragEnter += (_, e) =>
            {
                _dragEnd.Stop();
                _drops.Visibility = Visibility.Visible;
                e.Effects = DropEffect(e);
                e.Handled = true;
            };
            DragOver += (_, e) =>
            {
                _dragEnd.Stop();
                e.Effects = DropEffect(e);
                e.Handled = true;
            };
            DragLeave += (_, _) => _dragEnd.Start();
            Drop += (_, e) =>
            {
                _drops.Visibility = Visibility.Collapsed;
                Accept(e.Data, (e.KeyStates & DragDropKeyStates.ControlKey) != 0);
                e.Handled = true;
            };

            LayoutUpdated += (_, _) => PlaceThumbs();
            _lib.Updated += () =>
            {
                if (_open) Render();
            };
            _pocket.Changed += () =>
            {
                if (_open) BuildPocket();
            };
        }

        static DragDropEffects DropEffect(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.Copy;
            return (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }

        public void Toggle(Display? d = null, double? topPx = null)
        {
            if (_open) HideShelf();
            else OpenShelf(d, topPx);
        }

        public void OpenShelf(Display? d = null, double? topPx = null, string? tab = null)
        {
            d ??= Displays.AtCursor();
            if (tab != null)
            {
                _tab = tab;
                _trail.Clear();
            }
            double scale = d.Scale;
            Width = Math.Min(1120, d.Work.Width / scale - 60);
            Height = Math.Min(470, d.Work.Height / scale - 80);
            int wpx = (int)(Width * scale);
            int x = d.Bounds.Left + (d.Bounds.Width - wpx) / 2;
            int y = (int)(topPx ?? d.Work.Top) + (int)(8 * scale);

            _lib.Refresh();
            if (!_open)
            {
                _open = true;
                Left = -20000;
                Show();
                Glass.MovePx(this, x, y);
                Activate();
                var tt = new TranslateTransform(0, -18);
                _surface.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                _surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
                _outside.Start();
            }
            else
            {
                Glass.MovePx(this, x, y);
                Activate();
            }
            BuildTabs();
            BuildPocket();
            Render();
        }

        public void HideShelf()
        {
            if (!_open) return;
            _open = false;
            _outside.Stop();
            _pendingCollect = null;
            _banner.Visibility = Visibility.Collapsed;
            ClearThumbs();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) =>
            {
                if (!_open) Hide();
            };
            _surface.BeginAnimation(OpacityProperty, fade);
        }

        void CheckOutside()
        {
            if (!_open || _suppress || _dragging || IsActive) return;
            if (!Native.MouseDown()) return;
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
            if (!r.Contains(p.X, p.Y)) HideShelf();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox tb)
            {
                if (e.Key == Key.Escape)
                {
                    if (tb.Text.Length > 0) tb.Text = "";
                    else HideShelf();
                    e.Handled = true;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    HideShelf();
                    break;
                case Key.Back:
                    if (_trail.Count > 0) Up();
                    break;
                case Key.Space:
                    if (_hovered != null && _hoveredEntry != null) InspectEl(_hovered, _hoveredEntry);
                    break;
                default:
                    if (e.Key >= Key.A && e.Key <= Key.Z && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        _filter.Focus();
                        return;
                    }
                    return;
            }
            e.Handled = true;
        }

        void BuildTabs()
        {
            _tabs.Children.Clear();
            Tab("shelf", "Shelf", "\uE7B8");
            Tab("catalog", "Catalog", "\uE82D");
            foreach (var (id, label, glyph) in Library.Smart) Tab(id, label, glyph);
            foreach (var name in _s.Collections.Keys.OrderBy(k => k)) Tab("c:" + name, name, "\uE8F1");

            var add = Kit.Plain("\uE710", NewCollection, "new collection", 11);
            add.Margin = new Thickness(4, 0, 0, 0);
            _tabs.Children.Add(add);
            _sort.Content = "sort: " + _s.ShelfSort;
        }

        void Tab(string id, string label, string glyph)
        {
            bool on = _tab == id;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Kit.Icon(glyph, 11, on ? "OnAccent" : "SubText");
            g.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(g);
            sp.Children.Add(Kit.Text(label, 12.5, on, on ? "OnAccent" : "Text"));
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5, 10, 6), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, AllowDrop = true };
            if (on) b.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            else
            {
                b.Background = Brushes.Transparent;
                b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "RowHover");
                b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            }
            b.MouseLeftButtonUp += (_, _) =>
            {
                if (_pendingCollect != null && id.StartsWith("c:"))
                {
                    AddToCollection(id[2..], _pendingCollect);
                    _pendingCollect = null;
                    _banner.Visibility = Visibility.Collapsed;
                }
                _tab = id;
                _trail.Clear();
                _filter.Text = "";
                BuildTabs();
                BuildPocket();
                Render();
            };
            b.MouseRightButtonUp += (_, e) =>
            {
                if (!id.StartsWith("c:")) return;
                e.Handled = true;
                _s.Collections.Remove(id[2..]);
                _s.Touch();
                if (_tab == id) _tab = "shelf";
                BuildTabs();
                Render();
            };
            b.ToolTip = id.StartsWith("c:") ? "drop files here to add them. right-click to delete the collection" : null;
            b.Drop += (_, e) =>
            {
                e.Handled = true;
                _drops.Visibility = Visibility.Collapsed;
                var paths = DroppedPaths(e.Data);
                if (paths.Count == 0) return;
                if (id.StartsWith("c:")) AddToCollection(id[2..], paths);
                else if (id == "shelf") AddToShelf(paths);
            };
            _tabs.Children.Add(b);
        }

        void NewCollection()
        {
            var box = Kit.Field("", "name it, enter", _ => { });
            box.Width = 150;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    BuildTabs();
                    e.Handled = true;
                    return;
                }
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                string name = box.Text.Trim();
                if (name.Length == 0) return;
                if (!_s.Collections.ContainsKey(name)) _s.Collections[name] = new List<string>();
                if (_pendingCollect != null)
                {
                    AddToCollection(name, _pendingCollect);
                    _pendingCollect = null;
                    _banner.Visibility = Visibility.Collapsed;
                }
                _s.Touch();
                _tab = "c:" + name;
                BuildTabs();
                Render();
            };
            _tabs.Children.Add(box);
            box.Focus();
        }

        public void AskCollection(IEnumerable<string> paths)
        {
            _pendingCollect = paths.ToList();
            OpenShelf();
            _banner.Children.Clear();
            var t = Kit.Text($"pick a collection tab for {_pendingCollect.Count} item{(_pendingCollect.Count == 1 ? "" : "s")}, or hit + to make one", 12.5, false, "SubText");
            _banner.Children.Add(t);
            _banner.Visibility = Visibility.Visible;
        }

        public void AddToCollection(string name, IEnumerable<string> paths)
        {
            if (!_s.Collections.TryGetValue(name, out var list))
            {
                list = new List<string>();
                _s.Collections[name] = list;
            }
            int added = 0;
            foreach (var p in paths)
            {
                if (list.Contains(p, StringComparer.OrdinalIgnoreCase)) continue;
                list.Add(p);
                added++;
            }
            _s.Touch();
            _notes.Post(new Note { Title = "Added to " + name, Body = $"{added} item{(added == 1 ? "" : "s")}", Glyph = "\uE8F1" });
            if (_open)
            {
                BuildTabs();
                Render();
            }
        }

        public void AddToShelf(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                _s.ShelfItems.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase));
                _s.ShelfItems.Insert(0, p);
            }
            _s.Touch();
            if (_open && _tab == "shelf") Render();
        }

        void CycleSort()
        {
            int i = Array.IndexOf(Sorts, _s.ShelfSort);
            _s.ShelfSort = Sorts[(i + 1 + Sorts.Length) % Sorts.Length];
            _s.Touch();
            _sort.Content = "sort: " + _s.ShelfSort;
            Render();
        }

        void BuildDrops()
        {
            _drops.Children.Clear();
            DropTarget("\uE7B8", "Shelf", paths => AddToShelf(paths));
            DropTarget("\uF012", "Zip", paths =>
            {
                var z = Files.Zip(paths);
                if (z != null)
                {
                    AddToShelf(new[] { z });
                    _notes.Post("Zipped", Path.GetFileName(z), "\uF012", z);
                }
            });
            DropTarget("\uE8AB", "To PNG", paths => Convert(paths, ".png"));
            DropTarget("\uE8AB", "To JPG", paths => Convert(paths, ".jpg"));
            DropTarget("\uE71B", "Copy path", paths => Files.CopyText(string.Join(Environment.NewLine, paths)));
            DropTarget("\uE8F1", "Collection...", paths => AskCollection(paths));
            DropTarget("\uE946", "Inspect", paths =>
            {
                if (paths.Count > 0) Inspect?.Invoke(Obj.From(paths[0]), null, null);
            });
        }

        void Convert(List<string> paths, string ext)
        {
            var made = new List<string>();
            foreach (var p in paths.Where(Files.IsImage))
            {
                var r = Files.ConvertImage(p, ext);
                if (r != null) made.Add(r);
            }
            if (made.Count == 0) return;
            AddToShelf(made);
            _notes.Post("Converted", $"{made.Count} image{(made.Count == 1 ? "" : "s")} to {ext.TrimStart('.').ToUpperInvariant()}", "\uE8AB", made[0]);
        }

        void DropTarget(string glyph, string label, Action<List<string>> act)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Kit.Icon(glyph, 12, "AccentFill");
            g.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(g);
            sp.Children.Add(Kit.Text(label, 12.5));
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 16, 10), Margin = new Thickness(0, 0, 8, 0), AllowDrop = true, BorderThickness = new Thickness(1.5) };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            b.DragEnter += (_, e) =>
            {
                b.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
                b.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            b.DragOver += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            b.DragLeave += (_, _) =>
            {
                b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
                b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            };
            b.Drop += (_, e) =>
            {
                e.Handled = true;
                b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
                b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                _drops.Visibility = Visibility.Collapsed;
                var paths = DroppedPaths(e.Data);
                if (paths.Count > 0) act(paths);
            };
            _drops.Children.Add(b);
        }

        public static List<string> DroppedPaths(IDataObject data)
        {
            var list = new List<string>();
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    list.AddRange(files);
                }
                else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bmp)
                {
                    list.Add(Library.Stash(null, bmp));
                }
                else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Length > 0)
                {
                    list.Add(Library.Stash(text, null));
                }
            }
            catch
            {
            }
            return list;
        }

        void Accept(IDataObject data, bool copy)
        {
            var paths = DroppedPaths(data);
            if (paths.Count == 0) return;
            if (_trail.Count > 0)
            {
                MoveInto(_trail[^1], paths, copy);
                return;
            }
            if (_tab.StartsWith("c:"))
            {
                AddToCollection(_tab[2..], paths);
                return;
            }
            if (_tab == "catalog")
            {
                foreach (var p in paths.Where(Directory.Exists))
                    if (!_s.ShelfFolders.Contains(p, StringComparer.OrdinalIgnoreCase)) _s.ShelfFolders.Add(p);
                _s.Touch();
                var loose = paths.Where(p => !Directory.Exists(p)).ToList();
                if (loose.Count > 0) AddToShelf(loose);
                Render();
                return;
            }
            AddToShelf(paths);
            if (_tab != "shelf")
            {
                _tab = "shelf";
                BuildTabs();
                Render();
            }
        }

        void MoveInto(string dir, List<string> paths, bool copy)
        {
            var items = paths.Where(p => !string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) return;
            _suppress = true;
            bool ok;
            try
            {
                ok = Native.FileOp(copy, items, dir);
            }
            finally
            {
                _suppress = false;
            }
            if (ok)
            {
                if (!copy)
                {
                    foreach (var p in items) _s.ShelfItems.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase));
                    _s.Touch();
                }
                _notes.Post(new Note { Title = copy ? "Copied" : "Moved", Body = $"{items.Count} item{(items.Count == 1 ? "" : "s")} into {Path.GetFileName(dir.TrimEnd('\\'))}", Glyph = "\uE8DE", Open = dir });
            }
            if (_open) Render();
        }

        public void Portal(string dir)
        {
            if (!Directory.Exists(dir)) return;
            if (_trail.Count == 0 || !_trail[^1].Equals(dir, StringComparison.OrdinalIgnoreCase)) _trail.Add(dir);
            _filter.Text = "";
            Render(1);
        }

        public void Browse(string dir)
        {
            _tab = "catalog";
            _trail.Clear();
            OpenShelf();
            Portal(dir);
        }

        void Up()
        {
            if (_trail.Count == 0) return;
            _trail.RemoveAt(_trail.Count - 1);
            Render(-1);
        }

        void BuildCrumbs()
        {
            _crumbs.Children.Clear();
            if (_trail.Count == 0)
            {
                _crumbs.Visibility = Visibility.Collapsed;
                return;
            }
            _crumbs.Visibility = Visibility.Visible;
            var back = Kit.Plain("\uE72B", Up, "back (backspace)", 10);
            back.Margin = new Thickness(-6, 0, 6, 0);
            _crumbs.Children.Add(back);

            string label = _tab == "catalog" ? "Catalog" : _tab.StartsWith("c:") ? _tab[2..] : Library.Smart.FirstOrDefault(x => x.id == _tab).label ?? "Shelf";
            AddCrumb(label, () =>
            {
                _trail.Clear();
                Render(-1);
            });
            for (int i = 0; i < _trail.Count; i++)
            {
                int idx = i;
                _crumbs.Children.Add(Kit.Text("  ›  ", 12.5, false, "Faint"));
                string name = Path.GetFileName(_trail[i].TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = _trail[i];
                AddCrumb(name, () =>
                {
                    _trail.RemoveRange(idx + 1, _trail.Count - idx - 1);
                    Render(-1);
                });
            }
            var open = Kit.Plain("\uE8A7", () => Files.Open(_trail[^1]), "open in Explorer", 10);
            open.Margin = new Thickness(8, 0, 0, 0);
            _crumbs.Children.Add(open);
        }

        void AddCrumb(string text, Action click)
        {
            var t = Kit.Text(text, 12.5, false, "SubText");
            t.Cursor = Cursors.Hand;
            t.MouseEnter += (_, _) => t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            t.MouseLeave += (_, _) => t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            t.MouseLeftButtonUp += (_, _) => click();
            _crumbs.Children.Add(t);
        }

        List<Entry> Source()
        {
            if (_trail.Count > 0) return _lib.Sort(Library.List(_trail[^1], _s.ShowHidden), _s.ShelfSort);
            switch (_tab)
            {
                case "shelf":
                    _s.ShelfItems.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
                    return _s.ShelfItems.Select(Library.Make).Where(e => e != null).Select(e => e!).ToList();
                case "catalog":
                    return new List<Entry>();
            }
            return _lib.Sort(_lib.Get(_tab.StartsWith("c:") ? _tab[2..] : _tab), _s.ShelfSort, _tab != "recent");
        }

        void Render(int dir = 0)
        {
            BuildCrumbs();
            _content.Children.Clear();
            _hovered = null;
            _hoveredEntry = null;
            string q = _filter.Text.Trim();

            if (_tab == "catalog" && _trail.Count == 0)
            {
                RenderCatalog(q);
            }
            else
            {
                var list = Source();
                if (q.Length > 0) list = list.Where(e => Fuzzy.Score(q, e.Name) > 0).OrderByDescending(e => Fuzzy.Score(q, e.Name)).ToList();
                foreach (var e in list.Take(300)) _content.Children.Add(e.Dir && _trail.Count > 0 ? Spine(e.Path, e.Name) : Card(e));
                _empty.Text = list.Count > 0 ? "" : _tab switch
                {
                    "shelf" => "drop anything here to keep it close.\nfiles, text, images. drag them back out when you need them",
                    _ when !_lib.Ready && _trail.Count == 0 => "looking through your stuff...",
                    _ when q.Length > 0 => "no matches",
                    _ => "nothing here yet"
                };
            }

            if (dir != 0)
            {
                var st = new ScaleTransform(dir > 0 ? 0.94 : 1.05, dir > 0 ? 0.94 : 1.05);
                _content.RenderTransformOrigin = new Point(0.5, 0.3);
                _content.RenderTransform = st;
                var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
                _content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            _scroll.ScrollToTop();
        }

        void RenderCatalog(string q)
        {
            var folders = _cat.PlaceItems().Select(p => (p.Path, p.Name)).ToList();
            foreach (var f in _s.ShelfFolders)
                if (Directory.Exists(f) && !folders.Any(x => x.Path.Equals(f, StringComparison.OrdinalIgnoreCase)))
                    folders.Add((f, Path.GetFileName(f.TrimEnd('\\'))));
            if (q.Length > 0) folders = folders.Where(f => Fuzzy.Score(q, f.Name) > 0).OrderByDescending(f => Fuzzy.Score(q, f.Name)).ToList();
            foreach (var f in folders) _content.Children.Add(Spine(f.Path, f.Name));

            var add = new Border { Width = 44, Height = 176, CornerRadius = new CornerRadius(6), Margin = new Thickness(4, 50, 4, 0), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, ToolTip = "add a folder to the catalog (or drop one here)" };
            add.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            add.Child = Kit.Icon("\uE710", 14, "SubText");
            add.MouseLeftButtonUp += (_, _) =>
            {
                var dlg = new OpenFolderDialog { Title = "Add to the catalog" };
                _suppress = true;
                bool ok;
                try
                {
                    ok = dlg.ShowDialog(this) == true;
                }
                finally
                {
                    _suppress = false;
                }
                Activate();
                if (!ok) return;
                if (!_s.ShelfFolders.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase)) _s.ShelfFolders.Add(dlg.FolderName);
                _s.Touch();
                Render();
            };
            _content.Children.Add(add);
            _empty.Text = "";
        }

        FrameworkElement Spine(string path, string name)
        {
            int hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(path));
            double width = 42 + hash % 4 * 6;
            Color color;
            if (_s.Bookmarks.TryGetValue(path, out int tag) && tag > 0)
                color = (Color)ColorConverter.ConvertFromString(Files.Bookmarks[Math.Clamp(tag - 1, 0, Files.Bookmarks.Length - 1)]);
            else
                color = (Color)ColorConverter.ConvertFromString(SpineColors[hash % SpineColors.Length]);

            var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            grad.GradientStops.Add(new GradientStop(Shade(color, 1.18), 0));
            grad.GradientStops.Add(new GradientStop(color, 0.35));
            grad.GradientStops.Add(new GradientStop(Shade(color, 0.72), 1));

            var inner = new Grid();
            inner.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 14, 6, 0), Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), CornerRadius = new CornerRadius(1.5) });
            inner.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 14), Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), CornerRadius = new CornerRadius(1.5) });
            var label = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new RotateTransform(-90)
            };
            inner.Children.Add(label);

            int count = -1;
            try
            {
                count = Directory.EnumerateFileSystemEntries(path).Take(999).Count();
            }
            catch
            {
            }
            var tagText = new StackPanel();
            var tagName = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            tagName.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            tagText.Children.Add(tagName);
            if (count >= 0)
            {
                var tagSub = new TextBlock { Text = count >= 999 ? "999+ items" : $"{count} item{(count == 1 ? "" : "s")}", FontSize = 11, TextAlignment = TextAlignment.Center };
                tagSub.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                tagText.Children.Add(tagSub);
            }
            var nameTag = new Border
            {
                Child = tagText,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 5, 10, 6),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-90, -46, -90, 0),
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 1)
            };
            nameTag.SetResourceReference(Border.BackgroundProperty, "PillBg");
            nameTag.SetResourceReference(Border.BorderBrushProperty, "PillStroke");
            var tagSpin = new RotateTransform(-90);
            var tagScale = new ScaleTransform(0.6, 0.6);
            var tagMove = new TranslateTransform(0, 70);
            var tagGroup = new TransformGroup();
            tagGroup.Children.Add(tagScale);
            tagGroup.Children.Add(tagSpin);
            tagGroup.Children.Add(tagMove);
            nameTag.RenderTransform = tagGroup;
            inner.Children.Add(nameTag);

            var book = new Border
            {
                Width = width,
                Height = 176,
                CornerRadius = new CornerRadius(5, 5, 4, 4),
                Background = grad,
                Margin = new Thickness(3, 50, 3, 0),
                Child = inner,
                Cursor = Cursors.Hand,
                AllowDrop = true,
                ToolTip = path,
                RenderTransformOrigin = new Point(0.5, 1)
            };
            var tg = new TransformGroup();
            var lift = new TranslateTransform();
            var tilt = new RotateTransform();
            tg.Children.Add(tilt);
            tg.Children.Add(lift);
            book.RenderTransform = tg;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var entry = Library.Make(path) ?? new Entry { Path = path, Name = name, Dir = true, Cat = "folder" };
            book.MouseEnter += (_, _) =>
            {
                _hovered = book;
                _hoveredEntry = entry;
                Panel.SetZIndex(book, 50);
                var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
                var t = TimeSpan.FromMilliseconds(320);
                tagSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, t) { EasingFunction = spring });
                tagScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, t) { EasingFunction = spring });
                tagScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, t) { EasingFunction = spring });
                tagMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, t) { EasingFunction = ease });
                nameTag.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                label.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(160)));
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
                tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-3, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            };
            book.MouseLeave += (_, _) =>
            {
                if (_hovered == book)
                {
                    _hovered = null;
                    _hoveredEntry = null;
                }
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                Panel.SetZIndex(book, 0);
                var t = TimeSpan.FromMilliseconds(180);
                tagSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-90, t) { EasingFunction = ease });
                tagScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, t));
                tagScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, t));
                tagMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(70, t) { EasingFunction = ease });
                nameTag.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
                label.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            };
            Interactive(book, entry, () => Portal(path));
            book.DragEnter += (_, e) =>
            {
                _springPath = path;
                _spring.Stop();
                _spring.Start();
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
                e.Effects = DropEffect(e);
                e.Handled = true;
            };
            book.DragOver += (_, e) =>
            {
                e.Effects = DropEffect(e);
                e.Handled = true;
            };
            book.DragLeave += (_, _) =>
            {
                _spring.Stop();
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            };
            book.Drop += (_, e) =>
            {
                _spring.Stop();
                e.Handled = true;
                _drops.Visibility = Visibility.Collapsed;
                var paths = DroppedPaths(e.Data);
                if (paths.Count > 0) MoveInto(path, paths, (e.KeyStates & DragDropKeyStates.ControlKey) != 0);
            };
            return book;
        }

        static Color Shade(Color c, double k)
        {
            byte F(byte v) => (byte)Math.Clamp(v * k, 0, 255);
            return Color.FromRgb(F(c.R), F(c.G), F(c.B));
        }

        FrameworkElement Card(Entry e)
        {
            var color = Files.ColorFor(_s, e.Cat);
            var g = new Grid { Width = 92 };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var img = new Image { Width = 52, Height = 52, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            bool thumb = e.Cat is "image" or "video" or "doc";
            if (_cat.Icons.TryPeek(e.Path, 96, !thumb, out var cached)) img.Source = cached;
            else _cat.Icons.Get(e.Path, 96, !thumb, s => img.Source = s);
            g.Children.Add(img);

            if (_s.Bookmarks.TryGetValue(e.Path, out int tag) && tag > 0)
            {
                var flag = new Border
                {
                    Width = 8,
                    Height = 14,
                    CornerRadius = new CornerRadius(0, 0, 2, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 14, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Files.Bookmarks[Math.Clamp(tag - 1, 0, Files.Bookmarks.Length - 1)]))
                };
                g.Children.Add(flag);
            }

            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock { Text = e.Name, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, Margin = new Thickness(0, 4, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            text.Children.Add(name);
            var meta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            meta.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 1, 5, 0) });
            meta.Children.Add(Kit.Text(e.Dir ? "folder" : Kit.Size(e.Size), 10.5, false, "Faint"));
            text.Children.Add(meta);
            Grid.SetRow(text, 1);
            g.Children.Add(text);

            var card = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = e.Path, AllowDrop = e.Dir };
            card.MouseEnter += (_, _) =>
            {
                _hovered = card;
                _hoveredEntry = e;
                card.SetResourceReference(Border.BackgroundProperty, "RowHover");
            };
            card.MouseLeave += (_, _) =>
            {
                if (_hovered == card)
                {
                    _hovered = null;
                    _hoveredEntry = null;
                }
                card.Background = Brushes.Transparent;
            };
            Interactive(card, e, () =>
            {
                if (e.Dir) Portal(e.Path);
                else Files.Open(e.Path, _s);
            });
            card.MouseDown += (_, ev) =>
            {
                if (ev.ChangedButton != MouseButton.Middle) return;
                ev.Handled = true;
                if (_tab == "shelf" && _trail.Count == 0) _s.ShelfItems.RemoveAll(x => x.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
                else if (_tab.StartsWith("c:") && _trail.Count == 0 && _s.Collections.TryGetValue(_tab[2..], out var list)) list.RemoveAll(x => x.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
                else return;
                _s.Touch();
                Render();
            };
            if (e.Dir)
            {
                card.Drop += (_, ev) =>
                {
                    ev.Handled = true;
                    _drops.Visibility = Visibility.Collapsed;
                    var paths = DroppedPaths(ev.Data);
                    if (paths.Count > 0) MoveInto(e.Path, paths, (ev.KeyStates & DragDropKeyStates.ControlKey) != 0);
                };
            }
            return card;
        }

        void Interactive(FrameworkElement el, Entry e, Action click)
        {
            el.PreviewMouseLeftButtonDown += (_, ev) =>
            {
                _pressed = el;
                _down = ev.GetPosition(this);
            };
            el.MouseMove += (_, ev) =>
            {
                if (_pressed != el || ev.LeftButton != MouseButtonState.Pressed) return;
                var p = ev.GetPosition(this);
                if (Math.Abs(p.X - _down.X) < 6 && Math.Abs(p.Y - _down.Y) < 6) return;
                _pressed = null;
                DragOut(e);
            };
            el.MouseLeftButtonUp += (_, ev) =>
            {
                if (_pressed != el) return;
                _pressed = null;
                ev.Handled = true;
                click();
            };
            el.MouseRightButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                InspectEl(el, e);
            };
        }

        void DragOut(Entry e)
        {
            _dragging = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { e.Path });
                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch
            {
            }
            finally
            {
                _dragging = false;
                _drops.Visibility = Visibility.Collapsed;
            }
            if (!File.Exists(e.Path) && !Directory.Exists(e.Path))
            {
                _s.ShelfItems.RemoveAll(x => x.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
                _s.Touch();
                if (_open) Render();
            }
        }

        void InspectEl(FrameworkElement el, Entry e)
        {
            Rect? src = null;
            ImageSource? icon = null;
            try
            {
                var tl = el.PointToScreen(new Point(0, 0));
                var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
                src = new Rect(tl, br);
                if (el is Border { Child: Grid g }) icon = g.Children.OfType<Image>().FirstOrDefault()?.Source;
            }
            catch
            {
            }
            Inspect?.Invoke(Obj.From(e.Path), src, icon);
        }

        void BuildPocket()
        {
            ClearThumbs();
            _pocketRow.Children.Clear();
            _pocket.Prune();
            bool show = _tab == "shelf" && _trail.Count == 0 && _pocket.Items.Count > 0;
            _pocketRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            var h = new WindowInteropHelper(this).Handle;
            foreach (var entry in _pocket.Items.ToList())
            {
                var pe = entry;
                var host = new Border { Width = 200, Height = 116, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                host.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                host.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
                var title = Kit.Text(pe.Title, 11, false, "SubText");
                title.VerticalAlignment = VerticalAlignment.Bottom;
                title.Margin = new Thickness(8, 0, 8, 4);
                host.Child = title;
                host.ToolTip = "pocketed: click to bring it back";
                host.MouseLeftButtonUp += (_, _) =>
                {
                    _pocket.Release(pe);
                    HideShelf();
                };
                _pocketRow.Children.Add(host);
                var t = new Thumb(h, new IntPtr(pe.Hwnd));
                if (t.Valid) _thumbs.Add((host, t));
            }
        }

        void PlaceThumbs()
        {
            if (_thumbs.Count == 0 || !_open) return;
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            Native.GetWindowRect(h, out var wr);
            foreach (var (host, thumb) in _thumbs)
            {
                if (!host.IsLoaded || host.ActualWidth <= 0)
                {
                    thumb.Hide();
                    continue;
                }
                try
                {
                    var tl = host.PointToScreen(new Point(6, 6));
                    var br = host.PointToScreen(new Point(host.ActualWidth - 6, host.ActualHeight - 22));
                    thumb.Place(new Native.RECT { Left = (int)tl.X - wr.Left, Top = (int)tl.Y - wr.Top, Right = (int)br.X - wr.Left, Bottom = (int)br.Y - wr.Top });
                }
                catch
                {
                }
            }
        }

        void ClearThumbs()
        {
            foreach (var (_, t) in _thumbs) t.Dispose();
            _thumbs.Clear();
        }
    }
}
