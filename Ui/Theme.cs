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

        public static void Apply(string mode)
        {
            bool dark = mode == "dark" || (mode != "light" && SystemIsDark());
            IsDark = dark;
            var res = Application.Current.Resources;

            Color accent = dark ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x00, 0x7A, 0xFF);

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
