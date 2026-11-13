using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Heft.Ui;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Heft
{
    public partial class App : Application
    {
        Mutex? _single;
        Settings _s = null!;
        PhysicsEngine _physics = null!;
        AppCatalog _catalog = null!;
        Notifier _notes = null!;
        Toasts _toasts = null!;
        AudioFlow _audio = null!;
        Activity _activity = null!;
        NowPlaying _np = null!;
        Clips _clips = null!;
        Library _lib = null!;
        AppIndex _apps = null!;
        Commands _cmds = null!;
        ObjectActions _actions = null!;
        Pocket _pocket = null!;
        CursorLock _lock = null!;
        Brightness _brightness = null!;
        MouseHook _mouse = null!;
        TaskbarStyler _taskbar = null!;
        WinToasts _wintoasts = null!;
        Hotkeys _keys = null!;

        ArcDock _dock = null!;
        SidePanel _panel = null!;
        Panels _drop = null!;
        Shelf _shelf = null!;
        Launcher _launcher = null!;
        Inspector _inspector = null!;
        readonly List<EdgeTab> _tabs = new();
        readonly List<Bar> _bars = new();

        HwndSource? _shellMsgs;
        uint _taskbarCreated;
        Forms.NotifyIcon? _tray;
        Forms.ToolStripMenuItem? _physicsItem;
        DispatcherTimer? _autosave;
        readonly DispatcherTimer _state = new() { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer _peek = new();
        readonly DispatcherTimer _layoutDelay = new() { Interval = TimeSpan.FromMilliseconds(600) };
        string _appName = "";
        string _keySig = "";
        string _layoutSig = "";
        bool _focusApplied, _peeking, _quitting, _cleaned;
        int _tick;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _single = new Mutex(true, "heft.single.instance", out bool fresh);
            if (!fresh)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, ex) =>
            {
                PhysicsEngine.Log("ui error " + ex.Exception);
                ex.Handled = true;
            };

            _s = Settings.Load();
            Theme.Apply(_s.Theme);
            SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => Theme.Apply(_s.Theme)));

            _notes = new Notifier(_s);
            _toasts = new Toasts(_s, TopDip);
            _notes.Toast += n => _toasts.Show(n);
            _toasts.Clicked += Toasts.OpenTarget;

            _physics = new PhysicsEngine(_s);
            _catalog = new AppCatalog(_s);
            _catalog.Foreground += OnForeground;
            _audio = new AudioFlow(_s);
            _audio.Switched += name => _notes.Post(new Note { Title = "Switched audio", Body = name, Glyph = "\uE767" });
            _activity = new Activity(_s);
            _activity.Downloaded += path => Dispatcher.BeginInvoke(new Action(() =>
                _notes.Post(new Note { App = "Downloads", Title = "Downloaded", Body = Path.GetFileName(path), Glyph = "\uE896", Open = path })));
            _np = new NowPlaying();
            _clips = new Clips(_s);
            _lib = new Library(_s);
            _apps = new AppIndex();
            _cmds = new Commands();
            _pocket = new Pocket(_s);
            _pocket.RestoreLeftovers();
            _lock = new CursorLock(_s);
            _lock.Changed += on => Flash(on ? "cursor locked to this screen" : "cursor free");
            _brightness = new Brightness();
            _brightness.Changed += v => Dispatcher.BeginInvoke(new Action(() => Flash(v >= 0 ? $"brightness {v}%" : "this screen doesn't do brightness over cable")));
            _mouse = new MouseHook(_s, Dispatcher, _brightness);
            _mouse.Flash += Flash;
            _mouse.DesktopDoubleClick += Peek;
            _taskbar = new TaskbarStyler(_s);
            if (!_s.TakeOverToasts && _s.BannerBackup.Count > 0) WinToasts.Restore(_s);
            _wintoasts = new WinToasts(_s, n => _notes.Post(n));
            _keys = new Hotkeys();

            _actions = new ObjectActions(_s, _notes);
            _inspector = new Inspector(_s, _catalog, _actions);
            _shelf = new Shelf(_s, _catalog, _lib, _actions, _pocket, _notes);
            _launcher = new Launcher(_s, _catalog, _apps, _cmds, _clips, _notes, _lib);
            _drop = new Panels(_s, _catalog, _activity, _audio, _notes, _clips, _cmds, _np);
            _dock = new ArcDock(_s, _catalog);
            _panel = new SidePanel(_s, _catalog);
            _panel.Attach(new SettingsPages(_s, _audio, () => _keys.Failed, () => _wintoasts.Status, a => _panel.Dialog(a)));

            _actions.Inspect = o => InspectObj(o, null, null);
            _actions.ToShelf = p => _shelf.AddToShelf(p);
            _actions.ToCollection = (p, _) => _shelf.AskCollection(p);
            _actions.Rename = path => _inspector.Show(Obj.From(path), null, null, true);
            _actions.PinToDock = path => _catalog.AddFile(path);
            _actions.Browse = path => _shelf.Browse(path);
            _shelf.Inspect = InspectObj;
            _launcher.Inspect = InspectObj;
            _launcher.ToShelf = p =>
            {
                _shelf.AddToShelf(p);
                _shelf.OpenShelf(BarDisplay(), BarBottom(), "shelf");
            };
            _launcher.ToCollection = (name, p) => _shelf.AddToCollection(name, p);
            _launcher.Browse = _shelf.Browse;
            _dock.Inspect = (o, r) => InspectObj(o, r, null);
            _dock.SettingsRequested += () => _panel.Open();
            _panel.DockRequested += () => _dock.ShowDock(false, false);
            _panel.QuitRequested += Quit;
            _drop.OpenLauncherClip = () => _launcher.Open(":");
            _drop.OpenSettings = () => _panel.Open();
            _drop.OpenSettingsPage = tab => _panel.OpenTab(tab);

            RegisterCommands();

            _s.Changed += Sync;
            _layoutDelay.Tick += (_, _) =>
            {
                _layoutDelay.Stop();
                Layout(true);
            };
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => _layoutDelay.Start()));

            var hp = new HwndSourceParameters("heft-shell") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            _shellMsgs = new HwndSource(hp);
            _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            _shellMsgs.AddHook(ShellHook);

            _peek.Tick += (_, _) =>
            {
                _peek.Stop();
                _peeking = false;
                DesktopIcons.Set(!_s.HideDesktop);
            };

            SetupTray();
            Layout(true);
            Sync();

            _state.Tick += (_, _) => Tick();
            _state.Start();

            _autosave = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _autosave.Tick += (_, _) =>
            {
                _catalog.Flush();
                _s.SaveNow();
                _notes.Save();
            };
            _autosave.Start();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _catalog.Prewarm();
                _catalog.Scan();
                _apps.Load();
                _lib.Refresh(true);
                Radios.Warm();
            }), DispatcherPriority.ApplicationIdle);
        }

        double TopDip()
        {
            var d = Displays.Primary();
            var bar = _bars.FirstOrDefault(b => b.Display.Primary);
            double px = bar != null ? bar.BottomPx : d.Work.Top;
            return px / d.Scale;
        }

        Display BarDisplay()
        {
            var here = Displays.AtCursor();
            return _bars.FirstOrDefault(b => b.Display.Index == here.Index)?.Display ?? here;
        }

        double BarBottom(Display? d = null)
        {
            d ??= BarDisplay();
            var bar = _bars.FirstOrDefault(b => b.Display.Index == d.Index);
            return bar != null && bar.IsVisible ? bar.BottomPx : d.Work.Top;
        }

        void Flash(string text)
        {
            var here = Displays.AtCursor();
            var bar = _bars.FirstOrDefault(b => b.Display.Index == here.Index) ?? _bars.FirstOrDefault();
            if (bar != null && bar.IsVisible) bar.Flash(text);
        }

        void OnForeground(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string file = Path.GetFileNameWithoutExtension(path);
            if (file.Equals("heft", StringComparison.OrdinalIgnoreCase)) return;
            _appName = file.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "Desktop" : _catalog.DisplayName(path);
        }

        IntPtr ShellHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var b in _bars) b.Rebind();
                    Layout(true);
                    DesktopIcons.Set(!_s.HideDesktop);
                }), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        void Layout(bool force)
        {
            var all = Displays.All();
            string sig = string.Join("|", all.Select(d => d.Key + d.Scale)) + $"|{_s.BarEnabled}|{_s.BarScreens}|{_s.BarAutoHide}|{_s.BarHeight}|{_s.DockScreens}|{_s.DockSide}|{_s.ShowHandle}";
            if (!force && sig == _layoutSig) return;
            _layoutSig = sig;

            var wantBars = _s.BarEnabled ? Displays.BarScreens(_s, all) : new List<Display>();
            while (_bars.Count > wantBars.Count)
            {
                var b = _bars[^1];
                b.Shutdown();
                b.Close();
                _bars.RemoveAt(_bars.Count - 1);
            }
            for (int i = 0; i < wantBars.Count; i++)
            {
                if (i >= _bars.Count) _bars.Add(NewBar(wantBars[i]));
                _bars[i].Apply(wantBars[i]);
            }

            var edges = _s.ShowHandle ? Displays.DockEdges(_s, all) : new List<(Display display, string side)>();
            while (_tabs.Count > edges.Count)
            {
                _tabs[^1].Close();
                _tabs.RemoveAt(_tabs.Count - 1);
            }
            for (int i = 0; i < edges.Count; i++)
            {
                if (i >= _tabs.Count)
                {
                    var t = new EdgeTab();
                    t.DockRequested += () => _dock.ShowDock(true, false, t.Display, t.Side);
                    t.SettingsRequested += () => _panel.Open();
                    t.WorkAreaChanged += () => _layoutDelay.Start();
                    _tabs.Add(t);
                    t.Show();
                }
                _tabs[i].SetEdge(edges[i].display, edges[i].side);
            }
        }

        Bar NewBar(Display d)
        {
            var b = new Bar(_s, d);
            b.KeepOpen = () => _shelf.IsOpen || _drop.IsVisible;
            b.PillClicked += bar => _drop.Toggle("center", bar.Display, bar.BottomPx, bar.Display.Bounds.Left + bar.Display.Bounds.Width / 2.0);
            b.AudioClicked += (bar, x) => _drop.Toggle("audio", bar.Display, bar.BottomPx, x);
            b.NotesClicked += (bar, x) => _drop.Toggle("notes", bar.Display, bar.BottomPx, x);
            b.ClipClicked += () => _launcher.Open(":");
            b.FocusClicked += () => _cmds.Run("focus");
            b.LockClicked += () => _lock.Toggle();
            b.ShelfToggle += bar => _shelf.Toggle(bar.Display, bar.BottomPx);
            b.ShelfPull += bar =>
            {
                if (!_shelf.IsOpen) _shelf.OpenShelf(bar.Display, bar.BottomPx);
            };
            b.SettingsRequested += () => _panel.Open();
            b.Dropped += data =>
            {
                var paths = Shelf.DroppedPaths(data);
                if (paths.Count > 0) _shelf.AddToShelf(paths);
            };
            b.MediaToggle += () => _ = MediaAfter(_np.Toggle());
            b.MediaNext += () => _ = MediaAfter(_np.Next());
            b.MediaPrev += () => _ = MediaAfter(_np.Previous());
            b.AttentionClicked += () => _activity.FocusAttention();
            b.TimerClicked += () => _launcher.Open("");
            b.CopyClicked += () => _activity.FocusCopy();
            b.DownloadClicked += () => Files.Open(Library.DownloadsDir);
            return b;
        }

        async Task MediaAfter(Task t)
        {
            await t;
            await Task.Delay(250);
            await _np.Refresh();
            Tick();
        }

        void Tick()
        {
            _tick++;
            if (_tick % 2 == 0 && (_s.BarMedia || _drop.IsVisible)) _ = _np.Refresh();

            int vol = -1;
            bool muted = false;
            int battery = -1;
            bool charging = false;
            if (_tick % 2 == 0 || _bars.Count == 0)
            {
                vol = Audio.Volume(out muted);
                try
                {
                    var power = Forms.SystemInformation.PowerStatus;
                    bool none = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) || power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Unknown);
                    if (!none)
                    {
                        battery = (int)Math.Round(power.BatteryLifePercent * 100);
                        charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
                    }
                }
                catch
                {
                }
                _lastVol = vol;
                _lastMuted = muted;
                _lastBattery = battery;
                _lastCharging = charging;
            }

            var st = new BarState
            {
                App = _appName,
                MediaAny = _np.HasSession && (_np.Playing || _np.Title.Length > 0) && _np.Playing,
                MediaTitle = _np.Title,
                MediaArtist = _np.Artist,
                MediaPlaying = _np.Playing,
                Art = _np.Art,
                MediaPos = _np.Position,
                MediaDur = _np.Duration,
                Download = _activity.Download,
                Copy = _activity.Copy,
                Attention = _activity.AttentionApp,
                Timer = _notes.ActiveTimer,
                Cpu = _activity.Cpu,
                Ram = _activity.Ram,
                Gpu = _activity.Gpu,
                Down = _activity.NetDown,
                Up = _activity.NetUp,
                Volume = _lastVol,
                Muted = _lastMuted,
                Battery = _lastBattery,
                Charging = _lastCharging,
                Unseen = _notes.Unseen,
                Clip = _clips.Latest?.Summary ?? "",
                Focus = _s.FocusActive,
                Locked = _lock.Locked,
                Stack = _clips.Stacking ? _clips.Stack.Count : -1,
                Profile = _s.ActiveProfile
            };
            foreach (var b in _bars) b.Refresh(st);

            if (_tick % 10 == 0)
            {
                _pocket.Prune();
                if (_s.HideDesktop && !_peeking) DesktopIcons.Set(false);
            }
        }

        int _lastVol = -1, _lastBattery = -1;
        bool _lastMuted, _lastCharging;

        void Sync()
        {
            Layout(false);
            if (!_s.Enabled) _physics.StopAll();
            if (_physicsItem != null) _physicsItem.Checked = _s.Enabled;
            if (!_peeking) DesktopIcons.Set(!_s.HideDesktop);
            _wintoasts.Sync();

            if (_s.FocusActive != _focusApplied)
            {
                _focusApplied = _s.FocusActive;
                var apps = _s.FocusMute.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _audio.Focus(_s.FocusActive, apps);
                if (_s.FocusActive && _s.FocusProfile.Length > 0)
                {
                    var prof = _s.AudioProfiles.FirstOrDefault(x => x.Name == _s.FocusProfile);
                    if (prof != null) _audio.Apply(prof);
                }
                Flash(_s.FocusActive ? "focus on" : "focus off");
            }

            string sig = string.Join("|", Hotkeys.Defaults.Select(d => Hotkeys.Spec(_s, d.id))) + "|" + string.Join("|", _s.AudioProfiles.Select(p => p.Name + "=" + p.Hotkey));
            if (sig != _keySig)
            {
                _keySig = sig;
                RegisterKeys();
            }
        }

        void RegisterKeys()
        {
            _keys.Clear();
            var map = new Dictionary<string, Action>
            {
                ["launcher"] = () => _launcher.Toggle(),
                ["dock"] = () =>
                {
                    var (d, side) = Displays.DockForCursor(_s);
                    _dock.ShowDock(false, true, d, side);
                },
                ["settings"] = () => _panel.Toggle(),
                ["shelf"] = () => _shelf.Toggle(BarDisplay(), BarBottom()),
                ["audio"] = () => OpenDrop("audio"),
                ["notes"] = () => OpenDrop("notes"),
                ["center"] = () => OpenDrop("center"),
                ["lock"] = () => _lock.Toggle(),
                ["focus"] = () => _cmds.Run("focus"),
                ["pocket"] = PocketForeground,
                ["stack"] = () =>
                {
                    _clips.ToggleStack();
                    Flash(_clips.Stacking ? "paste stack on. copy away" : "paste stack off");
                },
                ["paste"] = () => _clips.PasteNext(),
                ["inspect"] = InspectExplorer,
                ["device"] = () =>
                {
                    var name = _audio.CycleOutput();
                    Flash(name != null ? "output: " + name : "only one output");
                },
                ["peek"] = Peek
            };
            foreach (var d in Hotkeys.Defaults)
                if (map.TryGetValue(d.id, out var act)) _keys.Add(Hotkeys.Spec(_s, d.id), act, d.label);
            foreach (var prof in _s.AudioProfiles.Where(p => p.Hotkey.Length > 0))
            {
                var pr = prof;
                _keys.Add(pr.Hotkey, () =>
                {
                    _audio.Apply(pr);
                    Flash("audio: " + pr.Name);
                }, "profile " + pr.Name);
            }
            if (_keys.Failed.Count > 0) PhysicsEngine.Log("hotkeys: " + string.Join("; ", _keys.Failed));
        }

        void OpenDrop(string page)
        {
            var d = BarDisplay();
            double x = page == "center" ? d.Bounds.Left + d.Bounds.Width / 2.0 : d.Bounds.Right - 120 * d.Scale;
            _drop.Toggle(page, d, BarBottom(d), x);
        }

        void Peek()
        {
            if (!_s.HideDesktop) return;
            _peeking = true;
            DesktopIcons.Set(true);
            _peek.Interval = TimeSpan.FromSeconds(Math.Clamp(_s.PeekSeconds, 3, 60));
            _peek.Stop();
            _peek.Start();
        }

        void PocketForeground()
        {
            var fg = Native.GetForegroundWindow();
            if (_pocket.Stash(fg)) Flash("pocketed. it's on the shelf");
        }

        void InspectObj(Obj o, Rect? src, System.Windows.Media.ImageSource? icon)
        {
            _inspector.Show(o, src, icon);
        }

        void InspectExplorer()
        {
            string? path = ExplorerSelection();
            if (path == null)
            {
                Flash("select something in File Explorer first");
                return;
            }
            InspectObj(Obj.From(path), null, null);
        }

        static string? ExplorerSelection()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return null;
                dynamic shell = Activator.CreateInstance(type)!;
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        if (Convert.ToInt64(w.HWND) != fg.ToInt64()) continue;
                        foreach (dynamic item in w.Document.SelectedItems())
                            return (string)item.Path;
                        return (string)w.Document.Folder.Self.Path;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        void RegisterCommands()
        {
            var c = _cmds;
            c.Add("heft", "launcher", "Launcher", "\uE721", () => _launcher.Open());
            c.Add("heft", "shelf", "Open the shelf", "\uE7B8", () => _shelf.OpenShelf(BarDisplay(), BarBottom(), "shelf"), "stash drawer");
            c.Add("heft", "catalog", "Open the catalog", "\uE82D", () => _shelf.OpenShelf(BarDisplay(), BarBottom(), "catalog"), "bookshelf folders books");
            foreach (var (id, label, glyph) in Library.Smart)
            {
                string tab = id;
                c.Add("heft", "col-" + id, "Collection: " + label, glyph, () => _shelf.OpenShelf(BarDisplay(), BarBottom(), tab), "collection shelf");
            }
            c.Add("heft", "audioflow", "AudioFlow", "\uE767", () => OpenDrop("audio"), "volume mixer sound devices");
            c.Add("heft", "notes", "Notifications", "\uEA8F", () => OpenDrop("notes"), "notification center history");
            c.Add("heft", "center", "Control center", "\uE9E9", () => OpenDrop("center"), "system monitor cpu gpu ram toggles");
            c.Add("heft", "settings", "heft settings", "\uE713", () => _panel.Open(), "preferences options");
            c.Add("heft", "dock", "Dock", "\uE8A9", () =>
            {
                var (d, side) = Displays.DockForCursor(_s);
                _dock.ShowDock(false, false, d, side);
            }, "wheel arc apps");
            c.Add("heft", "focus", "Focus mode", "\uE708", () =>
            {
                _s.FocusActive = !_s.FocusActive;
                _s.Touch();
            }, "do not disturb dnd quiet", false, () => _s.FocusActive ? "on" : "off");
            c.Add("heft", "lock", "Cursor lock", "\uE72E", () => _lock.Toggle(), "mouse monitor game trap clip", false, () => _lock.Locked ? "on" : "off");
            c.Add("heft", "physics", "Window physics", "\uE945", () =>
            {
                _s.Enabled = !_s.Enabled;
                _s.Touch();
            }, "throw fling", false, () => _s.Enabled ? "on" : "off");
            c.Add("heft", "desktop", "Desktop icons", "\uE7F4", () =>
            {
                _s.HideDesktop = !_s.HideDesktop;
                _s.Touch();
            }, "hide show clean desktop", false, () => _s.HideDesktop ? "hidden" : "showing");
            c.Add("heft", "taskbar", "Taskbar", "\uE75B", () =>
            {
                _s.TaskbarHide = _s.TaskbarHide == "off" ? "hidden" : "off";
                _s.Touch();
            }, "hide show", false, () => _s.TaskbarHide == "off" ? "showing" : _s.TaskbarHide);
            c.Add("heft", "stack", "Paste stack", "\uE77F", () => _clips.ToggleStack(), "clipboard queue", false, () => _clips.Stacking ? "on" : "off");
            c.Add("heft", "unpocket", "Bring back pocketed windows", "\uE8A7", () =>
            {
                foreach (var e in _pocket.Items.ToList()) _pocket.Release(e, false);
            }, "pocket restore");
            c.Add("heft", "timer-cancel", "Cancel timer", "\uE916", () => _notes.CancelTimer(), "stop");
            c.Add("heft", "theme", "Switch light / dark", "\uE793", () =>
            {
                _s.Theme = Theme.IsDark ? "light" : "dark";
                Theme.Apply(_s.Theme);
                _s.Touch();
            }, "dark mode light mode appearance");
            c.Add("heft", "share", "Share my setup", "\uE72D", () => _panel.OpenTab("share"), "export import heftpack");
            c.Add("heft", "rescan", "Rescan apps + projects", "\uE72C", () =>
            {
                _catalog.Scan();
                _apps.Load();
                _lib.Refresh(true);
            }, "refresh");
            c.Add("heft", "quit", "Quit heft", "\uE7E8", Quit, "exit close");

            c.Add("system", "pclock", "Lock PC", "\uE72E", () => Native.LockWorkStation(), "lock screen away");
            c.Add("system", "sleep", "Sleep", "\uE708", () => Native.SetSuspendState(false, false, false), "suspend");
            c.Add("system", "restart", "Restart", "\uE777", () => Launch.Run("shutdown.exe", "/r /t 0"), "reboot", true);
            c.Add("system", "shutdown", "Shut down", "\uE7E8", () => Launch.Run("shutdown.exe", "/s /t 0"), "power off", true);
            c.Add("system", "signout", "Sign out", "\uF3B1", () => Launch.Run("shutdown.exe", "/l"), "log off", true);
            c.Add("system", "recycle", "Empty recycle bin", "\uE74D", () => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2), "trash", true);
            c.Add("system", "taskmgr", "Task Manager", "\uE9D9", () => Launch.Run("taskmgr.exe"), "processes kill");
            c.Add("system", "snip", "Screenshot", "\uE722", () => Launch.Run("ms-screenclip:"), "snip capture");
            c.Add("system", "explorer", "Restart Explorer", "\uE72C", () =>
            {
                foreach (var p in Process.GetProcessesByName("explorer")) p.Kill();
            }, "fix taskbar");
            c.Add("system", "mute", "Mute / unmute", "\uE74F", () => Audio.ToggleMute(), "sound");
            c.Add("system", "output", "Next audio output", "\uE7F5", () =>
            {
                var name = _audio.CycleOutput();
                Flash(name != null ? "output: " + name : "only one output");
            }, "headphones speakers device");
            c.Add("system", "wifi", "Wi-Fi on/off", "\uE701", () => _ = Radios.Toggle("wifi"), "wireless internet");
            c.Add("system", "bluetooth", "Bluetooth on/off", "\uE702", () => _ = Radios.Toggle("bluetooth"), "bt");
            c.Add("system", "downloads", "Downloads folder", "\uE896", () => Files.Open(Library.DownloadsDir), "files");
        }

        void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("launcher", null, (_, _) => _launcher.Open());
            menu.Items.Add("shelf", null, (_, _) => _shelf.OpenShelf(BarDisplay(), BarBottom()));
            menu.Items.Add("settings", null, (_, _) => _panel.Open());
            menu.Items.Add(new Forms.ToolStripSeparator());
            _physicsItem = new Forms.ToolStripMenuItem("window physics") { Checked = _s.Enabled };
            _physicsItem.Click += (_, _) =>
            {
                _s.Enabled = !_s.Enabled;
                _s.Touch();
            };
            menu.Items.Add(_physicsItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("quit", null, (_, _) => Quit());

            _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "heft", ContextMenuStrip = menu, Visible = true };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) _panel.Toggle();
            };
        }

        static Drawing.Icon MakeIcon()
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var pen = new Drawing.Pen(Drawing.Color.White, 2.6f) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round };
                using var path = new Drawing.Drawing2D.GraphicsPath();
                const float x = 12, y = 7, w = 17, h = 15, r = 4;
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
                g.DrawLine(pen, 3, 11, 8, 11);
                g.DrawLine(pen, 5, 17, 8, 17);
                g.DrawLine(pen, 7, 26, 23, 26);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            Cleanup();
            Shutdown();
        }

        void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;
            Try(() => _state.Stop());
            Try(() => _autosave?.Stop());
            Try(() => _catalog?.Dispose());
            Try(() => _notes?.Save());
            Try(() => _s?.SaveNow());
            Try(() => _physics?.Dispose());
            Try(() => _mouse?.Dispose());
            Try(() => _taskbar?.Dispose());
            Try(() => _wintoasts?.Dispose());
            Try(() => _lock?.Dispose());
            Try(() => _brightness?.Dispose());
            Try(() => _activity?.Dispose());
            Try(() => _clips?.Dispose());
            Try(() => _audio?.Dispose());
            Try(() => _keys?.Dispose());
            Try(() =>
            {
                foreach (var e in _pocket.Items.ToList()) _pocket.Release(e, false);
            });
            Try(() => DesktopIcons.Set(true));
            Try(() =>
            {
                foreach (var b in _bars) b.Shutdown();
            });
            Try(() =>
            {
                if (_tray == null) return;
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            });
        }

        static void Try(Action a)
        {
            try
            {
                a();
            }
            catch
            {
            }
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            Cleanup();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!_quitting && _s != null) Cleanup();
            _single?.Dispose();
            base.OnExit(e);
        }
    }
}
