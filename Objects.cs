using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Elysium
{
    internal sealed class Obj
    {
        public string Kind = "file";
        public string Name = "";
        public string Path = "";
        public string? Detail;
        public object? Payload;

        public static Obj From(string path)
        {
            var o = new Obj { Path = path, Name = System.IO.Path.GetFileName(path.TrimEnd('\\')) };
            if (string.IsNullOrEmpty(o.Name)) o.Name = path;
            if (Directory.Exists(path)) o.Kind = path.Length <= 3 ? "drive" : "folder";
            else if (Files.IsApp(path))
            {
                o.Kind = "app";
                o.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            }
            return o;
        }

        public static Obj From(WheelItem w)
        {
            var o = From(w.Path);
            if (!string.IsNullOrEmpty(w.Name)) o.Name = w.Name;
            if (w.Kind == "uwp")
            {
                o.Kind = "uwp";
            }
            return o;
        }
    }

    internal sealed class ObjAction
    {
        public string Name = "";
        public string Glyph = "\uE8A7";
        public string Group = "";
        public bool Danger;
        public Action Run = () => { };
    }

    internal static class Files
    {
        public static readonly (string cat, string label, string color, string[] exts)[] Categories =
        {
            ("image", "Images", "#FF5FA8", new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".ico", ".heic", ".psd", ".raw" }),
            ("video", "Video", "#A472FF", new[] { ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".m4v", ".flv" }),
            ("audio", "Audio", "#FF9F45", new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus" }),
            ("code", "Code", "#3DDC97", new[] { ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".lua", ".json", ".html", ".css", ".scss", ".xaml", ".xml", ".yml", ".yaml", ".toml", ".sh", ".ps1", ".bat", ".md", ".sln", ".csproj", ".gradle", ".kt", ".swift", ".rb", ".php", ".sql", ".glsl", ".hlsl", ".toe", ".tox" }),
            ("doc", "Documents", "#4EA8FF", new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".pptx", ".ppt", ".xlsx", ".xls", ".csv", ".key", ".pages", ".epub" }),
            ("archive", "Archives", "#C49A6C", new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso" }),
            ("app", "Apps", "#9AA0A6", new[] { ".exe", ".msi", ".lnk", ".url", ".appref-ms", ".bat", ".cmd" }),
            ("model", "3D", "#FFD43B", new[] { ".fbx", ".obj", ".blend", ".stl", ".gltf", ".glb", ".3mf", ".step", ".stp" })
        };

        public static string Category(string path)
        {
            if (Directory.Exists(path)) return "folder";
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            foreach (var c in Categories)
                if (c.exts.Contains(ext)) return c.cat;
            return "other";
        }

        public static Color ColorFor(Settings s, string cat)
        {
            string hex = "#8E8E93";
            if (cat == "folder") hex = "#6C9BD2";
            foreach (var c in Categories)
                if (c.cat == cat) hex = c.color;
            if (s.CategoryColors.TryGetValue(cat, out var custom)) hex = custom;
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return Colors.Gray;
            }
        }

        public static readonly string[] Bookmarks = { "#FF453A", "#FF9F0A", "#FFD60A", "#32D74B", "#0A84FF", "#BF5AF2" };

        public static bool IsApp(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".exe" or ".lnk" or ".appref-ms" or ".url";
        }

        public static bool IsImage(string path) => Category(path) == "image" && !path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

        public static bool IsText(string path)
        {
            if (Category(path) == "code") return true;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".txt" or ".log" or ".ini" or ".cfg" or ".conf" or ".env" or ".csv" or ".gitignore";
        }

        public static void Open(string path, Settings? s = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                if (s != null)
                {
                    s.FileOpens.TryGetValue(path, out double v);
                    s.FileOpens[path] = v + 1;
                    if (s.FileOpens.Count > 2000)
                    {
                        foreach (var k in s.FileOpens.OrderBy(kv => kv.Value).Take(500).Select(kv => kv.Key).ToList()) s.FileOpens.Remove(k);
                    }
                    s.Touch();
                }
            }
            catch
            {
            }
        }

        public static void Reveal(string path)
        {
            try
            {
                var args = Directory.Exists(path) && path.Length > 3 ? $"\"{path}\"" : $"/select,\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        public static void CopyFiles(IEnumerable<string> paths)
        {
            try
            {
                var col = new StringCollection();
                col.AddRange(paths.ToArray());
                Clipboard.SetFileDropList(col);
            }
            catch
            {
            }
        }

        public static string Unique(string dir, string name)
        {
            string target = System.IO.Path.Combine(dir, name);
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
            string stem = System.IO.Path.GetFileNameWithoutExtension(name);
            string ext = System.IO.Path.GetExtension(name);
            for (int i = 2; i < 999; i++)
            {
                target = System.IO.Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(target) && !Directory.Exists(target)) return target;
            }
            return target;
        }

        public static string? Zip(IList<string> paths)
        {
            try
            {
                if (paths.Count == 0) return null;
                string dir = System.IO.Path.GetDirectoryName(paths[0].TrimEnd('\\')) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string name = paths.Count == 1 ? System.IO.Path.GetFileNameWithoutExtension(paths[0].TrimEnd('\\')) + ".zip" : "archive.zip";
                string target = Unique(dir, name);
                using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
                foreach (var p in paths)
                {
                    if (Directory.Exists(p))
                    {
                        string root = System.IO.Path.GetFileName(p.TrimEnd('\\'));
                        foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                            zip.CreateEntryFromFile(f, System.IO.Path.Combine(root, System.IO.Path.GetRelativePath(p, f)), CompressionLevel.Optimal);
                    }
                    else if (File.Exists(p))
                    {
                        zip.CreateEntryFromFile(p, System.IO.Path.GetFileName(p), CompressionLevel.Optimal);
                    }
                }
                return target;
            }
            catch
            {
                return null;
            }
        }

        public static string? Unzip(string path)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path)!;
                string target = Unique(dir, System.IO.Path.GetFileNameWithoutExtension(path));
                ZipFile.ExtractToDirectory(path, target);
                return target;
            }
            catch
            {
                return null;
            }
        }

        public static string? ConvertImage(string path, string ext)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                BitmapEncoder enc = ext switch
                {
                    ".jpg" => new JpegBitmapEncoder { QualityLevel = 92 },
                    ".bmp" => new BmpBitmapEncoder(),
                    ".gif" => new GifBitmapEncoder(),
                    ".tif" => new TiffBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                BitmapSource src = bmp;
                if (ext == ".jpg" && bmp.Format != PixelFormats.Bgr24 && bmp.Format != PixelFormats.Bgr32)
                {
                    var flat = new RenderTargetBitmap(bmp.PixelWidth, bmp.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
                        dc.DrawImage(bmp, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
                    }
                    flat.Render(dv);
                    src = flat;
                }
                enc.Frames.Add(BitmapFrame.Create(src));
                string target = Unique(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileNameWithoutExtension(path) + ext);
                using var fs = File.Create(target);
                enc.Save(fs);
                return target;
            }
            catch
            {
                return null;
            }
        }

        public static string? Ffmpeg()
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var p = System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(p)) return p;
                }
                catch
                {
                }
            }
            return null;
        }

        public static void FfmpegConvert(string path, string mode, Notifier notes)
        {
            var ff = Ffmpeg();
            if (ff == null) return;
            string dir = System.IO.Path.GetDirectoryName(path)!;
            string stem = System.IO.Path.GetFileNameWithoutExtension(path);
            string target;
            string args;
            if (mode == ".gif")
            {
                target = Unique(dir, stem + ".gif");
                args = $"-y -i \"{path}\" -vf \"fps=15,scale=480:-1:flags=lanczos\" \"{target}\"";
            }
            else
            {
                target = Unique(dir, stem + "-small.mp4");
                args = $"-y -i \"{path}\" -c:v libx264 -preset veryfast -crf 28 -c:a aac -b:a 96k \"{target}\"";
            }
            try
            {
                var proc = Process.Start(new ProcessStartInfo(ff, args) { CreateNoWindow = true, UseShellExecute = false });
                if (proc == null) return;
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    notes.Post("Converted", System.IO.Path.GetFileName(target), "\uE8AB", target)));
            }
            catch
            {
            }
        }

        public static void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
            }
        }

        public static bool Recycle(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string? ProjectRoot(string path)
        {
            string? dir = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                try
                {
                    if (Directory.Exists(System.IO.Path.Combine(dir, ".git"))
                        || File.Exists(System.IO.Path.Combine(dir, "package.json"))
                        || File.Exists(System.IO.Path.Combine(dir, "Cargo.toml"))
                        || Directory.EnumerateFiles(dir, "*.sln").Any()
                        || Directory.EnumerateFiles(dir, "*.csproj").Any()
                        || File.Exists(System.IO.Path.Combine(dir, "build.gradle"))
                        || File.Exists(System.IO.Path.Combine(dir, "pyproject.toml")))
                        return dir;
                }
                catch
                {
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        public static List<string> Related(string path, int max = 12)
        {
            var result = new List<string>();
            try
            {
                if (Directory.Exists(path)) return result;
                string dir = System.IO.Path.GetDirectoryName(path)!;
                string stem = System.IO.Path.GetFileNameWithoutExtension(path);
                string core = stem.Split('-', '_', ' ', '.')[0];
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (f.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
                    string s = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (s.Equals(stem, StringComparison.OrdinalIgnoreCase) || (core.Length >= 3 && s.StartsWith(core, StringComparison.OrdinalIgnoreCase)))
                        result.Add(f);
                    if (result.Count >= max) break;
                }
            }
            catch
            {
            }
            return result;
        }
    }

    internal sealed class ObjectActions
    {
        readonly Settings _s;
        readonly Notifier _notes;

        public Action<Obj>? Inspect;
        public Action<IEnumerable<string>>? ToShelf;
        public Action<IEnumerable<string>, string>? ToCollection;
        public Action<string>? Rename;
        public Action<string>? PinToDock;
        public Action<string>? Browse;
        public Action<IList<string>>? Share;

        public ObjectActions(Settings s, Notifier notes)
        {
            _s = s;
            _notes = notes;
        }

        public List<ObjAction> For(Obj o)
        {
            var list = new List<ObjAction>();
            void Add(string name, string glyph, string group, Action run, bool danger = false) =>
                list.Add(new ObjAction { Name = name, Glyph = glyph, Group = group, Run = run, Danger = danger });

            string p = o.Path;
            bool exists = File.Exists(p) || Directory.Exists(p);

            if (o.Kind == "window" && o.Payload is IntPtr hwnd)
            {
                Add("Bring back", "\uE8A7", "open", () => Native.Focus(hwnd));
                return list;
            }
            if (o.Kind == "uwp")
            {
                Add("Open", "\uE8A7", "open", () => Launch.Uwp(p));
                return list;
            }
            if (!exists && o.Kind != "url") return list;

            Add(o.Kind is "folder" or "drive" ? "Open in Explorer" : "Open", "\uE8A7", "open", () => Files.Open(p, _s));
            if (o.Kind is "folder" or "drive" && Browse != null) Add("Browse on the shelf", "\uE8B7", "open", () => Browse(p));
            if (o.Kind != "drive") Add("Show in folder", "\uE838", "open", () => Files.Reveal(p));
            if (Inspect != null) Add("Inspect", "\uE946", "open", () => Inspect(o));
            if (o.Kind == "file") Add("Open with...", "\uE7AC", "open", () => Launch.Run("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{p}\""));
            if (o.Kind == "app" && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Add("Run as admin", "\uE7EF", "open", () => Launch.Admin(p));
            if (o.Kind == "folder" && Projects.IsProject(p)) Add("Open in code editor", "\uE943", "open", () => Projects.OpenInEditor(p));

            if (ToShelf != null) Add("Put on the shelf", "\uE7B8", "organize", () => ToShelf(new[] { p }));
            if (ToCollection != null) Add("Add to collection...", "\uE710", "organize", () => ToCollection(new[] { p }, ""));
            if (PinToDock != null && o.Kind is "app" or "folder" or "file") Add("Pin to dock", "\uE718", "organize", () => PinToDock(p));
            Add("Color tag (cycle)", "\uE8EC", "organize", () =>
            {
                _s.Bookmarks.TryGetValue(p, out int cur);
                int next = (cur + 1) % (Files.Bookmarks.Length + 1);
                if (next == 0) _s.Bookmarks.Remove(p);
                else _s.Bookmarks[p] = next;
                _s.Touch();
            });

            Add("Copy", "\uE8C8", "share", () => Files.CopyFiles(new[] { p }));
            Add("Copy path", "\uE71B", "share", () => Files.CopyText(p));
            if (Share != null && o.Kind is "file" or "folder") Add("Send to phone", "\uE8EA", "share", () => Share(new[] { p }));
            if (o.Kind is "file" or "folder") Add("Compress to zip", "\uF012", "convert", () =>
            {
                var z = Files.Zip(new[] { p });
                if (z != null) _notes.Post("Zipped", System.IO.Path.GetFileName(z), "\uF012", z);
            });
            if (p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) Add("Extract here", "\uE8B7", "convert", () =>
            {
                var d = Files.Unzip(p);
                if (d != null) _notes.Post("Extracted", System.IO.Path.GetFileName(d), "\uE8B7", d);
            });
            if (Files.IsImage(p))
            {
                foreach (var ext in new[] { ".png", ".jpg", ".bmp" })
                {
                    if (p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
                    string e = ext;
                    Add($"Convert to {ext.TrimStart('.').ToUpperInvariant()}", "\uE8AB", "convert", () =>
                    {
                        var r = Files.ConvertImage(p, e);
                        if (r != null) _notes.Post("Converted", System.IO.Path.GetFileName(r), "\uE8AB", r);
                    });
                }
            }
            if (Files.Category(p) == "video" && Files.Ffmpeg() != null)
            {
                Add("Make a GIF", "\uE8AB", "convert", () => Files.FfmpegConvert(p, ".gif", _notes));
                Add("Shrink for Discord", "\uE8AB", "convert", () => Files.FfmpegConvert(p, "-small.mp4", _notes));
            }

            if (Rename != null && o.Kind is "file" or "folder") Add("Rename", "\uE8AC", "edit", () => Rename(p));
            if (o.Kind is "file" or "folder") Add("Move to recycle bin", "\uE74D", "edit", () => Files.Recycle(p), true);

            return list;
        }
    }

    internal static class Launch
    {
        public static void Run(string file, string args = "")
        {
            try
            {
                Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        public static void Admin(string file)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas" });
            }
            catch
            {
            }
        }

        public static void Uwp(string aumid) => Run("explorer.exe", $"shell:AppsFolder\\{aumid}");

        public static void Url(string url) => Run(url);
    }
}
