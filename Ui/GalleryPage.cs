using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal sealed class GalleryItem
    {
        public string Kind { get; set; } = "theme";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Download { get; set; } = "";
        public string[]? Colors { get; set; }
    }

    internal sealed partial class SettingsPages
    {
        static readonly HttpClient GalleryHttp = new() { Timeout = TimeSpan.FromSeconds(25) };
        List<GalleryItem>? _gallery;
        string _galleryNote = "";
        bool _galleryBusy;

        void GallerySection(Panel h, string kind)
        {
            h.Children.Add(Kit.Header(kind == "theme" ? "Get more themes" : "Get more extensions"));
            if (_galleryNote.Length > 0)
            {
                var note = Kit.Caption(_galleryNote);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(14, 2, 14, 6);
                note.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                h.Children.Add(note);
            }
            if (_gallery == null)
            {
                var load = Kit.Push(_galleryBusy ? "Loading..." : "Browse the gallery", () => _ = LoadGallery());
                load.HorizontalAlignment = HorizontalAlignment.Left;
                load.Margin = new Thickness(14, 4, 14, 4);
                h.Children.Add(load);
                var cap = Kit.Caption("pulls the list from github, free, no account");
                cap.Margin = new Thickness(14, 2, 14, 4);
                h.Children.Add(cap);
                return;
            }
            var items = _gallery.Where(g => g.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0)
            {
                var none = Kit.Caption("nothing in the gallery yet. be the first, see the readme for how to add yours");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(14, 2, 14, 4);
                h.Children.Add(none);
                return;
            }
            var rows = items.Select(it =>
            {
                var item = it;
                var get = Kit.Push("Install", () => _ = InstallGallery(item));
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                if (item.Colors is { Length: > 0 } && Theme.Paint(item.Colors) is Brush swatch)
                    right.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = Kit.Round(6), Background = swatch, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                right.Children.Add(get);
                return (UIElement)Kit.Row(item.Name, (item.Author.Length > 0 ? "by " + item.Author + "  ·  " : "") + item.Description, right);
            }).ToArray();
            h.Children.Add(Kit.Group(rows));
        }

        async Task LoadGallery()
        {
            if (_galleryBusy) return;
            _galleryBusy = true;
            Rebuild();
            try
            {
                string json = await GalleryHttp.GetStringAsync(_s.GalleryUrl);
                _gallery = JsonSerializer.Deserialize<List<GalleryItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? new List<GalleryItem>();
                _galleryNote = "";
            }
            catch (Exception ex)
            {
                _galleryNote = "couldn't reach the gallery: " + ex.Message;
            }
            _galleryBusy = false;
            Rebuild();
        }

        async Task InstallGallery(GalleryItem item)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "elysium-gallery-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
            try
            {
                var bytes = await GalleryHttp.GetByteArrayAsync(item.Download);
                await File.WriteAllBytesAsync(tmp, bytes);
                string? err = item.Kind.Equals("extension", StringComparison.OrdinalIgnoreCase)
                    ? Ext?.Install(tmp) ?? "extensions aren't loaded yet"
                    : Themes.Install(tmp);
                _galleryNote = err == null ? item.Name + " is installed" + (item.Kind == "theme" ? ". pick it up top" : ". flip it on below") : "couldn't install " + item.Name + ": " + err;
            }
            catch (Exception ex)
            {
                _galleryNote = "couldn't download " + item.Name + ": " + ex.Message;
            }
            finally
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                }
            }
            Rebuild();
        }
    }
}
