using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Elysium.Ui;

namespace Elysium
{
    public partial class App
    {
        FocusRing? _ring;
        readonly Spotlight _spot = new();
        readonly MediaHandoff _handoff = new();
        NoteCard? _noteCard;
        NoteEditor? _noteEditor;
        readonly HashSet<string> _notesHidden = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<IntPtr, (Native.RECT frame, bool max)>> _layouts = new();
        readonly Dictionary<string, string> _exeNames = new(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer _displayWait = new() { Interval = TimeSpan.FromMilliseconds(2500) };
        string _dispSig = "";
        string _lastOutput = "";
        bool _lastOutputPhones;
        string _featSig = "";
        long _stampAt;

        bool _featuresReady;
        readonly System.Diagnostics.Stopwatch _timeClock = System.Diagnostics.Stopwatch.StartNew();
        double _timeLast;

        void InitFeatures()
        {
            WinEvents.Start();
            _dispSig = DisplaySig();
            _displayWait.Tick += (_, _) =>
            {
                _displayWait.Stop();
                DisplaysChanged();
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _displayWait.Stop();
                _displayWait.Start();
            }));
            var output = _audio.DefaultOutput();
            _lastOutput = output?.Id ?? "";
            _lastOutputPhones = output != null && AudioFlow.LooksLikeHeadphones(output.Name);
            _audio.DevicesChanged += OutputChanged;
            _keyHook.Hold += OnHold;
            _mouse.Hold += OnHold;
            WinEvents.Foreground += NoteFor;

            _overview.Hooks.Pip = h => _pip.Pop(h);
            _overview.Hooks.Pocket = h =>
            {
                if (_pocket.Stash(h)) Flash("pocketed. it's on the shelf");
            };
            _overview.Hooks.Shot = ShotWindow;
            _overview.Hooks.Record = h =>
            {
                WinList.Switch(h);
                StartRecording(Native.FrameBounds(h), false);
            };
            _overview.Hooks.Remember = h =>
            {
                _rules?.Saw(h, Native.ProcessPath(h));
                RememberWindow();
            };
            _overview.Hooks.Note = h => EditNote(h);
            _overview.Hooks.Stack = (a, b) => _stacks.Join(a, b);
            _overview.Hooks.Inspect = path => InspectObj(Obj.From(path), null, null);
            _overview.Hooks.Audio = exe =>
            {
                var app = _audio.Apps().FirstOrDefault(a => a.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
                return app == null ? ((bool, bool)?)null : (app.Peak > 0.01f, app.Muted);
            };
            _overview.Hooks.Mute = (exe, mute) => _audio.SetMute(exe, mute);
            _overview.Hooks.Closed = () => _s.RecentlyClosed;
            _overview.Hooks.Reopen = Reopen;
            _overview.Hooks.Say = Flash;

            _start.ScreenTime = () =>
            {
                RollDay();
                return _s.ScreenTimeToday;
            };
            _start.AppName = exe => _exeNames.TryGetValue(exe, out var n) ? n : exe;
            _stacks.Favorites = Favorites;
            _featuresReady = true;
            SyncFeatures();
        }

