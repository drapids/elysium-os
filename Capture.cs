using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;
using DI = System.Drawing.Imaging;

namespace Elysium
{
    internal sealed class Shot
    {
        public byte[] Px = Array.Empty<byte>();
        public int W, H, Stride;
        public BitmapSource Image = null!;
        public Display Display = null!;
    }

    internal static class Capture
    {
        public static string Folder(Settings s)
        {
            string dir = s.CaptureFolder.Length > 0 ? s.CaptureFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static Shot? Grab(Display d)
        {
            var r = d.Bounds;
            if (r.Width <= 0 || r.Height <= 0) return null;
            try
            {
                using var bmp = new D.Bitmap(r.Width, r.Height, DI.PixelFormat.Format32bppRgb);
                using (var g = D.Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new D.Size(r.Width, r.Height), D.CopyPixelOperation.SourceCopy);
                var data = bmp.LockBits(new D.Rectangle(0, 0, r.Width, r.Height), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
                int stride = data.Stride;
                var px = new byte[stride * r.Height];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                bmp.UnlockBits(data);
                var src = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, px, stride);
                src.Freeze();
                return new Shot { Px = px, W = r.Width, H = r.Height, Stride = stride, Image = src, Display = d };
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("capture failed " + ex.Message);
                return null;
            }
        }

        public static Color At(Shot s, int x, int y)
        {
            x = Math.Clamp(x, 0, s.W - 1);
            y = Math.Clamp(y, 0, s.H - 1);
            int i = y * s.Stride + x * 4;
            return Color.FromRgb(s.Px[i + 2], s.Px[i + 1], s.Px[i]);
        }

        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Int32Rect Clamp(Shot s, Int32Rect r)
        {
            int x = Math.Clamp(r.X, 0, s.W - 1), y = Math.Clamp(r.Y, 0, s.H - 1);
            int w = Math.Clamp(r.Width, 1, s.W - x), h = Math.Clamp(r.Height, 1, s.H - y);
            return new Int32Rect(x, y, w, h);
        }

        public static BitmapSource Crop(Shot s, Int32Rect r)
        {
            var c = new CroppedBitmap(s.Image, Clamp(s, r));
            c.Freeze();
            return c;
        }

        static byte[] Bgra(BitmapSource src, out int w, out int h)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            w = conv.PixelWidth;
            h = conv.PixelHeight;
            var buf = new byte[w * h * 4];
            conv.CopyPixels(buf, w * 4, 0);
            return buf;
        }

