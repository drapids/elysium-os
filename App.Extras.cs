using System;
using System.IO;
using System.Linq;
using System.Windows;
using Heft.Ui;

namespace Heft
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

        void SyncBarLayout()
        {
            string sig = string.Join(",", _s.BarLayout);
            if (sig == _barSig) return;
            _barSig = sig;
            foreach (var b in _bars) b.Arrange();
        }
    }
}
