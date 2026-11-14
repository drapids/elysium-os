using System;
using System.IO;
using System.Linq;
using System.Windows;
using Elysium.Ui;

namespace Elysium
{
    public partial class App
    {
        Grab? _grab;
        string _lastDownload = "";
        DateTime _lastDownloadAt = DateTime.MinValue;
        string _deskId = "";
        int _deskCount = 1, _deskIndex;
        string _barSig = "";

        void WireBarExtras(Bar b)
        {
            b.DeskClicked += index => VirtualDesktops.Go(_deskIndex, index);
            b.MicClicked += () => SetMic(false, true);
            WireShellBar(b);
            b.ScratchClicked += (bar, x) => _drop.Toggle("scratch", bar.Display, bar.BottomPx, x);
            b.LastDownloadClicked += () =>
            {
                if (File.Exists(_lastDownload)) GrabDownload(_lastDownload);
            };
            b.LastDownloadDrag += () =>
            {
                if (!File.Exists(_lastDownload)) return;
                try
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { _lastDownload });
                    DragDrop.DoDragDrop(b, data, DragDropEffects.Copy | DragDropEffects.Link);
                }
                catch
                {
                }
            };
            b.SetDesks(_deskCount, _deskIndex);
            b.SetLastDownload(RecentDownload());
        }

        string RecentDownload()
        {
            if (_lastDownload.Length == 0 || (DateTime.Now - _lastDownloadAt).TotalMinutes > 30) return "";
            return Path.GetFileName(_lastDownload);
        }

        void Downloaded(string path)
        {
            _lastDownload = path;
            _lastDownloadAt = DateTime.Now;
            string name = RecentDownload();
            foreach (var b in _bars) b.SetLastDownload(name);
        }

        void GrabDownload(string? path = null)
        {
            path ??= Grab.Newest(Library.DownloadsDir);
            if (path == null || !File.Exists(path))
            {
                Flash("no downloads yet");
                return;
            }
            _grab ??= new Grab(_catalog);
            _grab.Show(path);
        }

        void DeskTick()
        {
            var (ids, cur) = VirtualDesktops.Read();
            _deskCount = ids.Count;
            _deskIndex = cur;
            string id = ids.Count > 1 ? ids[cur].ToString("N") : "";
            foreach (var b in _bars) b.SetDesks(_deskCount, _deskIndex);
            if (id != _deskId)
            {
                bool first = _deskId.Length == 0 && id.Length == 0;
                _deskId = id;
                if (!first) _ext?.Emit("desktop", _deskIndex + 1, _deskCount);
            }
            if (_tick % 20 == 0 && _lastDownload.Length > 0 && RecentDownload().Length == 0)
                foreach (var b in _bars) b.SetLastDownload("");
        }

        readonly System.Collections.Generic.Dictionary<string, string> _lnkExe = new(StringComparer.OrdinalIgnoreCase);

        int DockBadge(WheelItem item)
        {
            if (_notes.Unseen == 0 || item.Path.Length == 0) return 0;
            string name = item.Name;
            if (!_lnkExe.TryGetValue(item.Path, out var exe))
            {
                exe = Path.GetFileNameWithoutExtension(item.Path);
                if (item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var target = ShellLink.Target(item.Path);
                    if (target != null) exe = Path.GetFileNameWithoutExtension(target);
                }
                _lnkExe[item.Path] = exe;
            }
            return _notes.History.Count(n => !n.Seen && n.App.Length > 0 &&
                (n.App.Equals(name, StringComparison.OrdinalIgnoreCase) || n.App.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
                 (name.Length > 3 && n.App.StartsWith(name, StringComparison.OrdinalIgnoreCase)) ||
                 (exe.Length > 3 && n.AppId.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        void AppVolume(string target)
        {
            var parts = target.Split('|');
            string exe = parts.Length > 0 ? parts[0].Trim() : "*";
            string act = parts.Length > 1 ? parts[1] : "up";
            if (exe.Length == 0 || exe == "*")
            {
                var fg = Native.ProcessPath(Native.GetForegroundWindow());
                exe = fg == null ? "" : Path.GetFileNameWithoutExtension(fg);
            }
            if (exe.Length == 0) return;
            var app = _audio.Apps().FirstOrDefault(a => a.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
            if (app == null)
            {
                Flash(exe + " isn't playing anything");
                return;
            }
            string name = app.Name.Length > 0 ? app.Name : app.Exe;
            if (act == "mute")
            {
                _audio.SetMute(app.Exe, !app.Muted);
                Flash(name + (app.Muted ? " unmuted" : " muted"));
                return;
            }
            float step = act == "down" ? -0.06f : 0.06f;
            float level = Math.Clamp(app.Volume + step, 0f, 1f);
            if (app.Muted && step > 0) _audio.SetMute(app.Exe, false);
            _audio.SetVolume(app.Exe, level);
            Flash($"{name} {Math.Round(level * 100)}%");
        }

        void SyncBarLayout()
        {
            string sig = string.Join(",", _s.BarLayout);
            if (sig == _barSig) return;
            _barSig = sig;
            foreach (var b in _bars) b.Arrange();
        }
    }
}
