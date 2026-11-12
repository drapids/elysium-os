using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Heft
{
    internal sealed class AppCatalog : IDisposable
    {
        static readonly HashSet<string> Boring = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "applicationframehost", "searchhost", "searchapp", "startmenuexperiencehost",
            "shellexperiencehost", "lockapp", "textinputhost", "heft", "dwm", "systemsettings",
            "shellhost", "widgets", "consent", "openwith", "pickerhost", "rundll32", "dllhost"
        };

        static readonly string[] SkipDirs =
        {
            "node_modules", ".git", "obj", ".vs", "packages", "appdata", "$recycle.bin", ".cache", "venv", ".venv", "__pycache__", "library"
        };

        readonly Settings _s;
        readonly Native.WinEventProc _proc;
        readonly IntPtr _hook;
        readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

        string? _fgPath;
        DateTime _fgSince = DateTime.UtcNow;

        public List<WheelItem> Projects { get; private set; } = new();
        public string? HangarPath { get; private set; }
        public bool Scanning { get; private set; }
        public bool Scanned { get; private set; }

        public event Action? Updated;
        public event Action<string?>? Foreground;

        public IconCache Icons { get; } = new(Application.Current.Dispatcher);

        public AppCatalog(Settings settings)
        {
            _s = settings;
            _proc = OnForeground;
            _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            OnForeground(IntPtr.Zero, 0, Native.GetForegroundWindow(), 0, 0, 0, 0);
        }

        void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            Flush();
            _fgPath = hwnd == IntPtr.Zero ? null : Native.ProcessPath(hwnd);
            _fgSince = DateTime.UtcNow;
            Foreground?.Invoke(_fgPath);
        }

        public void Flush()
        {
            var now = DateTime.UtcNow;
            if (_fgPath != null && !Boring.Contains(Path.GetFileNameWithoutExtension(_fgPath)))
            {
                double secs = Math.Min((now - _fgSince).TotalSeconds, 1800);
                if (secs > 0.5)
                {
                    _s.Usage.TryGetValue(_fgPath, out double total);
                    _s.Usage[_fgPath] = total + secs;
                }
            }
            _fgSince = now;
        }

        public List<WheelItem> WheelItems(int max = 12)
        {
            var list = new List<WheelItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hidden = new HashSet<string>(_s.Hidden, StringComparer.OrdinalIgnoreCase);

            foreach (var p in _s.Pinned)
            {
                if (!Exists(p.Path) || !seen.Add(p.Path)) continue;
                list.Add(new WheelItem { Name = p.Name, Path = p.Path, Kind = "favorite" });
            }

            if (HangarPath != null && !hidden.Contains(HangarPath) && seen.Add(HangarPath))
                list.Add(new WheelItem { Name = "hangar29", Path = HangarPath, Kind = "project" });

            foreach (var p in Projects)
            {
                if (list.Count >= max) break;
                if (hidden.Contains(p.Path) || !seen.Add(p.Path)) continue;
                list.Add(new WheelItem { Name = p.Name, Path = p.Path, Kind = "project" });
            }

            foreach (var kv in _s.Usage.OrderByDescending(k => k.Value))
            {
                if (list.Count >= max) break;
                if (kv.Value < 120) break;
                var path = kv.Key;
                if (hidden.Contains(path) || seen.Contains(path)) continue;
                if (Boring.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                if (!File.Exists(path)) continue;
                seen.Add(path);
                list.Add(new WheelItem { Name = DisplayName(path), Path = path, Kind = "frequent" });
            }

            return list.Take(max).ToList();
        }

        static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        public bool IsPinned(string path) => _s.Pinned.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

        public void Pin(WheelItem item)
        {
            if (!IsPinned(item.Path)) _s.Pinned.Add(new WheelItem { Name = item.Name, Path = item.Path, Kind = "favorite" });
            _s.Hidden.RemoveAll(h => string.Equals(h, item.Path, StringComparison.OrdinalIgnoreCase));
            _s.Touch();
            Updated?.Invoke();
        }

        public void Unpin(string path)
        {
            _s.Pinned.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            _s.Touch();
            Updated?.Invoke();
        }

        public void Hide(string path)
        {
            _s.Pinned.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            if (!_s.Hidden.Contains(path, StringComparer.OrdinalIgnoreCase)) _s.Hidden.Add(path);
            _s.Touch();
            Updated?.Invoke();
        }

        public void AddFile(string path)
        {
            Pin(new WheelItem { Name = DisplayName(path), Path = path });
        }

        public void SetHangar(string path)
        {
            _s.Pinned.RemoveAll(p => Norm(p.Name) == "hangar29");
            _s.Pinned.Insert(0, new WheelItem { Name = "hangar29", Path = path, Kind = "favorite" });
            _s.HangarPinned = true;
            HangarPath = path;
            _s.Touch();
            Updated?.Invoke();
        }

        public bool Launch(WheelItem item)
        {
            try
            {
                var info = new ProcessStartInfo(item.Path) { UseShellExecute = true };
                var dir = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(dir) && File.Exists(item.Path)) info.WorkingDirectory = dir;
                Process.Start(info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string DisplayName(string path)
        {
            if (_names.TryGetValue(path, out var cached)) return cached;
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 28) name = desc.Trim();
                }
            }
            catch
            {
            }
            _names[path] = name;
            return name;
        }

        public ImageSource? Icon(string path, int size = 96, bool iconOnly = true) => Icons.Load(path, size, iconOnly);

        public void Prewarm()
        {
            foreach (var it in WheelItems(30)) Icons.Get(it.Path, 96, true, null);
            foreach (var it in PlaceItems()) Icons.Get(it.Path, 96, false, null);
        }

        public List<WheelItem> PlaceItems()
        {
            var list = new List<WheelItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            void Add(string name, string path, string kind)
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || !seen.Add(path)) return;
                list.Add(new WheelItem { Name = name, Path = path, Kind = kind });
            }

            Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "place");
            Add("Downloads", Path.Combine(home, "Downloads"), "place");
            Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "place");
            Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "place");
            Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "place");
            Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "place");

            foreach (var p in _s.Places)
                Add(Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, "place");

            if (_s.ShowDrives)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                        var letter = d.Name.TrimEnd('\\');
                        var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                        Add($"{label} ({letter})", d.RootDirectory.FullName, "drive");
                    }
                    catch
                    {
                    }
                }
            }
            return list;
        }

        public static List<WheelItem> FolderItems(string dir, string sort, bool foldersFirst, bool showHidden)
        {
            var entries = new List<(WheelItem item, bool isDir, DateTime time, long size, string ext)>();
            try
            {
                var info = new DirectoryInfo(dir);
                foreach (var e in info.EnumerateFileSystemInfos())
                {
                    try
                    {
                        var attr = e.Attributes;
                        if ((attr & FileAttributes.System) != 0 && (attr & FileAttributes.Hidden) != 0) continue;
                        if (!showHidden && (attr & FileAttributes.Hidden) != 0) continue;
                        if (e is DirectoryInfo)
                        {
                            entries.Add((new WheelItem { Name = e.Name, Path = e.FullName, Kind = "folder" }, true, e.LastWriteTimeUtc, 0, ""));
                        }
                        else
                        {
                            var name = e.Name;
                            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            var ext = e.Extension.ToLowerInvariant();
                            if (ext == ".lnk" || ext == ".url") name = Path.GetFileNameWithoutExtension(name);
                            long size = e is FileInfo fi ? fi.Length : 0;
                            entries.Add((new WheelItem { Name = name, Path = e.FullName, Kind = "file" }, false, e.LastWriteTimeUtc, size, ext));
                        }
                    }
                    catch
                    {
                    }
                    if (entries.Count >= 3000) break;
                }
            }
            catch
            {
            }

            var cmp = StringComparer.CurrentCultureIgnoreCase;
            IOrderedEnumerable<(WheelItem item, bool isDir, DateTime time, long size, string ext)> ordered = foldersFirst
                ? entries.OrderByDescending(e => e.isDir)
                : entries.OrderBy(_ => 0);

            ordered = sort switch
            {
                "recent" => ordered.ThenByDescending(e => e.time),
                "type" => ordered.ThenBy(e => e.ext, cmp).ThenBy(e => e.item.Name, cmp),
                "size" => ordered.ThenByDescending(e => e.size).ThenBy(e => e.item.Name, cmp),
                _ => ordered.ThenBy(e => e.item.Name, cmp)
            };

            return ordered.Take(800).Select(e => e.item).ToList();
        }

        public async void Scan()
        {
            if (Scanning) return;
            Scanning = true;
            Updated?.Invoke();

            var keywords = _s.ProjectNames.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).Where(k => Norm(k).Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!keywords.Any(k => Norm(k) == "hangar29")) keywords.Insert(0, "hangar29");

            List<WheelItem> found;
            try
            {
                found = await Task.Run(() => Find(keywords));
            }
            catch
            {
                found = new List<WheelItem>();
            }

            Projects = found.Where(f => Norm(f.Name) != "hangar29").ToList();
            HangarPath = found.FirstOrDefault(f => Norm(f.Name) == "hangar29")?.Path;

            if (HangarPath != null)
            {
                var pinned = _s.Pinned.FirstOrDefault(p => Norm(p.Name) == "hangar29");
                if (pinned != null && !Exists(pinned.Path))
                {
                    pinned.Path = HangarPath;
                    _s.Touch();
                }
                else if (pinned == null && !_s.HangarPinned)
                {
                    SetHangar(HangarPath);
                }
            }
            else
            {
                HangarPath = _s.Pinned.FirstOrDefault(p => Norm(p.Name) == "hangar29" && Exists(p.Path))?.Path;
            }

            Scanning = false;
            Scanned = true;
            Prewarm();
            Updated?.Invoke();
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static bool Junk(string fileName)
        {
            var n = Norm(fileName);
            return n.Contains("uninstall") || n.Contains("setup") || n.Contains("installer") || n.Contains("update") || n.Contains("crashpad") || n.Contains("squirrel");
        }

        static List<WheelItem> Find(List<string> keywords)
        {
            var running = new List<(string norm, string path)>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var path = Native.ProcessPath((uint)p.Id);
                    if (path != null) running.Add((Norm(p.ProcessName), path));
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }

            var shortcuts = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Walk(Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs"), 4, shortcuts, ".lnk", 3000);
            Walk(Path.Combine(common, @"Microsoft\Windows\Start Menu\Programs"), 4, shortcuts, ".lnk", 3000);
            Walk(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 1, shortcuts, ".lnk", 500);
            Walk(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), 1, shortcuts, ".lnk", 500);

            var installRoots = new List<string>();
            foreach (var root in new[]
                     {
                         Path.Combine(local, "Programs"),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                try
                {
                    if (Directory.Exists(root)) installRoots.AddRange(Directory.GetDirectories(root));
                }
                catch
                {
                }
            }

            var devExes = new List<string>();
            int budget = 8000;
            foreach (var dev in new[] { @"source\repos", @"Documents\GitHub", "Desktop", "Projects", "projects", "dev", "code", "Code", "Documents", "Downloads", "GitHub", "repos" })
            {
                var dir = Path.Combine(home, dev);
                if (Directory.Exists(dir)) budget = Walk(dir, 6, devExes, ".exe", budget);
                if (budget <= 0) break;
            }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (budget <= 0) break;
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                }
                catch
                {
                    continue;
                }
                foreach (var dev in new[] { "code", "dev", "projects", "repos", "src", "GitHub" })
                {
                    var dir = Path.Combine(drive.RootDirectory.FullName, dev);
                    if (Directory.Exists(dir)) budget = Walk(dir, 6, devExes, ".exe", budget);
                }
            }

            var results = new List<WheelItem>();
            foreach (var kw in keywords)
            {
                var k = Norm(kw);
                var candidates = new List<string>();
                candidates.AddRange(running.Where(r => r.norm.Contains(k)).Select(r => r.path).Where(p => !Junk(Path.GetFileName(p))));
                foreach (var dir in installRoots)
                {
                    if (!Norm(Path.GetFileName(dir)).Contains(k)) continue;
                    var exe = BestExe(dir, k);
                    if (exe != null) candidates.Add(exe);
                }
                candidates.AddRange(devExes.Where(e => Norm(Path.GetFileNameWithoutExtension(e)).Contains(k) && !Junk(Path.GetFileName(e))));

                string? hit = candidates
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(SafeTime)
                    .FirstOrDefault();

                hit ??= shortcuts.FirstOrDefault(s => Norm(Path.GetFileNameWithoutExtension(s)).Contains(k) && !Junk(Path.GetFileName(s)));

                if (hit != null) results.Add(new WheelItem { Name = kw, Path = hit, Kind = "project" });
            }
            return results;
        }

        static DateTime SafeTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }

        static string? BestExe(string dir, string keyword)
        {
            var exes = new List<string>();
            Walk(dir, 2, exes, ".exe", 400);
            var good = exes.Where(e => !Junk(Path.GetFileName(e))).ToList();
            return good.FirstOrDefault(e => Norm(Path.GetFileNameWithoutExtension(e)).Contains(keyword))
                   ?? good.OrderByDescending(e =>
                   {
                       try { return new FileInfo(e).Length; } catch { return 0L; }
                   }).FirstOrDefault();
        }

        static int Walk(string dir, int depth, List<string> into, string ext, int budget)
        {
            if (budget <= 0 || depth < 0) return budget;
            budget--;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*" + ext))
                    into.Add(f);
                if (depth == 0) return budget;
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(d).ToLowerInvariant();
                    if (SkipDirs.Contains(name) || name.StartsWith(".")) continue;
                    try
                    {
                        if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch
                    {
                        continue;
                    }
                    budget = Walk(d, depth - 1, into, ext, budget);
                    if (budget <= 0) break;
                }
            }
            catch
            {
            }
            return budget;
        }

        public void Dispose()
        {
            Icons.Dispose();
            Flush();
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        }
    }
}
