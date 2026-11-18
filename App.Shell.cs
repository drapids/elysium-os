using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Elysium.Ui;
using Microsoft.Win32;

namespace Elysium
{
    public partial class App
    {
        WindowFx? _fx;
        WindowShade? _shade;
        WindowBorders? _borders;
        AutoTile? _tiler;
        Osd? _osd;
        DockPreview? _dockPreview;
        AttentionBubble? _bubble;
        CalendarPanel? _calPanel;
        Calendars? _cals;
        DesktopPiles? _piles;
        Standby? _standby;
        SettingsPages? _pages;
        bool _atLogin, _shellReady;
        string _accentPath = "";
        readonly List<Toasts> _moreToasts = new();

        readonly DispatcherTimer _edgeGuard = new() { Interval = TimeSpan.FromMilliseconds(180) };
        int _edgesAsleep;

        void GuardEdges()
        {
            int level = EdgeGuard.Level();
            if (level == _edgesAsleep) return;
            _edgesAsleep = level;
            foreach (var t in _tabs) t.Sleep(level);
            foreach (var b in _bars)
            {
                if (level > 1) b.EdgeQuiet();
                b.GameQuiet(level > 1);
            }
        }

        void InitShell()
        {
            _keyHook.TaskManager += () => TaskMgr().Open();
            _keyHook.TaskKeys = _s.TaskManagerKeys;
            EdgeGuard.Gaming = () => _game;
            EdgeGuard.Learned = h => _gameWins.Contains(h);
            EdgeGuard.OwnClip = () => _lock != null && _lock.Locked;
            _edgeGuard.Tick += (_, _) => GuardEdges();
            _edgeGuard.Start();
            WinEvents.Foreground += _ => GuardEdges();
            _fx = new WindowFx(_s);
            _mouse.LeftDown += pt => _fx?.ArmClose(pt);
            _mouse.DesktopRight += DesktopMenu;
            _keyHook.AltF4 = () => _fx?.ArmForeground();
            _keyHook.SysKey += OnSysKey;
            _dock.Launching += (path, r) => _fx?.Launched(path, r);
            _dock.HoverTile += (path, r, side) =>
            {
                if (!_s.DockPreviews) return;
                if (_dockPreview == null)
                {
                    _dockPreview = new DockPreview();
                    _dockPreview.Picked += () => _dock.HideDock();
                }
                _dockPreview.Hover(path, r, side);
            };
            _shade = new WindowShade(_catalog) { Say = Flash };
            _borders = new WindowBorders();
            _tiler = new AutoTile(_s);
            if (_watch != null)
            {
                _watch.Opened += (h, path) =>
                {
                    _borders?.Opened(h);
                    if (!_game) _tiler?.Opened(h);
                };
                _watch.Closed += _ => _tiler?.Closed();
            }
            WinEvents.Minimized += (h, start) =>
            {
                if (start) _tiler?.Minimized(h);
            };
            WinEvents.Foreground += AccentFollow;
            _activity.Changed += () => Dispatcher.BeginInvoke(new Action(AttentionChanged));
            _cals = new Calendars(_s);
            _standby = new Standby(_s) { Info = StandbyData };
            _start.LiveText = LiveText;
            _start.Unread = (target, name) => DockBadge(new WheelItem { Path = target, Name = name });
            if (_pages != null)
            {
                _pages.WhatsNewRequested = () => new WhatsNew().Open();
                _pages.LockLookRequested = () => _ = MatchLockScreen();
                _pages.TaskManagerRequested = () => TaskMgr().Open();
                _pages.CalendarsChanged = () => _cals?.Refresh(true);
            }
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(new Action(Woke), DispatcherPriority.Background);
            };
            _shellReady = true;
            SyncShell();

            bool boot = _s.BootScreen == "always" || (_s.BootScreen == "startup" && _atLogin);
            if (boot) Moments.Boot(() =>
            {
                foreach (var b in _bars) b.Intro();
                Dispatcher.BeginInvoke(new Action(Welcome), DispatcherPriority.ApplicationIdle);
            });
            else Dispatcher.BeginInvoke(new Action(Welcome), DispatcherPriority.ApplicationIdle);
        }

