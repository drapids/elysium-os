using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Heft
{
    internal sealed class Entry
    {
        public string Path = "";
        public string Name = "";
        public bool Dir;
        public long Size;
        public DateTime Modified;
        public DateTime Accessed;
        public string Cat = "other";
    }

    internal static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class CShellLink
        {
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr data, uint flags);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            [PreserveSig] int GetClassID(out Guid id);
            [PreserveSig] int IsDirty();
            [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        }

        public static string? Target(string lnk)
        {
            try
            {
                var link = new CShellLink();
                ((IPersistFile)link).Load(lnk, 0);
                var sb = new StringBuilder(520);
                if (((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4) != 0) return null;
                Marshal.ReleaseComObject(link);
                var t = sb.ToString();
                return t.Length == 0 ? null : t;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class Library
    {
        public static readonly (string id, string label, string glyph)[] Smart =
        {
            ("recent", "Recent", "\uE81C"),
            ("downloads", "Downloads", "\uE896"),
            ("image", "Images", "\uEB9F"),
            ("code", "Code", "\uE943"),
            ("doc", "Docs", "\uE8A5"),
            ("archive", "Archives", "\uF012"),
            ("large", "Large", "\uEDA2"),
            ("unsorted", "Unsorted", "\uE7C3"),
            ("projects", "Projects", "\uE8F1"),
            ("dupes", "Duplicates", "\uE8C8")
        };

        static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "obj", "bin", ".vs", "packages", "venv", ".venv", "__pycache__", "$recycle.bin", "appdata", ".cache", "library", "target", "build", "dist"
        };

        readonly Settings _s;
        List<Entry> _scan = new();
        List<Entry> _projects = new();
        List<Entry> _dupes = new();
        Task? _dupeTask;
        DateTime _dupesAt = DateTime.MinValue;

        public bool DupesBusy => _dupeTask != null && !_dupeTask.IsCompleted;
        DateTime _scannedAt = DateTime.MinValue;
        Task? _running;

        public event Action? Updated;

        public Library(Settings s) => _s = s;

        public bool Ready => _scannedAt != DateTime.MinValue;

        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string DownloadsDir => Path.Combine(Home, "Downloads");
        public static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string StashDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft", "shelf");

        public void Refresh(bool force = false)
        {
            if (_running != null && !_running.IsCompleted) return;
            if (!force && (DateTime.Now - _scannedAt).TotalMinutes < 4) return;
            var roots = Roots();
            var extra = _s.Places.ToList();
            _running = Task.Run(() =>
            {
                var list = new List<Entry>();
                int budget = 25000;
                foreach (var r in roots) budget = Walk(r, 3, list, budget);
                List<Entry> projects;
                try
                {
                    projects = Projects.Find(extra);
                }
                catch
                {
                    projects = new List<Entry>();
                }
                return (list, projects);
            }).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                _scan = t.Result.list;
                _projects = t.Result.projects;
                _scannedAt = DateTime.Now;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Updated?.Invoke()));
            });
        }

        List<string> Roots()
        {
            var roots = new List<string>
            {
                DesktopDir,
                DownloadsDir,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            };
            roots.AddRange(_s.Places);
            roots.AddRange(_s.ShelfFolders);
            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static int Walk(string dir, int depth, List<Entry> into, int budget)
        {
            if (budget <= 0) return 0;
            try
            {
                var info = new DirectoryInfo(dir);
                foreach (var f in info.EnumerateFiles())
                {
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    into.Add(Make(f));
                    if (--budget <= 0) return 0;
                }
                if (depth <= 0) return budget;
                foreach (var d in info.EnumerateDirectories())
                {
                    if (Skip.Contains(d.Name) || d.Name.StartsWith('.')) continue;
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                    budget = Walk(d.FullName, depth - 1, into, budget);
                    if (budget <= 0) return 0;
                }
            }
            catch
            {
            }
            return budget;
        }

        public static Entry Make(FileSystemInfo f)
        {
            var e = new Entry
            {
                Path = f.FullName,
                Name = f.Name,
                Dir = f is DirectoryInfo,
                Modified = f.LastWriteTime,
                Accessed = f.LastAccessTime
            };
            if (f is FileInfo fi)
            {
                e.Size = fi.Length;
                e.Cat = Files.Category(fi.FullName);
            }
            else
            {
                e.Cat = "folder";
            }
            return e;
        }

        public static Entry? Make(string path)
        {
            try
            {
                if (Directory.Exists(path)) return Make(new DirectoryInfo(path));
                if (File.Exists(path)) return Make(new FileInfo(path));
            }
            catch
            {
            }
            return null;
        }

        public List<Entry> Get(string id)
        {
            switch (id)
            {
                case "recent":
                    return Recent();
                case "downloads":
                    return List(DownloadsDir, false);
                case "large":
                    return _scan.Where(e => e.Size > 200L * 1024 * 1024).OrderByDescending(e => e.Size).Take(200).ToList();
                case "unsorted":
                    var loose = List(DesktopDir, false).Concat(List(DownloadsDir, false))
                        .Where(e => !e.Dir && !Files.IsApp(e.Path) && (DateTime.Now - e.Modified).TotalDays > 2)
                        .OrderBy(e => e.Modified);
                    return loose.Take(300).ToList();
                case "projects":
                    return _projects.ToList();
                case "dupes":
                    FindDupes(false);
                    return _dupes.ToList();
                case "image":
                case "code":
                case "doc":
                case "archive":
                    return _scan.Where(e => e.Cat == id).OrderByDescending(e => e.Modified).Take(400).ToList();
            }

            if (_s.Collections.TryGetValue(id, out var refs))
                return refs.Select(Make).Where(e => e != null).Select(e => e!).ToList();
            return new List<Entry>();
        }

        public void FindDupes(bool force)
        {
            if (!Ready || DupesBusy) return;
            if (!force && (DateTime.Now - _dupesAt).TotalMinutes < 10) return;
            _dupesAt = DateTime.Now;
            var files = _scan.ToList();
            _dupeTask = Task.Run(() => Dupes.Find(files, () => false)).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                _dupes = t.Result;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Updated?.Invoke()));
            });
        }

        public static List<Entry> List(string dir, bool hidden)
        {
            var list = new List<Entry>();
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (!hidden && (f.Attributes & FileAttributes.Hidden) != 0) continue;
                    if ((f.Attributes & FileAttributes.System) != 0 && (f.Attributes & FileAttributes.Hidden) != 0) continue;
                    if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(Make(f));
                    if (list.Count >= 1500) break;
                }
            }
            catch
            {
            }
            return list;
        }

        static List<Entry> Recent()
        {
            var list = new List<Entry>();
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");
                var links = new DirectoryInfo(dir).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(80);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in links)
                {
                    var t = ShellLink.Target(l.FullName);
                    if (t == null || !seen.Add(t)) continue;
                    var e = Make(t);
                    if (e == null) continue;
                    e.Accessed = l.LastWriteTime;
                    list.Add(e);
                    if (list.Count >= 60) break;
                }
            }
            catch
            {
            }
            return list;
        }

        public List<Entry> Sort(List<Entry> list, string sort, bool foldersFirst = true)
        {
            IEnumerable<Entry> q = sort switch
            {
                "name" => list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
                "accessed" => list.OrderByDescending(e => e.Accessed),
                "size" => list.OrderByDescending(e => e.Size),
                "type" => list.OrderBy(e => e.Cat).ThenBy(e => Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
                "color" => list.OrderBy(e => _s.Bookmarks.TryGetValue(e.Path, out int b) ? b : 99).ThenBy(e => e.Cat).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
                "frequency" => list.OrderByDescending(e => _s.FileOpens.TryGetValue(e.Path, out double n) ? n : 0).ThenByDescending(e => e.Accessed),
                "relevance" => list.OrderByDescending(Relevance),
                _ => list.OrderByDescending(e => e.Modified)
            };
            if (foldersFirst && sort != "relevance") q = q.OrderByDescending(e => e.Dir);
            return q.ToList();
        }

        double Relevance(Entry e)
        {
            double opens = _s.FileOpens.TryGetValue(e.Path, out double n) ? n : 0;
            double recent = Math.Max(e.Modified.Ticks, e.Accessed.Ticks);
            double ageDays = (DateTime.Now.Ticks - recent) / (double)TimeSpan.TicksPerDay;
            double score = 1.0 / (1 + ageDays) * 10 + Math.Log(1 + opens) * 4;
            if (_s.Bookmarks.ContainsKey(e.Path)) score += 3;
            return score;
        }

        public List<Entry> Search(Func<Entry, bool> filter, int max = 200)
        {
            return _scan.Where(filter).Take(max).ToList();
        }

        public static string Stash(string? text, System.Windows.Media.Imaging.BitmapSource? image)
        {
            Directory.CreateDirectory(StashDir);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            if (image != null)
            {
                string path = Files.Unique(StashDir, $"image {stamp}.png");
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var fs = File.Create(path);
                enc.Save(fs);
                return path;
            }
            string first = (text ?? "").Split('\n')[0].Trim();
            if (first.Length > 28) first = first[..28];
            foreach (var c in Path.GetInvalidFileNameChars()) first = first.Replace(c, ' ');
            string file = Files.Unique(StashDir, (first.Length > 0 ? first : "note") + ".txt");
            File.WriteAllText(file, text ?? "");
            return file;
        }
    }
}
