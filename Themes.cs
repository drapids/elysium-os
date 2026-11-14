using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elysium
{
    public sealed class BarLook
    {
        public string[]? Fill { get; set; }
        public bool Gloss { get; set; }
        public string? Line { get; set; }
        public string? Text { get; set; }
        public string? Image { get; set; }
    }

    public sealed class DockLook
    {
        public string[]? Tile { get; set; }
        public string? Stroke { get; set; }
        public bool Gloss { get; set; }
        public double Round { get; set; } = 0.3;
    }

    public sealed class StartLook
    {
        public string? Scrim { get; set; }
        public string[]? Wash { get; set; }
        public string? Card { get; set; }
    }

    public sealed class ThemePack
    {
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Mode { get; set; } = "";
        public string Accent { get; set; } = "";
        public string Font { get; set; } = "";
        public double? Corner { get; set; }
        public double? Glass { get; set; }
        public double? Speed { get; set; }
        public string Sounds { get; set; } = "";
        public Dictionary<string, string> Colors { get; set; } = new();
        public Dictionary<string, string[]> Gradients { get; set; } = new();
        public BarLook? Bar { get; set; }
        public DockLook? Dock { get; set; }
        public StartLook? Start { get; set; }
        public string Cursor { get; set; } = "";
        public string Wallpaper { get; set; } = "";
        public string[]? WallpaperColors { get; set; }

        [JsonIgnore]
        public string Id { get; set; } = "";

        [JsonIgnore]
        public string Dir { get; set; } = "";
    }

    internal static class Themes
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium", "themes");

        static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ThemePack? Active { get; private set; }

        public static void Seed(Settings s)
        {
            if (s.ThemesSeeded && s.ThemesSeedVersion >= 2) return;
            try
            {
                Directory.CreateDirectory(Root);
                foreach (var (id, pack) in Starters())
                {
                    string dir = Path.Combine(Root, id);
                    if (s.ThemesSeeded && !Directory.Exists(dir)) continue;
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "theme.json"), JsonSerializer.Serialize(pack, Json));
                }
                s.ThemesSeeded = true;
                s.ThemesSeedVersion = 2;
                s.Touch();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("theme seed failed " + ex.Message);
            }
        }

        public static List<ThemePack> All()
        {
            var list = new List<ThemePack>();
            try
            {
                if (!Directory.Exists(Root)) return list;
                foreach (var dir in Directory.EnumerateDirectories(Root))
                {
                    var p = Load(Path.GetFileName(dir));
                    if (p != null) list.Add(p);
                }
            }
            catch
            {
            }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static ThemePack? Load(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try
            {
                string dir = Path.Combine(Root, id);
                string file = Path.Combine(dir, "theme.json");
                if (!File.Exists(file)) return null;
                var p = JsonSerializer.Deserialize<ThemePack>(File.ReadAllText(file), Json);
                if (p == null) return null;
                p.Id = id;
                p.Dir = dir;
                if (p.Name.Length == 0) p.Name = id;
                p.Colors ??= new Dictionary<string, string>();
                p.Gradients ??= new Dictionary<string, string[]>();
                return p;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("theme " + id + " failed to load " + ex.Message);
                return null;
            }
        }

        public static void Use(Settings s)
        {
            Active = s.ThemePack.Length > 0 ? Load(s.ThemePack) : null;
        }

        public static void Pick(Settings s, ThemePack? p)
        {
            s.ThemePack = p?.Id ?? "";
            Active = p;
            if (p == null)
            {
                Extras(s, null);
                s.Touch();
                return;
            }
            if (p.Mode is "dark" or "light" or "system") s.Theme = p.Mode;
            if (p.Accent.Length > 0)
            {
                s.AccentMode = p.Accent is "system" or "wallpaper" or "default" ? p.Accent : "custom";
                if (s.AccentMode == "custom") s.AccentHex = p.Accent;
            }
            s.UiFontName = p.Font;
            if (p.Corner is double c) s.CornerScale = Math.Clamp(c, 0.3, 1.8);
            if (p.Glass is double g) s.GlassAlpha = Math.Clamp(g, 0.4, 1.25);
            if (p.Speed is double sp) s.AnimScale = Math.Clamp(sp, 0.4, 2.5);
            if (p.Sounds.Length > 0) s.SoundPack = p.Sounds;
            Extras(s, p);
            s.Touch();
        }

        static void Extras(Settings s, ThemePack? p)
        {
            if (s.ThemeWallpaper)
            {
                string? wall = null;
                if (p != null)
                {
                    if (p.Wallpaper.Length > 0 && File.Exists(Path.Combine(p.Dir, p.Wallpaper))) wall = Path.Combine(p.Dir, p.Wallpaper);
                    else if (p.WallpaperColors is { Length: > 0 }) wall = Elysium.Wallpaper.Render(p.Id, p.WallpaperColors, p.Dir.Length > 0 ? p.Dir : Path.Combine(Root, p.Id));
                }
                if (wall != null)
                {
                    if (s.WallpaperBefore.Length == 0) s.WallpaperBefore = Elysium.Wallpaper.Current();
                    Elysium.Wallpaper.Set(wall);
                }
                else if (s.WallpaperBefore.Length > 0)
                {
                    Elysium.Wallpaper.Set(s.WallpaperBefore);
                    s.WallpaperBefore = "";
                }
            }
            if (s.ThemeCursor)
            {
                var scheme = p != null && p.Cursor.Length > 0 ? CursorScheme.Scheme(p.Cursor, p.Dir) : null;
                if (scheme != null)
                {
                    if (s.CursorsBefore.Count == 0) s.CursorsBefore = CursorScheme.Read();
                    CursorScheme.Write(scheme);
                }
                else if (s.CursorsBefore.Count > 0)
                {
                    CursorScheme.Write(s.CursorsBefore);
                    s.CursorsBefore = new Dictionary<string, string>();
                }
            }
        }

        public static string? Install(string file)
        {
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("theme.json", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return "there's no theme.json in it";
                string prefix = entry.FullName[..^entry.Name.Length];
                ThemePack? probe;
                using (var sr = new StreamReader(entry.Open())) probe = JsonSerializer.Deserialize<ThemePack>(sr.ReadToEnd(), Json);
                if (probe == null) return "theme.json didn't make sense";
                string id = Slug(probe.Name.Length > 0 ? probe.Name : Path.GetFileNameWithoutExtension(file));
                string dir = Path.Combine(Root, id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string full = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                foreach (var e in zip.Entries)
                {
                    if (e.Name.Length == 0 || !e.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    string rel = e.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                    string dest = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string? Export(ThemePack p, string dest)
        {
            try
            {
                if (File.Exists(dest)) File.Delete(dest);
                ZipFile.CreateFromDirectory(p.Dir, dest);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string SaveCurrent(Settings s, string name)
        {
            var p = new ThemePack
            {
                Name = name,
                Author = Environment.UserName,
                Mode = s.Theme,
                Accent = s.AccentMode == "custom" ? s.AccentHex : s.AccentMode,
                Font = s.UiFontName,
                Corner = s.CornerScale,
                Glass = s.GlassAlpha,
                Speed = s.AnimScale,
                Sounds = s.SoundPack
            };
            if (Active != null)
            {
                p.Colors = new Dictionary<string, string>(Active.Colors);
                p.Gradients = new Dictionary<string, string[]>(Active.Gradients);
                p.Bar = Active.Bar;
                p.Dock = Active.Dock;
                p.Start = Active.Start;
            }
            string id = Slug(name);
            string dir = Path.Combine(Root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "theme.json"), JsonSerializer.Serialize(p, Json));
            return dir;
        }

        public static string Slug(string name)
        {
            var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            string slug = new string(chars).Trim('-');
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Length == 0 ? "theme-" + DateTime.Now.ToString("HHmmss") : slug;
        }

        static IEnumerable<(string id, ThemePack pack)> Starters()
        {
            yield return ("frutiger-aero", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#8FDBFF", "#38A8EC", "#1E7FC8", "#8ED36B" },
                Name = "Frutiger Aero",
                Author = "elysium",
                Description = "glossy sky, bubbles and clean water. 2008 called",
                Mode = "light",
                Accent = "#1BA1E2",
                Font = "Segoe UI",
                Corner = 1.4,
                Glass = 0.9,
                Sounds = "glass",
                Colors = new()
                {
                    ["Text"] = "#FF0B3954",
                    ["SubText"] = "#B30B3954",
                    ["Faint"] = "#800B3954",
                    ["GroupBg"] = "#73FFFFFF",
                    ["GroupStroke"] = "#B3FFFFFF",
                    ["RowHover"] = "#4DFFFFFF",
                    ["PillStroke"] = "#E6FFFFFF",
                    ["FieldBg"] = "#B3FFFFFF",
                    ["FieldStroke"] = "#806FC3E8",
                    ["Separator"] = "#4D6FC3E8"
                },
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#F2F7FCFF", "#E0D6F0FC", "#D9AEDFF5" },
                    ["PushBg"] = new[] { "#FFFFFFFF", "#FFE3F4FD", "#FFC6E8F9" },
                    ["AccentFill"] = new[] { "#FF6DD0FA", "#FF1BA1E2", "#FF0B7DC1" },
                    ["SegThumb"] = new[] { "#FFFFFFFF", "#FFDDF1FC" },
                    ["TileBg"] = new[] { "#B3FFFFFF", "#80BFE6F8" }
                },
                Bar = new BarLook { Fill = new[] { "#F7FFFFFF", "#E6CDEBF9", "#D98ACFF0" }, Gloss = true, Line = "#FFFFFFFF", Text = "#FF0B3954" },
                Dock = new DockLook { Tile = new[] { "#E6FFFFFF", "#B37FCBEF" }, Stroke = "#FFFFFFFF", Gloss = true, Round = 0.32 },
                Start = new StartLook { Scrim = "#40BFE7FA", Wash = new[] { "#3387D3F5", "#1A2BA8E0" }, Card = "#66FFFFFF" }
            });
            yield return ("midnight", new ThemePack
            {
                Cursor = "Windows Black",
                WallpaperColors = new[] { "#141A3D", "#0B0F26", "#04050C" },
                Name = "Midnight",
                Author = "elysium",
                Description = "pure black, made for oled and late nights",
                Mode = "dark",
                Accent = "#7C5CFF",
                Corner = 1,
                Glass = 1.2,
                Colors = new()
                {
                    ["GroupBg"] = "#0DFFFFFF",
                    ["GroupStroke"] = "#14FFFFFF",
                    ["PillStroke"] = "#1FFFFFFF"
                },
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FA050507", "#FA0A0A10" },
                    ["AccentFill"] = new[] { "#FF9479FF", "#FF6A45FF" }
                },
                Bar = new BarLook { Fill = new[] { "#FF000000", "#FF000000" }, Line = "#1AFFFFFF" },
                Dock = new DockLook { Tile = new[] { "#F2101016", "#F2050508" }, Stroke = "#26FFFFFF" },
                Start = new StartLook { Scrim = "#D9000000" }
            });
            yield return ("paper", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FBF8F1", "#EFE8D8" },
                Name = "Paper",
                Author = "elysium",
                Description = "flat, quiet and warm. like a notebook",
                Mode = "light",
                Accent = "#1F1F1F",
                Font = "Georgia",
                Corner = 0.45,
                Glass = 1.25,
                Sounds = "soft",
                Colors = new()
                {
                    ["Text"] = "#FF1C1B19",
                    ["SubText"] = "#A61C1B19",
                    ["Faint"] = "#661C1B19",
                    ["GroupBg"] = "#FFFFFDF8",
                    ["GroupStroke"] = "#1F1C1B19",
                    ["PillStroke"] = "#261C1B19",
                    ["FieldBg"] = "#FFFFFFFF"
                },
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FFF6F3EC" }
                },
                Bar = new BarLook { Fill = new[] { "#FFF6F3EC" }, Line = "#261C1B19", Text = "#FF1C1B19" },
                Dock = new DockLook { Tile = new[] { "#FFFFFDF8" }, Stroke = "#261C1B19", Round = 0.18 },
                Start = new StartLook { Scrim = "#99F6F3EC", Card = "#B3FFFFFF" }
            });
            yield return ("y2k-chrome", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#F2F5FA", "#B7C1D1", "#E6EBF2", "#8C99AE" },
                Name = "Y2K Chrome",
                Author = "elysium",
                Description = "brushed metal, bubblegum accent, very millennium",
                Mode = "light",
                Accent = "#FF3EA5",
                Font = "Trebuchet MS",
                Corner = 1.6,
                Glass = 1.1,
                Sounds = "retro",
                Colors = new()
                {
                    ["Text"] = "#FF20242B",
                    ["SubText"] = "#A620242B",
                    ["PillStroke"] = "#FFFFFFFF",
                    ["GroupStroke"] = "#CCFFFFFF"
                },
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FAFBFCFE", "#F0C9D2DC", "#FAEEF1F5", "#F0B8C2CE" },
                    ["PushBg"] = new[] { "#FFFFFFFF", "#FFC9D1DB", "#FFF2F4F7" },
                    ["AccentFill"] = new[] { "#FFFF8CCB", "#FFFF3EA5", "#FFD10F7E" },
                    ["GroupBg"] = new[] { "#99FFFFFF", "#66D5DCE4" }
                },
                Bar = new BarLook { Fill = new[] { "#FFFDFDFD", "#FFCBD3DD", "#FFEFF2F6", "#FFB9C3CF" }, Gloss = true, Line = "#FFFFFFFF", Text = "#FF20242B" },
                Dock = new DockLook { Tile = new[] { "#FFFDFDFD", "#FFBCC6D1" }, Stroke = "#FFFFFFFF", Gloss = true, Round = 0.5 },
                Start = new StartLook { Scrim = "#59C9D2DC", Wash = new[] { "#26FF3EA5", "#1A8FA3B8" }, Card = "#80FFFFFF" }
            });
            yield return ("terminal", new ThemePack
            {
                Cursor = "Windows Black",
                WallpaperColors = new[] { "#060A06", "#0A160A" },
                Name = "Terminal",
                Author = "elysium",
                Description = "green on black, everything in mono",
                Mode = "dark",
                Accent = "#33FF66",
                Font = "Cascadia Mono, Consolas",
                Corner = 0.3,
                Glass = 1.25,
                Sounds = "retro",
                Colors = new()
                {
                    ["Text"] = "#FF39FF6E",
                    ["SubText"] = "#B339FF6E",
                    ["Faint"] = "#6639FF6E",
                    ["GroupBg"] = "#0D39FF6E",
                    ["GroupStroke"] = "#3339FF6E",
                    ["PillStroke"] = "#5939FF6E",
                    ["OnAccent"] = "#FF020A04",
                    ["Separator"] = "#2639FF6E"
                },
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FA020A04" }
                },
                Bar = new BarLook { Fill = new[] { "#FF020A04" }, Line = "#6639FF6E", Text = "#FF39FF6E" },
                Dock = new DockLook { Tile = new[] { "#F2020A04" }, Stroke = "#8039FF6E", Round = 0.08 },
                Start = new StartLook { Scrim = "#E6020A04" }
            });
            yield return ("sunset", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FFB36B", "#FF6F7D", "#B04A9A", "#3E1F6B" },
                Name = "Sunset",
                Author = "elysium",
                Description = "warm purple into orange, like 7pm in july",
                Mode = "dark",
                Accent = "#FF7A59",
                Corner = 1.2,
                Glass = 0.95,
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#E62B1338", "#E64A1E3B", "#E65E2633" },
                    ["AccentFill"] = new[] { "#FFFFA26B", "#FFFF6A59", "#FFE0457B" }
                },
                Bar = new BarLook { Fill = new[] { "#E62B1338", "#E65E2633" }, Line = "#33FFB38A" },
                Dock = new DockLook { Tile = new[] { "#E64A1E3B", "#E62B1338" }, Stroke = "#40FFB38A" },
                Start = new StartLook { Scrim = "#802B1338", Wash = new[] { "#40FF7A59", "#332B1338" } }
            });
        }
    }
}
