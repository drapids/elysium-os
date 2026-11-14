using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class Result
    {
        public string Key = "";
        public string Title = "";
        public string Sub = "";
        public string Hint = "";
        public string Glyph = "\uE721";
        public string? IconPath;
        public bool IconOnly = true;
        public Func<bool>? Run;
        public Action? Alt;
        public Obj? Obj;
        public string? Complete;
        public double Score;
        public bool Danger;
        public bool Header;
        public Color? Swatch;
        public ImageSource? Thumb;
    }

    internal sealed class Launcher : Window
    {
        static readonly (string prefix, string name, string glyph, string example)[] Prefixes =
        {
            ("?", "Web", "\uE774", "? how tall is the eiffel tower"),
            (">", "Command", "\uE756", "> ipconfig    or    > alias yt = https://youtube.com"),
            ("r ", "Files", "\uE8A5", "r screenshots    or    r pdfs this week > collection School"),
            ("@", "Apps", "\uE71D", "@ vsco"),
            ("#", "Settings", "\uE713", "# bluetooth"),
            ("=", "Calculator", "\uE8EF", "= 15% * 240    or    = 5 km to mi"),
            ("!", "System", "\uE7E8", "! lock"),
            (":", "Clipboard", "\uE77F", ": that link"),
            ("&", "heft", "\uE945", "& shelf"),
            ("/", "Folders", "\uE8B7", "/ c:\\users")
        };

        static readonly (string name, string uri, string keys)[] SettingsPages =
        {
            ("Display", "ms-settings:display", "screen resolution scale brightness night light hdr"),
            ("Sound", "ms-settings:sound", "audio speakers microphone volume"),
            ("Bluetooth & devices", "ms-settings:bluetooth", "bluetooth pair headphones mouse keyboard"),
            ("Wi-Fi", "ms-settings:network-wifi", "wifi wireless internet network"),
            ("Network status", "ms-settings:network-status", "internet ethernet ip"),
            ("VPN", "ms-settings:network-vpn", "vpn"),
            ("Personalization", "ms-settings:personalization", "wallpaper theme colors"),
            ("Background", "ms-settings:personalization-background", "wallpaper desktop picture"),
            ("Colors", "ms-settings:personalization-colors", "accent dark mode light mode"),
            ("Taskbar", "ms-settings:taskbar", "taskbar tray"),
            ("Apps & features", "ms-settings:appsfeatures", "uninstall programs apps"),
            ("Default apps", "ms-settings:defaultapps", "default browser open with"),
            ("Startup apps", "ms-settings:startupapps", "startup boot"),
            ("Notifications", "ms-settings:notifications", "notifications focus assist"),
            ("Focus assist", "ms-settings:quiethours", "do not disturb quiet"),
            ("Power & sleep", "ms-settings:powersleep", "power sleep battery screen timeout"),
            ("Battery", "ms-settings:batterysaver", "battery saver"),
            ("Storage", "ms-settings:storagesense", "disk space storage sense cleanup"),
            ("Windows Update", "ms-settings:windowsupdate", "update upgrade"),
            ("Mouse", "ms-settings:mousetouchpad", "mouse pointer cursor speed"),
            ("Keyboard", "ms-settings:keyboard", "keyboard typing"),
            ("Language & region", "ms-settings:regionlanguage", "language region input"),
            ("Date & time", "ms-settings:dateandtime", "clock time zone"),
            ("Accounts", "ms-settings:yourinfo", "account user profile"),
            ("Sign-in options", "ms-settings:signinoptions", "pin password hello"),
            ("Privacy", "ms-settings:privacy", "privacy permissions"),
            ("Microphone privacy", "ms-settings:privacy-microphone", "mic microphone access"),
            ("Camera privacy", "ms-settings:privacy-webcam", "camera webcam access"),
            ("Graphics", "ms-settings:display-advancedgraphics", "gpu graphics performance"),
            ("Game mode", "ms-settings:gaming-gamemode", "game mode gaming"),
            ("Xbox Game Bar", "ms-settings:gaming-gamebar", "game bar recording"),
            ("About this PC", "ms-settings:about", "specs pc name system info"),
            ("Recovery", "ms-settings:recovery", "reset recovery"),
            ("Troubleshoot", "ms-settings:troubleshoot", "fix troubleshoot"),
            ("Control Panel", "control", "control panel classic"),
            ("Device Manager", "devmgmt.msc", "drivers devices hardware"),
            ("Disk Management", "diskmgmt.msc", "partitions drives format"),
            ("Task Manager", "taskmgr", "processes performance kill"),
            ("Environment variables", "rundll32.exe sysdm.cpl,EditEnvironmentVariables", "path env variables"),
            ("Services", "services.msc", "services background"),
            ("Registry Editor", "regedit", "registry regedit")
        };

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly AppIndex _apps;
        readonly Commands _cmds;
        readonly Clips _clips;
        readonly Notifier _notes;
        readonly Library _lib;

        readonly Border _surface;

        public void Prewarm() => Glass.Prewarm(this, _surface);
        readonly TextBox _box;
        readonly TextBlock _placeholder;
        readonly Border _chip;
        readonly TextBlock _chipText;
        readonly StackPanel _list = new();
        readonly ScrollViewer _scroll;
        readonly TextBlock _footer;
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

        List<Result> _results = new();
        List<Result> _fileResults = new();
        List<string> _pipeItems = new();
        int _sel;
        string? _armed;
        CancellationTokenSource? _cts;
        IntPtr _previous;

        public Action<Obj, Rect?, ImageSource?>? Inspect;
        public Action<IEnumerable<string>>? ToShelf;
        public Action<string, IEnumerable<string>>? ToCollection;
        public Action<string>? Browse;
        public Action<IList<string>>? Share;

        public Launcher(Settings s, AppCatalog cat, AppIndex apps, Commands cmds, Clips clips, Notifier notes, Library lib)
        {
            _s = s;
            _cat = cat;
            _apps = apps;
            _cmds = cmds;
            _clips = clips;
            _notes = notes;
            _lib = lib;
            Glass.Attach(this, 18);
            Title = "heft launcher";
            Width = 680;
            SizeToContent = SizeToContent.Height;

            _surface = Glass.Surface(18);
            var stack = new StackPanel();

            var input = new Grid { Margin = new Thickness(18, 14, 18, 12) };
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _chipText = Kit.Text("", 12, true, "OnAccent");
            _chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 10, 0), Child = _chipText, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            _chip.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            input.Children.Add(_chip);

            var field = new Grid();
            _placeholder = Kit.Text("search apps, files, commands... or try \"remind me in 10 min to stretch\"", 19, false, "Faint");
            _placeholder.IsHitTestVisible = false;
            field.Children.Add(_placeholder);
            _box = new TextBox
            {
                FontSize = 19,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            _box.SetResourceReference(Control.ForegroundProperty, "Text");
            _box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Text");
            _box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "AccentFill");
            _box.Template = PlainBox();
            field.Children.Add(_box);
            Grid.SetColumn(field, 1);
            input.Children.Add(field);
            stack.Children.Add(input);

            var sep = new Border { Height = 1 };
            sep.SetResourceReference(Border.BackgroundProperty, "Separator");
            stack.Children.Add(sep);

            _scroll = new ScrollViewer { Content = _list, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Padding = new Thickness(8, 6, 8, 6) };
            stack.Children.Add(_scroll);

            _footer = Kit.Text("", 11, false, "Faint");
            _footer.Margin = new Thickness(20, 4, 20, 10);
            stack.Children.Add(_footer);

            _surface.Child = stack;
            Content = _surface;

            _box.TextChanged += (_, _) => Update();
            PreviewKeyDown += OnKey;
            Deactivated += (_, _) => Dismiss();
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                _ = RunFiles();
            };
        }

        static ControlTemplate PlainBox()
        {
            var t = new ControlTemplate(typeof(TextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            t.VisualTree = host;
            return t;
        }

        public void Toggle(string? text = null)
        {
            if (_shown && text == null)
            {
                Dismiss();
                return;
            }
            Open(text);
        }

        bool _start;

        public void OpenStart()
        {
            if (_shown && _start)
            {
                Dismiss();
                return;
            }
            _start = true;
            OpenInner(null);
        }

        public void Open(string? text = null)
        {
            _start = false;
            OpenInner(text);
        }

        bool _shown;

        public bool Shown => _shown;

        void OpenInner(string? text)
        {
            _previous = Native.GetForegroundWindow();
            if (!_apps.Loaded) _apps.Load();
            var d = Displays.AtCursor();
            _shown = true;
            _box.Text = text ?? "";
            Update();
            Glass.Present(this, _surface, () =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int wpx = (int)(Width * d.Scale);
                Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Bounds.Left + (d.Bounds.Width - wpx) / 2, d.Work.Top + (int)(d.Work.Height * 0.2), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, -8, 0.975, 210);
            Activate();
            if (!IsActive) Native.Focus(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
            if (text == null) _box.SelectAll();
        }

        public void Dismiss()
        {
            if (!_shown) return;
            _shown = false;
            _cts?.Cancel();
            _armed = null;
            Glass.Dismiss(this, _surface);
        }

        (string prefix, string body) Split(string text)
        {
            foreach (var p in Prefixes)
            {
                if (p.prefix == "r ")
                {
                    if (text.StartsWith("r ", StringComparison.OrdinalIgnoreCase)) return ("r ", text[2..]);
                    continue;
                }
                if (text.StartsWith(p.prefix)) return (p.prefix, text[p.prefix.Length..].TrimStart());
            }
            return ("", text);
        }

        void Update()
        {
            string text = _box.Text;
            _placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _armed = null;
            _fileResults.Clear();
            _pipeItems.Clear();
            _cts?.Cancel();

            var (prefix, body) = Split(text);
            var pre = Prefixes.FirstOrDefault(x => x.prefix == prefix);
            _chip.Visibility = prefix.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _chipText.Text = pre.name ?? "";

            string left = body, verb = "";
            if (prefix != ">")
            {
                var m = Regex.Match(body, @"^(.*?)\s+(>|\|)\s+(.*)$");
                if (m.Success)
                {
                    left = m.Groups[1].Value;
                    verb = m.Groups[3].Value.Trim();
                }
            }

            var list = new List<Result>();
            if (text.Length == 0) list = Home();
            else if (prefix.Length > 0 && body.Length == 0 && verb.Length == 0) list = Discover(prefix);
            else
            {
                switch (prefix)
                {
                    case "?":
                        list.Add(Web(body));
                        break;
                    case ">":
                        list = Shell(body);
                        break;
                    case "r ":
                        break;
                    case "@":
                        list = AppResults(left, 30);
                        break;
                    case "#":
                        list = SettingResults(body, 30);
                        break;
                    case "=":
                        list = CalcResults(body, true);
                        break;
                    case "!":
                        list = CommandResults(body, "system", 30);
                        break;
                    case "&":
                        list = CommandResults(body, "heft", 30);
                        break;
                    case ":":
                        list = ClipResults(body);
                        break;
                    case "/":
                        list = Folder(body);
                        break;
                    default:
                        list = Everything(left);
                        break;
                }
            }

            if (verb.Length > 0) list.Insert(0, PipeResult(prefix, left, verb, list));

            bool wantFiles = prefix == "r " || (prefix == "" && text.Trim().Length >= 3 && !text.StartsWith("remind", StringComparison.OrdinalIgnoreCase));
            if (wantFiles)
            {
                _debounce.Stop();
                _debounce.Start();
            }
            Show(list);
        }

        void Show(List<Result> list)
        {
            _results = list;
            _sel = _results.FindIndex(r => !r.Header);
            if (_sel < 0) _sel = 0;
            Render();
        }

        void Render()
        {
            _list.Children.Clear();
            for (int i = 0; i < _results.Count && i < 60; i++) _list.Children.Add(Row(_results[i], i));
            if (_results.Count == 0)
            {
                var t = Kit.Text(_debounce.IsEnabled || _cts != null ? "looking..." : "nothing yet", 13, false, "SubText");
                t.Margin = new Thickness(14, 10, 14, 10);
                _list.Children.Add(t);
            }
            Footer();
            Scroll();
        }

        void Footer()
        {
            var r = Current();
            if (_armed != null) _footer.Text = "press enter again to confirm";
            else if (r?.Obj != null) _footer.Text = "enter open   \u00b7   ctrl+enter show in folder   \u00b7   shift+enter inspect   \u00b7   ctrl+p pin   \u00b7   tab complete";
            else _footer.Text = "enter run   \u00b7   tab complete   \u00b7   ctrl+p pin   \u00b7   type ? > r @ # = ! : & / for modes";
        }

        Result? Current() => _sel >= 0 && _sel < _results.Count ? _results[_sel] : null;

        FrameworkElement Row(Result r, int index)
        {
            if (r.Header)
            {
                var h = Kit.Text(r.Title, 11, true, "SubText");
                h.Margin = new Thickness(12, 8, 12, 4);
                return h;
            }

            bool on = index == _sel;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconHost = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) };
            var glyph = Kit.Icon(r.Glyph, 15, on ? "Text" : "SubText");
            iconHost.Children.Add(glyph);
            if (r.IconPath != null)
            {
                var img = new Image { Width = 28, Height = 28 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                iconHost.Children.Add(img);
                string path = r.IconPath;
                if (_cat.Icons.TryPeek(path, 64, r.IconOnly, out var cached))
                {
                    img.Source = cached;
                    if (cached != null) glyph.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _cat.Icons.Get(path, 64, r.IconOnly, src =>
                    {
                        img.Source = src;
                        if (src != null) glyph.Visibility = Visibility.Collapsed;
                    });
                }
            }
            if (r.Swatch is Color sw)
            {
                glyph.Visibility = Visibility.Collapsed;
                iconHost.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(sw), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)) });
            }
            else if (r.Thumb != null)
            {
                glyph.Visibility = Visibility.Collapsed;
                var thumb = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = r.Thumb, Stretch = Stretch.UniformToFill } };
                iconHost.Children.Add(thumb);
            }
            g.Children.Add(iconHost);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Kit.Text(r.Title, 14, on));
            if (r.Sub.Length > 0) text.Children.Add(Kit.Text(r.Sub, 11.5, false, "SubText"));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            if (r.Hint.Length > 0)
            {
                var hint = Kit.Text(r.Hint, 11, false, on ? "SubText" : "Faint");
                hint.Margin = new Thickness(10, 0, 4, 0);
                Grid.SetColumn(hint, 2);
                g.Children.Add(hint);
            }

            var row = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Cursor = Cursors.Hand };
            if (on) row.SetResourceReference(Border.BackgroundProperty, r.Danger ? "AccentSoft" : "RowHover");
            else row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) =>
            {
                _sel = index;
                Execute(false);
            };
            row.MouseRightButtonUp += (_, _) =>
            {
                _sel = index;
                InspectCurrent();
            };
            return row;
        }

        void Scroll()
        {
            if (_sel < 0 || _sel >= _list.Children.Count) return;
            if (_list.Children[_sel] is FrameworkElement fe) fe.BringIntoView();
        }

        void Move(int dir)
        {
            if (_results.Count == 0) return;
            int i = _sel;
            for (int n = 0; n < _results.Count; n++)
            {
                i = (i + dir + _results.Count) % _results.Count;
                if (!_results[i].Header) break;
            }
            _sel = i;
            _armed = null;
            Render();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Escape:
                    if (_box.Text.Length > 0) _box.Text = "";
                    else Dismiss();
                    break;
                case Key.Down:
                    Move(1);
                    break;
                case Key.Up:
                    Move(-1);
                    break;
                case Key.Enter:
                    if (shift) InspectCurrent();
                    else Execute(ctrl);
                    break;
                case Key.Tab:
                    var c = Current();
                    if (c?.Complete != null)
                    {
                        _box.Text = c.Complete;
                        _box.CaretIndex = _box.Text.Length;
                    }
                    else Move(shift ? -1 : 1);
                    break;
                case Key.P when ctrl:
                    Pin();
                    break;
                case Key.I when ctrl:
                    InspectCurrent();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        void Pin()
        {
            var r = Current();
            if (r == null || r.Key.Length == 0) return;
            if (_s.PinnedCommands.Remove(r.Key)) r.Hint = "unpinned";
            else
            {
                _s.PinnedCommands.Insert(0, r.Key);
                r.Hint = "pinned";
            }
            _s.Touch();
            Render();
        }

        void InspectCurrent()
        {
            var r = Current();
            if (r?.Obj == null || Inspect == null) return;
            Rect? src = null;
            if (_sel < _list.Children.Count && _list.Children[_sel] is Border b && b.Child is Grid g && g.Children[0] is FrameworkElement icon)
            {
                try
                {
                    var tl = icon.PointToScreen(new Point(0, 0));
                    var br = icon.PointToScreen(new Point(icon.ActualWidth, icon.ActualHeight));
                    src = new Rect(tl, br);
                }
                catch
                {
                }
            }
            var o = r.Obj;
            Dismiss();
            Inspect(o, src, null);
        }

        void Execute(bool alt)
        {
            var r = Current();
            if (r == null || r.Header) return;
            if (r.Danger && _armed != r.Key)
            {
                _armed = r.Key;
                Footer();
                return;
            }
            _armed = null;
            Remember(r);
            if (alt && r.Alt != null)
            {
                Dismiss();
                r.Alt();
                return;
            }
            if (r.Run == null) return;
            bool close = r.Run();
            if (close) Dismiss();
        }

        void Remember(Result r)
        {
            if (r.Key.Length == 0) return;
            var h = _s.CommandHistory.FirstOrDefault(x => x.Key == r.Key);
            if (h == null)
            {
                h = new HistoryEntry { Key = r.Key };
                _s.CommandHistory.Add(h);
            }
            h.Score = Decayed(h) + 1;
            h.Last = DateTime.Now;
            h.Query = _box.Text;
            if (_s.CommandHistory.Count > 300)
                _s.CommandHistory = _s.CommandHistory.OrderByDescending(Decayed).Take(220).ToList();
            _s.Touch();
        }

        static double Decayed(HistoryEntry h)
        {
            double days = (DateTime.Now - h.Last).TotalDays;
            return h.Score * Math.Pow(0.5, days / 14);
        }

        double Boost(string key, string query)
        {
            var h = _s.CommandHistory.FirstOrDefault(x => x.Key == key);
            if (h == null) return 0;
            double b = Math.Log(1 + Decayed(h)) * 2.5;
            if (query.Length > 0 && h.Query.StartsWith(query, StringComparison.OrdinalIgnoreCase)) b += 4;
            return b;
        }

        List<Result> Rank(List<Result> list, string q, int take)
        {
            foreach (var r in list) r.Score += Boost(r.Key, q);
            return list.Where(r => r.Score > 0).OrderByDescending(r => r.Score).Take(take).ToList();
        }

        List<Result> Home()
        {
            var list = new List<Result>();
            var pinned = _s.PinnedCommands.Select(Resolve).Where(r => r != null).Select(r => r!).ToList();
            if (pinned.Count > 0)
            {
                list.Add(new Result { Header = true, Title = "PINNED" });
                foreach (var p in pinned) p.Hint = "pinned";
                list.AddRange(pinned);
            }
            var recent = _s.CommandHistory.OrderByDescending(Decayed).Select(h => Resolve(h.Key)).Where(r => r != null && !_s.PinnedCommands.Contains(r.Key)).Take(6).Select(r => r!).ToList();
            if (recent.Count > 0)
            {
                list.Add(new Result { Header = true, Title = "RECENT" });
                list.AddRange(recent);
            }
            if (_start)
            {
                list.Add(new Result { Header = true, Title = "ALL APPS" });
                foreach (var a in _apps.Apps.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) list.Add(AppResult(a, 1));
                list.Add(new Result { Header = true, Title = "POWER" });
                foreach (var id in new[] { "pclock", "sleep", "restart", "shutdown", "signout" })
                {
                    var c = _cmds.Get(id);
                    if (c != null) list.Add(CommandResult(c, 1));
                }
                list.Add(new Result { Title = "Windows Start menu", Sub = "the original one (ctrl + esc works too)", Glyph = "\uE782", Run = () => Go(() => Native.Combo(0x11, 0x1B)) });
            }
            var timer = _notes.ActiveTimer;
            if (timer != null)
            {
                list.Insert(0, new Result
                {
                    Title = $"Timer: {Math.Max(0, (int)(timer.Due - DateTime.Now).TotalMinutes)}m {(timer.Due - DateTime.Now).Seconds}s left",
                    Sub = "enter to cancel it",
                    Glyph = "\uE916",
                    Run = () =>
                    {
                        _notes.CancelTimer();
                        return true;
                    }
                });
            }
            if (list.Count == 0 || (!_start && list.All(r => r.Header))) return Discover("");
            return list;
        }

        Result? Resolve(string key)
        {
            int colon = key.IndexOf(':');
            if (colon < 0) return null;
            string kind = key[..colon], val = key[(colon + 1)..];
            switch (kind)
            {
                case "app":
                    var a = _apps.Apps.FirstOrDefault(x => x.Id == val);
                    return a == null ? null : AppResult(a, 1);
                case "file":
                    return File.Exists(val) || Directory.Exists(val) ? FileResult(val, 1) : null;
                case "cmd":
                    var c = _cmds.Get(val);
                    return c == null ? null : CommandResult(c, 1);
                case "set":
                    var sp = SettingsPages.FirstOrDefault(x => x.uri == val);
                    return sp.name == null ? null : SettingResult(sp, 1);
                case "custom":
                    var cc = _s.CustomCommands.FirstOrDefault(x => x.Alias.Equals(val, StringComparison.OrdinalIgnoreCase));
                    return cc == null ? null : CustomResult(cc, 1);
            }
            return null;
        }

        List<Result> Discover(string prefix)
        {
            var list = new List<Result>();
            if (prefix.Length == 0)
            {
                list.Add(new Result { Header = true, Title = "MODES" });
                foreach (var p in Prefixes)
                {
                    string pp = p.prefix;
                    list.Add(new Result
                    {
                        Title = $"{p.prefix.Trim()}   {p.name}",
                        Sub = p.example,
                        Glyph = p.glyph,
                        Complete = pp,
                        Run = () =>
                        {
                            _box.Text = pp;
                            _box.CaretIndex = pp.Length;
                            return false;
                        }
                    });
                }
                list.Add(new Result { Header = true, Title = "ALSO WORKS" });
                foreach (var (t, sub) in new[]
                         {
                             ("remind me in 10 min to stretch", "reminders send a heft notification"),
                             ("timer 25m", "the bar counts down"),
                             ("pdfs from this week", "plain english file search"),
                             ("r screenshots > shelf", "pipe results into an action")
                         })
                {
                    string tt = t;
                    list.Add(new Result
                    {
                        Title = t,
                        Sub = sub,
                        Glyph = "\uE82F",
                        Complete = t,
                        Run = () =>
                        {
                            _box.Text = tt;
                            _box.CaretIndex = tt.Length;
                            return false;
                        }
                    });
                }
                return list;
            }

            var pre = Prefixes.First(x => x.prefix == prefix);
            list.Add(new Result { Title = pre.name, Sub = "e.g. " + pre.example, Glyph = pre.glyph, Run = () => false });
            switch (prefix)
            {
                case ">":
                    foreach (var c in _s.CustomCommands.Take(20)) list.Add(CustomResult(c, 1));
                    list.Add(new Result { Title = "> alias name = what to run", Sub = "make your own command. chain steps with ;   use wait 2, &shelf, !lock, type hello", Glyph = "\uE710", Run = () => false });
                    break;
                case "!":
                    return list.Concat(CommandResults("", "system", 40)).ToList();
                case "&":
                    return list.Concat(CommandResults("", "heft", 60)).ToList();
                case ":":
                    return list.Concat(ClipResults("")).ToList();
                case "/":
                    return list.Concat(Folder("")).ToList();
                case "@":
                    return list.Concat(_apps.Apps.Take(0).Select(a => AppResult(a, 1))).ToList();
            }
            list.Add(new Result { Header = true, Title = "PIPE ACTIONS (left > action)" });
            foreach (var v in new[] { "open", "reveal", "shelf", "collection <name>", "zip", "copy", "path", "move <folder>", "copyto <folder>", "convert png|jpg", "tag red|blue|...", "rename trip-{n}", "share", "delete" })
                list.Add(new Result { Title = v, Glyph = "\uE76C", Run = () => false });
            return list;
        }

        Result Web(string q)
        {
            bool url = Regex.IsMatch(q, @"^(https?://)?[\w-]+(\.[\w-]+)+(/\S*)?$") && !q.Contains(' ');
            if (url)
            {
                string u = q.StartsWith("http") ? q : "https://" + q;
                return new Result { Title = "Open " + q, Sub = u, Glyph = "\uE774", Run = () => Go(() => Launch.Url(u)) };
            }
            return new Result
            {
                Title = $"Search the web for \"{q}\"",
                Sub = Uri.TryCreate(_s.SearchEngine, UriKind.Absolute, out var engine) ? engine.Host : "web",
                Glyph = "\uE774",
                Run = () => Go(() => Launch.Url((_s.SearchEngine.Length > 0 ? _s.SearchEngine : "https://www.google.com/search?q=") + Uri.EscapeDataString(q)))
            };
        }

        static bool Go(Action a)
        {
            a();
            return true;
        }

        List<Result> Shell(string body)
        {
            var list = new List<Result>();
            var alias = Regex.Match(body, @"^alias\s+([^=]+?)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
            if (alias.Success)
            {
                string name = alias.Groups[1].Value.Trim(), run = alias.Groups[2].Value.Trim();
                list.Add(new Result
                {
                    Title = $"Save command \"{name}\"",
                    Sub = run,
                    Glyph = "\uE710",
                    Run = () =>
                    {
                        _s.CustomCommands.RemoveAll(c => c.Alias.Equals(name, StringComparison.OrdinalIgnoreCase));
                        _s.CustomCommands.Add(new CustomCommand { Alias = name, Run = run });
                        _s.Touch();
                        _notes.Post("Command saved", $"type {name} to run it", "\uE756");
                        return true;
                    }
                });
                return list;
            }
            foreach (var c in _s.CustomCommands)
            {
                double sc = Fuzzy.Score(body, c.Alias);
                if (sc > 0) list.Add(CustomResult(c, sc + 5));
            }
            list.Add(new Result { Title = $"Run: {body}", Sub = "opens a terminal and keeps it open", Glyph = "\uE756", Hint = "cmd", Run = () => Go(() => Commands.Shell(body, true)), Alt = () => Commands.Shell(body, false) });
            return Rank(list, body, 20);
        }

        Result CustomResult(CustomCommand c, double score) => new()
        {
            Key = "custom:" + c.Alias,
            Title = c.Alias,
            Sub = c.Run,
            Glyph = "\uE756",
            Hint = "your command",
            Score = score,
            Run = () =>
            {
                _ = _cmds.Script(c.Run);
                return true;
            }
        };

        Result AppResult(AppIndex.App a, double score) => new()
        {
            Key = "app:" + a.Id,
            Title = a.Name,
            Sub = a.IsPath ? a.Id : "app",
            IconPath = AppIndex.IconPath(a),
            Glyph = "\uE71D",
            Hint = "app",
            Score = score,
            Obj = a.IsPath ? Obj.From(a.Id) : new Obj { Kind = "uwp", Name = a.Name, Path = a.Id },
            Run = () => Go(() => AppIndex.Launch(a)),
            Alt = a.IsPath ? () => Files.Reveal(a.Id) : null
        };

        List<Result> AppResults(string q, int take)
        {
            var list = new List<Result>();
            foreach (var a in _apps.Apps)
            {
                double sc = Fuzzy.Score(q, a.Name);
                if (sc > 0) list.Add(AppResult(a, sc * 1.15));
            }
            return Rank(list, q, take);
        }

        Result SettingResult((string name, string uri, string keys) p, double score) => new()
        {
            Key = "set:" + p.uri,
            Title = p.name,
            Sub = p.uri.StartsWith("ms-settings") ? "Windows settings" : p.uri,
            Glyph = "\uE713",
            Hint = "setting",
            Score = score,
            Run = () => Go(() =>
            {
                var parts = p.uri.Split(' ', 2);
                Launch.Run(parts[0], parts.Length > 1 ? parts[1] : "");
            })
        };

        List<Result> SettingResults(string q, int take)
        {
            var list = new List<Result>();
            foreach (var p in SettingsPages)
            {
                double sc = Math.Max(Fuzzy.Score(q, p.name), p.keys.Split(' ').Max(k => Fuzzy.Score(q, k)) * 0.8);
                if (sc > 0) list.Add(SettingResult(p, sc * 0.7));
            }
            return Rank(list, q, take);
        }

        Result CommandResult(Command c, double score)
        {
            string? state = c.State?.Invoke();
            return new Result
            {
                Key = "cmd:" + c.Id,
                Title = c.Name,
                Sub = state ?? (c.Group == "heft" ? "heft" : "system"),
                Glyph = c.Glyph,
                Hint = c.Group == "heft" ? "&" : "!",
                Score = score,
                Danger = c.Danger,
                Run = () =>
                {
                    c.Run();
                    return true;
                }
            };
        }

        List<Result> CommandResults(string q, string? group, int take)
        {
            var list = new List<Result>();
            foreach (var c in _cmds.All)
            {
                if (group != null && c.Group != group) continue;
                double sc = Math.Max(Fuzzy.Score(q, c.Name), c.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(k => Fuzzy.Score(q, k) * 0.75).DefaultIfEmpty(0).Max());
                if (q.Length == 0) sc = 1;
                if (sc > 0) list.Add(CommandResult(c, sc));
            }
            return Rank(list, q, take);
        }

        List<Result> CalcResults(string q, bool forced)
        {
            var list = new List<Result>();
            if (Calc.TryConvert(q, out string conv))
            {
                list.Add(new Result { Title = conv, Sub = q, Glyph = "\uE8EF", Hint = "enter to copy", Score = 50, Run = () => Go(() => Files.CopyText(conv.Split(' ')[0])) });
            }
            else if (Calc.TryEval(q, out double v))
            {
                string s = Calc.Format(v);
                list.Add(new Result { Title = "= " + s, Sub = q, Glyph = "\uE8EF", Hint = "enter to copy", Score = 50, Run = () => Go(() => Files.CopyText(s)) });
            }
            else if (forced)
            {
                list.Add(new Result { Title = "...", Sub = "keep typing a math problem or something like 5 km to mi", Glyph = "\uE8EF", Run = () => false });
            }
            return list;
        }

        List<Result> ClipResults(string q)
        {
            var list = new List<Result>();
            foreach (var c in _clips.History)
            {
                double sc = q.Length == 0 ? 1 : Fuzzy.Score(q, c.Summary);
                if (sc <= 0) continue;
                var clip = c;
                var smart = Smart(c);
                list.Add(new Result
                {
                    Title = c.Summary.Length > 90 ? c.Summary[..90] + "..." : c.Summary,
                    Sub = (c.Pinned ? "pinned \u00b7 " : "") + smart.kind + " \u00b7 " + Kit.Ago(c.Time),
                    Glyph = smart.glyph,
                    Swatch = smart.swatch,
                    Thumb = c.Kind == "image" ? c.Image : null,
                    IconPath = smart.icon,
                    IconOnly = false,
                    Hint = "enter pastes",
                    Score = sc,
                    Run = () =>
                    {
                        _clips.Put(clip);
                        var prev = _previous;
                        Dismiss();
                        _ = PasteInto(prev);
                        return false;
                    },
                    Alt = () => _clips.Put(clip)
                });
            }
            return list.OrderByDescending(r => r.Score).Take(40).ToList();
        }

        static (string kind, string glyph, Color? swatch, string? icon) Smart(Clip c)
        {
            if (c.Kind == "image") return ("image", "\uEB9F", null, null);
            if (c.Kind == "files") return (c.Files.Count == 1 ? "file" : "files", "\uE8A5", null, c.Files.Count > 0 ? c.Files[0] : null);
            string t = c.Text.Trim();
            if (t.Length > 0 && t.Length <= 40 && t.IndexOf('\n') < 0)
            {
                string hex = t.StartsWith('#') ? t : "#" + t;
                bool hexish = t.StartsWith('#') || (t.Length == 6 && t.Any(ch => ch is >= 'a' and <= 'f' or >= 'A' and <= 'F'));
                if (hexish && System.Text.RegularExpressions.Regex.IsMatch(hex, "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
                {
                    try
                    {
                        return ("color " + hex.ToUpperInvariant(), "\uE790", (Color)ColorConverter.ConvertFromString(hex), null);
                    }
                    catch
                    {
                    }
                }
                var rgb = System.Text.RegularExpressions.Regex.Match(t, @"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})");
                if (rgb.Success && byte.TryParse(rgb.Groups[1].Value, out byte r) && byte.TryParse(rgb.Groups[2].Value, out byte g) && byte.TryParse(rgb.Groups[3].Value, out byte b))
                    return ("color", "\uE790", Color.FromRgb(r, g, b), null);
            }
            if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && t.IndexOf(' ') < 0)
                return ("link \u00b7 " + uri.Host.Replace("www.", ""), "\uE71B", null, null);
            if (t.Length > 2 && t.Length < 260 && t.IndexOf('\n') < 0 && (t[1] == ':' || t.StartsWith("\\\\")))
            {
                string p = t.Trim('"');
                try
                {
                    if (System.IO.File.Exists(p) || System.IO.Directory.Exists(p)) return ("path", "\uE8B7", null, p);
                }
                catch
                {
                }
            }
            if (t.Contains('@') && t.IndexOf(' ') < 0 && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}$")) return ("email", "\uE715", null, null);
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\d\s,.\-+*/()%^=]+$") && t.Any(char.IsDigit)) return ("number", "\uE8EF", null, null);
            if (t.Contains('\n') && (t.Contains('{') || t.Contains(';') || t.Contains("=>") || t.Contains("def ") || t.Contains("function"))) return ("code", "\uE943", null, null);
            return ("text", "\uE8C8", null, null);
        }

        static async Task PasteInto(IntPtr target)
        {
            if (target != IntPtr.Zero) Native.Focus(target);
            await Task.Delay(120);
            await Native.WaitForModifiersUp();
            Native.Combo(0x11, 0x56);
        }

        List<Result> Folder(string body)
        {
            var list = new List<Result>();
            string p = Environment.ExpandEnvironmentVariables(body.Trim());
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..];
            if (p.Length == 0)
            {
                foreach (var pl in _cat.PlaceItems()) list.Add(FolderResult(pl.Path, pl.Name, 1));
                return list;
            }
            string dir = p.EndsWith("\\") || p.EndsWith("/") ? p : Path.GetDirectoryName(p) ?? p;
            string part = p.EndsWith("\\") || p.EndsWith("/") ? "" : Path.GetFileName(p);
            if (p.Length == 2 && p[1] == ':')
            {
                dir = p + "\\";
                part = "";
            }
            if (!Directory.Exists(dir)) return list;
            foreach (var e in Library.List(dir, _s.ShowHidden))
            {
                double sc = part.Length == 0 ? 1 : Fuzzy.Score(part, e.Name);
                if (sc <= 0) continue;
                list.Add(FolderResult(e.Path, e.Name, sc + (e.Dir ? 0.5 : 0)));
            }
            return list.OrderByDescending(r => r.Score).Take(50).ToList();
        }

        Result FolderResult(string path, string name, double score)
        {
            bool dir = Directory.Exists(path);
            return new Result
            {
                Key = "file:" + path,
                Title = name,
                Sub = path,
                IconPath = path,
                IconOnly = dir,
                Glyph = dir ? "\uE8B7" : "\uE8A5",
                Hint = dir ? "tab to go in" : "",
                Score = score,
                Complete = dir ? "/" + path.TrimEnd('\\') + "\\" : null,
                Obj = Obj.From(path),
                Run = () => Go(() => Files.Open(path, _s)),
                Alt = dir ? () => Browse?.Invoke(path) : () => Files.Reveal(path)
            };
        }

        Result FileResult(string path, double score)
        {
            bool dir = Directory.Exists(path);
            string name = Path.GetFileName(path.TrimEnd('\\'));
            return new Result
            {
                Key = "file:" + path,
                Title = name.Length > 0 ? name : path,
                Sub = Path.GetDirectoryName(path) ?? path,
                IconPath = path,
                IconOnly = dir || Files.IsApp(path),
                Glyph = dir ? "\uE8B7" : "\uE8A5",
                Hint = dir ? "folder" : Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
                Score = score,
                Obj = Obj.From(path),
                Run = () => Go(() => Files.Open(path, _s)),
                Alt = () => Files.Reveal(path)
            };
        }

        List<Result> Everything(string q)
        {
            var list = new List<Result>();

            if (When.TryReminder(q, out var due, out var what))
            {
                list.Add(new Result
                {
                    Title = $"Remind you {(due.Date == DateTime.Today ? "at" : due.ToString("ddd"))} {due:h:mm tt}",
                    Sub = what,
                    Glyph = "\uE787",
                    Score = 100,
                    Run = () =>
                    {
                        _notes.Remind(due, what);
                        _notes.Post(new Note { Title = "Got it", Body = $"{what}  \u00b7  {due:h:mm tt}", Glyph = "\uE787" });
                        return true;
                    }
                });
            }
            if (When.TryTimer(q, out var span, out var label))
            {
                list.Add(new Result
                {
                    Title = $"Start a {(span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} min" : $"{span.TotalSeconds:0} sec")} timer",
                    Sub = label.Length > 0 ? label : "it shows in the top bar",
                    Glyph = "\uE916",
                    Score = 100,
                    Run = () =>
                    {
                        _notes.CancelTimer();
                        _notes.Remind(DateTime.Now + span, label, true);
                        return true;
                    }
                });
            }
            list.AddRange(CalcResults(q, false));

            foreach (var c in _s.CustomCommands)
            {
                double sc = Fuzzy.Score(q, c.Alias);
                if (c.Alias.Equals(q.Trim(), StringComparison.OrdinalIgnoreCase)) sc += 40;
                if (sc > 0) list.Add(CustomResult(c, sc * 1.2));
            }
            list.AddRange(AppResults(q, 12));
            list.AddRange(WindowResults(q, 6));
            list.AddRange(CommandResults(q, null, 8).Select(r =>
            {
                r.Score *= 0.85;
                return r;
            }));
            list.AddRange(SettingResults(q, 4));

            foreach (var kv in _s.FileOpens.OrderByDescending(k => k.Value).Take(200))
            {
                double sc = Fuzzy.Score(q, Path.GetFileName(kv.Key));
                if (sc > 0 && (File.Exists(kv.Key) || Directory.Exists(kv.Key))) list.Add(FileResult(kv.Key, sc * 0.9));
            }

            var ranked = Rank(list, q, 14);
            var nl = FileQuery.Parse(q, false);
            if (nl.Natural)
            {
                ranked.Insert(0, new Result { Header = true, Title = "FILES: " + nl.Describe.ToUpperInvariant() });
            }
            ranked.Add(Web(q));
            return ranked;
        }

        List<Result> WindowResults(string q, int take)
        {
            var list = new List<Result>();
            if (q.Trim().Length < 2) return list;
            List<AppWin> wins;
            try
            {
                wins = WinList.All();
            }
            catch
            {
                return list;
            }
            foreach (var w in wins)
            {
                string exe = w.Exe;
                double sc = Math.Max(Fuzzy.Score(q, w.Title) * 0.95, exe.Length > 0 ? Fuzzy.Score(q, exe) * 1.05 : 0);
                if (sc <= 0) continue;
                var hwnd = w.Hwnd;
                string title = w.Title.Length > 0 ? w.Title : exe;
                list.Add(new Result
                {
                    Key = "win:" + exe.ToLowerInvariant(),
                    Title = "Switch to " + title,
                    Sub = (exe.Length > 0 ? exe.ToLowerInvariant() + "  \u00b7  " : "") + (w.Min ? "minimized, " : "") + "shift+enter closes it",
                    Glyph = "\uE8A7",
                    IconPath = w.Path,
                    Hint = "open",
                    Score = sc * 1.1,
                    Run = () =>
                    {
                        Dispatcher.BeginInvoke(() => WinList.Switch(hwnd), DispatcherPriority.Background);
                        return true;
                    },
                    Alt = () => WinList.Close(hwnd)
                });
            }
            return list.OrderByDescending(r => r.Score).Take(take).ToList();
        }

        async Task RunFiles()
        {
            string text = _box.Text;
            var (prefix, body) = Split(text);
            string left = body, verb = "";
            var m = Regex.Match(body, @"^(.*?)\s+(>|\|)\s+(.*)$");
            if (m.Success && prefix != ">")
            {
                left = m.Groups[1].Value;
                verb = m.Groups[3].Value.Trim();
            }
            if (left.Trim().Length == 0) return;

            var q = FileQuery.Parse(left, prefix == "r ");
            bool natural = q.Natural || prefix == "r ";
            if (!natural)
            {
                q = FileQuery.Parse(left, true);
                q.Name = left.Trim();
            }

            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            List<string> paths;
            try
            {
                paths = await FileQuery.Run(q, natural ? 60 : 8, cts.Token);
            }
            catch
            {
                return;
            }
            if (cts.IsCancellationRequested || _box.Text != text) return;
            _cts = null;

            _pipeItems = paths;
            var files = paths.Select((p, i) => FileResult(p, (natural ? 30 : 0.5) - i * 0.01)).ToList();
            var current = _results.Where(r => r.Key.Length == 0 || !paths.Contains(r.Key.Replace("file:", ""), StringComparer.OrdinalIgnoreCase)).ToList();

            if (verb.Length > 0)
            {
                current.RemoveAll(r => r.Key == "pipe");
                current.Insert(0, PipeResult(prefix, left, verb, files));
                current.AddRange(files);
            }
            else if (natural)
            {
                int header = current.FindIndex(r => r.Header && r.Title.StartsWith("FILES"));
                if (header < 0)
                {
                    current.Insert(0, new Result { Header = true, Title = "FILES: " + (q.Describe.Length > 0 ? q.Describe : left).ToUpperInvariant() });
                    header = 0;
                }
                current.InsertRange(header + 1, files.Count > 0 ? files : new List<Result> { new() { Title = "no files matched", Sub = "windows search might still be indexing", Glyph = "\uE946", Run = () => false } });
            }
            else if (files.Count > 0)
            {
                int web = current.FindIndex(r => r.Title.StartsWith("Search the web"));
                if (web < 0) web = current.Count;
                current.Insert(web, new Result { Header = true, Title = "FILES" });
                current.InsertRange(web + 1, files);
            }

            var keep = Current();
            _results = current;
            int idx = keep != null ? _results.IndexOf(keep) : -1;
            _sel = idx >= 0 ? idx : Math.Max(0, _results.FindIndex(r => !r.Header));
            Render();
        }

        Result PipeResult(string prefix, string left, string verb, List<Result> source)
        {
            var parts = verb.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string v = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            List<string> Paths()
            {
                if (_pipeItems.Count > 0) return _pipeItems.ToList();
                return source.Where(r => r.Obj != null && (File.Exists(r.Obj.Path) || Directory.Exists(r.Obj.Path))).Select(r => r.Obj!.Path).ToList();
            }

            Result? Top() => source.FirstOrDefault(r => !r.Header && r.Run != null);

            int count = Paths().Count;
            string what = count > 0 ? $"{count} item{(count == 1 ? "" : "s")}" : (prefix == "r " || FileQuery.Parse(left, false).Natural ? "searching..." : Top()?.Title ?? "nothing");
            var r = new Result { Key = "pipe", Glyph = "\uE76C", Score = 999 };

            switch (v)
            {
                case "launch":
                case "open":
                case "run":
                    r.Title = $"{what}  \u2192  open";
                    r.Run = () =>
                    {
                        var t = Top();
                        if (count == 0 && t?.Run != null) return t.Run();
                        foreach (var p in Paths().Take(10)) Files.Open(p, _s);
                        return true;
                    };
                    break;
                case "reveal":
                    r.Title = $"{what}  \u2192  show in folder";
                    r.Run = () => Go(() =>
                    {
                        var p = Paths().FirstOrDefault();
                        if (p != null) Files.Reveal(p);
                    });
                    break;
                case "shelf":
                    r.Title = $"{what}  \u2192  shelf";
                    r.Run = () => Go(() => ToShelf?.Invoke(Paths()));
                    break;
                case "collection":
                case "collect":
                    string name = arg.Length > 0 ? arg : "Collected";
                    r.Title = $"{what}  \u2192  collection \"{name}\"";
                    r.Run = () => Go(() => ToCollection?.Invoke(name, Paths()));
                    break;
                case "zip":
                    r.Title = $"{what}  \u2192  zip";
                    r.Run = () => Go(() =>
                    {
                        var z = Files.Zip(Paths());
                        if (z != null) _notes.Post("Zipped", Path.GetFileName(z), "\uF012", z);
                    });
                    break;
                case "copy":
                    r.Title = $"{what}  \u2192  copy to clipboard";
                    r.Run = () => Go(() => Files.CopyFiles(Paths()));
                    break;
                case "path":
                case "paths":
                    r.Title = $"{what}  \u2192  copy paths";
                    r.Run = () => Go(() => Files.CopyText(string.Join(Environment.NewLine, Paths())));
                    break;
                case "move":
                case "copyto":
                    string dest = ResolveDir(arg);
                    bool copy = v == "copyto";
                    r.Title = $"{what}  \u2192  {(copy ? "copy" : "move")} to {(dest.Length > 0 ? dest : "(type a folder)")}";
                    r.Danger = !copy;
                    r.Run = () =>
                    {
                        if (!Directory.Exists(dest)) return false;
                        var ps = Paths();
                        if (Native.FileOp(copy, ps, dest)) _notes.Post(new Note { Title = copy ? "Copied" : "Moved", Body = $"{ps.Count} items to {Path.GetFileName(dest.TrimEnd('\\'))}", Glyph = "\uE8DE", Open = dest });
                        return true;
                    };
                    break;
                case "convert":
                    string ext = "." + (arg.Length > 0 ? arg.TrimStart('.').ToLowerInvariant() : "png");
                    if (ext == ".jpeg") ext = ".jpg";
                    r.Title = $"{what}  \u2192  convert to {ext.TrimStart('.').ToUpperInvariant()}";
                    r.Run = () => Go(() =>
                    {
                        int n = 0;
                        foreach (var p in Paths().Where(Files.IsImage))
                            if (Files.ConvertImage(p, ext) != null) n++;
                        _notes.Post("Converted", $"{n} images", "\uE8AB");
                    });
                    break;
                case "tag":
                    string[] names = { "red", "orange", "yellow", "green", "blue", "purple" };
                    int idx = Array.IndexOf(names, arg.ToLowerInvariant()) + 1;
                    r.Title = $"{what}  \u2192  tag {(idx > 0 ? names[idx - 1] : "(red, orange, yellow, green, blue, purple)")}";
                    r.Run = () =>
                    {
                        if (idx <= 0) return false;
                        foreach (var p in Paths()) _s.Bookmarks[p] = idx;
                        _s.Touch();
                        return true;
                    };
                    break;
                case "rename":
                    var plan = BulkRename.Plan(Paths(), arg);
                    if (arg.Length == 0)
                    {
                        r.Title = $"{what}  \u2192  rename (type a pattern)";
                        r.Sub = "{n} counts up, {name} keeps the old name, {date} is when it changed. like trip-{n} or {date} {name}";
                        r.Run = () => false;
                        break;
                    }
                    var firstOne = plan.FirstOrDefault();
                    r.Title = plan.Count == 0
                        ? $"{what}  \u2192  rename: nothing would change"
                        : $"{plan.Count} item{(plan.Count == 1 ? "" : "s")}  \u2192  {Path.GetFileName(firstOne.from)}  \u2192  {Path.GetFileName(firstOne.to)}{(plan.Count > 1 ? "  ..." : "")}";
                    r.Danger = plan.Count > 0;
                    r.Run = () =>
                    {
                        if (plan.Count == 0) return false;
                        int n = BulkRename.Apply(plan);
                        _notes.Post(new Note { Title = "Renamed", Body = $"{n} of {plan.Count} items. \"undo rename\" in the launcher puts them back", Glyph = "\uE8AC" });
                        return true;
                    };
                    break;
                case "share":
                case "send":
                case "phone":
                    r.Title = $"{what}  \u2192  send to phone";
                    r.Run = () => Go(() => Share?.Invoke(Paths()));
                    break;
                case "delete":
                case "trash":
                    r.Title = $"{what}  \u2192  move to recycle bin";
                    r.Danger = true;
                    r.Run = () => Go(() =>
                    {
                        int n = Paths().Count(Files.Recycle);
                        _notes.Post("Recycled", $"{n} items", "\uE74D");
                    });
                    break;
                default:
                    r.Title = $"don't know \"{v}\" yet";
                    r.Sub = "try open, reveal, shelf, collection <name>, zip, copy, path, move <folder>, copyto <folder>, convert png, tag blue, rename <pattern>, share, delete";
                    r.Run = () => false;
                    break;
            }
            if (r.Sub.Length == 0) r.Sub = r.Danger ? "enter twice to do it" : "enter to do it";
            return r;
        }

        string ResolveDir(string arg)
        {
            if (arg.Length == 0) return "";
            string a = Environment.ExpandEnvironmentVariables(arg.Trim().Trim('"'));
            if (Directory.Exists(a)) return a;
            foreach (var p in _cat.PlaceItems())
                if (p.Name.Equals(a, StringComparison.OrdinalIgnoreCase)) return p.Path;
            string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), a);
            return Directory.Exists(home) ? home : "";
        }
    }
}