        void Welcome()
        {
            if (!_s.SetupSeen)
            {
                if (Settings.Existed)
                {
                    _s.SetupSeen = true;
                    _s.Touch();
                }
                else
                {
                    var first = new FirstRun(_s) { StartWithWindows = on => Elysium.Startup.CanSet && Elysium.Startup.Set(on, _s.StartEarly) };
                    first.Open();
                    _s.SeenVersion = Changelog.Version;
                    _s.Touch();
                    return;
                }
            }
            if (_s.SeenVersion == Changelog.Version) return;
            _s.SeenVersion = Changelog.Version;
            _s.Touch();
            if (_s.ShowWhatsNew) new WhatsNew().Open();
        }

        void SyncShell()
        {
            if (!_shellReady) return;
            _fx?.Sync();
            _borders?.Sync(_s.ThemedBorders);
            _mouse.DesktopMenu = _s.DesktopMenu;
            _keyHook.TaskKeys = _s.TaskManagerKeys;
            if (_s.DesktopPiles && _piles == null)
            {
                _piles = new DesktopPiles(_s, _catalog) { ItemMenu = (p, el) => FileMenu(p, el) };
            }
            _piles?.Sync();
            if (!_s.DockPreviews) _dockPreview?.Close2();
            if (!_s.AttentionBounce)
            {
                _bubble?.Clear();
                foreach (var b in _bars) b.Glow(false);
            }
            if (_s.AccentMode != "app" && Theme.AppColor != null)
            {
                Theme.AppColor = null;
                _accentPath = "";
            }
            if (_s.CalendarOn) _cals?.Refresh();
        }

