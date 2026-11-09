using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Heft
{
    internal static class ShellImage
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE
        {
            public int cx, cy;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr bitmap);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DIBSECTION
        {
            public BITMAP dsBm;
            public BITMAPINFOHEADER dsBmih;
            public uint dsBitfields0, dsBitfields1, dsBitfields2;
            public IntPtr dshSection;
            public uint dsOffset;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        static extern int GetObject(IntPtr handle, int size, ref DIBSECTION section);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        public static BitmapSource? Get(string path, int size, bool iconOnly = false)
        {
            IShellItemImageFactory? factory = null;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
                int flags = iconOnly ? 0x4 : 0x0;
                if (factory.GetImage(new SIZE { cx = size, cy = size }, flags, out IntPtr hbm) != 0 || hbm == IntPtr.Zero)
                    return null;
                try
                {
                    return FromHBitmap(hbm);
                }
                finally
                {
                    DeleteObject(hbm);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        static BitmapSource? FromHBitmap(IntPtr hbm)
        {
            var ds = new DIBSECTION();
            if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), ref ds) == 0) return null;

            int w = ds.dsBm.bmWidth;
            int h = ds.dsBm.bmHeight;
            int stride = ds.dsBm.bmWidthBytes;

            if (ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32 || w <= 0 || h <= 0)
            {
                var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                fallback.Freeze();
                return fallback;
            }

            var bytes = new byte[stride * h];
            Marshal.Copy(ds.dsBm.bmBits, bytes, 0, bytes.Length);

            if (ds.dsBmih.biHeight > 0)
            {
                var row = new byte[stride];
                for (int top = 0, bottom = h - 1; top < bottom; top++, bottom--)
                {
                    Buffer.BlockCopy(bytes, top * stride, row, 0, stride);
                    Buffer.BlockCopy(bytes, bottom * stride, bytes, top * stride, stride);
                    Buffer.BlockCopy(row, 0, bytes, bottom * stride, stride);
                }
            }

            bool alpha = false;
            for (int i = 3; i < bytes.Length; i += 4)
            {
                if (bytes[i] != 0)
                {
                    alpha = true;
                    break;
                }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, alpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, bytes, stride);
            bmp.Freeze();
            return bmp;
        }
    }
}
