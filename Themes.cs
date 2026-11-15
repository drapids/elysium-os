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
            if (s.ThemesSeeded && s.ThemesSeedVersion >= 3) return;
            try
            {
                Directory.CreateDirectory(Root);
                foreach (var (id, pack) in Starters())
                {
                    string dir = Path.Combine(Root, id);
                    if (s.ThemesSeeded && !Directory.Exists(dir) && !(s.ThemesSeedVersion < 3 && NewInThree.Contains(id))) continue;
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "theme.json"), JsonSerializer.Serialize(pack, Json));
                }
                s.ThemesSeeded = true;
                s.ThemesSeedVersion = 3;
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
            var was = Active;
            if (p != null && (s.LookBefore == null || s.LookBefore.Count == 0)) s.LookBefore = Snap(s);
            s.ThemePack = p?.Id ?? "";
            Active = p;
            if (p == null)
            {
                Restore(s, was);
                Extras(s, null);
                s.Touch();
                return;
            }
            if (was != null && s.LookBefore is { Count: > 0 } before) Reset(s, before);
            if (p.Mode is "dark" or "light" or "system") s.Theme = p.Mode;
            if (p.Accent.Length > 0)
            {
                s.AccentMode = p.Accent is "system" or "wallpaper" or "default" or "app" ? p.Accent : "custom";
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

        static Dictionary<string, string> Snap(Settings s) => new()
        {
            ["Theme"] = s.Theme,
            ["AccentMode"] = s.AccentMode,
            ["AccentHex"] = s.AccentHex,
            ["UiFontName"] = s.UiFontName,
            ["CornerScale"] = s.CornerScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["GlassAlpha"] = s.GlassAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["AnimScale"] = s.AnimScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SoundPack"] = s.SoundPack
        };

        static double Num(Dictionary<string, string> d, string k, double fallback) =>
            d.TryGetValue(k, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : fallback;

        static string Str(Dictionary<string, string> d, string k, string fallback) => d.TryGetValue(k, out var v) && v != null ? v : fallback;

        static void Reset(Settings s, Dictionary<string, string> b)
        {
            s.Theme = Str(b, "Theme", "system");
            s.AccentMode = Str(b, "AccentMode", "default");
            s.AccentHex = Str(b, "AccentHex", "#0A84FF");
            s.UiFontName = Str(b, "UiFontName", "");
            s.CornerScale = Math.Clamp(Num(b, "CornerScale", 1), 0.3, 1.8);
            s.GlassAlpha = Math.Clamp(Num(b, "GlassAlpha", 1), 0.4, 1.25);
            s.AnimScale = Math.Clamp(Num(b, "AnimScale", 1), 0.4, 2.5);
            s.SoundPack = Str(b, "SoundPack", "glass");
        }

        static void Restore(Settings s, ThemePack? was)
        {
            var b = s.LookBefore;
            if (b == null || b.Count == 0)
            {
                if (was != null && was.Accent.Length > 0)
                {
                    s.AccentMode = "default";
                    s.AccentHex = "#0A84FF";
                }
                if (was != null && was.Font.Length > 0) s.UiFontName = "";
                if (was?.Corner != null) s.CornerScale = 1;
                if (was?.Glass != null) s.GlassAlpha = 1;
                if (was?.Speed != null) s.AnimScale = 1;
                return;
            }
            Reset(s, b);
            s.LookBefore = new Dictionary<string, string>();
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

        static readonly HashSet<string> NewInThree = new(StringComparer.OrdinalIgnoreCase) { "graphite", "fjord", "blossom", "moss", "vapor", "ember" };

        static Dictionary<string, string> Ink(string ink, string line, string card, string field, string pill, bool dark, Dictionary<string, string>? more = null)
        {
            var d = new Dictionary<string, string>
            {
                ["Text"] = "#F2" + ink,
                ["SubText"] = "#A6" + ink,
                ["Faint"] = "#66" + ink,
                ["GroupBg"] = card,
                ["GroupStroke"] = (dark ? "#1A" : "#1F") + line,
                ["Separator"] = (dark ? "#17" : "#1A") + line,
                ["RowHover"] = (dark ? "#12" : "#0D") + ink,
                ["PushStroke"] = (dark ? "#1F" : "#26") + line,
                ["SegTrack"] = (dark ? "#1F" : "#14") + ink,
                ["SwitchOff"] = (dark ? "#2E" : "#26") + ink,
                ["SliderTrack"] = (dark ? "#2E" : "#21") + ink,
                ["FieldBg"] = field,
                ["FieldStroke"] = (dark ? "#24" : "#2E") + line,
                ["PillBg"] = pill,
                ["PillStroke"] = (dark ? "#2E" : "#33") + line,
                ["TileBg"] = (dark ? "#14" : "#0F") + ink
            };
            if (more != null) foreach (var kv in more) d[kv.Key] = kv.Value;
            return d;
        }

        static IEnumerable<(string id, ThemePack pack)> Starters()
        {
            yield return ("frutiger-aero", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#9BE2FF", "#3FB0F0", "#1E7FC8", "#7FD36B", "#C9F2FF" },
                Name = "Frutiger Aero",
                Author = "elysium",
                Description = "glossy sky, bubbles and clean water. 2008 called",
                Mode = "light",
                Accent = "#1BA1E2",
                Font = "Segoe UI",
                Corner = 1.4,
                Glass = 0.9,
                Sounds = "glass",
                Colors = Ink("0B3954", "6FC3E8", "#80FFFFFF", "#CCFFFFFF", "#E6F4FBFF", false, new() { ["PillStroke"] = "#E6FFFFFF", ["GroupStroke"] = "#B3FFFFFF", ["RowHover"] = "#4DFFFFFF" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#F2F9FDFF", "#E6DAF1FC", "#E0B4E3F7" },
                    ["PushBg"] = new[] { "#FFFFFFFF", "#FFE6F5FD", "#FFC8E9FA" },
                    ["PushHover"] = new[] { "#FFFFFFFF", "#FFEFF9FE", "#FFD6EFFB" },
                    ["AccentFill"] = new[] { "#FF79D5FB", "#FF1BA1E2", "#FF0B7DC1" },
                    ["SegThumb"] = new[] { "#FFFFFFFF", "#FFDDF1FC" },
                    ["TileBg"] = new[] { "#B3FFFFFF", "#80BFE6F8" }
                },
                Bar = new BarLook { Fill = new[] { "#F7FFFFFF", "#E6D3EEFA", "#D98ACFF0" }, Gloss = true, Line = "#FFFFFFFF", Text = "#FF0B3954" },
                Dock = new DockLook { Tile = new[] { "#F2FFFFFF", "#CC9AD7F3" }, Stroke = "#FFFFFFFF", Gloss = true, Round = 0.32 },
                Start = new StartLook { Scrim = "#4DBFE7FA", Wash = new[] { "@radial", "#5587D3F5", "#002BA8E0" }, Card = "#80FFFFFF" }
            });
            yield return ("midnight", new ThemePack
            {
                Cursor = "Windows Black",
                WallpaperColors = new[] { "#1B1F4D", "#07081A", "#2A145C", "#020205" },
                Name = "Midnight",
                Author = "elysium",
                Description = "true black with a violet glow. made for oled and late nights",
                Mode = "dark",
                Accent = "#8B6CFF",
                Corner = 1,
                Glass = 1.2,
                Colors = Ink("F3F2FF", "FFFFFF", "#0AFFFFFF", "#0FFFFFFF", "#F20A0A10", true),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#FB07070C", "#FB0E0B1C" },
                    ["AccentFill"] = new[] { "@135", "#FFA996FF", "#FF6A45FF" },
                    ["PushBg"] = new[] { "#24FFFFFF", "#14FFFFFF" }
                },
                Bar = new BarLook { Fill = new[] { "#FF000000" }, Line = "#1AFFFFFF" },
                Start = new StartLook { Scrim = "#E6000000", Wash = new[] { "@radial", "#3D7C5CFF", "#00000000" } }
            });
            yield return ("paper", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FBF8F1", "#EFE6D2", "#F6F0E3" },
                Name = "Paper",
                Author = "elysium",
                Description = "flat, quiet and warm. like a good notebook",
                Mode = "light",
                Accent = "#C2410C",
                Font = "Georgia",
                Corner = 0.45,
                Glass = 1.25,
                Sounds = "soft",
                Colors = Ink("1C1B19", "1C1B19", "#FFFFFDF8", "#FFFFFFFF", "#FFFBF8F1", false),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FFF6F2E9" },
                    ["PushBg"] = new[] { "#FFFFFDF8" }
                },
                Bar = new BarLook { Fill = new[] { "#FFF6F2E9" }, Line = "#261C1B19", Text = "#FF1C1B19" },
                Dock = new DockLook { Tile = new[] { "#FFFFFDF8" }, Stroke = "#261C1B19", Round = 0.18 },
                Start = new StartLook { Scrim = "#B3F6F2E9", Card = "#CCFFFFFF" }
            });
            yield return ("y2k-chrome", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#F2F5FA", "#B7C1D1", "#FFB3DD", "#8C99AE" },
                Name = "Y2K Chrome",
                Author = "elysium",
                Description = "brushed metal, bubblegum accent, very millennium",
                Mode = "light",
                Accent = "#FF3EA5",
                Font = "Trebuchet MS",
                Corner = 1.6,
                Glass = 1.1,
                Sounds = "retro",
                Colors = Ink("20242B", "20242B", "#99FFFFFF", "#E6FFFFFF", "#F2F4F6F9", false, new() { ["PillStroke"] = "#FFFFFFFF", ["GroupStroke"] = "#CCFFFFFF" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FAFBFCFE", "#F0C9D2DC", "#FAEEF1F5", "#F0B8C2CE" },
                    ["PushBg"] = new[] { "#FFFFFFFF", "#FFC9D1DB", "#FFF2F4F7" },
                    ["AccentFill"] = new[] { "#FFFF9AD2", "#FFFF3EA5", "#FFD10F7E" },
                    ["GroupBg"] = new[] { "#99FFFFFF", "#66D5DCE4" }
                },
                Bar = new BarLook { Fill = new[] { "#FFFDFDFD", "#FFCBD3DD", "#FFEFF2F6", "#FFB9C3CF" }, Gloss = true, Line = "#FFFFFFFF", Text = "#FF20242B" },
                Dock = new DockLook { Tile = new[] { "#FFFDFDFD", "#FFBCC6D1" }, Stroke = "#FFFFFFFF", Gloss = true, Round = 0.5 },
                Start = new StartLook { Scrim = "#66C9D2DC", Wash = new[] { "@135", "#33FF3EA5", "#1A8FA3B8" }, Card = "#80FFFFFF" }
            });
            yield return ("terminal", new ThemePack
            {
                Cursor = "Windows Black",
                WallpaperColors = new[] { "#060A06", "#0A160A", "#04140A" },
                Name = "Terminal",
                Author = "elysium",
                Description = "green on black, everything in mono",
                Mode = "dark",
                Accent = "#33FF66",
                Font = "Cascadia Mono, Consolas",
                Corner = 0.3,
                Glass = 1.25,
                Speed = 0.8,
                Sounds = "retro",
                Colors = Ink("39FF6E", "39FF6E", "#0D39FF6E", "#1439FF6E", "#F2020A04", true, new() { ["OnAccent"] = "#FF020A04", ["Text"] = "#FF39FF6E", ["Knob"] = "#FF39FF6E" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "#FA020A04" },
                    ["PushBg"] = new[] { "#1A39FF6E" }
                },
                Bar = new BarLook { Fill = new[] { "#FF020A04" }, Line = "#6639FF6E", Text = "#FF39FF6E" },
                Dock = new DockLook { Tile = new[] { "#F2020A04" }, Stroke = "#8039FF6E", Round = 0.08 },
                Start = new StartLook { Scrim = "#E6020A04" }
            });
            yield return ("sunset", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FFB36B", "#FF6F7D", "#B04A9A", "#3E1F6B", "#FFD49A" },
                Name = "Sunset",
                Author = "elysium",
                Description = "warm purple into orange, like 7pm in july",
                Mode = "dark",
                Accent = "#FF7A59",
                Corner = 1.2,
                Glass = 0.95,
                Colors = Ink("FFF1EA", "FFC9AE", "#14FFE3D6", "#1AFFE3D6", "#E62C1533", true),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@150", "#E8251135", "#E841193A", "#E85A2433" },
                    ["AccentFill"] = new[] { "@120", "#FFFFB26B", "#FFFF6A59", "#FFE0457B" }
                },
                Bar = new BarLook { Fill = new[] { "@90", "#E62B1338", "#E64A1E3B", "#E65E2633" }, Line = "#33FFB38A" },
                Start = new StartLook { Scrim = "#992B1338", Wash = new[] { "@radial", "#55FF7A59", "#002B1338" } }
            });
            yield return ("graphite", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#3A3F4A", "#16171B", "#2B2D33", "#4A5160" },
                Name = "Graphite",
                Author = "elysium",
                Description = "neutral dark grey, cyan accent, nothing loud",
                Mode = "dark",
                Accent = "#5AC8FA",
                Corner = 1.1,
                Glass = 1.05,
                Sounds = "glass",
                Colors = Ink("F5F5F7", "FFFFFF", "#10FFFFFF", "#14FFFFFF", "#EB2A2A2D", true),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@165", "#EE232326", "#EE18181B" },
                    ["AccentFill"] = new[] { "@135", "#FF7AD7FF", "#FF32ADE6" }
                },
                Bar = new BarLook { Fill = new[] { "#E61C1C1F" }, Line = "#14FFFFFF" },
                Start = new StartLook { Scrim = "#B3101012" }
            });
            yield return ("fjord", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#5E81AC", "#2E3440", "#4C566A", "#88C0D0", "#3B4252" },
                Name = "Fjord",
                Author = "elysium",
                Description = "cold blue-grey, like a lake in norway at 4pm",
                Mode = "dark",
                Accent = "#88C0D0",
                Corner = 0.9,
                Glass = 1.1,
                Sounds = "soft",
                Colors = Ink("ECEFF4", "D8DEE9", "#143B4252", "#1A434C5E", "#F03B4252", true, new() { ["OnAccent"] = "#FF2E3440", ["Danger"] = "#FFBF616A" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#F2353C4A", "#F22B313C" },
                    ["AccentFill"] = new[] { "@135", "#FF8FBCBB", "#FF88C0D0", "#FF81A1C1" }
                },
                Bar = new BarLook { Fill = new[] { "#F02E3440" }, Line = "#334C566A", Text = "#FFECEFF4" },
                Start = new StartLook { Scrim = "#B32E3440", Wash = new[] { "@radial", "#3388C0D0", "#002E3440" } }
            });
            yield return ("blossom", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FFE3EC", "#F7C6D9", "#E7D3F5", "#FFF1E6", "#F9B8CF" },
                Name = "Blossom",
                Author = "elysium",
                Description = "soft pink and lilac, rounded and cozy",
                Mode = "light",
                Accent = "#E0628C",
                Corner = 1.5,
                Glass = 0.95,
                Speed = 1.1,
                Sounds = "soft",
                Colors = Ink("4A2B3A", "B9718E", "#99FFFFFF", "#D9FFFFFF", "#F2FFF7FA", false, new() { ["GroupStroke"] = "#33E7A9C0" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#F2FFF7FA", "#EBFBE6EF", "#E6EFE3FA" },
                    ["AccentFill"] = new[] { "@135", "#FFF59BB8", "#FFE0628C", "#FFC2549F" },
                    ["SegThumb"] = new[] { "#FFFFFFFF", "#FFFFF0F5" }
                },
                Bar = new BarLook { Fill = new[] { "@90", "#F2FFF7FA", "#F2F8ECFA" }, Line = "#40E7A9C0", Text = "#FF4A2B3A" },
                Start = new StartLook { Scrim = "#59FBE6EF", Wash = new[] { "@radial", "#40F59BB8", "#00E7D3F5" }, Card = "#99FFFFFF" }
            });
            yield return ("moss", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#2F4A2A", "#14200F", "#5A7A3E", "#0C130A", "#3E5C34" },
                Name = "Moss",
                Author = "elysium",
                Description = "deep forest green with a warm sage accent",
                Mode = "dark",
                Accent = "#9CC97A",
                Corner = 1.0,
                Glass = 1.05,
                Sounds = "soft",
                Colors = Ink("EAF2E3", "CFE3BF", "#12E3F2D6", "#18E3F2D6", "#EB1A2318", true, new() { ["OnAccent"] = "#FF14200F" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#EE1C2619", "#EE121A10" },
                    ["AccentFill"] = new[] { "@135", "#FFB7DB97", "#FF86B862" }
                },
                Bar = new BarLook { Fill = new[] { "#E6151D12" }, Line = "#269CC97A" },
                Start = new StartLook { Scrim = "#A60C130A", Wash = new[] { "@radial", "#339CC97A", "#000C130A" } }
            });
            yield return ("vapor", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#FF71CE", "#B967FF", "#01CDFE", "#1A0F2E", "#05FFA1" },
                Name = "Vapor",
                Author = "elysium",
                Description = "neon pink and cyan on deep purple. aesthetic",
                Mode = "dark",
                Accent = "#FF71CE",
                Corner = 1.25,
                Glass = 0.95,
                Sounds = "retro",
                Colors = Ink("F7EEFF", "E3B8FF", "#14E3B8FF", "#1AE3B8FF", "#E6241040", true, new() { ["Danger"] = "#FFFF5C8A" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@150", "#EB1D0F36", "#EB2D1048", "#EB1A0F2E" },
                    ["AccentFill"] = new[] { "@90", "#FFFF71CE", "#FFB967FF", "#FF01CDFE" }
                },
                Bar = new BarLook { Fill = new[] { "@90", "#E62D1048", "#E61D0F36", "#E6102A44" }, Line = "#59FF71CE" },
                Start = new StartLook { Scrim = "#8C1A0F2E", Wash = new[] { "@135", "#40FF71CE", "#1AB967FF", "#3301CDFE" } }
            });
            yield return ("ember", new ThemePack
            {
                Cursor = "Windows Default",
                WallpaperColors = new[] { "#3A2418", "#1A110C", "#7A3E1F", "#C2702E", "#120B08" },
                Name = "Ember",
                Author = "elysium",
                Description = "coffee browns and a glowing orange. warm and slow",
                Mode = "dark",
                Accent = "#F0A35E",
                Corner = 1.2,
                Glass = 1.05,
                Speed = 1.1,
                Sounds = "soft",
                Colors = Ink("F5E8DC", "E8C9AE", "#12F5E1CF", "#18F5E1CF", "#EB2A1D16", true, new() { ["OnAccent"] = "#FF1A110C" }),
                Gradients = new()
                {
                    ["GlassTint"] = new[] { "@160", "#EE2A1E17", "#EE1C140F" },
                    ["AccentFill"] = new[] { "@135", "#FFF7C084", "#FFF0A35E", "#FFD9773A" }
                },
                Bar = new BarLook { Fill = new[] { "#E61E1510" }, Line = "#26F0A35E" },
                Start = new StartLook { Scrim = "#A61A110C", Wash = new[] { "@radial", "#40F0A35E", "#001A110C" } }
            });
        }
    }
}
