using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;
using DI = System.Drawing.Imaging;

namespace Elysium
{
    internal static class Backdrop
    {
        public static BitmapSource? Capture(Native.RECT r, int down = 9, int passes = 3)
        {
            if (r.Width <= 0 || r.Height <= 0) return null;
            try
            {
                using var full = new D.Bitmap(r.Width, r.Height, DI.PixelFormat.Format32bppRgb);
                using (var g = D.Graphics.FromImage(full))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new D.Size(r.Width, r.Height), D.CopyPixelOperation.SourceCopy);
                return Soften(full, down, passes);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("backdrop failed " + ex.Message);
                return null;
            }
        }

        public static BitmapSource? Shot(Native.RECT r)
        {
            if (r.Width <= 0 || r.Height <= 0) return null;
            try
            {
                using var full = new D.Bitmap(r.Width, r.Height, DI.PixelFormat.Format32bppRgb);
                using (var g = D.Graphics.FromImage(full))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new D.Size(r.Width, r.Height), D.CopyPixelOperation.SourceCopy);
                var data = full.LockBits(new D.Rectangle(0, 0, r.Width, r.Height), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
                try
                {
                    var shot = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * r.Height, data.Stride);
                    shot.Freeze();
                    return shot;
                }
                finally
                {
                    full.UnlockBits(data);
                }
            }
            catch
            {
                return null;
            }
        }

        public static (BitmapSource? sharp, BitmapSource? soft) Grab(Native.RECT r, int down = 9, int passes = 3)
        {
            if (r.Width <= 0 || r.Height <= 0) return (null, null);
            try
            {
                using var full = new D.Bitmap(r.Width, r.Height, DI.PixelFormat.Format32bppRgb);
                using (var g = D.Graphics.FromImage(full))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new D.Size(r.Width, r.Height), D.CopyPixelOperation.SourceCopy);
                var data = full.LockBits(new D.Rectangle(0, 0, r.Width, r.Height), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
                BitmapSource sharp;
                try
                {
                    sharp = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * r.Height, data.Stride);
                }
                finally
                {
                    full.UnlockBits(data);
                }
                sharp.Freeze();
                return (sharp, Soften(full, down, passes));
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("backdrop failed " + ex.Message);
                return (null, null);
            }
        }

        public static BitmapSource? Soften(D.Bitmap src, int down, int passes)
        {
            int w = Math.Max(8, src.Width / Math.Max(1, down));
            int h = Math.Max(8, src.Height / Math.Max(1, down));
            using var small = new D.Bitmap(w, h, DI.PixelFormat.Format32bppRgb);
            using (var g = D.Graphics.FromImage(small))
            {
                g.InterpolationMode = D.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = D.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new D.Rectangle(0, 0, w, h));
            }
            var data = small.LockBits(new D.Rectangle(0, 0, w, h), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
            int stride = data.Stride;
            var px = new byte[stride * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            small.UnlockBits(data);
            for (int i = 0; i < passes; i++) Blur(px, w, h, stride, 2);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, stride);
            bmp.Freeze();
            return bmp;
        }

        static void Blur(byte[] px, int w, int h, int stride, int radius)
        {
            var tmp = new byte[px.Length];
            int span = radius * 2 + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += px[row + Math.Clamp(k, 0, w - 1) * 4 + c];
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x * 4 + c] = (byte)(sum / span);
                        int outX = Math.Clamp(x - radius, 0, w - 1);
                        int inX = Math.Clamp(x + radius + 1, 0, w - 1);
                        sum += px[row + inX * 4 + c] - px[row + outX * 4 + c];
                    }
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * stride + x * 4 + c];
                    for (int y = 0; y < h; y++)
                    {
                        px[y * stride + x * 4 + c] = (byte)(sum / span);
                        int outY = Math.Clamp(y - radius, 0, h - 1);
                        int inY = Math.Clamp(y + radius + 1, 0, h - 1);
                        sum += tmp[inY * stride + x * 4 + c] - tmp[outY * stride + x * 4 + c];
                    }
                }
            }
        }
    }
}
