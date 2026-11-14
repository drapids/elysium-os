using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Elysium.Ui
{
    internal sealed partial class SettingsPages
    {
        public static readonly (string id, string label)[] Tabs =
        {
            ("general", "General"),
            ("look", "Look"),
            ("dock", "Dock & physics"),
            ("windows", "Windows"),
            ("files", "Files"),
            ("shell", "Bar & desktop"),
            ("audio", "Audio"),
            ("alerts", "Alerts & focus"),
            ("calendar", "Calendar"),
            ("search", "Search & files"),
            ("extensions", "Extensions"),
            ("keys", "Keys"),
            ("share", "Share"),
            ("about", "About")
        };

        readonly Settings _s;
        readonly AudioFlow _audio;
        readonly Func<IEnumerable<string>> _keyErrors;
        readonly Func<string> _toastStatus;
        readonly Action<Action> _dialog;

        public Action? SortDownloads;
        public Action? FindDupes;

        public SettingsPages(Settings s, AudioFlow audio, Func<IEnumerable<string>> keyErrors, Func<string> toastStatus, Action<Action> dialog)
        {
            _s = s;
            _audio = audio;
            _keyErrors = keyErrors;
            _toastStatus = toastStatus;
            _dialog = dialog;
        }

        void Touch() => _s.Touch();

        CheckBox Sw(bool v, Action<bool> set) => Kit.Switch(v, x =>
        {
            set(x);
            Touch();
        });

        static string Pct(double v) => $"{Math.Round(v * 100)}%";

        string _page = "";
        Panel? _host;

        void Rebuild()
        {
            if (_host == null) return;
            if (_query.Length > 0 && _go != null) Find(_query, _host, _go, false);
            else Build(_page, _host);
        }

        public void Build(string page, Panel host)
        {
            _page = page;
            _host = host;
            _query = "";
            host.Children.Clear();
            switch (page)
            {
                case "general":
                    General(host);
                    GeneralMore(host);
                    break;
                case "look":
                    LookPage(host);
                    break;
                case "windows":
                    WindowsPage(host);
                    WindowMore(host);
                    break;
                case "files":
                    FilesPage(host);
                    break;
                case "shell":
                    Shell(host);
                    ShellMore(host);
                    break;
                case "audio":
                    AudioPage(host);
                    break;
                case "alerts":
                    Alerts(host);
                    AlertsMore(host);
                    break;
                case "calendar":
                    CalendarPage(host);
                    break;
                case "about":
                    AboutPage(host);
                    break;
                case "search":
                    Search(host);
                    break;
                case "extensions":
                    ExtensionsPage(host);
                    break;
                case "keys":
                    Keys(host);
                    break;
                case "share":
                    Share(host);
                    break;
            }
        }

        void General(Panel h)
        {
            h.Children.Add(Kit.Header("Startup"));
            var early = Sw(_s.StartEarly, v =>
            {
                _s.StartEarly = v;
                if (_s.StartWithWindows) ApplyStartup(true, v);
            });
            early.IsEnabled = _s.StartWithWindows;
            var start = Sw(_s.StartWithWindows, v =>
            {
                _s.StartWithWindows = v;
                early.IsEnabled = v;
                ApplyStartup(v, _s.StartEarly);
            });
            string startCaption = Startup.CanSet
                ? "elysium opens on its own when you sign in, in the background"
                : "works once you run the built elysium.exe (not dotnet run)";
            h.Children.Add(Kit.Group(
                Kit.Row("Start with Windows", startCaption, start),
                Kit.Row("Start before other apps", "opens the second you sign in, ahead of the normal startup list. off puts elysium in the regular startup list instead", early)));

            h.Children.Add(Kit.Header("Game mode"));
            var skip = Kit.Field(string.Join(", ", _s.GameSkip), "vlc, obs64", v =>
            {
                _s.GameSkip = v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)).Where(x => x.Length > 0).ToList();
                Touch();
            });
            skip.Width = 170;
            h.Children.Add(Kit.Group(
                Kit.Row("Game mode", "when something goes fullscreen elysium gets quiet: no sounds, slower polling, nothing refreshing in the background", Sw(_s.GameMode, v => _s.GameMode = v)),
                Kit.Row("Hold notifications", "they wait in the bell and you get one summary when you're out", Sw(_s.GameHoldToasts, v => _s.GameHoldToasts = v)),
                Kit.Row("Ignore the Windows key", "so tapping it mid game doesn't pop Start", Sw(_s.GameBlockWin, v => _s.GameBlockWin = v)),
                Kit.Row("Not games", "apps that go fullscreen but shouldn't count", skip)));

            h.Children.Add(Kit.Header("Sounds"));
            var events = new StackPanel();
            foreach (var (id, label) in Sfx.Events)
            {
                string ev = id;
                bool on = !_s.SoundEvents.TryGetValue(ev, out bool cur) || cur;
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                var play = Kit.Plain("\uE768", () => Sfx.Test(ev), "play it", 10);
                play.Margin = new Thickness(0, 0, 10, 0);
                right.Children.Add(play);
                right.Children.Add(Sw(on, v => _s.SoundEvents[ev] = v));
                if (events.Children.Count > 0) events.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Sep") });
                events.Children.Add(Kit.Row(label, null, right));
            }
            h.Children.Add(Kit.Group(
                Kit.Row("Sounds", "off by default. soft little clicks and chimes for elysium stuff", Sw(_s.SoundsOn, v =>
                {
                    _s.SoundsOn = v;
                    if (v) Sfx.Test("chime");
                })),
                Kit.SliderRow("Volume", 0, 1, _s.SoundVolume, Pct, v =>
                {
                    _s.SoundVolume = v;
                    Touch();
                    Sfx.Test("tick");
                }),
                Kit.Block("Pack", null, Kit.Segments(Sfx.AllPacks().Select(p => p.label).ToArray(), Sfx.AllPacks().Select(p => p.id).ToArray(), _s.SoundPack, v =>
                {
                    _s.SoundPack = v;
                    Touch();
                    Sfx.Test("notify");
                })),
                Kit.Row("Quieter while music plays", null, Sw(_s.SoundDuck, v => _s.SoundDuck = v))));
            h.Children.Add(Kit.Group(events));

            h.Children.Add(Kit.Header("Shortcuts"));
            var cap = Kit.Caption("ctrl + alt + y shows every shortcut elysium has. click one to do it");
            cap.Margin = new Thickness(14, 4, 14, 0);
            h.Children.Add(cap);
        }

        void ApplyStartup(bool on, bool early)
        {
            string exe = Environment.ProcessPath ?? "";
            var ui = Application.Current?.Dispatcher;
            System.Threading.Tasks.Task.Run(() => Startup.Set(on, early)).ContinueWith(t =>
            {
                bool ok = !t.IsFaulted && t.Result;
                ui?.BeginInvoke(new Action(() =>
                {
                    _s.StartupPath = on && ok ? exe : "";
                    _s.Touch();
                }));
            });
        }

        void WindowsPage(Panel h)
        {
            h.Children.Add(Kit.Header("Snap zones"));
            var snapRows = new List<UIElement>
            {
                Kit.Row("Snap zones", "hold shift while you drag a window and drop it into a zone", Sw(_s.SnapZones, v => _s.SnapZones = v)),
                Kit.SliderRow("Gap", 0, 24, _s.SnapGap, v => v < 1 ? "none" : Math.Round(v) + "px", v =>
                {
                    _s.SnapGap = Math.Round(v);
                    Touch();
                })
            };
            var layoutIds = SnapZones.Layouts.Select(l => l.id).ToArray();
            var layoutLabels = SnapZones.Layouts.Select(l => l.label).ToArray();
            foreach (var d in Displays.All())
            {
                string key = d.Key;
                string cur = _s.SnapLayouts.TryGetValue(key, out var lay) && lay.Length > 0 ? lay : "auto";
                snapRows.Add(Kit.Block($"Monitor {d.Index + 1}{(d.Primary ? " (main)" : "")}", null, Kit.Segments(layoutLabels, layoutIds, cur, v =>
                {
                    _s.SnapLayouts[key] = v;
                    Touch();
                })));
            }
            h.Children.Add(Kit.Group(snapRows.ToArray()));
            RulesSection(h);
            WindowExtras(h);

            h.Children.Add(Kit.Header("Window lerp"));
            h.Children.Add(Kit.Group(
                Kit.Row("Lerp", "drag a window by its title bar and it trails your mouse smoothly, cutting corners. hold ctrl to drag the normal way. needs window physics on", Sw(_s.Lerp, v => _s.Lerp = v)),
                Kit.SliderRow("Lag", 0, 1, _s.LerpLag, v => v < 0.2 ? "tight" : v < 0.5 ? "smooth" : v < 0.8 ? "floaty" : "lazy", v =>
                {
                    _s.LerpLag = v;
                    Touch();
                }),
                Kit.SliderRow("Wobble", 0, 1, _s.LerpWobble, v => v < 0.1 ? "none" : Pct(v), v =>
                {
                    _s.LerpWobble = v;
                    Touch();
                }),
                Kit.Row("Snap to edges", "let go at the left or right edge for half the screen, top edge maximizes", Sw(_s.LerpSnap, v => _s.LerpSnap = v))));

            h.Children.Add(Kit.Header("Window stacks"));
            h.Children.Add(Kit.Group(
                Kit.Row("Stack windows into tabs", "drag a title bar onto another window's title bar and hold for a sec. click tabs to switch, scroll to flip, middle-click a tab to pull it out", Sw(_s.WindowStacks, v => _s.WindowStacks = v))));

            h.Children.Add(Kit.Header("Picture in picture"));
            h.Children.Add(Kit.Group(
                Kit.Row("How", "ctrl + alt + o pops the window you're on into a small always-on-top window in the corner. same keys (or the little bar on it) put it back", null),
                Kit.SliderRow("Size", 0.12, 0.45, _s.PipSize, Pct, v =>
                {
                    _s.PipSize = v;
                    Touch();
                })));
        }

        void FilesPage(Panel h)
        {
            h.Children.Add(Kit.Header("Shelf"));
            h.Children.Add(Kit.Group(
                Kit.Row("Piles", "once the shelf gets busy, same kind of stuff stacks into a pile. click to spread it out, drag it to take the whole pile", Sw(_s.ShelfPiles, v => _s.ShelfPiles = v)),
                Kit.Row("Take things off when you drag them out", "turn off to leave a copy on the shelf", Sw(_s.ShelfTakeOut, v => _s.ShelfTakeOut = v))));

            h.Children.Add(Kit.Header("Preview"));
            h.Children.Add(Kit.Group(
                Kit.Row("Space previews in File Explorer", "select a file and hit space, like quick look on a mac", Sw(_s.PreviewSpace, v => _s.PreviewSpace = v))));

            h.Children.Add(Kit.Header("Downloads"));
            var sortNow = Kit.Push("Sort now", () => SortDownloads?.Invoke());
            h.Children.Add(Kit.Group(
                Kit.Row("Sort Downloads into folders", "images, videos, docs, installers and so on, once a file has sat there a few minutes", Sw(_s.AutoSortDownloads, v => _s.AutoSortDownloads = v)),
                Kit.Row("Tidy it right now", null, sortNow)));

            h.Children.Add(Kit.Header("Version shelf"));
            string folders = _s.VersionFolders.Count > 0 ? string.Join(", ", _s.VersionFolders) : VersionShelf.DefaultFolders();
            h.Children.Add(Kit.Group(
                Kit.Row("Keep older versions", "every time a file changes elysium keeps a copy, so you can go back from inspect. off by default", Sw(_s.VersionsOn, v => _s.VersionsOn = v)),
                Kit.Block("Folders it watches", "comma separated", Kit.Field(folders, "Documents, Desktop", v =>
                {
                    var list = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(System.IO.Directory.Exists).ToList();
                    _s.VersionFolders = list;
                    Touch();
                })),
                Kit.SliderRow("Copies per file", 3, 60, _s.VersionKeep, v => $"{v:0}", v =>
                {
                    _s.VersionKeep = (int)Math.Round(v);
                    Touch();
                })));

            h.Children.Add(Kit.Header("Send to phone"));
            h.Children.Add(Kit.Group(
                Kit.Row("How", "inspect a file > send to phone, or type > share after a search in the launcher. scan the code with your phone, same wi-fi", null),
                Kit.SliderRow("Link lasts", 1, 60, _s.ShareMinutes, v => $"{v:0} min", v =>
                {
                    _s.ShareMinutes = (int)Math.Round(v);
                    Touch();
                })));

            h.Children.Add(Kit.Header("Projects & duplicates"));
            h.Children.Add(Kit.Group(
                Kit.Row("On the shelf", "the shelf has Projects (anything with git, package.json, .sln...) and Duplicates tabs", null),
                Kit.Row("Look for duplicates now", null, Kit.Push("Scan", () => FindDupes?.Invoke()))));
        }

        void Shell(Panel h)
        {
            h.Children.Add(Kit.Header("Top bar"));
            h.Children.Add(Kit.Group(
                Kit.Row("Show the top bar", null, Sw(_s.BarEnabled, v => _s.BarEnabled = v)),
                Kit.Row("Screens", null, Kit.Segments(new[] { "Main", "All" }, new[] { "primary", "all" }, _s.BarScreens, v =>
                {
                    _s.BarScreens = v;
                    Touch();
                })),
                Kit.Row("Auto-hide", "slides down when your mouse hits the top", Sw(_s.BarAutoHide, v => _s.BarAutoHide = v)),
                Kit.SliderRow("Height", 22, 44, _s.BarHeight, v => $"{v:0}", v =>
                {
                    _s.BarHeight = Math.Round(v);
                    Touch();
                }),
                Kit.SliderRow("Tint", 0, 1, _s.BarTint, Pct, v =>
                {
                    _s.BarTint = v;
                    Touch();
                }),
                Kit.Row("Blur behind", null, Sw(_s.BarBlur, v => _s.BarBlur = v))));

            h.Children.Add(Kit.Header("What the bar shows"));
            h.Children.Add(Kit.Group(
                Kit.Row("App name", null, Sw(_s.BarApp, v => _s.BarApp = v)),
                Kit.Row("Music in the pill", "track, art and controls while something plays", Sw(_s.BarMedia, v => _s.BarMedia = v)),
                Kit.Row("Date", null, Sw(_s.BarDate, v => _s.BarDate = v)),
                Kit.Row("Seconds", null, Sw(_s.BarSeconds, v => _s.BarSeconds = v)),
                Kit.Row("24-hour clock", null, Sw(_s.Bar24h, v => _s.Bar24h = v)),
                Kit.Row("CPU / GPU / RAM", null, Sw(_s.BarStats, v => _s.BarStats = v)),
                Kit.Row("Volume", null, Sw(_s.BarVolume, v => _s.BarVolume = v)),
                Kit.Row("Battery", null, Sw(_s.BarBattery, v => _s.BarBattery = v))));

            BarEditor(h);
            ShellExtras(h);

            h.Children.Add(Kit.Header("Taskbar"));
            h.Children.Add(Kit.Group(
                Kit.Block("Look", null, Kit.Segments(new[] { "Normal", "Clear", "Blur", "Acrylic" }, new[] { "normal", "clear", "blur", "acrylic" }, _s.TaskbarStyle, v =>
                {
                    _s.TaskbarStyle = v;
                    Touch();
                })),
                Kit.SliderRow("Tint", 0, 1, _s.TaskbarTint, Pct, v =>
                {
                    _s.TaskbarTint = v;
                    Touch();
                }),
                Kit.Block("Visibility", null, Kit.Segments(new[] { "Always", "Auto-hide", "Gone" }, new[] { "off", "auto", "hidden" }, _s.TaskbarHide, v =>
                {
                    _s.TaskbarHide = v;
                    Touch();
                })),
                Kit.Row("Windows key opens elysium Start", "fullscreen glass start with your real Start pins, taskbar apps, widgets and every app. ctrl + esc still opens the old one", Sw(_s.WinKeyLauncher, v => _s.WinKeyLauncher = v)),
                Kit.Row("Center the icons", null, Sw(_s.TaskbarCenter, v => _s.TaskbarCenter = v)),
                Kit.Row("Hide the Start button", "the windows key still works", Sw(_s.HideStart, v => _s.HideStart = v)),
                Kit.Row("Hide the tray", "clock and tray icons. the top bar covers it", Sw(_s.HideTray, v => _s.HideTray = v))));

            h.Children.Add(Kit.Header("Desktop"));
            h.Children.Add(Kit.Group(
                Kit.Row("Hide desktop icons", "nothing gets deleted. it's all still on the shelf and in the dock", Sw(_s.HideDesktop, v => _s.HideDesktop = v)),
                Kit.SliderRow("Peek for", 3, 30, _s.PeekSeconds, v => $"{v:0}s", v =>
                {
                    _s.PeekSeconds = Math.Round(v);
                    Touch();
                })));
            var peek = Kit.Caption("double-click empty desktop (or ctrl + alt + e) to peek at the icons");
            peek.Margin = new Thickness(14, 4, 14, 0);
            h.Children.Add(peek);

            h.Children.Add(Kit.Header("Monitors"));
            h.Children.Add(Kit.Group(
                Kit.Block("Dock edge goes on", "outer edges only, never the seam between screens", Kit.Segments(new[] { "One", "Mirror", "Every", "Main", "Cursor" }, new[] { "outer", "mirror", "all", "primary", "cursor" }, _s.DockScreens, v =>
                {
                    _s.DockScreens = v;
                    Touch();
                })),
                Kit.Row("Lock the cursor in fullscreen games", "keeps it from sliding onto the other monitor. ctrl + alt + l anytime", Sw(_s.AutoLockFullscreen, v => _s.AutoLockFullscreen = v))));

            h.Children.Add(Kit.Header("Hot corners"));
            h.Children.Add(Kit.Group(
                Kit.Block("Top left", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Spaces", "Center", "Desktop" }, new[] { "none", "start", "overview", "spaces", "center", "desktop" }, _s.HotCorners.TryGetValue("tl", out var vtl) ? vtl : "none", v =>
                {
                    _s.HotCorners["tl"] = v;
                    Touch();
                })),
                Kit.Block("Top right", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Spaces", "Center", "Desktop" }, new[] { "none", "start", "overview", "spaces", "center", "desktop" }, _s.HotCorners.TryGetValue("tr", out var vtr) ? vtr : "none", v =>
                {
                    _s.HotCorners["tr"] = v;
                    Touch();
                })),
                Kit.Block("Bottom left", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Spaces", "Center", "Desktop" }, new[] { "none", "start", "overview", "spaces", "center", "desktop" }, _s.HotCorners.TryGetValue("bl", out var vbl) ? vbl : "none", v =>
                {
                    _s.HotCorners["bl"] = v;
                    Touch();
                })),
                Kit.Block("Bottom right", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Spaces", "Center", "Desktop" }, new[] { "none", "start", "overview", "spaces", "center", "desktop" }, _s.HotCorners.TryGetValue("br", out var vbr) ? vbr : "none", v =>
                {
                    _s.HotCorners["br"] = v;
                    Touch();
                }))));
            var hc = Kit.Caption("push your mouse into a corner of any screen. games in fullscreen are left alone");
            hc.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(hc);

            h.Children.Add(Kit.Header("Gestures"));
            h.Children.Add(Kit.Group(
                Kit.Row("Scroll the top bar", "volume", Sw(_s.GestureVolume, v => _s.GestureVolume = v)),
                Kit.Row("Scroll the screen edge", "brightness, on the side opposite the dock", Sw(_s.GestureBrightness, v => _s.GestureBrightness = v)),
                Kit.Row("Middle-click the top bar", "play / pause", Sw(_s.GestureMedia, v => _s.GestureMedia = v)),
                Kit.Row("Scroll a window's title bar", "see-through windows", Sw(_s.TitleOpacity, v => _s.TitleOpacity = v)),
                Kit.Row("Middle-click a title bar", "pin it on top", Sw(_s.TitlePin, v => _s.TitlePin = v))));
        }

        void AudioPage(Panel h)
        {
            AudioExtras(h);
            h.Children.Add(Kit.Header("Switching"));
            h.Children.Add(Kit.Group(
                Kit.Row("Switch automatically", "when a device higher on your list plugs in, it takes over", Sw(_s.AudioAutoSwitch, v => _s.AudioAutoSwitch = v))));

            h.Children.Add(Kit.Header("Priority"));
            var list = new StackPanel();
            var all = _audio.Devices(false).Concat(_audio.Devices(true)).ToList();
            var ordered = _s.DevicePriority.Select(id => all.FirstOrDefault(d => d.Id == id)).Where(d => d != null).Select(d => d!).ToList();
            ordered.AddRange(all.Where(d => !_s.DevicePriority.Contains(d.Id)));
            foreach (var d in ordered)
            {
                var dev = d;
                bool ranked = _s.DevicePriority.Contains(d.Id);
                var row = new DockPanel { Margin = new Thickness(14, 4, 8, 4) };
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(Kit.Plain("\uE70E", () =>
                {
                    Rank(dev.Id, -1);
                    Build("audio", h);
                }, "higher", 10));
                btns.Children.Add(Kit.Plain("\uE70D", () =>
                {
                    Rank(dev.Id, 1);
                    Build("audio", h);
                }, "lower", 10));
                if (ranked)
                    btns.Children.Add(Kit.Plain("\uE711", () =>
                    {
                        _s.DevicePriority.Remove(dev.Id);
                        Touch();
                        Build("audio", h);
                    }, "unrank", 9));
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(Kit.Text(d.Name, 12.5, ranked));
                name.Children.Add(Kit.Caption((d.Input ? "mic" : "output") + (ranked ? $"  ·  #{_s.DevicePriority.IndexOf(d.Id) + 1}" : "  ·  not ranked")));
                row.Children.Add(name);
                list.Children.Add(row);
            }
            h.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Group"), Child = list });

            h.Children.Add(Kit.Header("Profiles"));
            if (_s.AudioProfiles.Count == 0)
            {
                var c = Kit.Caption("save one from AudioFlow (click the volume in the top bar)");
                c.Margin = new Thickness(14, 0, 14, 0);
                h.Children.Add(c);
            }
            else
            {
                var rows = _s.AudioProfiles.Select(p =>
                {
                    var prof = p;
                    var field = Kit.Field(p.Hotkey, "hotkey, like Ctrl+Alt+1", v =>
                    {
                        prof.Hotkey = v.Trim();
                        Touch();
                    });
                    field.Width = 150;
                    return (UIElement)Kit.Row(p.Name, $"{p.Volumes.Count} app volumes saved", field);
                }).ToArray();
                h.Children.Add(Kit.Group(rows));
            }
        }

        void Rank(string id, int dir)
        {
            var list = _s.DevicePriority;
            int i = list.IndexOf(id);
            if (i < 0)
            {
                list.Add(id);
                i = list.Count - 1;
                if (dir > 0)
                {
                    Touch();
                    return;
                }
            }
            int j = Math.Clamp(i + dir, 0, list.Count - 1);
            if (i != j) (list[i], list[j]) = (list[j], list[i]);
            Touch();
        }

        void Alerts(Panel h)
        {
            ModesSection(h);

            h.Children.Add(Kit.Header("Notifications"));
            h.Children.Add(Kit.Group(
                Kit.Row("elysium-style Windows notifications", "hides Windows' own popups and shows them the elysium way. " + _toastStatus(), Sw(_s.TakeOverToasts, v => _s.TakeOverToasts = v)),
                Kit.SliderRow("Stay up for", 2, 20, _s.ToastSeconds, v => $"{v:0}s", v =>
                {
                    _s.ToastSeconds = Math.Round(v);
                    Touch();
                })));

            if (_s.NoteRules.Count > 0)
            {
                h.Children.Add(Kit.Header("Rules"));
                var rows = _s.NoteRules.ToList().Select(kv =>
                {
                    string app = kv.Key;
                    return (UIElement)Kit.Row(app, null, Kit.Segments(new[] { "Pop up", "Silent", "Block" }, new[] { "normal", "silent", "block" }, kv.Value, v =>
                    {
                        if (v == "normal") _s.NoteRules.Remove(app);
                        else _s.NoteRules[app] = v;
                        Touch();
                    }));
                }).ToArray();
                h.Children.Add(Kit.Group(rows));
            }
            var tip = Kit.Caption("set rules per app from the notification center: click allowed / silent / blocked next to an app");
            tip.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(tip);

            h.Children.Add(Kit.Header("Focus mode"));
            var profiles = new[] { "" }.Concat(_s.AudioProfiles.Select(p => p.Name)).ToArray();
            h.Children.Add(Kit.Group(
                Kit.Row("Focus is on", "ctrl + alt + f", Sw(_s.FocusActive, v => _s.FocusActive = v)),
                Kit.Block("Mute these apps", "comma separated, like discord, spotify", Kit.Field(_s.FocusMute, "discord, spotify", v =>
                {
                    _s.FocusMute = v;
                    Touch();
                })),
                Kit.Block("Still let these notify me", "everything else goes straight to history", Kit.Field(_s.FocusAllow, "outlook, teams", v =>
                {
                    _s.FocusAllow = v;
                    Touch();
                })),
                Kit.Block("Switch to audio profile", null, Kit.Segments(profiles.Select(p => p.Length == 0 ? "None" : p).Take(4).ToArray(), profiles.Take(4).ToArray(), _s.FocusProfile, v =>
                {
                    _s.FocusProfile = v;
                    Touch();
                }))));
        }

        void Search(Panel h)
        {
            h.Children.Add(Kit.Header("Launcher"));
            string cmds = string.Join(Environment.NewLine, _s.CustomCommands.Select(c => $"{c.Alias} = {c.Run}"));
            h.Children.Add(Kit.Group(
                Kit.Block("Web search", "the ? mode and the last result", Kit.Field(_s.SearchEngine, "https://www.google.com/search?q=", v =>
                {
                    _s.SearchEngine = v.Trim();
                    Touch();
                })),
                Kit.Block("Your commands", "one per line: name = what to run. chain steps with ;   e.g.  study = &focus ; spotify: ; wait 2 ; https://docs.google.com", Kit.Field(cmds, "yt = https://youtube.com", v =>
                {
                    _s.CustomCommands = v.Split('\n').Select(l => l.Trim()).Where(l => l.Contains('='))
                        .Select(l => new CustomCommand { Alias = l[..l.IndexOf('=')].Trim(), Run = l[(l.IndexOf('=') + 1)..].Trim() })
                        .Where(c => c.Alias.Length > 0 && c.Run.Length > 0).ToList();
                    Touch();
                }, true))));

            h.Children.Add(Kit.Header("Clipboard"));
            h.Children.Add(Kit.Group(
                Kit.Row("Keep clipboard history", "stays in memory, pinned text is saved", Sw(_s.ClipHistory, v => _s.ClipHistory = v)),
                Kit.SliderRow("How many", 10, 150, _s.ClipMax, v => $"{v:0}", v =>
                {
                    _s.ClipMax = (int)Math.Round(v);
                    Touch();
                })));

            h.Children.Add(Kit.Header("File colors"));
            var rows = Files.Categories.Select(c =>
            {
                string cat = c.cat;
                string cur = _s.CategoryColors.TryGetValue(cat, out var custom) ? custom : c.color;
                var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0) };
                try
                {
                    swatch.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(cur));
                }
                catch
                {
                }
                var field = Kit.Field(cur, c.color, v =>
                {
                    v = v.Trim();
                    if (v.Length == 0 || v.Equals(c.color, StringComparison.OrdinalIgnoreCase)) _s.CategoryColors.Remove(cat);
                    else _s.CategoryColors[cat] = v.StartsWith("#") ? v : "#" + v;
                    Touch();
                });
                field.Width = 96;
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(swatch);
                right.Children.Add(field);
                return (UIElement)Kit.Row(c.label, null, right);
            }).ToArray();
            h.Children.Add(Kit.Group(rows));
        }

        public Action? PhoneDrop;

        void Share(Panel h)
        {
            h.Children.Add(Kit.Header("Phone"));
            h.Children.Add(Kit.Group(
                Kit.Row("Send files from your phone", "shows a qr code, your phone uploads straight to Downloads\\from phone. same wi-fi, nothing in the cloud", Kit.Push("Start", () => PhoneDrop?.Invoke(), true)),
                Kit.Row("Send files to your phone", Hotkeys.Spec(_s, "share").Length > 0 ? "select something in explorer and hit " + Hotkeys.Spec(_s, "share").ToLowerInvariant().Replace("+", " + ") : "give it a key in the keys tab, then select something in explorer and hit it", null)));

            h.Children.Add(Kit.Header("Your setup"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 12) };
            row.Children.Add(Kit.Push("Export...", () => _dialog(() =>
            {
                var dlg = new SaveFileDialog { Title = "Share your elysium setup", Filter = "elysium setup|*.elysiumpack", FileName = "my setup.elysiumpack" };
                if (dlg.ShowDialog() != true) return;
                SharePack.Export(_s, dlg.FileName);
            }), true));
            var imp = Kit.Push("Import...", () => _dialog(() =>
            {
                var dlg = new OpenFileDialog { Title = "Load an elysium setup", Filter = "elysium setup|*.elysiumpack;*.heftpack;*.json" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    SharePack.Import(_s, dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "elysium");
                }
            }));
            imp.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(imp);
            h.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Group"), Child = row });
            var cap = Kit.Caption("exports the look, bar, taskbar, gestures, keys, your commands and audio profiles. personal stuff like history, file paths and collections stays on your pc");
            cap.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(cap);
        }
    }
}
