using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Heft
{
    internal sealed class VersionInfo
    {
        public string Path = "";
        public string Original = "";
        public DateTime When;
        public long Size;
    }

    internal sealed class VersionShelf : IDisposable
    {
        static readonly string Root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft", "versions");
        static readonly HashSet<string> SkipExt = new(StringComparer.OrdinalIgnoreCase) { ".tmp", ".temp", ".crdownload", ".part", ".lock", ".lnk", ".ini", ".log", ".db-journal", ".swp", ".~lock" };
        const long MaxFile = 50L * 1024 * 1024;
        const long MaxTotal = 2L * 1024 * 1024 * 1024;

        readonly Settings _s;
        readonly List<FileSystemWatcher> _watchers = new();
        readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
        readonly Timer _flush;
        string _sig = "";
        int _busy;

        public VersionShelf(Settings s)
        {
            _s = s;
            _flush = new Timer(_ => Flush(), null, 3000, 3000);
        }

        public static string DefaultFolders()
        {
            return string.Join(", ", new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) });
        }

        IEnumerable<string> Folders()
        {
            var list = _s.VersionFolders.Count > 0
                ? _s.VersionFolders
                : new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public void Sync()
        {
            string sig = _s.VersionsOn + "|" + string.Join("|", Folders());
            if (sig == _sig) return;
            _sig = sig;
            foreach (var w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
            if (!_s.VersionsOn) return;
            foreach (var dir in Folders())
            {
                try
                {
                    var w = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024
                    };
                    w.Changed += (_, e) => Note(e.FullPath);
                    w.Created += (_, e) => Note(e.FullPath);
                    w.Renamed += (_, e) => Note(e.FullPath);
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("version watch failed " + dir + ": " + ex.Message);
                }
            }
        }

        void Note(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string name = System.IO.Path.GetFileName(path);
            if (name.StartsWith("~$") || name.StartsWith(".") || name.StartsWith("~")) return;
            if (SkipExt.Contains(System.IO.Path.GetExtension(path))) return;
            if (path.Contains("\\node_modules\\") || path.Contains("\\.git\\") || path.Contains("\\obj\\") || path.Contains("\\bin\\")) return;
            if (path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return;
            _pending[path] = DateTime.Now;
        }

        void Flush()
        {
            if (_pending.IsEmpty || Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var now = DateTime.Now;
                foreach (var kv in _pending.ToArray())
                {
                    if ((now - kv.Value).TotalSeconds < 4) continue;
                    _pending.TryRemove(kv.Key, out DateTime ignore);
                    Save(kv.Key);
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("version flush failed " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        static string Key(string path)
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return Convert.ToHexString(bytes)[..16];
        }

        void Save(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > MaxFile) return;
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary)) != 0) return;
                string dir = System.IO.Path.Combine(Root, Key(path));
                Directory.CreateDirectory(dir);
                File.WriteAllText(System.IO.Path.Combine(dir, "path.txt"), path);

                var existing = new DirectoryInfo(dir).EnumerateFiles("*.v").OrderByDescending(f => f.Name).ToList();
                if (existing.Count > 0 && existing[0].Length == info.Length && SameBytes(existing[0].FullName, path)) return;

                string target = System.IO.Path.Combine(dir, info.LastWriteTime.ToString("yyyyMMdd-HHmmss-fff") + ".v");
                if (File.Exists(target)) return;
                using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = File.Create(target))
                {
                    src.CopyTo(dst);
                }
                File.SetLastWriteTime(target, info.LastWriteTime);

                int keep = Math.Clamp(_s.VersionKeep, 3, 200);
                foreach (var old in new DirectoryInfo(dir).EnumerateFiles("*.v").OrderByDescending(f => f.Name).Skip(keep))
                {
                    try
                    {
                        old.Delete();
                    }
                    catch
                    {
                    }
                }
                Trim();
            }
            catch
            {
            }
        }

        static bool SameBytes(string a, string b)
        {
            try
            {
                using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fa.Length != fb.Length) return false;
                var ba = new byte[65536];
                var bb = new byte[65536];
                while (true)
                {
                    int na = fa.Read(ba, 0, ba.Length);
                    int nb = fb.Read(bb, 0, bb.Length);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                }
            }
            catch
            {
                return false;
            }
        }

        long _checkedAt;

        void Trim()
        {
            long now = Environment.TickCount64;
            if (now - _checkedAt < 60000) return;
            _checkedAt = now;
            try
            {
                var all = new DirectoryInfo(Root).EnumerateFiles("*.v", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc).ToList();
                long total = all.Sum(f => f.Length);
                foreach (var f in all)
                {
                    if (total <= MaxTotal) break;
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

        public static List<VersionInfo> For(string path)
        {
            var list = new List<VersionInfo>();
            try
            {
                string dir = System.IO.Path.Combine(Root, Key(path));
                if (!Directory.Exists(dir)) return list;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.v").OrderByDescending(f => f.Name))
                    list.Add(new VersionInfo { Path = f.FullName, Original = path, When = f.LastWriteTime, Size = f.Length });
            }
            catch
            {
            }
            return list;
        }

        public static string? Restore(VersionInfo v)
        {
            try
            {
                if (File.Exists(v.Original))
                {
                    string dir = System.IO.Path.GetDirectoryName(v.Original)!;
                    string stem = System.IO.Path.GetFileNameWithoutExtension(v.Original);
                    string ext = System.IO.Path.GetExtension(v.Original);
                    string backup = Files.Unique(dir, $"{stem} (before restore){ext}");
                    File.Copy(v.Original, backup);
                }
                File.Copy(v.Path, v.Original, true);
                return v.Original;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("restore failed " + ex.Message);
                return null;
            }
        }

        public static string? OpenCopy(VersionInfo v)
        {
            try
            {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "heft-versions");
                Directory.CreateDirectory(tmp);
                string stem = System.IO.Path.GetFileNameWithoutExtension(v.Original);
                string ext = System.IO.Path.GetExtension(v.Original);
                string file = System.IO.Path.Combine(tmp, $"{stem} ({v.When:MMM d HH.mm}){ext}");
                File.Copy(v.Path, file, true);
                Files.Open(file);
                return file;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _flush.Dispose();
            foreach (var w in _watchers)
            {
                try
                {
                    w.Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
        }
    }
}
