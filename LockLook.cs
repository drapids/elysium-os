using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Elysium.Ui;

namespace Elysium
{
    internal static class LockLook
    {
        public static async Task<string?> Apply()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "lockscreen.png");
                var d = Displays.Primary(Displays.All());
                int w = Math.Max(1280, d.Bounds.Width), h = Math.Max(720, d.Bounds.Height);
                var visual = new DrawingVisual();
                using (var g = visual.RenderOpen())
                {
                    string wall = Wallpaper.Current();
                    if (File.Exists(wall))
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.DecodePixelWidth = w;
                        img.UriSource = new Uri(wall);
                        img.EndInit();
                        img.Freeze();
                        double k = Math.Max(w / (double)img.PixelWidth, h / (double)img.PixelHeight);
                        double iw = img.PixelWidth * k, ih = img.PixelHeight * k;
                        g.DrawImage(img, new Rect((w - iw) / 2, (h - ih) / 2, iw, ih));
                    }
                    else g.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                    var a = Theme.Accent;
                    var tint = new LinearGradientBrush(Color.FromArgb(0x70, a.R, a.G, a.B), Color.FromArgb(Theme.IsDark ? (byte)0xB0 : (byte)0x60, 0, 0, 0), 90);
                    g.DrawRectangle(tint, null, new Rect(0, 0, w, h));
                }
                var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    enc.Save(fs);
                }
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask();
                await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file).AsTask();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
