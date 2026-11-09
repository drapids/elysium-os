using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Heft
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
            _thread = new Thread(Run) { IsBackground = true, Name = "heft-icons", Priority = ThreadPriority.BelowNormal };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        static string Key(string path, int size, bool iconOnly) => $"{size}|{(iconOnly ? 1 : 0)}|{path}";

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
