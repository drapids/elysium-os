using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        public Action? IconsChanged;

        static readonly string[] AccentPresets = { "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#AC8E68", "#8E8E93" };

        void ReTheme()
        {
            Touch();
            Theme.Apply(_s.Theme);
        }

        void LookPage(Panel h)
        {
            h.Children.Add(Kit.Header("Look"));
            h.Children.Add(Kit.Group(
                Kit.Block("Light or dark", null, Kit.Segments(new[] { "Auto", "Dark", "Light" }, new[] { "system", "dark", "light" }, _s.Theme is "dark" or "light" ? _s.Theme : "system", v =>
                {
                    _s.Theme = v;
                    ReTheme();
                })),
                Kit.Block("Accent color", null, Kit.Segments(new[] { "heft blue", "Windows", "Wallpaper", "Pick" }, new[] { "default", "system", "wallpaper", "custom" }, _s.AccentMode, v =>
                {
                    _s.AccentMode = v;
                    ReTheme();
                    Rebuild();
                }))));

            if (_s.AccentMode == "custom")
            {
                var swatches = new WrapPanel { Margin = new Thickness(14, 10, 14, 6) };
                foreach (var hex in AccentPresets)
                {
                    string hx = hex;
                    var c = (Color)ColorConverter.ConvertFromString(hx);
                    bool on = _s.AccentHex.Equals(hx, StringComparison.OrdinalIgnoreCase);
                    var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, BorderThickness = new Thickness(on ? 3 : 0), BorderBrush = Brushes.White, ToolTip = hx };
                    dot.MouseLeftButtonUp += (_, _) =>
                    {
                        _s.AccentHex = hx;
                        ReTheme();
                        Rebuild();
                    };
                    swatches.Children.Add(dot);
                }
                var field = Kit.Field(_s.AccentHex, "#0A84FF", v =>
                {
                    string t = v.Trim();
                    if (!t.StartsWith('#')) t = "#" + t;
                    try
                    {
                        ColorConverter.ConvertFromString(t);
                        _s.AccentHex = t;
                        ReTheme();
                    }
                    catch
                    {
                    }
                });
                field.Width = 110;
                var custom = new StackPanel();
                custom.Children.Add(swatches);
                custom.Children.Add(Kit.Row("Hex", "or paste any color", field));
                h.Children.Add(Kit.Group(custom));
            }
            else if (_s.AccentMode == "wallpaper")
            {
                var cap = Kit.Caption("heft pulls the boldest color out of your wallpaper and updates when you change it");
                cap.Margin = new Thickness(14, 6, 14, 0);
                h.Children.Add(cap);
            }

            h.Children.Add(Kit.Header("Feel"));
            var font = Kit.Field(_s.UiFontName, "Segoe UI Variable", v =>
            {
                _s.UiFontName = v.Trim();
                ReTheme();
            });
            font.Width = 170;
            h.Children.Add(Kit.Group(
                Kit.SliderRow("Glass", 0.4, 1.25, _s.GlassAlpha, v => v < 0.7 ? "see-through" : v < 1.05 ? "normal" : "solid", v =>
                {
                    _s.GlassAlpha = v;
                    ReTheme();
                }),
                Kit.SliderRow("Corners", 0.3, 1.8, _s.CornerScale, v => v < 0.6 ? "sharp" : v < 1.2 ? "round" : "rounder", v =>
                {
                    _s.CornerScale = v;
                    ReTheme();
                }),
                Kit.SliderRow("Animations", 0.4, 2.5, _s.AnimScale, v => v < 0.75 ? "snappy" : v < 1.3 ? "normal" : "slow", v =>
                {
                    _s.AnimScale = v;
                    ReTheme();
                }),
                Kit.Row("Font", "any font you have installed. empty uses the default", font)));
            var note = Kit.Caption("corner changes show up as panels reopen");
            note.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(note);

            h.Children.Add(Kit.Header("Top bar color per monitor"));
            var tints = new List<UIElement>();
            foreach (var d in Displays.All())
            {
                string key = d.Key;
                string cur = _s.MonitorTints.TryGetValue(key, out var hx) ? hx : "";
                var f = Kit.Field(cur, "none", v =>
                {
                    string t = v.Trim();
                    if (t.Length == 0)
                    {
                        _s.MonitorTints.Remove(key);
                        ReTheme();
                        return;
                    }
                    if (!t.StartsWith('#')) t = "#" + t;
                    try
                    {
                        ColorConverter.ConvertFromString(t);
                        _s.MonitorTints[key] = t;
                        ReTheme();
                    }
                    catch
                    {
                    }
                });
                f.Width = 110;
                tints.Add(Kit.Row($"Monitor {d.Index + 1}{(d.Primary ? " (main)" : "")}", $"{d.Bounds.Width}×{d.Bounds.Height}", f));
            }
            h.Children.Add(Kit.Group(tints.ToArray()));

            h.Children.Add(Kit.Header("Icon pack"));
            var packs = new List<string> { "" };
            try
            {
                Directory.CreateDirectory(IconCache.PackRoot);
                packs.AddRange(Directory.EnumerateDirectories(IconCache.PackRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))!);
            }
            catch
            {
            }
            var ids = packs.Select(p => p ?? "").ToArray();
            var labels = ids.Select(p => p.Length == 0 ? "None" : p).ToArray();
            var openIcons = Kit.Push("Open folder", () =>
            {
                Directory.CreateDirectory(IconCache.PackRoot);
                Files.Open(IconCache.PackRoot);
            });
            h.Children.Add(Kit.Group(
                Kit.Block("Pack", null, Kit.Segments(labels, ids, ids.Contains(_s.IconPack) ? _s.IconPack : "", v =>
                {
                    _s.IconPack = v;
                    IconCache.Pack = v;
                    Touch();
                    IconsChanged?.Invoke();
                })),
                Kit.Row("Make your own", "a folder in here is a pack. drop pngs named after the app (chrome.png, code.png, spotify.png)", openIcons)));
        }
    }
}
