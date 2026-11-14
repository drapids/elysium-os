using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Elysium.Ui
{
    internal sealed partial class SettingsPages
    {
        string _themeNote = "";
        string _themeName = "";

        void ThemeGallery(Panel h)
        {
            h.Children.Add(Kit.Header("Themes"));
            if (_themeNote.Length > 0)
            {
                var note = Kit.Caption(_themeNote);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(14, 2, 14, 6);
                note.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                h.Children.Add(note);
                _themeNote = "";
            }
            var wrap = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
            wrap.Children.Add(ThemeCard(null));
            foreach (var p in Themes.All()) wrap.Children.Add(ThemeCard(p));
            h.Children.Add(wrap);

            var actions = new WrapPanel { Margin = new Thickness(14, 6, 14, 4) };
            var install = Kit.Push("Install a theme...", () => _dialog(() =>
            {
                var dlg = new OpenFileDialog { Title = "Install an elysium theme", Filter = "elysium theme|*.elysiumtheme;*.hefttheme;*.zip" };
                if (dlg.ShowDialog() != true) return;
                string? err = Themes.Install(dlg.FileName);
                _themeNote = err == null ? "installed. pick it below" : "couldn't install it: " + err;
                Rebuild();
            }));
            actions.Children.Add(install);
            var folder = Kit.Push("Open themes folder", () =>
            {
                Directory.CreateDirectory(Themes.Root);
                Files.Open(Themes.Root);
            });
            folder.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(folder);
            var active = Themes.Active;
            if (active != null)
            {
                var share = Kit.Push("Share this one", () =>
                {
                    string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Themes.Slug(active.Name) + ".elysiumtheme");
                    string? err = Themes.Export(active, dest);
                    _themeNote = err == null ? "saved " + Path.GetFileName(dest) + " to your desktop. send it to whoever" : "couldn't export it: " + err;
                    Rebuild();
                });
                share.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(share);
            }
            h.Children.Add(actions);

            var save = new DockPanel { Margin = new Thickness(14, 8, 14, 4) };
            var name = Kit.Field(_themeName, "name your look", v => _themeName = v.Trim());
            var keep = Kit.Push("Save current look", () =>
            {
                Keyboard.ClearFocus();
                string n = name.Text.Trim();
                if (n.Length == 0)
                {
                    _themeNote = "give it a name first";
                    Rebuild();
                    return;
                }
                string dir = Themes.SaveCurrent(_s, n);
                _themeName = "";
                _themeNote = "saved. edit theme.json in " + dir + " to go wild (gradients, the bar, the dock)";
                Rebuild();
            });
            keep.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(keep, Dock.Right);
            save.Children.Add(keep);
            save.Children.Add(name);
            h.Children.Add(save);
            h.Children.Add(Kit.Group(
                Kit.Row("Themes change my wallpaper", "picking the elysium look puts your old one back", Sw(_s.ThemeWallpaper, v => _s.ThemeWallpaper = v)),
                Kit.Row("Themes change my cursor", "uses the theme's cursor set, or a windows one", Sw(_s.ThemeCursor, v => _s.ThemeCursor = v))));
            GallerySection(h, "theme");
        }

        static string[]? Grad(ThemePack? p, string key)
        {
            if (p == null || p.Gradients == null) return null;
            return p.Gradients.TryGetValue(key, out var stops) ? stops : null;
        }

        FrameworkElement ThemeCard(ThemePack? p)
        {
            bool on = (Themes.Active?.Id ?? "") == (p?.Id ?? "");
            var box = new StackPanel { Width = 150 };
            var preview = new Grid { Height = 64, ClipToBounds = true };
            var bg = new Border { CornerRadius = Kit.Round(9) };
            bg.Background = Theme.Paint(Grad(p, "GlassTint"))
                ?? (p?.Mode == "light" ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF5)) : new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)));
            preview.Children.Add(bg);
            var bar = new Border { Height = 12, VerticalAlignment = VerticalAlignment.Top, CornerRadius = Kit.Round(9, 9, 0, 0) };
            bar.Background = Theme.Paint(p?.Bar?.Fill) ?? new SolidColorBrush(Color.FromArgb(0xC0, 0x16, 0x16, 0x18));
            preview.Children.Add(bar);
            if (p?.Bar?.Gloss == true)
            {
                var gloss = new Border { Height = 6, VerticalAlignment = VerticalAlignment.Top, CornerRadius = Kit.Round(9, 9, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)) };
                preview.Children.Add(gloss);
            }
            Color accent = Color.FromRgb(0x0A, 0x84, 0xFF);
            try
            {
                if (p != null && p.Accent.StartsWith('#')) accent = (Color)ColorConverter.ConvertFromString(p.Accent);
            }
            catch
            {
            }
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 10) };
            dots.Children.Add(new Border { Width = 34, Height = 14, CornerRadius = new CornerRadius(7), Background = Theme.Paint(Grad(p, "AccentFill")) ?? new SolidColorBrush(accent) });
            var dockTile = new Border { Width = 18, Height = 18, Margin = new Thickness(8, 0, 0, -2), CornerRadius = new CornerRadius(18 * (p?.Dock?.Round ?? 0.3)), Background = Theme.Paint(p?.Dock?.Tile) ?? new SolidColorBrush(Color.FromArgb(0xD9, 0x2C, 0x2C, 0x2E)), BorderThickness = new Thickness(1), BorderBrush = Theme.Paint(p?.Dock?.Stroke) ?? new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)) };
            dots.Children.Add(dockTile);
            preview.Children.Add(dots);
            box.Children.Add(preview);
            var title = Kit.Text(p?.Name ?? "elysium", 12.5, true);
            title.Margin = new Thickness(2, 7, 0, 0);
            box.Children.Add(title);
            var sub = Kit.Text(p == null ? "the default look" : p.Description.Length > 0 ? p.Description : p.Author, 11, false, "SubText");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.MaxHeight = 32;
            sub.Margin = new Thickness(2, 1, 0, 0);
            box.Children.Add(sub);
            var card = new Border
            {
                Child = box,
                Padding = new Thickness(8),
                Margin = new Thickness(4),
                CornerRadius = Kit.Round(12),
                BorderThickness = new Thickness(on ? 2 : 1),
                Cursor = Cursors.Hand,
                ToolTip = p == null ? "back to elysium's own look" : "click to use it"
            };
            card.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            card.SetResourceReference(Border.BorderBrushProperty, on ? "AccentFill" : "GroupStroke");
            card.MouseLeftButtonUp += (_, _) =>
            {
                Themes.Pick(_s, p);
                Rebuild();
            };
            return card;
        }
    }
}