        public static async Task<string> Ocr(BitmapSource region)
        {
            try
            {
                var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                             ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
                if (engine == null) return "";
                double k = 1;
                int big = Math.Max(region.PixelWidth, region.PixelHeight);
                if (region.PixelHeight < 80) k = 3;
                else if (region.PixelHeight < 240) k = 2;
                double max = Windows.Media.Ocr.OcrEngine.MaxImageDimension;
                if (big * k > max) k = max / big;
                BitmapSource src = region;
                if (Math.Abs(k - 1) > 0.01) src = new TransformedBitmap(region, new ScaleTransform(k, k));
                var buf = Bgra(src, out int w, out int h);
                var writer = new Windows.Storage.Streams.DataWriter();
                writer.WriteBytes(buf);
                var ibuf = writer.DetachBuffer();
                using var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(ibuf, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                var result = await engine.RecognizeAsync(sb);
                return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("ocr failed " + ex.Message);
                return "";
            }
        }

        public static string? Qr(BitmapSource region)
        {
            try
            {
                var buf = Bgra(region, out int w, out int h);
                var source = new ZXing.RGBLuminanceSource(buf, w, h, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
                var reader = new ZXing.BarcodeReaderGeneric();
                reader.Options.TryHarder = true;
                var result = reader.Decode(source);
                return result?.Text;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("qr read failed " + ex.Message);
                return null;
            }
        }

        public static List<(Color color, double share)> Palette(BitmapSource region, int k = 6)
        {
            var list = new List<(Color, double)>();
            BitmapSource src = region;
            int big = Math.Max(region.PixelWidth, region.PixelHeight);
            if (big > 96)
            {
                double s = 96.0 / big;
                src = new TransformedBitmap(region, new ScaleTransform(s, s));
            }
            var buf = Bgra(src, out int w, out int h);
            int n = w * h;
            if (n == 0) return list;
            var pts = new double[n, 3];
            for (int i = 0; i < n; i++)
            {
                pts[i, 0] = buf[i * 4 + 2];
                pts[i, 1] = buf[i * 4 + 1];
                pts[i, 2] = buf[i * 4];
            }
            k = Math.Min(k, n);
            var centers = new double[k, 3];
            var rng = new Random(7);
            int first = rng.Next(n);
            for (int c = 0; c < 3; c++) centers[0, c] = pts[first, c];
            var dist = new double[n];
            for (int ci = 1; ci < k; ci++)
            {
                double total = 0;
                for (int i = 0; i < n; i++)
                {
                    double best = double.MaxValue;
                    for (int j = 0; j < ci; j++) best = Math.Min(best, D2(pts, i, centers, j));
                    dist[i] = best;
                    total += best;
                }
                double pick = rng.NextDouble() * total;
                int chosen = 0;
                for (int i = 0; i < n; i++)
                {
                    pick -= dist[i];
                    if (pick <= 0)
                    {
                        chosen = i;
                        break;
                    }
                }
                for (int c = 0; c < 3; c++) centers[ci, c] = pts[chosen, c];
            }
            var assign = new int[n];
            var counts = new int[k];
            for (int iter = 0; iter < 14; iter++)
            {
                var sums = new double[k, 3];
                Array.Clear(counts);
                for (int i = 0; i < n; i++)
                {
                    int best = 0;
                    double bd = double.MaxValue;
                    for (int j = 0; j < k; j++)
                    {
                        double d = D2(pts, i, centers, j);
                        if (d < bd)
                        {
                            bd = d;
                            best = j;
                        }
                    }
                    assign[i] = best;
                    counts[best]++;
                    for (int c = 0; c < 3; c++) sums[best, c] += pts[i, c];
                }
                for (int j = 0; j < k; j++)
                {
                    if (counts[j] == 0) continue;
                    for (int c = 0; c < 3; c++) centers[j, c] = sums[j, c] / counts[j];
                }
            }
            var order = Enumerable.Range(0, k).Where(j => counts[j] > 0).OrderByDescending(j => counts[j]).ToList();
            foreach (var j in order)
            {
                var col = Color.FromRgb((byte)Math.Round(centers[j, 0]), (byte)Math.Round(centers[j, 1]), (byte)Math.Round(centers[j, 2]));
                bool dup = list.Any(x => Math.Abs(x.Item1.R - col.R) + Math.Abs(x.Item1.G - col.G) + Math.Abs(x.Item1.B - col.B) < 28);
                if (!dup) list.Add((col, counts[j] / (double)n));
            }
            return list;
        }

        static double D2(double[,] pts, int i, double[,] centers, int j)
        {
            double dr = pts[i, 0] - centers[j, 0], dg = pts[i, 1] - centers[j, 1], db = pts[i, 2] - centers[j, 2];
            return dr * dr * 0.3 + dg * dg * 0.59 + db * db * 0.11;
        }

        public static BitmapSource Swatches(IList<Color> colors, int size = 120)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                for (int i = 0; i < colors.Count; i++)
                    dc.DrawRectangle(new SolidColorBrush(colors[i]), null, new Rect(i * size, 0, size, size));
            }
            var rtb = new RenderTargetBitmap(Math.Max(1, colors.Count * size), size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static bool CopyImage(BitmapSource b)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    Clipboard.SetImage(b);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return false;
        }

        public static bool CopyText(string text)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return false;
        }

        public static string Save(Settings s, BitmapSource b, string? path = null)
        {
            path ??= Path.Combine(Folder(s), $"elysium {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(b));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
        }

        public static string? Ffmpeg() => Files.Ffmpeg();
    }

    internal sealed class Recording
    {
        readonly Process _p;
        public readonly string Output;
        public readonly bool Gif;
        public readonly DateTime Started = DateTime.Now;

        Recording(Process p, string output, bool gif)
        {
            _p = p;
            Output = output;
            Gif = gif;
        }

        public static Recording? Start(Settings s, Native.RECT r, bool gif)
        {
            var ff = Files.Ffmpeg();
            if (ff == null) return null;
            int w = r.Width / 2 * 2, h = r.Height / 2 * 2;
            if (w < 16 || h < 16) return null;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "elysium");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            string mp4 = Path.Combine(dir, $"elysium {stamp}.mp4");
            string args = $"-y -f gdigrab -framerate 30 -offset_x {r.Left} -offset_y {r.Top} -video_size {w}x{h} -draw_mouse 1 -i desktop -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p \"{mp4}\"";
            var psi = new ProcessStartInfo(ff, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
            try
            {
                var p = Process.Start(psi);
                if (p == null) return null;
                p.ErrorDataReceived += (_, _) => { };
                p.BeginErrorReadLine();
                return new Recording(p, mp4, gif);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("record failed " + ex.Message);
                return null;
            }
        }

        public async Task<string?> Stop()
        {
            try
            {
                await _p.StandardInput.WriteAsync("q");
                await _p.StandardInput.FlushAsync();
                _p.StandardInput.Close();
            }
            catch
            {
            }
            await Task.Run(() =>
            {
                if (!_p.WaitForExit(8000))
                {
                    try
                    {
                        _p.Kill();
                    }
                    catch
                    {
                    }
                }
            });
            if (!File.Exists(Output)) return null;
            if (!Gif) return Output;
            var ff = Files.Ffmpeg();
            if (ff == null) return Output;
            string gif = Path.ChangeExtension(Output, ".gif");
            string args = $"-y -i \"{Output}\" -vf \"fps=15,scale='min(720,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a\" \"{gif}\"";
            await Task.Run(() =>
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(ff, args) { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(120000);
                }
                catch
                {
                }
            });
            if (!File.Exists(gif)) return Output;
            try
            {
                File.Delete(Output);
            }
            catch
            {
            }
            return gif;
        }
    }
}
