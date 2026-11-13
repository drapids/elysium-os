using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class Panels : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly Activity _act;
        readonly AudioFlow _audio;
        readonly Notifier _notes;
        readonly Clips _clips;
        readonly Commands _cmds;
        readonly NowPlaying _np;

        readonly Border _surface;
        readonly ScrollViewer _scroll;
        readonly StackPanel _body = new() { Margin = new Thickness(14, 12, 14, 14) };
        readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(1000) };
        readonly DispatcherTimer _meters = new() { Interval = TimeSpan.FromMilliseconds(70) };
        readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(60) };
        readonly List<(string exe, Border meter)> _meterRows = new();
        readonly Dictionary<string, (Border meter, TextBlock value)> _sys = new();

        string _page = "";
        string _noteFilter = "";
        string _noteApp = "";
        bool _suppress;

        public Action? OpenLauncherClip;
        public Action? OpenSettings;
        public Action<string>? OpenSettingsPage;

        public Panels(Settings s, AppCatalog cat, Activity act, AudioFlow audio, Notifier notes, Clips clips, Commands cmds, NowPlaying np)
        {
            _s = s;
            _cat = cat;
            _act = act;
            _audio = audio;
            _notes = notes;
            _clips = clips;
            _cmds = cmds;
            _np = np;
            Glass.Attach(this, 16);
            Title = "heft panel";
            Width = 400;
            SizeToContent = SizeToContent.Height;

            _surface = Glass.Surface(16);
            _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            _surface.Child = _scroll;
            Content = _surface;

            Deactivated += (_, _) =>
            {
                if (!_suppress) Close2();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && e.OriginalSource is not TextBox) Close2();
            };
            _live.Tick += (_, _) => Live();
            _meters.Tick += (_, _) => Meters();
            _outside.Tick += (_, _) =>
            {
                if (!IsVisible || _suppress || IsActive || !Native.MouseDown()) return;
                Native.GetCursorPos(out var p);
                Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
                if (!r.Contains(p.X, p.Y)) Close2();
            };
            _audio.DevicesChanged += () =>
            {
                if (IsVisible && (_page == "audio" || _page == "center")) Build();
            };
            _notes.Changed += () =>
            {
                if (IsVisible && _page == "notes") Build();
            };
        }

        public string Page => _shown ? _page : "";

        public void Toggle(string page, Display d, double topPx, double anchorX)
        {
            if (_shown && _page == page)
            {
                Close2();
                return;
            }
            Open(page, d, topPx, anchorX);
        }

        public void Open(string page, Display d, double topPx, double anchorX)
        {
            _page = page;
            Width = page switch { "audio" => 440, "notes" => 420, _ => 420 };
            MaxHeight = d.Work.Height / d.Scale - 60;
            _scroll.MaxHeight = MaxHeight - 4;
            Build();

            _shown = true;
            Glass.Present(this, _surface, () =>
            {
                UpdateLayout();
                int wpx = (int)(Width * d.Scale);
                int x = page == "center" ? (int)(anchorX - wpx / 2.0) : (int)(anchorX - wpx + 40 * d.Scale);
                x = Math.Clamp(x, d.Bounds.Left + 8, d.Bounds.Right - wpx - 8);
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(h, Native.HWND_TOPMOST, x, (int)topPx + (int)(8 * d.Scale), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, -10, 0.985, 230);
            Activate();
            _live.Start();
            _outside.Start();
            if (page == "audio") _meters.Start();
            if (page == "notes") _notes.MarkSeen();
        }

        bool _shown;

        public bool Shown => _shown;

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            _live.Stop();
            _meters.Stop();
            _outside.Stop();
            Glass.Dismiss(this, _surface);
        }

        void Build()
        {
            _body.Children.Clear();
            _meterRows.Clear();
            _sys.Clear();
            switch (_page)
            {
                case "audio":
                    BuildAudio();
                    break;
                case "notes":
                    BuildNotes();
                    break;
                default:
                    BuildCenter();
                    break;
            }
        }

        void Live()
        {
            if (_page == "center") UpdateSys();
            if (_page == "audio")
            {
                var apps = _audio.Apps().Select(a => a.Exe).ToList();
                var shown = _meterRows.Select(m => m.exe).ToList();
                if (!apps.SequenceEqual(shown)) Build();
            }
        }

        static TextBlock Title2(string t)
        {
            var tb = Kit.Text(t, 15, true);
            tb.Margin = new Thickness(2, 0, 0, 10);
            return tb;
        }

        static TextBlock Section(string t)
        {
            var tb = Kit.Text(t, 11.5, true, "SubText");
            tb.Margin = new Thickness(2, 14, 0, 6);
            return tb;
        }

        Border Tile(string glyph, string label, bool on, Action click, string? sub = null)
        {
            var sp = new StackPanel();
            var top = new Grid();
            var circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Left };
            circle.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "SegTrack");
            circle.Child = Kit.Icon(glyph, 13, on ? "OnAccent" : "Text");
            top.Children.Add(circle);
            sp.Children.Add(top);
            var l = Kit.Text(label, 12, true);
            l.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(l);
            if (sub != null) sp.Children.Add(Kit.Text(sub, 10.5, false, "SubText"));
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(3), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            b.MouseLeftButtonUp += (_, _) =>
            {
                click();
                Dispatcher.BeginInvoke(new Action(Build), DispatcherPriority.Background);
            };
            return b;
        }

        void BuildCenter()
        {
            _body.Children.Add(Title2("Control center"));

            var tiles = new UniformGrid4();
            tiles.Add(Tile("\uE708", "Focus", _s.FocusActive, () => _cmds.Run("focus"), _s.FocusActive ? "on" : "off"));
            tiles.Add(Tile("\uE72E", "Cursor lock", _cmds.Get("lock")?.State?.Invoke() == "on", () => _cmds.Run("lock")));
            tiles.Add(Tile("\uE7F4", "Desktop icons", !_s.HideDesktop, () => _cmds.Run("desktop")));
            tiles.Add(Tile("\uE945", "Physics", _s.Enabled, () => _cmds.Run("physics")));
            tiles.Add(Tile("\uE75B", "Taskbar", _s.TaskbarHide == "off", () => _cmds.Run("taskbar")));
            tiles.Add(Tile("\uE77F", "Paste stack", _clips.Stacking, () => _clips.ToggleStack()));
            var wifi = Radios.Get("wifi");
            if (wifi != null) tiles.Add(Tile("\uE701", "Wi-Fi", wifi.Value, () => _ = Radios.Toggle("wifi")));
            var bt = Radios.Get("bluetooth");
            if (bt != null) tiles.Add(Tile("\uE702", "Bluetooth", bt.Value, () => _ = Radios.Toggle("bluetooth")));
            _body.Children.Add(tiles.Panel);

            if (_np.HasSession && _np.Title.Length > 0)
            {
                _body.Children.Add(Section("NOW PLAYING"));
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var art = new Image { Width = 44, Height = 44, Source = _np.Art, Margin = new Thickness(0, 0, 12, 0), Stretch = Stretch.UniformToFill };
                art.Clip = new RectangleGeometry(new Rect(0, 0, 44, 44), 8, 8);
                row.Children.Add(art);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(Kit.Text(_np.Title, 13, true));
                info.Children.Add(Kit.Text(_np.Artist, 12, false, "SubText"));
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                var ctl = new StackPanel { Orientation = Orientation.Horizontal };
                ctl.Children.Add(Kit.Plain("\uE892", () => _ = _np.Previous(), "previous", 11));
                ctl.Children.Add(Kit.Plain(_np.Playing ? "\uE769" : "\uE768", () => _ = Refresh(_np.Toggle()), "play / pause", 12));
                ctl.Children.Add(Kit.Plain("\uE893", () => _ = _np.Next(), "next", 11));
                Grid.SetColumn(ctl, 2);
                row.Children.Add(ctl);
                _body.Children.Add(Card(row));
            }

            _body.Children.Add(Section("SYSTEM"));
            var sys = new StackPanel();
            foreach (var key in new[] { "CPU", "GPU", "RAM", "Network", "Battery" })
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.Children.Add(Kit.Text(key, 12, false, "SubText"));
                var meter = Kit.Meter(0, 170);
                meter.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(meter, 1);
                g.Children.Add(meter);
                var val = Kit.Text("", 12);
                val.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(val, 2);
                g.Children.Add(val);
                sys.Children.Add(g);
                _sys[key] = (meter, val);
            }
            _body.Children.Add(Card(sys));
            UpdateSys();

            _body.Children.Add(Section("SOUND OUT"));
            _body.Children.Add(Card(DeviceList(false, 4)));

            var latest = _clips.Latest;
            if (latest != null)
            {
                _body.Children.Add(Section("CLIPBOARD"));
                FrameworkElement preview;
                if (latest.Kind == "image" && latest.Image != null)
                {
                    preview = new Image { Source = latest.Image, MaxHeight = 120, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                }
                else
                {
                    var t = Kit.Text(latest.Summary, 12.5);
                    t.TextWrapping = TextWrapping.Wrap;
                    t.MaxHeight = 60;
                    preview = t;
                }
                var clipRow = new DockPanel();
                var more = Kit.Push("History", () =>
                {
                    Close2();
                    OpenLauncherClip?.Invoke();
                });
                DockPanel.SetDock(more, Dock.Right);
                more.Margin = new Thickness(10, 0, 0, 0);
                clipRow.Children.Add(more);
                clipRow.Children.Add(preview);
                _body.Children.Add(Card(clipRow));
            }

            if (_s.AudioProfiles.Count > 0)
            {
                _body.Children.Add(Section("AUDIO PROFILES"));
                _body.Children.Add(Profiles());
            }

            var foot = new DockPanel { Margin = new Thickness(2, 14, 0, 0) };
            var settings = Kit.Push("Settings", () =>
            {
                Close2();
                OpenSettings?.Invoke();
            });
            DockPanel.SetDock(settings, Dock.Right);
            foot.Children.Add(settings);
            foot.Children.Add(Kit.Text("made by mason arcieri", 11, false, "Faint"));
            _body.Children.Add(foot);
        }

        async Task Refresh(Task t)
        {
            await t;
            await Task.Delay(300);
            await _np.Refresh();
            if (IsVisible && _page == "center") Build();
        }

        static Border Card(UIElement child)
        {
            var b = new Border { Child = child, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            return b;
        }

        void UpdateSys()
        {
            if (_sys.Count == 0) return;
            void Set(string k, double frac, string text)
            {
                if (!_sys.TryGetValue(k, out var v)) return;
                Kit.SetMeter(v.meter, frac);
                v.value.Text = text;
            }
            Set("CPU", _act.Cpu / 100.0, $"{_act.Cpu}%");
            Set("GPU", Math.Max(0, _act.Gpu) / 100.0, _act.Gpu >= 0 ? $"{_act.Gpu}%" : "n/a");
            Set("RAM", _act.Ram / 100.0, $"{_act.Ram}%");
            Set("Network", Math.Min(1, (_act.NetDown + _act.NetUp) / (12.5 * 1024 * 1024)), $"↓{Kit.Size((long)_act.NetDown)}/s ↑{Kit.Size((long)_act.NetUp)}/s");
            try
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool none = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
                Set("Battery", none ? 1 : power.BatteryLifePercent, none ? "plugged in, no battery" : $"{Math.Round(power.BatteryLifePercent * 100)}%{(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? " charging" : "")}");
            }
            catch
            {
            }
        }

        StackPanel DeviceList(bool input, int max)
        {
            var sp = new StackPanel();
            foreach (var d in _audio.Devices(input).Take(max == 0 ? 50 : max))
            {
                var dev = d;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var check = Kit.Icon(d.Default ? "\uE73E" : "", 11, "AccentFill");
                check.Width = 18;
                g.Children.Add(check);
                var name = Kit.Text(d.Name.Length > 0 ? d.Name : "device", 12.5, d.Default);
                Grid.SetColumn(name, 1);
                g.Children.Add(name);
                sp.Children.Add(Kit.Hover(g, () =>
                {
                    _audio.SetDefault(dev.Id);
                    Build();
                }, 8));
            }
            if (sp.Children.Count == 0) sp.Children.Add(Kit.Text("no devices found", 12, false, "SubText"));
            return sp;
        }

        WrapPanel Profiles()
        {
            var wrap = new WrapPanel();
            foreach (var p in _s.AudioProfiles)
            {
                var prof = p;
                bool on = _s.ActiveProfile == p.Name;
                var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 11, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = "click to switch. right-click to delete" + (p.Hotkey.Length > 0 ? $"  ({p.Hotkey})" : "") };
                chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                chip.Child = Kit.Text(p.Name, 12, on, on ? "OnAccent" : "Text");
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    _audio.Apply(prof);
                    Build();
                };
                chip.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _s.AudioProfiles.Remove(prof);
                    _s.Touch();
                    Build();
                };
                wrap.Children.Add(chip);
            }
            return wrap;
        }

        void BuildAudio()
        {
            var head = new DockPanel();
            var gear = Kit.Plain("\uE713", () =>
            {
                Close2();
                OpenSettingsPage?.Invoke("audio");
            }, "AudioFlow settings", 12);
            DockPanel.SetDock(gear, Dock.Right);
            head.Children.Add(gear);
            head.Children.Add(Title2("AudioFlow"));
            _body.Children.Add(head);

            int master = Audio.Volume(out bool muted);
            var mrow = new Grid();
            mrow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            var mute = Kit.Plain(muted ? "\uE74F" : "\uE767", () =>
            {
                Audio.ToggleMute();
                Build();
            }, "mute everything", 13);
            mrow.Children.Add(mute);
            var mslider = new Slider { Style = (Style)FindResource("Mac"), Minimum = 0, Maximum = 100, Value = Math.Max(0, master), Margin = new Thickness(8, 0, 8, 0) };
            var mval = Kit.Text($"{Math.Max(0, master)}%", 12, false, "SubText");
            mslider.ValueChanged += (_, e) =>
            {
                int cur = Audio.Volume(out bool ignore);
                Audio.Nudge((int)Math.Round(e.NewValue) - cur);
                mval.Text = $"{(int)e.NewValue}%";
            };
            Grid.SetColumn(mslider, 1);
            mrow.Children.Add(mslider);
            Grid.SetColumn(mval, 2);
            mrow.Children.Add(mval);
            _body.Children.Add(Card(mrow));

            _body.Children.Add(Section("APPS"));
            var apps = _audio.Apps();
            var list = new StackPanel();
            var outputs = _audio.Devices(false);
            foreach (var a in apps)
            {
                var app = a;
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconHost = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                iconHost.Children.Add(Kit.Icon(a.System ? "\uE7F3" : "\uE71D", 13, "SubText"));
                if (a.Path != null)
                {
                    var img = new Image { Width = 26, Height = 26 };
                    iconHost.Children.Add(img);
                    _cat.Icons.Get(a.Path, 64, true, src => img.Source = src);
                }
                g.Children.Add(iconHost);

                var mid = new StackPanel();
                var nameRow = new DockPanel();
                string route = _s.AppRoutes.TryGetValue(a.Exe, out var rid) ? outputs.FirstOrDefault(o => o.Id == rid)?.Name ?? "" : "";
                if (!a.System)
                {
                    var routeBtn = new TextBlock { Text = route.Length > 0 ? "→ " + Short(route) : "default output", FontSize = 11, Cursor = Cursors.Hand, ToolTip = "route this app to a different output" };
                    routeBtn.SetResourceReference(TextBlock.ForegroundProperty, route.Length > 0 ? "AccentFill" : "Faint");
                    routeBtn.MouseLeftButtonUp += (_, _) => RouteMenu(routeBtn, app, outputs);
                    DockPanel.SetDock(routeBtn, Dock.Right);
                    nameRow.Children.Add(routeBtn);
                }
                nameRow.Children.Add(Kit.Text(a.Name, 12.5, true));
                mid.Children.Add(nameRow);

                var slider = new Slider { Style = (Style)FindResource("Mac"), Minimum = 0, Maximum = 1, Value = a.Volume };
                slider.ValueChanged += (_, e) => _audio.SetVolume(app.Exe, (float)e.NewValue);
                mid.Children.Add(slider);
                var meter = Kit.Meter(a.Peak, 250);
                meter.Height = 3;
                meter.HorizontalAlignment = HorizontalAlignment.Left;
                meter.Margin = new Thickness(2, 0, 0, 0);
                mid.Children.Add(meter);
                _meterRows.Add((a.Exe, meter));
                Grid.SetColumn(mid, 1);
                g.Children.Add(mid);

                var muteBtn = Kit.Plain(a.Muted ? "\uE74F" : "\uE767", () =>
                {
                    _audio.SetMute(app.Exe, !app.Muted);
                    Build();
                }, a.Muted ? "unmute" : "mute", 12);
                muteBtn.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(muteBtn, 2);
                g.Children.Add(muteBtn);
                list.Children.Add(g);
            }
            if (apps.Count == 0) list.Children.Add(Kit.Text("nothing is playing sound right now", 12, false, "SubText"));
            _body.Children.Add(Card(list));

            _body.Children.Add(Section("OUTPUT"));
            _body.Children.Add(Card(DeviceList(false, 0)));
            _body.Children.Add(Section("MICROPHONE"));
            _body.Children.Add(Card(DeviceList(true, 0)));

            _body.Children.Add(Section("PROFILES"));
            var profBox = new StackPanel();
            profBox.Children.Add(Profiles());
            var save = Kit.Field("", "save current setup as... (enter)", _ => { });
            save.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                string name = save.Text.Trim();
                if (name.Length == 0) return;
                var p = _audio.Capture(name);
                var old = _s.AudioProfiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (old != null)
                {
                    p.Hotkey = old.Hotkey;
                    _s.AudioProfiles.Remove(old);
                }
                _s.AudioProfiles.Add(p);
                _s.ActiveProfile = name;
                _s.Touch();
                Build();
            };
            profBox.Children.Add(save);
            _body.Children.Add(profBox);
        }

        static string Short(string name)
        {
            int paren = name.IndexOf('(');
            string s = paren > 2 ? name[..paren].Trim() : name;
            return s.Length > 22 ? s[..22] + "..." : s;
        }

        void RouteMenu(FrameworkElement anchor, AudioApp app, List<AudioDevice> outputs)
        {
            var menu = new ContextMenu();
            var def = new MenuItem { Header = "Default output" };
            def.Click += (_, _) =>
            {
                _audio.Route(app.Exe, null, out _);
                _s.AppRoutes.Remove(app.Exe);
                _s.Touch();
                Build();
            };
            menu.Items.Add(def);
            foreach (var o in outputs)
            {
                var dev = o;
                var mi = new MenuItem { Header = o.Name };
                mi.Click += (_, _) =>
                {
                    if (_audio.Route(app.Exe, dev.Id, out string err))
                    {
                        _s.AppRoutes[app.Exe] = dev.Id;
                        _s.Touch();
                    }
                    else
                    {
                        _notes.Post("Couldn't route " + app.Name, err, "\uE7BA");
                    }
                    Build();
                };
                menu.Items.Add(mi);
            }
            _suppress = true;
            menu.Closed += (_, _) => _suppress = false;
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        void Meters()
        {
            if (_meterRows.Count == 0) return;
            var apps = _audio.Apps().ToDictionary(a => a.Exe, a => a.Peak, StringComparer.OrdinalIgnoreCase);
            foreach (var (exe, meter) in _meterRows)
            {
                double peak = apps.TryGetValue(exe, out float p) ? p : 0;
                if (meter.Child is Border fill)
                {
                    double target = Math.Clamp(peak, 0, 1) * meter.Width;
                    fill.Width = target > fill.Width ? target : fill.Width * 0.82 + target * 0.18;
                }
            }
        }

        void BuildNotes()
        {
            var head = new DockPanel();
            var clear = Kit.Push("Clear all", () => _notes.Clear());
            DockPanel.SetDock(clear, Dock.Right);
            head.Children.Add(clear);
            head.Children.Add(Title2("Notifications"));
            _body.Children.Add(head);

            var search = Kit.Field(_noteFilter, "search notifications", _ => { });
            search.TextChanged += (_, _) =>
            {
                _noteFilter = search.Text;
                RebuildNoteList();
            };
            search.Margin = new Thickness(0, 0, 0, 8);
            _body.Children.Add(search);

            if (_s.Reminders.Count > 0)
            {
                _body.Children.Add(Section("COMING UP"));
                var up = new StackPanel();
                foreach (var r in _s.Reminders.OrderBy(r => r.Due))
                {
                    var rem = r;
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var x = Kit.Plain("\uE711", () =>
                    {
                        _s.Reminders.Remove(rem);
                        _s.Touch();
                        Build();
                    }, "cancel", 9);
                    DockPanel.SetDock(x, Dock.Right);
                    row.Children.Add(x);
                    var t = Kit.Text($"{r.Due:h:mm tt}{(r.Due.Date != DateTime.Today ? r.Due.ToString(" ddd") : "")}  ·  {(r.Timer ? "timer " : "")}{r.Text}", 12.5);
                    row.Children.Add(t);
                    up.Children.Add(row);
                }
                _body.Children.Add(Card(up));
            }

            var apps = _notes.History.GroupBy(n => n.App).Select(g => g.Key).ToList();
            if (apps.Count > 1)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                foreach (var a in new[] { "" }.Concat(apps))
                {
                    string app = a;
                    bool on = _noteApp == a;
                    var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand };
                    chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                    chip.Child = Kit.Text(a.Length == 0 ? "All" : a, 11.5, on, on ? "OnAccent" : "Text");
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        _noteApp = app;
                        Build();
                    };
                    chips.Children.Add(chip);
                }
                _body.Children.Add(chips);
            }

            _noteList = new StackPanel();
            _body.Children.Add(_noteList);
            RebuildNoteList();
        }

        StackPanel? _noteList;

        void RebuildNoteList()
        {
            if (_noteList == null) return;
            _noteList.Children.Clear();
            var items = _notes.History.Where(n => _noteApp.Length == 0 || n.App == _noteApp);
            if (_noteFilter.Trim().Length > 0)
            {
                string f = _noteFilter.Trim();
                items = items.Where(n => (n.Title + " " + n.Body + " " + n.App).Contains(f, StringComparison.OrdinalIgnoreCase));
            }
            var groups = items.Take(150).GroupBy(n => n.App).ToList();
            if (groups.Count == 0)
            {
                var t = Kit.Text("all caught up", 12.5, false, "SubText");
                t.Margin = new Thickness(2, 14, 0, 0);
                _noteList.Children.Add(t);
                return;
            }
            foreach (var g in groups)
            {
                string app = g.Key;
                var head = new DockPanel { Margin = new Thickness(2, 14, 0, 6) };
                string rule = _notes.Rule(app);
                var ruleBtn = new TextBlock { Text = rule == "normal" ? "allowed" : rule == "silent" ? "silent" : "blocked", FontSize = 11, Cursor = Cursors.Hand, ToolTip = "click to cycle: allowed (pops up), silent (history only), blocked (dropped)" };
                ruleBtn.SetResourceReference(TextBlock.ForegroundProperty, rule == "normal" ? "Faint" : "AccentFill");
                ruleBtn.MouseLeftButtonUp += (_, _) =>
                {
                    string next = rule switch { "normal" => "silent", "silent" => "block", _ => "normal" };
                    if (next == "normal") _s.NoteRules.Remove(app);
                    else _s.NoteRules[app] = next;
                    _s.Touch();
                    RebuildNoteList();
                };
                DockPanel.SetDock(ruleBtn, Dock.Right);
                head.Children.Add(ruleBtn);
                var clr = Kit.Plain("\uE711", () => _notes.Clear(app), "clear these", 9);
                clr.Margin = new Thickness(0, -6, 6, -6);
                DockPanel.SetDock(clr, Dock.Right);
                head.Children.Add(clr);
                head.Children.Add(Kit.Text($"{app.ToUpperInvariant()}  {g.Count()}", 11.5, true, "SubText"));
                _noteList.Children.Add(head);

                var stack = new StackPanel();
                foreach (var n in g.Take(12))
                {
                    var note = n;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var ic = Kit.Icon(n.Glyph, 12, "AccentFill");
                    ic.VerticalAlignment = VerticalAlignment.Top;
                    ic.Margin = new Thickness(0, 3, 10, 0);
                    row.Children.Add(ic);
                    var txt = new StackPanel();
                    var top = new DockPanel();
                    var time = Kit.Text(Kit.Ago(n.Time), 11, false, "Faint");
                    DockPanel.SetDock(time, Dock.Right);
                    top.Children.Add(time);
                    top.Children.Add(Kit.Text(n.Title.Length > 0 ? n.Title : n.App, 12.5, true));
                    txt.Children.Add(top);
                    if (n.Body.Length > 0)
                    {
                        var b = Kit.Text(n.Body, 12, false, "SubText");
                        b.TextWrapping = TextWrapping.Wrap;
                        b.MaxHeight = 48;
                        txt.Children.Add(b);
                    }
                    Grid.SetColumn(txt, 1);
                    row.Children.Add(txt);
                    var x = Kit.Plain("\uE711", () => _notes.Remove(note), "dismiss", 8);
                    x.VerticalAlignment = VerticalAlignment.Top;
                    x.Margin = new Thickness(6, -2, -4, 0);
                    Grid.SetColumn(x, 2);
                    row.Children.Add(x);
                    stack.Children.Add(Kit.Hover(row, () =>
                    {
                        Toasts.OpenTarget(note);
                        Close2();
                    }, 9));
                }
                _noteList.Children.Add(Card(stack));
            }
        }
    }

    internal sealed class UniformGrid4
    {
        public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new() { Columns = 4 };
        public void Add(UIElement e) => Panel.Children.Add(e);
    }

    internal static class Radios
    {
        static IReadOnlyList<Windows.Devices.Radios.Radio>? _cache;
        static DateTime _at = DateTime.MinValue;

        static IReadOnlyList<Windows.Devices.Radios.Radio>? List()
        {
            if (_cache != null && (DateTime.Now - _at).TotalSeconds < 5) return _cache;
            try
            {
                var task = Windows.Devices.Radios.Radio.GetRadiosAsync().AsTask();
                if (!task.Wait(400)) return _cache;
                _cache = task.Result;
                _at = DateTime.Now;
            }
            catch
            {
                _cache = null;
            }
            return _cache;
        }

        public static async void Warm()
        {
            try
            {
                _cache = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                _at = DateTime.Now;
            }
            catch
            {
            }
        }

        static Windows.Devices.Radios.RadioKind Kind(string k) => k == "wifi" ? Windows.Devices.Radios.RadioKind.WiFi : Windows.Devices.Radios.RadioKind.Bluetooth;

        public static bool? Get(string kind)
        {
            var r = List()?.FirstOrDefault(x => x.Kind == Kind(kind));
            return r == null ? null : r.State == Windows.Devices.Radios.RadioState.On;
        }

        public static async Task Toggle(string kind)
        {
            try
            {
                await Windows.Devices.Radios.Radio.RequestAccessAsync();
                var r = List()?.FirstOrDefault(x => x.Kind == Kind(kind));
                if (r == null) return;
                await r.SetStateAsync(r.State == Windows.Devices.Radios.RadioState.On ? Windows.Devices.Radios.RadioState.Off : Windows.Devices.Radios.RadioState.On);
                _at = DateTime.MinValue;
            }
            catch
            {
            }
        }
    }
}
