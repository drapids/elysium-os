using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal static class Theme
    {
        public static bool IsDark { get; private set; } = true;
        public static event Action? Applied;
        public static double Corner { get; private set; } = 1;
        public static double Speed { get; private set; } = 1;
        public static Color Accent { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);

        static Settings? _cfg;
        static string _wallKey = "";
        static Color? _wallAccent;
        static bool _wallBusy;

        public static void Configure(Settings s) => _cfg = s;

        static Color PickAccent(bool dark)
        {
            var fallback = dark ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x00, 0x7A, 0xFF);
            var s = _cfg;
            if (s == null) return fallback;
            switch (s.AccentMode)
            {
                case "custom":
                    try
                    {
                        return (Color)ColorConverter.ConvertFromString(s.AccentHex);
                    }
                    catch
                    {
                        return fallback;
                    }
                case "system":
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                        if (key?.GetValue("AccentColor") is int abgr)
                        {
                            byte r = (byte)(abgr & 0xFF), g = (byte)((abgr >> 8) & 0xFF), b = (byte)((abgr >> 16) & 0xFF);
                            return Color.FromRgb(r, g, b);
                        }
                    }
                    catch
                    {
                    }
                    return fallback;
                case "wallpaper":
                    WallAccent();
                    return _wallAccent ?? fallback;
                default:
                    return fallback;
            }
        }

        static string WallpaperPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                string p = key?.GetValue("WallPaper") as string ?? "";
                if (p.Length > 0 && System.IO.File.Exists(p)) return p;
            }
            catch
            {
            }
            string t = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            return System.IO.File.Exists(t) ? t : "";
        }

        static void WallAccent()
        {
            string path = WallpaperPath();
            if (path.Length == 0) return;
            string key;
            try
            {
                key = path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return;
            }
            if (key == _wallKey || _wallBusy) return;
            _wallBusy = true;
            var ui = Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                Color? pick = null;
                try
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.DecodePixelWidth = 160;
                    img.UriSource = new Uri(path);
                    img.EndInit();
                    img.Freeze();
                    var colors = Capture.Palette(img, 8);
                    double best = -1;
                    foreach (var (c, share) in colors)
                    {
                        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
                        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                        double sat = max <= 0 ? 0 : (max - min) / max;
                        double score = sat * 0.75 + Math.Min(share * 4, 1) * 0.25 - Math.Abs(max - 0.75) * 0.3;
                        if (score > best)
                        {
                            best = score;
                            pick = c;
                        }
                    }
                    if (pick != null)
                    {
                        var p = pick.Value;
                        double m = Math.Max(p.R, Math.Max(p.G, p.B)) / 255.0;
                        double k = m < 0.55 ? 0.62 / Math.Max(0.05, m) : m > 0.92 ? 0.85 / m : 1;
                        pick = Color.FromRgb((byte)Math.Clamp(p.R * k, 0, 255), (byte)Math.Clamp(p.G * k, 0, 255), (byte)Math.Clamp(p.B * k, 0, 255));
                    }
                }
                catch
                {
                }
                ui.BeginInvoke(new Action(() =>
                {
                    _wallBusy = false;
                    _wallKey = key;
                    _wallAccent = pick;
                    if (_cfg != null) Apply(_cfg.Theme);
                }));
            });
        }

        static object? _defaultFont;

        public static void Apply(string mode)
        {
            _defaultFont ??= Application.Current.Resources["UiFont"];
            bool dark = mode == "dark" || (mode != "light" && SystemIsDark());
            IsDark = dark;
            var res = Application.Current.Resources;

            Color accent = PickAccent(dark);
            Accent = accent;
            Corner = Math.Clamp(_cfg?.CornerScale ?? 1, 0.3, 1.8);
            Speed = Math.Clamp(_cfg?.AnimScale ?? 1, 0.4, 2.5);
            double glass = Math.Clamp(_cfg?.GlassAlpha ?? 1, 0.4, 1.25);
            string font = _cfg?.UiFontName ?? "";
            if (font.Trim().Length > 0)
            {
                try
                {
                    res["UiFont"] = new FontFamily(font.Trim() + ", Segoe UI Variable Text, Segoe UI");
                }
                catch
                {
                }
            }
            else if (_defaultFont != null) res["UiFont"] = _defaultFont;

            Set(res, "Text", dark ? "#F2FFFFFF" : "#E6000000");
            Set(res, "SubText", dark ? "#A6FFFFFF" : "#8C000000");
            Set(res, "Faint", dark ? "#66FFFFFF" : "#59000000");

            Set(res, "GroupBg", dark ? "#12FFFFFF" : "#8CFFFFFF");
            Set(res, "GroupStroke", dark ? "#17FFFFFF" : "#0F000000");
            Set(res, "Separator", dark ? "#14FFFFFF" : "#14000000");
            Set(res, "RowHover", dark ? "#0FFFFFFF" : "#0A000000");

            Set(res, "PushBg", dark ? "#2EFFFFFF" : "#FFFFFFFF");
            Set(res, "PushHover", dark ? "#3DFFFFFF" : "#FFF7F7F7");
            Set(res, "PushPress", dark ? "#24FFFFFF" : "#FFEDEDED");
            Set(res, "PushStroke", dark ? "#1AFFFFFF" : "#1F000000");

            Set(res, "SegTrack", dark ? "#1FFFFFFF" : "#14000000");
            Set(res, "SegThumb", dark ? "#5C636366" : "#FFFFFFFF");

            Set(res, "SwitchOff", dark ? "#29FFFFFF" : "#24000000");
            Set(res, "SliderTrack", dark ? "#29FFFFFF" : "#1F000000");
            Set(res, "Knob", "#FFFFFFFF");

            Set(res, "FieldBg", dark ? "#14FFFFFF" : "#B3FFFFFF");
            Set(res, "FieldStroke", dark ? "#1AFFFFFF" : "#1F000000");

            Set(res, "PillBg", dark ? "#D92C2C2E" : "#E6F7F7F9");
            Set(res, "PillStroke", dark ? "#29FFFFFF" : "#26000000");
            Set(res, "TileBg", dark ? "#14FFFFFF" : "#66FFFFFF");

            byte tintA = (byte)Math.Clamp((dark ? 0xC8 : 0xD8) * glass, 40, 250);
            res["GlassTint"] = Freeze(new SolidColorBrush(dark ? Color.FromArgb(tintA, 0x1C, 0x1C, 0x1F) : Color.FromArgb(tintA, 0xF3, 0xF3, 0xF5)));
            Set(res, "Scrim", dark ? "#73000000" : "#40000000");
            Set(res, "OnAccent", "#FFFFFFFF");
            res["AccentFill"] = Freeze(new SolidColorBrush(accent));
            res["AccentHover"] = Freeze(new SolidColorBrush(Lighten(accent, dark ? 0.12 : -0.08)));
            res["AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)));

            var sheen = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x40 : (byte)0xCC, 255, 255, 255), 0));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x0A : (byte)0x33, 255, 255, 255), 0.5));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x14 : (byte)0x14, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0), 1));
            sheen.Freeze();
            res["Sheen"] = sheen;

            Applied?.Invoke();
        }

        public static void Glass(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            var src = HwndSource.FromHwnd(h);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;

            int dark = IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            Native.RoundCorners(h);

            if (Environment.OSVersion.Version.Build >= 22621)
            {
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(h, ref m);
                int backdrop = 3;
                Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            else
            {
                Native.LegacyAcrylic(h, IsDark ? 0xB01E1E1Eu : 0xB0F2F2F2u);
            }
        }

        static Color Lighten(Color c, double amount)
        {
            byte F(byte v) => (byte)Math.Clamp(v + amount * 255, 0, 255);
            return Color.FromRgb(F(c.R), F(c.G), F(c.B));
        }

        static void Set(ResourceDictionary res, string key, string hex)
        {
            res[key] = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));
        }

        static SolidColorBrush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        static bool SystemIsDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return true;
            }
        }
    }
}
