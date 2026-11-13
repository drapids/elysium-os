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
        KeyHook _keyHook = null!;

        ArcDock _dock = null!;
        SidePanel _panel = null!;
        Panels _drop = null!;
        Shelf _shelf = null!;
        Launcher _launcher = null!;
        Inspector _inspector = null!;
        StartData _startData = null!;
        StartScreen _start = null!;
        Overview _overview = null!;
        WindowStacks _stacks = null!;
        SnapZones? _snap;
        Pip _pip = null!;
        QuickShare _share = null!;
        VersionShelf _versions = null!;
        DownloadSorter _sorter = null!;
        Slit _slit = null!;
        ShortcutSheet _sheet = null!;
        Spaces _spaces = null!;
        readonly Dictionary<string, Action> _keyActions = new();
        Lens _lens = null!;
        ExtHost _ext = null!;
        string _songKey = "";
        Recording? _rec;
        RecChip? _recChip;
        readonly DispatcherTimer _cornerTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
        string _cornerHit = "";
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
            Sfx.Init(_s, () => _np != null && _np.Playing);
            Theme.Configure(_s);
            IconCache.Pack = _s.IconPack;
            Theme.Apply(_s.Theme);
            Task.Run(IconCache.TrimDisk);
            HealStartup();
            SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => Theme.Apply(_s.Theme)));

            _notes = new Notifier(_s);
            _toasts = new Toasts(_s, d => BarBottom(d));
            _notes.Toast += n =>
            {
                if (HoldToast()) return;
                _toasts.Show(n);
                Sfx.Play(n.App == "heft" && (n.Title == "Reminder" || n.Title == "Timer done") ? "chime" : "notify");
            };
            _toasts.Clicked += Toasts.OpenTarget;

            _physics = new PhysicsEngine(_s);
            _catalog = new AppCatalog(_s);
            _catalog.Foreground += OnForeground;
            _stacks = new WindowStacks(_s, _catalog);
            _snap = new SnapZones(_s, _physics);
            _stacks.RestoreLeftovers();
            _physics.StackHover += (h, on) => Dispatcher.BeginInvoke(new Action(() => _stacks.Hover(h, on)));
            _physics.StackDrop += (a, b) => Dispatcher.BeginInvoke(new Action(() => _stacks.Join(a, b)));
            _pip = new Pip(_s);
            _pip.Say += text => Flash(text);
            _audio = new AudioFlow(_s);
            _audio.Switched += name => _notes.Post(new Note { Title = "Switched audio", Body = name, Glyph = "\uE767" });
            _activity = new Activity(_s);
            _activity.Downloaded += path => Dispatcher.BeginInvoke(new Action(() =>
            {
                Downloaded(path);
                _notes.Post(new Note { App = "Downloads", Title = "Downloaded", Body = Path.GetFileName(path), Glyph = "\uE896", Open = path });
            }));
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
            _mouse.Physics = _physics;
            _mouse.DesktopDoubleClick += Peek;
            _taskbar = new TaskbarStyler(_s);
            if (!_s.TakeOverToasts && _s.BannerBackup.Count > 0) WinToasts.Restore(_s);
            _wintoasts = new WinToasts(_s, n => _notes.Post(n));
            _keys = new Hotkeys();
            _keyHook = new KeyHook(_s, Dispatcher);
            _keyHook.ExplorerSpace += PreviewExplorer;
            _share = new QuickShare(_s, _notes);
            _versions = new VersionShelf(_s);
            _sorter = new DownloadSorter(_s, _notes);

            _actions = new ObjectActions(_s, _notes);
            _inspector = new Inspector(_s, _catalog, _actions);
            _shelf = new Shelf(_s, _catalog, _lib, _actions, _pocket, _notes);
            _launcher = new Launcher(_s, _catalog, _apps, _cmds, _clips, _notes, _lib);
            _drop = new Panels(_s, _catalog, _activity, _audio, _notes, _clips, _cmds, _np);
            _dock = new ArcDock(_s, _catalog);
            _panel = new SidePanel(_s, _catalog);
            _startData = new StartData(_apps);
            _start = new StartScreen(_s, _catalog, _apps, _startData, _cmds, _notes, _np, _activity, _lib);
            _overview = new Overview(_catalog);
            var warm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            warm.Tick += (_, _) =>
            {
                warm.Stop();
                _start.Prewarm();
            };
            warm.Start();
            _slit = new Slit(p => _catalog.Icon(p, 64, Files.IsApp(p)));
            _sheet = new ShortcutSheet(_s, RunAction);
            _spaces = new Spaces(_s, _catalog, _lib, _np, _startData);
            _spaces.Inspect = (o, r) => InspectObj(o, r, null);
            _spaces.Say = text => Flash(text);
            _lens = new Lens(_s);
            _lens.Say = text => Flash(text);
            _lens.ToShelf = path => _shelf.AddToShelf(new[] { path });
            _lens.ToSpace = path => _spaces.AddFile(path);
            _lens.Share = paths => _share.Share(paths);
            _lens.Pin = (img, r) => PinImage(img, r);
            _lens.Markup = OpenMarkup;
            _lens.Record = StartRecording;
            _start.Search = text => _launcher.Open(text);
            _start.OpenSettings = () => _panel.Open();
            _start.Overview = () => _overview.Open();
            var pages = new SettingsPages(_s, _audio, () => _keys.Failed, () => _wintoasts.Status, a => _panel.Dialog(a));
            pages.SortDownloads = () =>
            {
                _sorter.Sweep(true);
                Flash("tidying Downloads");
            };
            pages.FindDupes = () =>
            {
                _lib.FindDupes(true);
                _shelf.OpenShelf(BarDisplay(), BarBottom(), "dupes");
            };
            pages.Cmds = _cmds;
            InitWindows(pages);
            _ext = new ExtHost(_s, _cmds, _notes, _np);
            pages.Ext = _ext;
            pages.IconsChanged = () =>
            {
                _catalog.Icons.Clear();
                _start.MarkDirty();
            };
            pages.KeyFree = spec => _keys.Free(spec);
            pages.PauseKeys = on =>
            {
                _keyHook.Paused = on;
                if (on) _keys.Clear();
                else RegisterKeys();
            };
            _panel.Attach(pages);

            _actions.Inspect = o => InspectObj(o, null, null);
            _actions.ToShelf = p => _shelf.AddToShelf(p);
            _actions.ToCollection = (p, _) => _shelf.AskCollection(p);
            _actions.Rename = path => _inspector.Show(Obj.From(path), null, null, true);
            _actions.PinToDock = path => _catalog.AddFile(path);
            _actions.Browse = path => _shelf.Browse(path);
            _actions.Share = paths => _share.Share(paths);
            _launcher.Share = paths => _share.Share(paths);
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
            _ext.Changed += () => _shelf.RefreshDrops();
            _ext.BarChanged += () =>
            {
                foreach (var bar in _bars) bar.SetExt(_ext.BarItems);
            };
            _ext.KeysUpdated += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_keyHook.Paused) RegisterKeys();
            }), DispatcherPriority.Background);
            _shelf.ExtraDrops = () => _ext.DropActions;
            _start.ExtWidgets = () => _ext.Widgets;
            _activity.Downloaded += path => Dispatcher.BeginInvoke(new Action(() => _ext.Emit("download", path)));
            _clips.Changed += () => Dispatcher.BeginInvoke(new Action(() => _ext.Emit("clipboard", _clips.Latest?.Text ?? "")));
            _ext.Start();
            _keyHook.WinTapped += () =>
            {
                if (_game && _s.GameBlockWin) return;
                _start.Toggle();
            };
            _mouse.Corner += corner =>
            {
                _cornerHit = corner;
                _cornerTimer.Stop();
                _cornerTimer.Start();
            };
            _cornerTimer.Tick += (_, _) =>
            {
                _cornerTimer.Stop();
                FireCorner();
            };

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
            _rules?.Saw(Native.GetForegroundWindow(), path);
            string file = Path.GetFileNameWithoutExtension(path);
            if (file.Equals("heft", StringComparison.OrdinalIgnoreCase)) return;
            _appName = file.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "Desktop" : _catalog.DisplayName(path);
            _ext?.Emit("window", _appName, path);
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
            _ext?.Emit("monitor", all.Count);

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
            b.KeepOpen = () => _shelf.IsOpen || _drop.Shown;
            if (_ext != null) b.SetExt(_ext.BarItems);
            WireBarExtras(b);
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
                if (paths.Count == 0) return;
                _shelf.AddToShelf(paths);
                if (b.SlitOpen)
                {
                    _slit.Swallow(b, paths);
                    b.Gulp();
                }
                else Sfx.Play("drop");
            };
            b.DragNear += bar =>
            {
                if (_shelf.IsOpen) return;
                if (_s.DropSlit) bar.OpenSlit();
                else _shelf.OpenShelf(bar.Display, bar.BottomPx);
            };
            b.SlitSpring += bar =>
            {
                bar.CloseSlit();
                _shelf.OpenShelf(bar.Display, bar.BottomPx, "shelf");
            };
            b.EmptyClicked += bar =>
            {
                if (_shelf.IsOpen && !_shelf.Pinned) _shelf.HideShelf();
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
            CheckGame();
            if (_game) return;
            if (_tick % 2 == 0) DeskTick();
            if (_tick % 2 == 0 && (_s.BarMedia || _drop.Shown || (_ext != null && _ext.WantsMedia))) _ = _np.Refresh();
            if (_ext != null)
            {
                string song = _np.HasSession ? _np.Title + "|" + _np.Artist + "|" + _np.Playing : "";
                if (song != _songKey)
                {
                    _songKey = song;
                    _ext.Emit("song", _np.Title, _np.Artist, _np.Playing);
                }
            }

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
            SyncBarLayout();
            if (!_s.Enabled) _physics.StopAll();
            if (_physicsItem != null) _physicsItem.Checked = _s.Enabled;
            if (!_peeking) DesktopIcons.Set(!_s.HideDesktop);
            _wintoasts.Sync();
            _versions.Sync();

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

            string sig = string.Join("|", Hotkeys.Defaults.Select(d => Hotkeys.Spec(_s, d.id))) + "|" + string.Join("|", _s.AudioProfiles.Select(p => p.Name + "=" + p.Hotkey))
                         + "|" + string.Join("|", _s.CustomKeys.Select(k => k.Keys + "=" + k.Kind + ":" + k.Target))
                         + "|" + string.Join("|", _s.Remaps.Select(kv => kv.Key + ">" + kv.Value));
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
                ["peek"] = Peek,
                ["overview"] = () => _overview.Toggle(),
                ["spaces"] = () => _spaces.Toggle(),
                ["pip"] = () => _pip.Toggle(),
                ["share"] = ShareExplorer,
                ["stacknext"] = () => _stacks.CycleForeground(),
                ["sheet"] = () => _sheet.Toggle(),
                ["capture"] = () => _lens.Open(),
                ["pickcolor"] = () => _lens.Open("color"),
                ["start"] = () => _start.Toggle(),
                ["reopen"] = ReopenLast,
                ["scratch"] = () => OpenDrop("scratch"),
                ["lastdl"] = () => GrabDownload(),
                ["remember"] = RememberWindow
            };
            _keyActions.Clear();
            foreach (var kv in map) _keyActions[kv.Key] = kv.Value;
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
            if (_ext != null)
                foreach (var (spec, run, label) in _ext.Hotkeys) _keys.Add(spec, run, label);
            foreach (var ck in _s.CustomKeys)
            {
                var k = ck;
                if (k.Keys.Length > 0) _keys.Add(k.Keys, () => RunCustom(k), k.Label.Length > 0 ? k.Label : k.Target);
            }
            _keyHook.SetRemaps(_s.Remaps);
            if (_keys.Failed.Count > 0) PhysicsEngine.Log("hotkeys: " + string.Join("; ", _keys.Failed));
        }

        void RunCustom(CustomKey k)
        {
            try
            {
                string t = k.Target;
                switch (k.Kind)
                {
                    case "heft":
                        if (!_cmds.Run(t)) RunAction(t);
                        break;
                    case "app":
                        Launch.Run(t);
                        break;
                    case "file":
                    case "folder":
                        Files.Open(t, _s);
                        break;
                    case "url":
                        Launch.Url(t.Contains("://") ? t : "https://" + t);
                        break;
                    case "text":
                        Clips.PasteNow(t);
                        break;
                    case "script":
                        _ = _cmds.Script(t);
                        break;
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("custom shortcut failed " + ex.Message);
                Flash("that shortcut didn't work");
            }
        }

        void FireCorner()
        {
            string corner = _cornerHit;
            if (corner.Length == 0 || Native.MouseDown()) return;
            Native.GetCursorPos(out var p);
            var d = Displays.At(p.X, p.Y);
            var b = d.Bounds;
            bool still = corner switch
            {
                "tl" => p.X <= b.Left + 1 && p.Y <= b.Top + 1,
                "tr" => p.X >= b.Right - 2 && p.Y <= b.Top + 1,
                "bl" => p.X <= b.Left + 1 && p.Y >= b.Bottom - 2,
                _ => p.X >= b.Right - 2 && p.Y >= b.Bottom - 2
            };
            if (!still) return;
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && CursorLock.IsFullscreen(fg, out _)) return;
            string action = _s.HotCorners.TryGetValue(corner, out var a) ? a : "none";
            if (action != "none") Sfx.Play("tick");
            FireAction(action);
        }

        bool RunAction(string id)
        {
            if (id.StartsWith("profile:", StringComparison.Ordinal))
            {
                string name = id[8..];
                var prof = _s.AudioProfiles.FirstOrDefault(p => p.Name == name);
                if (prof == null) return false;
                _audio.Apply(prof);
                Flash("audio: " + prof.Name);
                return true;
            }
            if (id.StartsWith("corner:", StringComparison.Ordinal)) return FireAction(id[7..]);
            if (id.StartsWith("custom:", StringComparison.Ordinal))
            {
                var ck = _s.CustomKeys.FirstOrDefault(c => c.Id == id[7..]);
                if (ck == null) return false;
                RunCustom(ck);
                return true;
            }
            if (_keyActions.TryGetValue(id, out var act))
            {
                act();
                return true;
            }
            return FireAction(id);
        }

        bool FireAction(string action)
        {
            switch (action)
            {
                case "start":
                    _start.Toggle();
                    break;
                case "overview":
                    _overview.Toggle();
                    break;
                case "launcher":
                    _launcher.Toggle();
                    break;
                case "center":
                    OpenDrop("center");
                    break;
                case "shelf":
                    _shelf.Toggle(BarDisplay(), BarBottom());
                    break;
                case "desktop":
                    Native.Combo(0x5B, 0x44);
                    break;
                case "lock":
                    Native.LockWorkStation();
                    break;
                case "spaces":
                    _spaces.Toggle();
                    break;
                default:
                    return false;
            }
            return true;
        }

        void PinImage(System.Windows.Media.Imaging.BitmapSource img, Native.RECT? at)
        {
            var pin = new PinShot(_s, img);
            pin.Say = text => Flash(text);
            pin.Markup = OpenMarkup;
            Native.RECT r;
            if (at != null) r = at.Value;
            else
            {
                var d = Displays.AtCursor();
                int w = Math.Min(img.PixelWidth, d.Work.Width - 80), h = Math.Min(img.PixelHeight, d.Work.Height - 80);
                int x = d.Work.Left + (d.Work.Width - w) / 2, y = d.Work.Top + (d.Work.Height - h) / 2;
                r = new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
            }
            pin.ShowAt(r);
        }

        void OpenMarkup(System.Windows.Media.Imaging.BitmapSource img)
        {
            var m = new MarkupWindow(_s, img);
            m.Say = text => Flash(text);
            m.Pin = b => PinImage(b, null);
            m.Open();
        }

        void StartRecording(Native.RECT r, bool gif)
        {
            if (_rec != null)
            {
                Flash("already recording");
                return;
            }
            var rec = Recording.Start(_s, r, gif);
            if (rec == null)
            {
                Flash("couldn't start recording");
                return;
            }
            _rec = rec;
            _recChip = new RecChip();
            _recChip.StopRequested += () => _ = StopRecording();
            _recChip.ShowNear(r);
            Sfx.Play("tick");
        }

        async Task StopRecording()
        {
            var rec = _rec;
            if (rec == null) return;
            _rec = null;
            _recChip?.Done();
            _recChip = null;
            Flash(rec.Gif ? "making the gif..." : "saving the video...");
            string? path = await rec.Stop();
            if (path == null)
            {
                Flash("recording didn't save");
                return;
            }
            _notes.Post(rec.Gif ? "GIF ready" : "Recording saved", Path.GetFileName(path), "\uE714", path);
            _shelf.AddToShelf(new[] { path });
            Sfx.Play("chime");
        }

        void HealStartup()
        {
            if (!_s.StartWithWindows || !Heft.Startup.CanSet) return;
            string exe = Environment.ProcessPath ?? "";
            if (string.Equals(_s.StartupPath, exe, StringComparison.OrdinalIgnoreCase)) return;
            bool early = _s.StartEarly;
            Task.Run(() => Heft.Startup.Set(true, early)).ContinueWith(t =>
            {
                if (t.IsFaulted || !t.Result) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _s.StartupPath = exe;
                    _s.Touch();
                }));
            });
        }

        void PreviewExplorer()
        {
            var paths = ExplorerSelection(true);
            if (paths.Count == 0)
            {
                Native.Tap(0x20);
                return;
            }
            if (_inspector.IsShowing) _inspector.Close();
            InspectObj(Obj.From(paths[0]), null, null);
        }

        void ShareExplorer()
        {
            var paths = ExplorerSelection(true);
            if (paths.Count == 0)
            {
                Flash("select something in File Explorer first");
                return;
            }
            _share.Share(paths);
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
            var paths = ExplorerSelection(false);
            if (paths.Count == 0)
            {
                Flash("select something in File Explorer first");
                return;
            }
            InspectObj(Obj.From(paths[0]), null, null);
        }

        static List<string> ExplorerSelection(bool selectionOnly)
        {
            var list = new List<string>();
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return list;
                dynamic shell = Activator.CreateInstance(type)!;
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        if (Convert.ToInt64(w.HWND) != fg.ToInt64()) continue;
                        foreach (dynamic item in w.Document.SelectedItems())
                        {
                            string p = (string)item.Path;
                            if (!string.IsNullOrEmpty(p)) list.Add(p);
                            if (list.Count >= 200) break;
                        }
                        if (list.Count == 0 && !selectionOnly) list.Add((string)w.Document.Folder.Self.Path);
                        return list;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        void RegisterCommands()
        {
            var c = _cmds;
            c.Add("heft", "launcher", "Launcher", "\uE721", () => _launcher.Open());
            c.Add("heft", "start", "Start", "\uE782", () => _start.Open(), "start menu home");
            c.Add("heft", "overview", "Overview (all windows)", "\uE8A9", () => _overview.Open(), "windows switcher mission control expose");
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
            c.Add("heft", "scratch", "Scratchpad", "\uE70B", () => OpenDrop("scratch"), "notes scratch jot pad text");
            c.Add("heft", "last-download", "Grab your last download", "\uE896", () => GrabDownload(), "download recent file drag");
            c.Add("heft", "reopen", "Reopen last closed app", "\uE7A7", ReopenLast, "undo close restore app again");
            c.Add("heft", "remember-window", "Remember where this window goes", "\uE8A7", RememberWindow, "window rule position monitor place open");
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
            c.Add("heft", "spaces", "Spaces", "\uE80A", () => _spaces.Open(), "spatial desktop canvas board zoom widgets");
            c.Add("heft", "pip", "Picture in picture", "\uE8A7", () => _pip.Toggle(), "pip mini floating corner window");
            c.Add("heft", "sheet", "Every shortcut", "\uE765", () => _sheet.Open(), "keys keyboard cheat sheet help hotkeys");
            c.Add("heft", "sort-downloads", "Sort Downloads now", "\uE8CB", () => _sorter.Sweep(true), "tidy organize downloads folders");
            c.Add("heft", "find-dupes", "Find duplicate files", "\uE8C8", () =>
            {
                _lib.FindDupes(true);
                _shelf.OpenShelf(BarDisplay(), BarBottom(), "dupes");
            }, "duplicates copies clean space");
            c.Add("heft", "undo-rename", "Undo rename", "\uE7A7", () =>
            {
                int n = BulkRename.Undo();
                Flash(n > 0 ? $"put back {n} name{(n == 1 ? "" : "s")}" : "nothing to undo");
            }, "undo rename back");
            c.Add("heft", "sounds", "Sounds", "\uE767", () =>
            {
                _s.SoundsOn = !_s.SoundsOn;
                _s.Touch();
                if (_s.SoundsOn) Sfx.Test("chime");
            }, "audio clicks chimes sfx", false, () => _s.SoundsOn ? "on" : "off");
            c.Add("heft", "lerp", "Window lerp", "\uE945", () =>
            {
                _s.Lerp = !_s.Lerp;
                _s.Touch();
            }, "smooth drag lag trail", false, () => _s.Lerp ? "on" : "off");
            c.Add("heft", "unstack", "Pull this window out of its stack", "\uE8A9", () => _stacks.Take(Native.GetForegroundWindow()), "tabs window stack");
            c.Add("heft", "share-stop", "Stop sharing", "\uE8EA", () => _share.Stop(), "quick share phone");
            c.Add("heft", "screenshot", "Screenshot", "\uE722", () => _lens.Open("shot"), "capture snip screen grab print");
            c.Add("heft", "copy-text", "Copy text from the screen", "\uE8D2", () => _lens.Open("text"), "ocr read text extract");
            c.Add("heft", "pick-color", "Pick a color", "\uE790", () => _lens.Open("color"), "hex eyedropper picker");
            c.Add("heft", "grab-palette", "Grab colors from the screen", "\uE771", () => _lens.Open("aesthetic"), "palette aesthetic vibe colors");
            c.Add("heft", "read-qr", "Read a QR code on screen", "\uED14", () => _lens.Open("qr"), "qr scan barcode");
            c.Add("heft", "measure", "Measure the screen", "\uED5E", () => _lens.Open("measure"), "ruler pixels distance");
            c.Add("heft", "record", "Record the screen", "\uE714", () => _lens.Open("gif"), "gif video capture recording");
            c.Add("heft", "stop-recording", "Stop recording", "\uE71A", () => _ = StopRecording(), "recording gif");
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
            Try(() => _stacks?.ReleaseAll());
            Try(() => _snap?.Dispose());
            Try(() => _watch?.Dispose());
            Try(() => _grab?.Close());
            Try(() => _pip?.ReleaseAll());
            Try(() => _share?.Dispose());
            Try(() => _versions?.Dispose());
            Try(() => _sorter?.Dispose());
            Try(() => _spaces?.Close2());
            Try(() => _ext?.Dispose());
            Try(() => Sfx.Stop());
            Try(() => _notes?.Save());
            Try(() => _s?.SaveNow());
            Try(() => _physics?.Dispose());
            Try(() => _mouse?.Dispose());
            Try(() => _keyHook?.Dispose());
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
