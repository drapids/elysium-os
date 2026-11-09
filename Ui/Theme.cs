using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal static class Theme
    {
        public static bool IsDark { get; private set; } = true;

        public static void Apply(string mode)
        {
            bool dark = mode == "dark" || (mode != "light" && SystemIsDark());
            IsDark = dark;
            var (accent, accentHover) = Accent(dark);
            var res = Application.Current.Resources;

            Set(res, "PanelBg", dark ? "#F7202020" : "#F7F3F3F3");
            Set(res, "WheelBg", dark ? "#F2202020" : "#F5F6F6F6");
            Set(res, "CardBg", dark ? "#0DFFFFFF" : "#B3FFFFFF");
            Set(res, "CardHover", dark ? "#17FFFFFF" : "#E6FFFFFF");
            Set(res, "CardPress", dark ? "#08FFFFFF" : "#80FFFFFF");
            Set(res, "SegSelected", dark ? "#1FFFFFFF" : "#FFFFFFFF");
            Set(res, "FieldFocus", dark ? "#E61C1C1C" : "#FFFFFFFF");
            Set(res, "Stroke", dark ? "#17FFFFFF" : "#12000000");
            Set(res, "StrokeStrong", dark ? "#33FFFFFF" : "#29000000");
            Set(res, "Text", dark ? "#FFFFFFFF" : "#E4000000");
            Set(res, "SubText", dark ? "#C5FFFFFF" : "#9E000000");
            Set(res, "Faint", dark ? "#80FFFFFF" : "#70000000");
            Set(res, "TrackBg", dark ? "#8BFFFFFF" : "#72000000");
            Set(res, "ThumbOuter", dark ? "#FF454545" : "#FFFFFFFF");
            Set(res, "ToggleOffStroke", dark ? "#9AFFFFFF" : "#72000000");
            Set(res, "ToggleOffKnob", dark ? "#CDFFFFFF" : "#9E000000");
            Set(res, "OnAccent", dark ? "#FF000000" : "#FFFFFFFF");
            res["AccentFill"] = Freeze(new SolidColorBrush(accent));
            res["AccentHover"] = Freeze(new SolidColorBrush(accentHover));
            res["AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)));
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

        static (Color main, Color hover) Accent(bool dark)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
                if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
                {
                    Color At(int i) => Color.FromRgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
                    return dark ? (At(1), At(2)) : (At(4), At(3));
                }
            }
            catch
            {
            }
            return dark
                ? (Color.FromRgb(0x60, 0xCD, 0xFF), Color.FromRgb(0x99, 0xEB, 0xFF))
                : (Color.FromRgb(0x00, 0x5F, 0xB8), Color.FromRgb(0x00, 0x78, 0xD4));
        }
    }
}