        void ShellTick()
        {
            if (_standby == null || !_s.StandbyOn || _standby.On || _game) return;
            if (Native.IdleSeconds() < Math.Clamp(_s.StandbyMinutes, 1, 120) * 60) return;
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && CursorLock.IsFullscreen(fg, out _)) return;
            if (Native.DisplayWanted()) return;
            _standby.Enter();
        }

        void ShellKeys(Dictionary<string, Action> map)
        {
            map["shade"] = () => _shade?.Toggle(Native.GetForegroundWindow());
            map["standby"] = () =>
            {
                if (_standby == null) return;
                if (_standby.On) _standby.Leave();
                else _standby.Enter();
            };
            map["retile"] = () => _tiler?.Now();
            map["taskmgr"] = () => TaskMgr().Toggle();
        }

        void ShellCommands(Commands c)
        {
            c.Add("elysium", "shade", "Roll this window up / down", "\uE70E", () => _shade?.Toggle(Native.GetForegroundWindow()), "shade roll up collapse title bar window");
            c.Add("elysium", "standby", "Standby mode", "\uE708", () => _standby?.Enter(), "standby clock screensaver idle away");
            c.Add("elysium", "taskmgr", "Task manager", "\uE9D9", () => TaskMgr().Open(), "task manager processes end task kill cpu memory ram performance startup apps");
            c.Add("elysium", "startup-apps", "Startup apps", "\uE7E8", () => TaskMgr().Open("startup"), "startup boot sign in launch on login task manager");
            c.Add("elysium", "retile", "Tile windows now", "\uF0E2", () => _tiler?.Now(), "tile tiling arrange grid windows");
            c.Add("elysium", "calendar", "Calendar", "\uE787", () =>
            {
                var d = BarDisplay();
                OpenCalendar(d, BarBottom(d), d.Bounds.Right - 160 * d.Scale);
            }, "calendar month events google apple ical");
            c.Add("elysium", "whatsnew", "What's new in elysium", "\uE789", () => new WhatsNew().Open(), "changelog update version");
            c.Add("elysium", "setup", "Run elysium setup again", "\uE713", () => new FirstRun(_s) { StartWithWindows = on => Elysium.Startup.CanSet && Elysium.Startup.Set(on, _s.StartEarly) }.Open(), "first run welcome onboarding");
            c.Add("elysium", "background-apps", "Background apps", "\uE70E", () =>
            {
                var b = _bars.FirstOrDefault();
                if (b != null) _ = TrayMenu(b);
            }, "tray system tray notification area discord steam");
        }

        TaskManager? _taskMgr;

        TaskManager TaskMgr()
        {
            if (_taskMgr != null) return _taskMgr;
            _taskMgr = new TaskManager(_s, _catalog, () => _activity.Gpu, () => (_activity.NetDown, _activity.NetUp))
            {
                SetKeys = on => _keyHook.TaskKeys = on
            };
            return _taskMgr;
        }

        void DisposeShell()
        {
            Try(() => _taskMgr?.Quit());
            _edgeGuard.Stop();
            Try(() => _fx?.Dispose());
            Try(() => _shade?.Dispose());
            Try(() => _borders?.Dispose());
            Try(() => _standby?.Leave());
            Try(Moments.Close);
            if (_piles != null) Try(() => DesktopIcons.Set(!_s.HideDesktop));
        }

        void WireShellBar(Bar b)
        {
            b.ClockClicked += (bar, x) => OpenCalendar(bar.Display, bar.BottomPx, x);
            b.RecClicked += () => _ = StopRecording();
            b.TrayClicked += host => _ = TrayMenu(host);
        }

        void OpenCalendar(Display d, double top, double x)
        {
            if (_cals == null) return;
            if (_calPanel == null)
            {
                _calPanel = new CalendarPanel(_s, _cals)
                {
                    Reminders = () => _s.Reminders,
                    OpenSettings = () => _panel.OpenTab("calendar")
                };
            }
            _calPanel.Toggle(d, top, x);
        }

        void ShowToast(Note n)
        {
            var all = Displays.All();
            var primary = Displays.Primary(all);
            string mode = _s.NotifyScreen;
            if (mode == "all" && all.Count > 1)
            {
                _toasts.Where = () => Displays.Primary(Displays.All());
                _toasts.Show(n);
                var others = all.Where(d => d.Index != primary.Index).ToList();
                while (_moreToasts.Count < others.Count)
                {
                    var t = new Toasts(_s, d => BarBottom(d));
                    t.Clicked += Toasts.OpenTarget;
                    _moreToasts.Add(t);
                }
                for (int i = 0; i < others.Count; i++)
                {
                    int index = others[i].Index;
                    var fallback = others[i];
                    _moreToasts[i].Where = () => Displays.All().FirstOrDefault(x => x.Index == index) ?? fallback;
                    _moreToasts[i].Show(n);
                }
                return;
            }
            if (mode == "main") _toasts.Where = () => Displays.Primary(Displays.All());
            else if (int.TryParse(mode, out int pick)) _toasts.Where = () => Displays.All().FirstOrDefault(x => x.Index == pick) ?? Displays.AtCursor();
            else _toasts.Where = null;
            _toasts.Show(n);
        }

        void Power(string kind, Action run)
        {
            if (_s.ShutdownScreen) Moments.Power(kind, run);
            else run();
        }

        void LockPc()
        {
            if (_s.LockLook) Moments.LockFlash(() => Native.LockWorkStation());
            else Native.LockWorkStation();
        }

        void Woke()
        {
            _standby?.Leave();
            if (!_s.WakeAnim) return;
            foreach (var b in _bars) b.Intro();
        }

        void OnSysKey(uint vk)
        {
            if (vk == 0x14)
            {
                if (_s.OsdStyle == "off") return;
                bool on = (Native.GetKeyState(0x14) & 1) != 0;
                ShowOsd("caps", "\uE765", on ? "caps lock on" : "caps lock off", -1);
                return;
            }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (_s.HideWinOsd) Osd.HideWindowsFlyout();
                if (_s.OsdStyle == "off") return;
                int v = Audio.Volume(out bool muted);
                if (v < 0) return;
                ShowOsd("vol", muted || v == 0 ? "\uE74F" : v < 34 ? "\uE993" : v < 67 ? "\uE994" : "\uE995", muted ? "muted" : "volume", muted ? 0 : v / 100.0);
            };
            t.Start();
        }

        void BrightnessShown(int v)
        {
            if (v < 0)
            {
                Flash("this screen doesn't do brightness over cable");
                return;
            }
            if (_s.OsdStyle == "off") Flash($"brightness {v}%");
            else ShowOsd("bright", "\uE706", "brightness", v / 100.0);
        }

        void ShowOsd(string key, string glyph, string text, double level)
        {
            if (_s.OsdStyle == "island")
            {
                var here = Displays.AtCursor();
                var bar = _bars.FirstOrDefault(b => b.IsVisible && b.Display.Index == here.Index) ?? _bars.FirstOrDefault(b => b.IsVisible);
                if (bar != null && !_game)
                {
                    bar.Pulse(glyph, level >= 0 ? text + "  " + Math.Round(level * 100) + "%" : text, level, 1500);
                    return;
                }
            }
            _osd ??= new Osd();
            _osd.Show(key, glyph, text, level);
        }

        void AttentionChanged()
        {
            if (!_s.AttentionBounce || _game) return;
            var h = _activity.Attention;
            if (h != IntPtr.Zero)
            {
                if (_bubble == null)
                {
                    _bubble = new AttentionBubble(_s, _catalog);
                    _bubble.Clicked += () => _activity.FocusAttention();
                }
                _bubble.Ping(h, Native.ProcessPath(h));
                foreach (var b in _bars) b.Glow(true);
            }
            else
            {
                _bubble?.Clear();
                foreach (var b in _bars) b.Glow(false);
            }
        }

        void AccentFollow(IntPtr h)
        {
            if (_s.AccentMode != "app" || h == IntPtr.Zero) return;
            var path = Native.ProcessPath(h);
            if (path == null || path.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            if (path.Equals(_accentPath, StringComparison.OrdinalIgnoreCase)) return;
            _accentPath = path;
            var color = AppAccent.For(path, _catalog);
            if (color == Theme.AppColor) return;
            Theme.AppColor = color;
            Theme.Apply(_s.Theme);
        }

        string? LiveText(string target, string name)
        {
            string exe = WinList.ExeOf(target).ToLowerInvariant();
            if (exe == "spotify" && _np.Title.Length > 0 && _np.HasSession) return _np.Title;
            if (name.Contains("weather", StringComparison.OrdinalIgnoreCase) && _start.Data.Weather.Length > 0) return _start.Data.Weather;
            return null;
        }

        StandbyInfo StandbyData()
        {
            var info = new StandbyInfo
            {
                Title = _np.HasSession ? _np.Title : "",
                Artist = _np.Artist,
                Art = _np.Art,
                Playing = _np.Playing,
                Pos = _np.Position,
                Dur = _np.Duration,
                Weather = _start.Data.Weather,
                WeatherGlyph = _start.Data.WeatherGlyph,
                Unseen = _notes.Unseen,
                Notes = _notes.History.Where(n => !n.Seen).Take(3).Select(n => (n.App.Length > 0 ? n.App + ": " : "") + n.Title).ToList()
            };
            if (_cals != null && _s.CalendarFeeds.Count > 0)
            {
                var next = _cals.On(DateTime.Today).Concat(_cals.On(DateTime.Today.AddDays(1))).FirstOrDefault(e => e.End > DateTime.Now);
                if (next != null) info.NextEvent = next.Title + "  ·  " + (next.AllDay ? (next.Start.Date == DateTime.Today ? "today" : "tomorrow") : next.Start.ToString("h:mm tt").ToLowerInvariant());
            }
            var soon = _s.Reminders.Where(r => r.Due > DateTime.Now).OrderBy(r => r.Due).FirstOrDefault();
            if (info.NextEvent.Length == 0 && soon != null && soon.Due.Date == DateTime.Today) info.NextEvent = (soon.Text.Length > 0 ? soon.Text : "reminder") + "  ·  " + soon.Due.ToString("h:mm tt").ToLowerInvariant();
            return info;
        }

        void DesktopMenu(Native.POINT pt)
        {
            bool icon = false;
            try
            {
                var el = System.Windows.Automation.AutomationElement.FromPoint(new Point(pt.X, pt.Y));
                icon = el != null && el.Current.ControlType == System.Windows.Automation.ControlType.ListItem;
            }
            catch
            {
            }
            if (icon)
            {
                Expander.RightClick();
                return;
            }
            UIElement? owner = _bars.FirstOrDefault(b => b.IsVisible);
            owner ??= _start;
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var items = new List<MenuEntry>
            {
                MenuEntry.Item("\uE8F4", "new folder", () =>
                {
                    string path = Path.Combine(desk, "New folder");
                    for (int i = 2; Directory.Exists(path); i++) path = Path.Combine(desk, $"New folder ({i})");
                    try
                    {
                        Directory.CreateDirectory(path);
                        _actions.Rename?.Invoke(path);
                    }
                    catch
                    {
                        Flash("couldn't make a folder there");
                    }
                })
            };
            if (Clipboard.ContainsFileDropList())
                items.Add(MenuEntry.Item("\uE77F", "paste", () =>
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                    if (files.Count > 0) Native.FileOp(true, files, desk);
                }));
            items.Add(MenuEntry.Sep());
            items.Add(MenuEntry.Toggle("\uE7F4", "desktop icons", !_s.HideDesktop, () =>
            {
                _s.HideDesktop = !_s.HideDesktop;
                if (!_s.DesktopPiles) DesktopIcons.Set(!_s.HideDesktop);
                _s.Touch();
            }));
            items.Add(MenuEntry.Toggle("\uF156", "piles", _s.DesktopPiles, () =>
            {
                _s.DesktopPiles = !_s.DesktopPiles;
                _s.Touch();
            }));
            if (_s.AutoTile) items.Add(MenuEntry.Item("\uF0E2", "tile windows now", () => _tiler?.Now()));
            items.Add(MenuEntry.Sep());
            items.Add(MenuEntry.Item("\uE771", "change wallpaper", () => Launch.Run("ms-settings:personalization-background")));
            items.Add(MenuEntry.Item("\uE790", "elysium look", () => _panel.OpenTab("look")));
            items.Add(MenuEntry.Item("\uE7F4", "display settings", () => Launch.Run("ms-settings:display")));
            items.Add(MenuEntry.Item("\uE756", "terminal here", () =>
            {
                string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
                if (File.Exists(wt)) Launch.Run(wt, $"-d \"{desk}\"");
                else Launch.Run("cmd.exe", $"/k cd /d \"{desk}\"");
            }));
            items.Add(MenuEntry.Sep());
            items.Add(MenuEntry.Item("\uE712", "more options", Expander.RightClick));
            GlassMenu.Show(owner, items);
        }

        void FileMenu(string p, FrameworkElement el)
        {
            var items = new List<MenuEntry>
            {
                MenuEntry.Item("\uE8A7", "open", () => Files.Open(p, _s)),
                MenuEntry.Item("\uE946", "quick look", () => InspectObj(Obj.From(p), null, null)),
                MenuEntry.Item("\uE838", "show in folder", () => Files.Reveal(p)),
                MenuEntry.Item("\uE8C8", "copy", () => Files.CopyFiles(new[] { p })),
                MenuEntry.Item("\uE8AC", "rename", () => _actions.Rename?.Invoke(p)),
                MenuEntry.Sep(),
                MenuEntry.Item("\uE74D", "delete", () =>
                {
                    if (Files.Recycle(p)) Flash("moved to the recycle bin");
                }, true)
            };
            GlassMenu.Show(el, items);
        }

        async Task TrayMenu(FrameworkElement host)
        {
            var hidden = new HashSet<string>(_s.HiddenTrayApps, StringComparer.OrdinalIgnoreCase);
            var apps = await Task.Run(() => BackgroundApps.Scan(hidden));
            var items = new List<MenuEntry> { MenuEntry.Title("running in the background") };
            if (apps.Count == 0) items.Add(MenuEntry.Item("\uE73E", "nothing right now", () => { }));
            foreach (var a in apps.Take(20))
            {
                var app = a;
                items.Add(MenuEntry.Sub("\uE71D", app.Name, new List<MenuEntry>
                {
                    MenuEntry.Item("\uE8A7", "open it", () => BackgroundApps.Open(app)),
                    MenuEntry.Item("\uE711", "quit", () => BackgroundApps.Quit(app)),
                    MenuEntry.Item("\uE74D", "end task", () => BackgroundApps.Kill(app), true),
                    MenuEntry.Item("\uE7B3", "hide from this list", () =>
                    {
                        _s.HiddenTrayApps.Add(app.Exe);
                        _s.Touch();
                    })
                }));
            }
            items.Add(MenuEntry.Sep());
            items.Add(MenuEntry.Item("\uE9D9", "task manager", () => TaskMgr().Open()));
            if (_s.HiddenTrayApps.Count > 0)
            {
                items.Add(MenuEntry.Sep());
                items.Add(MenuEntry.Item("\uE7B3", "show hidden ones again", () =>
                {
                    _s.HiddenTrayApps.Clear();
                    _s.Touch();
                }));
            }
            GlassMenu.Show(host, items);
        }

        async Task MatchLockScreen()
        {
            string? err = await LockLook.Apply();
            Flash(err == null ? "lock screen matches your theme now" : "couldn't change the lock screen: " + err);
        }
    }
}
