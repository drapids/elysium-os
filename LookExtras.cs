using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Elysium
{
    internal static class Wallpaper
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SpiSet(uint action, uint param, string value, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SpiGet(uint action, uint param, StringBuilder value, uint winIni);

        public static string Current()
        {
            try
            {
                var sb = new StringBuilder(520);
                return SpiGet(0x73, (uint)sb.Capacity, sb, 0) ? sb.ToString() : "";
            }
            catch
            {
                return "";
            }
        }

        public static bool Set(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                return SpiSet(0x14, 0, path, 3);
            }
            catch
            {
                return false;
            }
        }

        public static string? Render(string id, string[]? stops, string dir)
        {
            try
            {
                var d = Displays.Primary(Displays.All());
                int w = Math.Max(1280, d.Bounds.Width), h = Math.Max(720, d.Bounds.Height);
                var colors = (stops ?? Array.Empty<string>()).Select(Parse).Where(c => c != null).Select(c => c!.Value).ToList();
                if (colors.Count == 0) return null;
                if (colors.Count == 1) colors.Add(colors[0]);
                var visual = new DrawingVisual();
                using (var g = visual.RenderOpen())
                {
                    var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
                    for (int i = 0; i < colors.Count; i++) brush.GradientStops.Add(new GradientStop(colors[i], i / (double)(colors.Count - 1)));
                    g.DrawRectangle(brush, null, new Rect(0, 0, w, h));
                    var seed = new Random(id.Aggregate(17, (a, ch) => a * 31 + ch));
                    int blobs = Math.Max(3, colors.Count + 2);
                    for (int i = 0; i < blobs; i++)
                    {
                        var c = colors[(i + 1) % colors.Count];
                        double cx = 0.1 + seed.NextDouble() * 0.8, cy = 0.1 + seed.NextDouble() * 0.8;
                        double r = 0.35 + seed.NextDouble() * 0.35;
                        var blob = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = r, RadiusY = r * w / h };
                        blob.GradientStops.Add(new GradientStop(Color.FromArgb(0x9A, c.R, c.G, c.B), 0));
                        blob.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, c.R, c.G, c.B), 0.45));
                        blob.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                        g.DrawRectangle(blob, null, new Rect(0, 0, w, h));
                    }
                    var glow = new RadialGradientBrush(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.72, 0.22), GradientOrigin = new Point(0.72, 0.22), RadiusX = 0.5, RadiusY = 0.6 };
                    g.DrawRectangle(glow, null, new Rect(0, 0, w, h));
                    var shade = new RadialGradientBrush { Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.85, RadiusY = 0.85 };
                    shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
                    shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, 0, 0, 0), 1));
                    g.DrawRectangle(shade, null, new Rect(0, 0, w, h));
                    g.DrawRectangle(Grain(), null, new Rect(0, 0, w, h));
                    if (id == "frutiger-aero")
                    {
                        var rng = new Random(7);
                        for (int i = 0; i < 26; i++)
                        {
                            double r = 18 + rng.NextDouble() * 70;
                            var c = new Point(rng.NextDouble() * w, h * 0.35 + rng.NextDouble() * h * 0.65);
                            var bubble = new RadialGradientBrush(Color.FromArgb(0x10, 255, 255, 255), Color.FromArgb(0x50, 255, 255, 255)) { GradientOrigin = new Point(0.35, 0.3) };
                            g.DrawEllipse(bubble, new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), 1.2), c, r, r);
                        }
                    }
                    if (id == "terminal")
                    {
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0x33, 0xFF, 0x66)), 1);
                        for (int x = 0; x < w; x += 48) g.DrawLine(pen, new Point(x, 0), new Point(x, h));
                        for (int y = 0; y < h; y += 48) g.DrawLine(pen, new Point(0, y), new Point(w, y));
                    }
                }
                var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "wallpaper.png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    enc.Save(fs);
                }
                return path;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("wallpaper render failed " + ex.Message);
                return null;
            }
        }

        static Brush Grain()
        {
            const int n = 192;
            var px = new byte[n * n * 4];
            var rng = new Random(3);
            for (int i = 0; i < n * n; i++)
            {
                byte v = (byte)(rng.Next(2) * 255);
                byte a = (byte)rng.Next(0, 9);
                byte pre = (byte)(v * a / 255);
                px[i * 4] = pre;
                px[i * 4 + 1] = pre;
                px[i * 4 + 2] = pre;
                px[i * 4 + 3] = a;
            }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze();
            return b;
        }

        static Color? Parse(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class CursorScheme
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiReload(uint action, uint param, IntPtr value, uint winIni);

        static readonly string[] Roles =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"
        };

        public static Dictionary<string, string> Read()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
                if (key == null) return map;
                map[""] = key.GetValue("")?.ToString() ?? "";
                foreach (var r in Roles) map[r] = key.GetValue(r)?.ToString() ?? "";
            }
            catch
            {
            }
            return map;
        }

        public static void Write(Dictionary<string, string> map)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true);
                if (key == null) return;
                foreach (var kv in map) key.SetValue(kv.Key, kv.Value, kv.Key.Length == 0 ? RegistryValueKind.String : RegistryValueKind.ExpandString);
                SpiReload(0x57, 0, IntPtr.Zero, 3);
            }
            catch
            {
            }
        }

        public static Dictionary<string, string>? Scheme(string name, string? packDir)
        {
            if (packDir != null)
            {
                var dir = System.IO.Path.Combine(packDir, "cursors");
                if (Directory.Exists(dir))
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = name };
                    foreach (var r in Roles)
                    {
                        var file = Directory.EnumerateFiles(dir).FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals(r, StringComparison.OrdinalIgnoreCase));
                        map[r] = file ?? "";
                    }
                    if (map.Values.Any(v => v.Length > 0 && v != name)) return map;
                }
            }
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes");
                if (key == null) return null;
                string? found = key.GetValueNames().FirstOrDefault(n => n.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (found == null || key.GetValue(found) is not string list) return null;
                var parts = list.Split(',');
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = found };
                for (int i = 0; i < Roles.Length; i++) map[Roles[i]] = i < parts.Length ? parts[i] : "";
                return map;
            }
            catch
            {
                return null;
            }
        }
    }
}