        IReadOnlyList<(string name, string path)> Favorites()
        {
            var list = new List<(string name, string path)>();
            foreach (var path in _s.Usage.OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
            {
                if (list.Count >= 14) break;
                if (!File.Exists(path) || !Files.IsApp(path)) continue;
                list.Add((_catalog.DisplayName(path), path));
            }
            return list;
        }

        void SyncFeatures()
        {
            if (!_featuresReady) return;
            SyncShell();
            string sig = $"{_s.FocusBorder}|{_game}";
            if (sig == _featSig) return;
            _featSig = sig;
            bool ring = _s.FocusBorder && !_game;
            if (ring) (_ring ??= new FocusRing()).Enable(true);
            else _ring?.Enable(false);
        }

        void ExtraKeys(Dictionary<string, Action> map)
        {
            map["rescue"] = () => WinOps.Rescue(Flash);
            map["unstick"] = Unstick;
            map["micmute"] = () => SetMic(!_audio.MicMuted(), true);
            map["spotlight"] = () =>
            {
                _spot.Toggle(_s.SpotlightDim);
                Flash(_spot.On ? "spotlight on" : "spotlight off");
            };
            map["appnote"] = () => EditNote(Native.GetForegroundWindow());
            if (_s.PastePlain) map["pasteplain"] = () => _ = PastePlain();
            ShellKeys(map);
        }

        void FeatureCommands(Commands c)
        {
            ShellCommands(c);
            c.Add("elysium", "rescue", "Rescue a stuck window", "\uE7BA", () => WinOps.Rescue(Flash), "fullscreen game stuck other monitor move unfullscreen borderless");
            c.Add("elysium", "bring-back", "Bring back off-screen windows", "\uE7F4", () =>
            {
                int n = WinOps.BringBack();
                Flash(n == 0 ? "everything's on screen" : $"brought back {n} window{(n == 1 ? "" : "s")}");
            }, "offscreen lost window monitor unplugged");
            c.Add("elysium", "unstick", "Free the mouse", "\uE7C9", Unstick, "cursor stuck locked clip mouse trapped");
            c.Add("elysium", "mic", "Mute / unmute your mic", "\uE720", () => SetMic(!_audio.MicMuted(), true), "microphone mute unmute talk", false, () => _audio.MicMuted() ? "on" : "off");
            c.Add("elysium", "spotlight", "Spotlight", "\uE7B3", () =>
            {
                _spot.Toggle(_s.SpotlightDim);
                Flash(_spot.On ? "spotlight on" : "spotlight off");
            }, "focus dim everything else reading", false, () => _spot.On ? "on" : "off");
            c.Add("elysium", "app-note", "Note on this app", "\uE70B", () => EditNote(_rules?.LastHwnd ?? Native.GetForegroundWindow()), "sticky note remember app reminder");
            c.Add("elysium", "borderless", "Make this window borderless fullscreen", "\uE740", () =>
            {
                var h = _rules?.LastHwnd ?? IntPtr.Zero;
                if (h == IntPtr.Zero || !Native.IsWindow(h))
                {
                    Flash("click a window first");
                    return;
                }
                WinOps.Borderless(h);
                Flash(WinOps.IsBorderless(h) ? "borderless. run it again to undo" : "back to normal");
            }, "fullscreen windowed game borderless");
        }

        void Unstick()
        {
            _lock.Unlock();
            Native.ClipCursor(IntPtr.Zero);
            Flash("mouse is free");
        }

        bool _micMuted;
        long _micAt;

        bool MicState()
        {
            long now = Environment.TickCount64;
            if (now - _micAt < 3000) return _micMuted;
            _micAt = now;
            try
            {
                _micMuted = _audio.HasMic && _audio.MicMuted();
            }
            catch
            {
                _micMuted = false;
            }
            return _micMuted;
        }

        void SetMic(bool mute, bool announce)
        {
            if (!_audio.HasMic)
            {
                if (announce) Flash("no mic found");
                return;
            }
            _audio.SetMicMute(mute);
            _micMuted = mute;
            _micAt = Environment.TickCount64;
            Sfx.Blip(!mute);
            if (announce) Flash(mute ? "mic muted" : "mic on");
            Tick();
        }

        void SetupHold()
        {
            string key = _s.PttKey.Trim();
            _keyHook.HoldVk = 0;
            _mouse.HoldX = 0;
            if (_s.PttMode == "off" || key.Length == 0) return;
            if (key.Equals("Mouse4", StringComparison.OrdinalIgnoreCase)) _mouse.HoldX = 1;
            else if (key.Equals("Mouse5", StringComparison.OrdinalIgnoreCase)) _mouse.HoldX = 2;
            else if (Hotkeys.TryParse(key, out _, out uint vk)) _keyHook.HoldVk = vk;
            _keyHook.HoldSwallow = _s.PttSwallow;
            if (_s.PttMode == "talk" && _audio.HasMic) _audio.SetMicMute(true);
        }

        void OnHold(bool down)
        {
            if (_s.PttMode == "talk") SetMic(!down, false);
            else if (_s.PttMode == "mute") SetMic(down, false);
        }

        async Task PastePlain()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    Flash("nothing to paste");
                    return;
                }
                string text = Clipboard.GetText();
                Clipboard.SetText(text);
                await Native.WaitForModifiersUp();
                await Task.Delay(20);
                Native.Combo(0x11, 0x56);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("paste plain failed " + ex.Message);
            }
        }

        async void ShotWindow(IntPtr h)
        {
            try
            {
                WinList.Switch(h);
                await Task.Delay(320);
                var f = Native.FrameBounds(h);
                var d = WinOps.DisplayOf(h);
                var shot = Capture.Grab(d);
                if (shot == null) return;
                var r = Capture.Clamp(shot, new Int32Rect(f.Left - d.Bounds.Left, f.Top - d.Bounds.Top, f.Width, f.Height));
                var img = Capture.Crop(shot, r);
                Capture.CopyImage(img);
                Capture.Save(_s, img);
                PinImage(img, f);
                Flash("copied and saved");
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("window shot failed " + ex.Message);
            }
        }

        static string DisplaySig() => string.Join(";", Displays.All().Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal));

        void Stamp()
        {
            if (!_s.MonitorMemory) return;
            long now = Environment.TickCount64;
            if (now - _stampAt < 4000) return;
            _stampAt = now;
            string sig = DisplaySig();
            if (sig != _dispSig) return;
            var map = new Dictionary<IntPtr, (Native.RECT frame, bool max)>();
            foreach (var w in WinList.All(false)) map[w.Hwnd] = (w.Frame, Native.IsZoomed(w.Hwnd));
            _layouts[sig] = map;
        }

        void DisplaysChanged()
        {
            string sig = DisplaySig();
            if (sig == _dispSig) return;
            _dispSig = sig;
            if (_s.MonitorMemory && _layouts.TryGetValue(sig, out var map))
            {
                int moved = 0;
                foreach (var (h, spot) in map)
                {
                    if (!Native.IsWindow(h) || Native.IsIconic(h)) continue;
                    if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                    SnapZones.Place(h, spot.frame);
                    if (spot.max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                    moved++;
                }
                if (moved > 0) Flash("put your windows back");
                return;
            }
            int back = WinOps.BringBack();
            if (back > 0) Flash($"brought back {back} window{(back == 1 ? "" : "s")}");
        }

        void OutputChanged()
        {
            var output = _audio.DefaultOutput();
            string id = output?.Id ?? "";
            if (id == _lastOutput) return;
            bool wasPhones = _lastOutputPhones;
            _lastOutput = id;
            _lastOutputPhones = output != null && AudioFlow.LooksLikeHeadphones(output.Name);
            if (!_s.HeadphonePause || !wasPhones || _lastOutputPhones) return;
            _ = PauseMusic();
        }

        async Task PauseMusic()
        {
            try
            {
                await _np.Refresh();
                if (!_np.Playing) return;
                await _np.Toggle();
                Flash("headphones out, paused");
            }
            catch
            {
            }
        }

        void RollDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_s.ScreenTimeDay == today) return;
            _s.ScreenTimeDay = today;
            _s.ScreenTimeToday.Clear();
        }

        void TrackTime()
        {
            double now = _timeClock.Elapsed.TotalSeconds;
            double seconds = Math.Clamp(now - _timeLast, 0, 5);
            _timeLast = now;
            if (!_s.ScreenTimeCard) return;
            if (Native.IdleSeconds() > 120) return;
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            var path = Native.ProcessPath(fg);
            if (string.IsNullOrEmpty(path)) return;
            string exe = Path.GetFileNameWithoutExtension(path);
            if (exe.Equals("explorer", StringComparison.OrdinalIgnoreCase) || exe.Equals("elysium", StringComparison.OrdinalIgnoreCase) || exe.Equals("LockApp", StringComparison.OrdinalIgnoreCase)) return;
            RollDay();
            if (!_exeNames.ContainsKey(exe)) _exeNames[exe] = _catalog.DisplayName(path);
            _s.ScreenTimeToday[exe] = _s.ScreenTimeToday.TryGetValue(exe, out var t) ? t + seconds : seconds;
        }

        void NoteFor(IntPtr h)
        {
            if (_s.AppNotes.Count == 0)
            {
                _noteCard?.Away();
                return;
            }
            var path = Native.ProcessPath(h);
            string exe = path == null ? "" : Path.GetFileNameWithoutExtension(path);
            if (exe.Length == 0 || !_s.AppNotes.TryGetValue(exe, out var text) || text.Length == 0 || _notesHidden.Contains(exe) || _game || !WinList.IsApp(h))
            {
                _noteCard?.Away();
                return;
            }
            if (_noteCard == null)
            {
                _noteCard = new NoteCard();
                _noteCard.Dismissed += x => _notesHidden.Add(x);
                _noteCard.Edit += x =>
                {
                    var fg = Native.GetForegroundWindow();
                    EditNote(fg);
                };
            }
            _noteCard.ShowOn(h, exe, _catalog.DisplayName(path!), text);
        }

        void EditNote(IntPtr h)
        {
            var path = h == IntPtr.Zero ? null : Native.ProcessPath(h);
            if (string.IsNullOrEmpty(path))
            {
                Flash("click on an app first");
                return;
            }
            string exe = Path.GetFileNameWithoutExtension(path);
            if (exe.Equals("elysium", StringComparison.OrdinalIgnoreCase))
            {
                Flash("click on an app first");
                return;
            }
            if (_noteEditor == null)
            {
                _noteEditor = new NoteEditor();
                _noteEditor.Saved += (x, text) =>
                {
                    if (text.Length == 0) _s.AppNotes.Remove(x);
                    else _s.AppNotes[x] = text;
                    _notesHidden.Remove(x);
                    _s.Touch();
                    _noteCard?.Away();
                    Flash(text.Length == 0 ? "note deleted" : "saved. it'll show up when you're in there");
                };
            }
            _noteEditor.Open(exe, _catalog.DisplayName(path), _s.AppNotes.TryGetValue(exe, out var cur) ? cur : "");
        }

        void FeatureTick()
        {
            SyncFeatures();
            ShellTick();
            if (_tick % 4 == 0) Stamp();
            if (_s.MediaHandoff && _tick % 2 == 0) _ = _handoff.Tick(true);
            if (_start.Shown && _tick % 2 == 1) _ = StartSong();
        }

        async Task StartSong()
        {
            try
            {
                await _np.Refresh();
                _start.SongChanged();
            }
            catch
            {
            }
        }

        void DisposeFeatures()
        {
            DisposeShell();
            _spot.Off();
            _ring?.Enable(false);
            WinEvents.Dispose();
            if (_s.PttMode == "talk" && _audio.HasMic) _audio.SetMicMute(false);
        }
    }
}
