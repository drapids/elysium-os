using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Elysium
{
    internal sealed class IconCache : IDisposable
    {
        readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Action<ImageSource?>>> _waiting = new(StringComparer.OrdinalIgnoreCase);
        readonly BlockingCollection<(string key, string path, int size, bool iconOnly)> _queue = new();
        readonly Dispatcher _ui;
        readonly Thread _thread;

        public IconCache(Dispatcher ui)
        {
            _ui = ui;
            _thread = new Thread(Run) { IsBackground = true, Name = "elysium-icons", Priority = ThreadPriority.BelowNormal };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        static string Key(string path, int size, bool iconOnly) => $"{size}|{(iconOnly ? 1 : 0)}|{path}";

        public static string Pack = "";
        public static readonly string PackRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium", "icons");
        static readonly string DiskDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "elysium", "iconcache");

        public void Clear()
        {
            lock (_cache) _cache.Clear();
        }

        static ImageSource? FromPack(string path)
        {
            if (Pack.Length == 0) return null;
            try
            {
                string dir = System.IO.Path.Combine(PackRoot, Pack);
                if (!System.IO.Directory.Exists(dir)) return null;
                var names = new List<string>();
                string clean = path.TrimEnd('\\');
                string name = System.IO.Path.GetFileNameWithoutExtension(clean);
                names.Add(name);
                names.Add(name.Replace(" ", "").ToLowerInvariant());
                if (clean.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string? target = ShellLink.Target(clean);
                    if (target != null) names.Add(System.IO.Path.GetFileNameWithoutExtension(target));
                }
                foreach (var n in names)
                {
                    if (n.Length == 0) continue;
                    foreach (var ext in new[] { ".png", ".ico", ".jpg" })
                    {
                        string f = System.IO.Path.Combine(dir, n + ext);
                        if (System.IO.File.Exists(f)) return Decode(f, 0);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        static BitmapSource? Decode(string file, int width)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (width > 0) img.DecodePixelWidth = width;
            img.UriSource = new Uri(file);
            img.EndInit();
            img.Freeze();
            return img;
        }

        static string? DiskFile(string path, int size, bool iconOnly)
        {
            try
            {
                long stamp;
                if (System.IO.File.Exists(path)) stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                else return null;
                if (iconOnly && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
                var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes($"{path.ToLowerInvariant()}|{size}|{iconOnly}|{stamp}"));
                return System.IO.Path.Combine(DiskDir, Convert.ToHexString(hash)[..24] + ".png");
            }
            catch
            {
                return null;
            }
        }

        static void SaveDisk(string file, ImageSource img)
        {
            try
            {
                if (img is not BitmapSource bs) return;
                System.IO.Directory.CreateDirectory(DiskDir);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bs));
                string tmp = file + ".tmp";
                using (var fs = System.IO.File.Create(tmp)) enc.Save(fs);
                System.IO.File.Move(tmp, file, true);
            }
            catch
            {
            }
        }

        public static void TrimDisk()
        {
            try
            {
                if (!System.IO.Directory.Exists(DiskDir)) return;
                var files = new System.IO.DirectoryInfo(DiskDir).GetFiles("*.png");
                long total = 0;
                foreach (var f in files) total += f.Length;
                if (total < 200L * 1024 * 1024) return;
                Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
                foreach (var f in files)
                {
                    if (total < 120L * 1024 * 1024) break;
                    total -= f.Length;
                    try
                    {
                        f.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public bool TryPeek(string path, int size, bool iconOnly, out ImageSource? image)
        {
            lock (_cache) return _cache.TryGetValue(Key(path, size, iconOnly), out image);
        }

        public void Get(string path, int size, bool iconOnly, Action<ImageSource?>? done)
        {
            if (string.IsNullOrEmpty(path))
            {
                done?.Invoke(null);
                return;
            }
            var key = Key(path, size, iconOnly);
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var hit))
                {
                    done?.Invoke(hit);
                    return;
                }
                if (_waiting.TryGetValue(key, out var list))
                {
                    if (done != null) list.Add(done);
                    return;
                }
                _waiting[key] = done != null ? new List<Action<ImageSource?>> { done } : new List<Action<ImageSource?>>();
            }
            try
            {
                _queue.Add((key, path, size, iconOnly));
            }
            catch
            {
            }
        }

        public ImageSource? Load(string path, int size, bool iconOnly)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var key = Key(path, size, iconOnly);
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var hit)) return hit;
            }
            var img = LoadNow(path, size, iconOnly);
            lock (_cache) _cache[key] = img;
            return img;
        }

        void Run()
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                ImageSource? img = null;
                try
                {
                    img = LoadNow(job.path, job.size, job.iconOnly);
                }
                catch
                {
                }

                List<Action<ImageSource?>>? waiters;
                lock (_cache)
                {
                    _cache[job.key] = img;
                    _waiting.Remove(job.key, out waiters);
                }

                if (waiters != null && waiters.Count > 0)
                {
                    var result = img;
                    _ui.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        foreach (var w in waiters) w(result);
                    }));
                }
            }
        }

        static ImageSource? LoadNow(string path, int size, bool iconOnly)
        {
            var packed = FromPack(path);
            if (packed != null) return packed;
            string? disk = DiskFile(path, size, iconOnly);
            if (disk != null && System.IO.File.Exists(disk))
            {
                try
                {
                    return Decode(disk, 0);
                }
                catch
                {
                }
            }
            var fresh = LoadShell(path, size, iconOnly);
            if (fresh != null && disk != null) SaveDisk(disk, fresh);
            return fresh;
        }

        static ImageSource? LoadShell(string path, int size, bool iconOnly)
        {
            ImageSource? img = ShellImage.Get(path, size, iconOnly);
            if (img == null && !iconOnly) img = ShellImage.Get(path, size, true);
            if (img != null) return img;

            try
            {
                System.Drawing.Icon? icon = null;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    try { icon = System.Drawing.Icon.ExtractIcon(path, 0, 64); } catch { }
                }
                icon ??= System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    icon.Dispose();
                    return src;
                }
            }
            catch
            {
            }
            return null;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
