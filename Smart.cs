using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Elysium
{
    internal static class Projects
    {
        static readonly string[] FileMarkers = { "package.json", "Cargo.toml", "go.mod", "pyproject.toml", "requirements.txt", "pom.xml", "build.gradle", "CMakeLists.txt", "project.godot", "pubspec.yaml", "composer.json", "Gemfile" };
        static readonly string[] GlobMarkers = { "*.sln", "*.csproj", "*.uproject", "*.toe" };
        static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "obj", "bin", ".vs", "packages", "venv", ".venv", "__pycache__", "target", "build", "dist", "out", "AppData", "Library"
        };

        public static bool IsProject(string dir)
        {
            try
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))) return true;
                foreach (var m in FileMarkers)
                    if (File.Exists(Path.Combine(dir, m))) return true;
                foreach (var g in GlobMarkers)
                    if (Directory.EnumerateFiles(dir, g).Any()) return true;
            }
            catch
            {
            }
            return false;
        }

        public static List<Entry> Find(IEnumerable<string> extraRoots)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new List<string>
            {
                Path.Combine(home, "source"),
                Path.Combine(home, "repos"),
                Path.Combine(home, "Projects"),
                Path.Combine(home, "projects"),
                Path.Combine(home, "dev"),
                Path.Combine(home, "code"),
                Path.Combine(home, "GitHub"),
                Path.Combine(home, "Documents", "GitHub"),
                Path.Combine(home, "Documents", "Projects"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(home, "Downloads")
            };
            roots.AddRange(extraRoots);
            var found = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            int budget = 4000;
            foreach (var r in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                budget = Walk(r, 3, found, budget);
                if (budget <= 0) break;
            }
            return found.Values.OrderByDescending(e => e.Modified).Take(120).ToList();
        }

        static int Walk(string dir, int depth, Dictionary<string, Entry> found, int budget)
        {
            if (budget <= 0) return 0;
            budget--;
            try
            {
                if (IsProject(dir))
                {
                    if (!found.ContainsKey(dir))
                    {
                        var e = Library.Make(dir);
                        if (e != null)
                        {
                            e.Modified = LastTouched(dir);
                            e.Cat = "folder";
                            found[dir] = e;
                        }
                    }
                    return budget;
                }
                if (depth <= 0) return budget;
                foreach (var d in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    if (Skip.Contains(d.Name) || d.Name.StartsWith('.')) continue;
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                    budget = Walk(d.FullName, depth - 1, found, budget);
                    if (budget <= 0) return 0;
                }
            }
            catch
            {
            }
            return budget;
        }

        static DateTime LastTouched(string dir)
        {
            var best = DateTime.MinValue;
            try
            {
                string gitIndex = Path.Combine(dir, ".git", "index");
                if (File.Exists(gitIndex)) best = File.GetLastWriteTime(gitIndex);
                foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos().Take(200))
                    if (f.LastWriteTime > best && !f.Name.StartsWith('.')) best = f.LastWriteTime;
            }
            catch
            {
            }
            return best == DateTime.MinValue ? Directory.GetLastWriteTime(dir) : best;
        }

        public static string? Readme(string dir, int chars = 360)
        {
            try
            {
                var file = Directory.EnumerateFiles(dir, "README*").FirstOrDefault();
                if (file == null) return null;
                var lines = File.ReadLines(file).Take(40)
                    .Select(l => l.Trim().TrimStart('#', '>', '*', '-', ' ').Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("![") && !l.StartsWith("<") && !l.StartsWith("[!"));
                var text = string.Join(" ", lines);
                return text.Length > chars ? text[..chars].TrimEnd() + "..." : text;
            }
            catch
            {
                return null;
            }
        }

        public static List<(string subject, string ago)> Commits(string dir, int n = 5)
        {
            var list = new List<(string, string)>();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) return list;
            try
            {
                var psi = new ProcessStartInfo("git", $"-C \"{dir}\" log -n {n} --pretty=format:%s%x1f%ar")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi);
                if (p == null) return list;
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(2500))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                    }
                    return list;
                }
                foreach (var line in outTask.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.TrimEnd('\r').Split('\u001f');
                    if (parts.Length == 2) list.Add((parts[0], parts[1]));
                }
            }
            catch
            {
            }
            return list;
        }

        public static string? Editor()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var c in new[]
                     {
                         Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                         Path.Combine(pf, "Microsoft VS Code", "Code.exe"),
                         Path.Combine(local, "Programs", "cursor", "Cursor.exe"),
                         Path.Combine(local, "Programs", "Zed", "Zed.exe")
                     })
                if (File.Exists(c)) return c;
            return null;
        }

        public static void OpenInEditor(string dir)
        {
            var ed = Editor();
            try
            {
                if (ed != null)
                {
                    Process.Start(new ProcessStartInfo(ed, $"\"{dir}\"") { UseShellExecute = false, WorkingDirectory = dir });
                    return;
                }
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c code \"{dir}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch
            {
                Files.Open(dir);
            }
        }
    }

    internal static class Dupes
    {
        public static List<Entry> Find(IEnumerable<Entry> files, Func<bool> cancelled)
        {
            var result = new List<Entry>();
            var bySize = files.Where(e => !e.Dir && e.Size >= 16 * 1024)
                .GroupBy(e => e.Size)
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Key)
                .Take(400);
            int groups = 0;
            foreach (var g in bySize)
            {
                if (cancelled()) break;
                var byQuick = g.GroupBy(e => Quick(e.Path)).Where(q => q.Key != null && q.Count() > 1);
                foreach (var q in byQuick)
                {
                    var byFull = q.Key!.Length > 0 && g.Key <= 300L * 1024 * 1024
                        ? q.GroupBy(e => Full(e.Path)).Where(f => f.Key != null && f.Count() > 1).Select(f => f.ToList())
                        : new[] { q.ToList() };
                    foreach (var set in byFull)
                    {
                        result.AddRange(set.OrderBy(e => e.Modified));
                        groups++;
                    }
                }
                if (groups >= 120 || result.Count >= 400) break;
            }
            return result;
        }

        static string? Quick(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buf = new byte[65536];
                int a = fs.Read(buf, 0, buf.Length);
                using var sha = SHA1.Create();
                sha.TransformBlock(buf, 0, a, null, 0);
                if (fs.Length > buf.Length * 2)
                {
                    fs.Seek(-buf.Length, SeekOrigin.End);
                    int b = fs.Read(buf, 0, buf.Length);
                    sha.TransformBlock(buf, 0, b, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToHexString(sha.Hash!);
            }
            catch
            {
                return null;
            }
        }

        static string? Full(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                using var sha = SHA1.Create();
                return Convert.ToHexString(sha.ComputeHash(fs));
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class BulkRename
    {
        static List<(string from, string to)>? _last;

        public static bool CanUndo => _last != null && _last.Count > 0;

        public static List<(string from, string to)> Plan(IList<string> paths, string pattern)
        {
            var plan = new List<(string, string)>();
            pattern = pattern.Trim().Trim('"');
            if (pattern.Length == 0 || paths.Count == 0) return plan;
            if (!pattern.Contains("{n") && paths.Count > 1) pattern += "-{n}";
            int width = paths.Count.ToString().Length;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var p in paths)
            {
                i++;
                bool dir = Directory.Exists(p);
                if (!dir && !File.Exists(p)) continue;
                string folder = Path.GetDirectoryName(p.TrimEnd('\\')) ?? "";
                string name = dir ? Path.GetFileName(p.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(p);
                string ext = dir ? "" : Path.GetExtension(p);
                DateTime stamp;
                try
                {
                    stamp = dir ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p);
                }
                catch
                {
                    stamp = DateTime.Now;
                }
                string n = i.ToString();
                string next = pattern
                    .Replace("{nnn}", i.ToString("D3"))
                    .Replace("{nn}", i.ToString("D2"))
                    .Replace("{n}", n.PadLeft(width, '0'))
                    .Replace("{name}", name)
                    .Replace("{ext}", ext.TrimStart('.'))
                    .Replace("{date}", stamp.ToString("yyyy-MM-dd"))
                    .Replace("{time}", stamp.ToString("HH.mm"))
                    .Replace("{folder}", Path.GetFileName(folder));
                foreach (var c in Path.GetInvalidFileNameChars()) next = next.Replace(c.ToString(), "");
                next = next.Trim().TrimEnd('.');
                if (next.Length == 0) continue;
                if (!dir && !pattern.Contains("{ext}") && Path.GetExtension(next).Length == 0) next += ext;
                string target = Path.Combine(folder, next);
                if (target.Equals(p, StringComparison.Ordinal)) continue;
                if (!taken.Add(target)) continue;
                plan.Add((p, target));
            }
            return plan;
        }

        public static int Apply(List<(string from, string to)> plan)
        {
            var temp = new List<(string tmp, string to, string from)>();
            var sources = new HashSet<string>(plan.Select(x => x.from), StringComparer.OrdinalIgnoreCase);
            foreach (var (from, to) in plan)
            {
                bool exists = File.Exists(to) || Directory.Exists(to);
                if (exists && !sources.Contains(to) && !to.Equals(from, StringComparison.OrdinalIgnoreCase)) continue;
                string tmp = Path.Combine(Path.GetDirectoryName(from.TrimEnd('\\')) ?? "", ".elysium-" + Guid.NewGuid().ToString("N")[..10]);
                try
                {
                    if (Directory.Exists(from)) Directory.Move(from, tmp);
                    else File.Move(from, tmp);
                    temp.Add((tmp, to, from));
                }
                catch
                {
                }
            }
            var done = new List<(string, string)>();
            foreach (var (tmp, to, from) in temp)
            {
                try
                {
                    if (Directory.Exists(tmp)) Directory.Move(tmp, to);
                    else File.Move(tmp, to);
                    done.Add((from, to));
                }
                catch
                {
                    try
                    {
                        if (Directory.Exists(tmp)) Directory.Move(tmp, from);
                        else File.Move(tmp, from);
                    }
                    catch
                    {
                    }
                }
            }
            _last = done;
            return done.Count;
        }

        public static int Undo()
        {
            if (_last == null) return 0;
            var back = _last.Select(x => (x.to, x.from)).ToList();
            _last = null;
            int n = Apply(back);
            _last = null;
            return n;
        }
    }

    internal sealed class DownloadSorter : IDisposable
    {
        static readonly HashSet<string> Partial = new(StringComparer.OrdinalIgnoreCase) { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".aria2" };

        static readonly Dictionary<string, string> Folder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image"] = "Images",
            ["video"] = "Videos",
            ["audio"] = "Music",
            ["doc"] = "Documents",
            ["archive"] = "Archives",
            ["app"] = "Installers",
            ["code"] = "Code",
            ["model"] = "3D"
        };

        readonly Settings _s;
        readonly Notifier _notes;
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(2) };
        bool _busy;

        public DownloadSorter(Settings s, Notifier notes)
        {
            _s = s;
            _notes = notes;
            _timer.Tick += (_, _) => Sweep(false);
            _timer.Start();
        }

        public void Sweep(bool now)
        {
            if ((!_s.AutoSortDownloads && !now) || _busy) return;
            _busy = true;
            string root = Library.DownloadsDir;
            Task.Run(() => Move(root, now)).ContinueWith(t =>
            {
                _busy = false;
                int n = t.IsCompletedSuccessfully ? t.Result : 0;
                if (n > 0) System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    _notes.Post(new Note { App = "Downloads", Title = "Tidied Downloads", Body = $"sorted {n} file{(n == 1 ? "" : "s")} into folders", Glyph = "\uE8B7", Open = root })));
            });
        }

        static int Move(string root, bool now)
        {
            if (!Directory.Exists(root)) return 0;
            int moved = 0;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles())
            {
                try
                {
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (Partial.Contains(f.Extension) || f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!now && (DateTime.Now - f.LastWriteTime).TotalMinutes < 3) continue;
                    if (File.Exists(f.FullName + ".crdownload") || File.Exists(f.FullName + ".part")) continue;
                    string cat = Files.Category(f.FullName);
                    string sub = Folder.TryGetValue(cat, out var name) ? name : "Other";
                    string dest = Path.Combine(root, sub);
                    Directory.CreateDirectory(dest);
                    using (new FileStream(f.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                    string target = Files.Unique(dest, f.Name);
                    File.Move(f.FullName, target);
                    moved++;
                }
                catch
                {
                }
            }
            return moved;
        }

        public void Dispose() => _timer.Stop();
    }
}
