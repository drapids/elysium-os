using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Heft
{
    public sealed class WheelItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "app";
    }

    public sealed class PocketEntry
    {
        public long Hwnd { get; set; }
        public uint Pid { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Max { get; set; }
        public string Title { get; set; } = "";
    }

    public sealed class HistoryEntry
    {
        public string Key { get; set; } = "";
        public string Query { get; set; } = "";
        public double Score { get; set; }
        public DateTime Last { get; set; }
    }

    public sealed class CustomKey
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string Keys { get; set; } = "";
        public string Kind { get; set; } = "app";
        public string Target { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public sealed class CustomCommand
    {
        public string Alias { get; set; } = "";
        public string Run { get; set; } = "";
    }

    public sealed class Settings
    {
        public bool Enabled { get; set; } = true;
        public double Throw { get; set; } = 1.0;
        public double Friction { get; set; } = 0.35;
        public double Bounce { get; set; } = 0.55;
        public double Gravity { get; set; } = 0.0;
        public bool Stacking { get; set; } = true;
        public bool ShakeToMinimize { get; set; } = true;
        public double ShakeSensitivity { get; set; } = 0.5;
        public bool CrossMonitors { get; set; } = true;
        public string IgnoredApps { get; set; } = "";
        public string Theme { get; set; } = "system";
        public bool StartWithWindows { get; set; } = false;
        public bool ShowHandle { get; set; } = true;
        public string Preset { get; set; } = "chill";

        public string DockSide { get; set; } = "right";
        public double IconSize { get; set; } = 56;
        public double Magnify { get; set; } = 0.6;
        public List<string> Places { get; set; } = new();
        public bool ShowDrives { get; set; } = true;
        public string FolderSort { get; set; } = "name";
        public bool FoldersFirst { get; set; } = true;
        public bool ShowHidden { get; set; } = false;
        public bool TypeToFilter { get; set; } = true;

        public bool AutoStart { get; set; } = true;
        public bool InstantStart { get; set; } = true;

        public bool BarEnabled { get; set; } = true;
        public bool BarAutoHide { get; set; } = false;
        public double BarHeight { get; set; } = 30;
        public double BarTint { get; set; } = 0.45;
        public bool BarBlur { get; set; } = true;
        public bool BarApp { get; set; } = true;
        public bool BarMedia { get; set; } = true;
        public bool BarDate { get; set; } = true;
        public bool BarSeconds { get; set; } = false;
        public bool Bar24h { get; set; } = false;
        public bool BarStats { get; set; } = true;
        public bool BarVolume { get; set; } = true;
        public bool BarBattery { get; set; } = true;

        public string TaskbarStyle { get; set; } = "blur";
        public double TaskbarTint { get; set; } = 0.25;
        public string TaskbarHide { get; set; } = "off";
        public bool TaskbarCenter { get; set; } = false;
        public bool HideStart { get; set; } = false;
        public bool HideTray { get; set; } = false;

        public bool HideDesktop { get; set; } = true;
        public double PeekSeconds { get; set; } = 8;

        public bool GestureVolume { get; set; } = true;
        public bool GestureBrightness { get; set; } = true;
        public bool GestureMedia { get; set; } = true;
        public bool TitleOpacity { get; set; } = true;
        public bool TitlePin { get; set; } = true;
        public bool WinKeyLauncher { get; set; } = true;
        public Dictionary<string, string> HotCorners { get; set; } = new() { ["tl"] = "overview", ["tr"] = "none", ["bl"] = "start", ["br"] = "none" };

        public List<string> ShelfItems { get; set; } = new();
        public Dictionary<string, int> Bookmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PocketEntry> Pocketed { get; set; } = new();

        public string DockScreens { get; set; } = "outer";
        public string BarScreens { get; set; } = "all";
        public bool AutoLockFullscreen { get; set; } = false;
        public Dictionary<string, string> Keys { get; set; } = new();

        public bool TakeOverToasts { get; set; } = true;
        public double ToastSeconds { get; set; } = 6;
        public Dictionary<string, string> NoteRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BannerBackup { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Reminder> Reminders { get; set; } = new();

        public bool FocusActive { get; set; } = false;
        public string FocusMute { get; set; } = "discord, spotify";
        public string FocusAllow { get; set; } = "";
        public string FocusProfile { get; set; } = "";

        public List<AudioProfile> AudioProfiles { get; set; } = new();
        public string ActiveProfile { get; set; } = "";
        public bool AudioAutoSwitch { get; set; } = true;
        public List<string> DevicePriority { get; set; } = new();
        public Dictionary<string, string> AppRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ClipHistory { get; set; } = true;
        public int ClipMax { get; set; } = 40;
        public List<string> PinnedClips { get; set; } = new();

        public Dictionary<string, List<string>> Collections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CategoryColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> FileOpens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string ShelfSort { get; set; } = "modified";
        public List<string> ShelfFolders { get; set; } = new();

        public List<HistoryEntry> CommandHistory { get; set; } = new();
        public List<string> PinnedCommands { get; set; } = new();
        public List<CustomCommand> CustomCommands { get; set; } = new();
        public string SearchEngine { get; set; } = "https://www.google.com/search?q=";

        public bool SoundsOn { get; set; } = false;
        public double SoundVolume { get; set; } = 0.5;
        public string SoundPack { get; set; } = "glass";
        public bool SoundDuck { get; set; } = true;
        public Dictionary<string, bool> SoundEvents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool StartEarly { get; set; } = true;
        public string StartupPath { get; set; } = "";

        public bool ShelfPinned { get; set; } = false;
        public bool DropSlit { get; set; } = true;
        public bool ShelfTakeOut { get; set; } = true;

        public string CaptureMode { get; set; } = "shot";
        public string CaptureFolder { get; set; } = "";
        public List<string> Palette { get; set; } = new();

        public bool Lerp { get; set; } = false;
        public double LerpLag { get; set; } = 0.45;
        public double LerpWobble { get; set; } = 0.25;
        public bool LerpSnap { get; set; } = true;
        public bool WindowStacks { get; set; } = true;
        public List<PocketEntry> StackedAway { get; set; } = new();
        public double PipSize { get; set; } = 0.24;

        public bool PreviewSpace { get; set; } = true;
        public bool AutoSortDownloads { get; set; } = false;
        public bool VersionsOn { get; set; } = false;
        public List<string> VersionFolders { get; set; } = new();
        public int VersionKeep { get; set; } = 20;
        public int ShareMinutes { get; set; } = 10;

        public List<CustomKey> CustomKeys { get; set; } = new();
        public Dictionary<string, string> Remaps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<WheelItem> Pinned { get; set; } = new();
        public List<string> Hidden { get; set; } = new();
        public string ProjectNames { get; set; } = "hangar29, underlay, veneer, driftwood, substrate, gnatty, hyperboi, handoff, wintd, tox-shelf";
        public bool HangarPinned { get; set; } = false;
        public Dictionary<string, double> Usage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action? Changed;

        static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft");
        static readonly string FilePath = System.IO.Path.Combine(Dir, "settings.json");
        static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        DispatcherTimer? _saveTimer;

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
                    if (s != null)
                    {
                        s.Usage = new Dictionary<string, double>(s.Usage, StringComparer.OrdinalIgnoreCase);
                        s.Bookmarks = new Dictionary<string, int>(s.Bookmarks, StringComparer.OrdinalIgnoreCase);
                        s.NoteRules = new Dictionary<string, string>(s.NoteRules, StringComparer.OrdinalIgnoreCase);
                        s.BannerBackup = new Dictionary<string, int>(s.BannerBackup, StringComparer.OrdinalIgnoreCase);
                        s.AppRoutes = new Dictionary<string, string>(s.AppRoutes, StringComparer.OrdinalIgnoreCase);
                        s.Collections = new Dictionary<string, List<string>>(s.Collections, StringComparer.OrdinalIgnoreCase);
                        s.CategoryColors = new Dictionary<string, string>(s.CategoryColors, StringComparer.OrdinalIgnoreCase);
                        s.FileOpens = new Dictionary<string, double>(s.FileOpens, StringComparer.OrdinalIgnoreCase);
                        s.SoundEvents = new Dictionary<string, bool>(s.SoundEvents ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
                        s.StackedAway ??= new List<PocketEntry>();
                        s.VersionFolders ??= new List<string>();
                        s.CustomKeys ??= new List<CustomKey>();
                        s.Palette ??= new List<string>();
                        s.Remaps = new Dictionary<string, string>(s.Remaps ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        return s;
                    }
                }
            }
            catch
            {
            }
            return new Settings();
        }

        public void Touch()
        {
            Changed?.Invoke();
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveTimer.Tick += (_, _) =>
                {
                    _saveTimer.Stop();
                    SaveNow();
                };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch
            {
            }
        }

        public void ApplyPreset(string name)
        {
            Preset = name;
            switch (name)
            {
                case "bouncy":
                    Throw = 1.25; Friction = 0.18; Bounce = 0.8; Gravity = 0.0;
                    break;
                case "chaos":
                    Throw = 1.7; Friction = 0.08; Bounce = 0.75; Gravity = 0.6;
                    break;
                default:
                    Preset = "chill";
                    Throw = 0.85; Friction = 0.5; Bounce = 0.35; Gravity = 0.0;
                    break;
            }
            Touch();
        }

        public HashSet<string> IgnoredSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in IgnoredApps.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }
    }
}
