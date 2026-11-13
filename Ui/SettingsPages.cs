using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal sealed class SettingsPages
    {
        public static readonly (string id, string label)[] Tabs =
        {
            ("dock", "Dock & physics"),
            ("shell", "Bar & desktop"),
            ("audio", "Audio"),
            ("alerts", "Alerts & focus"),
            ("search", "Search & files"),
            ("keys", "Keys"),
            ("share", "Share")
        };

        readonly Settings _s;
        readonly AudioFlow _audio;
        readonly Func<IEnumerable<string>> _keyErrors;
        readonly Func<string> _toastStatus;
        readonly Action<Action> _dialog;

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

        public void Build(string page, Panel host)
        {
            host.Children.Clear();
            switch (page)
            {
                case "shell":
                    Shell(host);
                    break;
                case "audio":
                    AudioPage(host);
                    break;
                case "alerts":
                    Alerts(host);
                    break;
                case "search":
                    Search(host);
                    break;
                case "keys":
                    Keys(host);
                    break;
                case "share":
                    Share(host);
                    break;
            }
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
                Kit.Row("Windows key opens heft Start", "fullscreen glass start with your real Start pins, taskbar apps, widgets and every app. ctrl + esc still opens the old one", Sw(_s.WinKeyLauncher, v => _s.WinKeyLauncher = v)),
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
                Kit.Block("Top left", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Center", "Desktop" }, new[] { "none", "start", "overview", "center", "desktop" }, _s.HotCorners.TryGetValue("tl", out var vtl) ? vtl : "none", v =>
                {
                    _s.HotCorners["tl"] = v;
                    Touch();
                })),
                Kit.Block("Top right", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Center", "Desktop" }, new[] { "none", "start", "overview", "center", "desktop" }, _s.HotCorners.TryGetValue("tr", out var vtr) ? vtr : "none", v =>
                {
                    _s.HotCorners["tr"] = v;
                    Touch();
                })),
                Kit.Block("Bottom left", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Center", "Desktop" }, new[] { "none", "start", "overview", "center", "desktop" }, _s.HotCorners.TryGetValue("bl", out var vbl) ? vbl : "none", v =>
                {
                    _s.HotCorners["bl"] = v;
                    Touch();
                })),
                Kit.Block("Bottom right", null, Kit.Segments(new[] { "Off", "Start", "Windows", "Center", "Desktop" }, new[] { "none", "start", "overview", "center", "desktop" }, _s.HotCorners.TryGetValue("br", out var vbr) ? vbr : "none", v =>
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
            h.Children.Add(Kit.Header("Notifications"));
            h.Children.Add(Kit.Group(
                Kit.Row("heft-style Windows notifications", "hides Windows' own popups and shows them the heft way. " + _toastStatus(), Sw(_s.TakeOverToasts, v => _s.TakeOverToasts = v)),
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

        void Keys(Panel h)
        {
            h.Children.Add(Kit.Header("Shortcuts"));
            var rows = Hotkeys.Defaults.Select(d =>
            {
                string id = d.id;
                var field = Kit.Field(Hotkeys.Spec(_s, id), d.fallback, v =>
                {
                    v = v.Trim();
                    if (v.Equals(d.fallback, StringComparison.OrdinalIgnoreCase)) _s.Keys.Remove(id);
                    else _s.Keys[id] = v;
                    Touch();
                });
                field.Width = 150;
                return (UIElement)Kit.Row(d.label, null, field);
            }).ToArray();
            h.Children.Add(Kit.Group(rows));
            var cap = Kit.Caption("type it like Ctrl+Alt+Space or Win+Shift+S. leave it empty to turn a shortcut off");
            cap.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(cap);

            var errors = _keyErrors().ToList();
            if (errors.Count > 0)
            {
                h.Children.Add(Kit.Header("Didn't work"));
                h.Children.Add(Kit.Group(errors.Select(e => (UIElement)Kit.Row(e, null, null)).ToArray()));
            }
        }

        void Share(Panel h)
        {
            h.Children.Add(Kit.Header("Your setup"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 12) };
            row.Children.Add(Kit.Push("Export...", () => _dialog(() =>
            {
                var dlg = new SaveFileDialog { Title = "Share your heft setup", Filter = "heft setup|*.heftpack", FileName = "my setup.heftpack" };
                if (dlg.ShowDialog() != true) return;
                SharePack.Export(_s, dlg.FileName);
            }), true));
            var imp = Kit.Push("Import...", () => _dialog(() =>
            {
                var dlg = new OpenFileDialog { Title = "Load a heft setup", Filter = "heft setup|*.heftpack;*.json" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    SharePack.Import(_s, dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "heft");
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
