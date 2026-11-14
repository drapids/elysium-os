using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Elysium.Ui
{
    internal sealed class WindowBorders
    {
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;
        const int DEFAULT = unchecked((int)0xFFFFFFFF);

        readonly HashSet<IntPtr> _done = new();
        bool _on;
        IntPtr _active;

        public WindowBorders()
        {
            WinEvents.Foreground += h =>
            {
                if (!_on) return;
                var old = _active;
                _active = h;
                if (old != IntPtr.Zero && old != h) Paint(old, false);
                Paint(h, true);
            };
            Theme.Applied += () =>
            {
                if (!_on) return;
                foreach (var h in _done.ToList()) Paint(h, h == _active);
            };
        }

        public void Sync(bool on)
        {
            if (on == _on) return;
            _on = on;
            if (on)
            {
                _active = Native.GetForegroundWindow();
                foreach (var w in WinList.All()) Paint(w.Hwnd, w.Hwnd == _active);
            }
            else
            {
                foreach (var h in _done.ToList()) Reset(h);
                _done.Clear();
            }
        }

        public void Opened(IntPtr h)
        {
            if (_on) Paint(h, h == Native.GetForegroundWindow());
        }

        static int Ref(Color c) => c.R | (c.G << 8) | (c.B << 16);

        void Paint(IntPtr h, bool active)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !WinList.IsApp(h)) return;
            bool dark = Theme.IsDark;
            var accent = Theme.Accent;
            var idle = dark ? Color.FromRgb(0x3A, 0x3A, 0x3E) : Color.FromRgb(0xC8, 0xC8, 0xCE);
            var cap = dark ? Color.FromRgb(0x1C, 0x1C, 0x1F) : Color.FromRgb(0xF3, 0xF3, 0xF5);
            var text = dark ? Color.FromRgb(0xF2, 0xF2, 0xF5) : Color.FromRgb(0x1C, 0x1C, 0x1F);
            Set(h, DWMWA_BORDER_COLOR, Ref(active ? accent : idle));
            Set(h, DWMWA_CAPTION_COLOR, Ref(cap));
            Set(h, DWMWA_TEXT_COLOR, Ref(text));
            _done.Add(h);
            if (_done.Count > 300) _done.RemoveWhere(x => !Native.IsWindow(x));
        }

        static void Reset(IntPtr h)
        {
            if (!Native.IsWindow(h)) return;
            Set(h, DWMWA_BORDER_COLOR, DEFAULT);
            Set(h, DWMWA_CAPTION_COLOR, DEFAULT);
            Set(h, DWMWA_TEXT_COLOR, DEFAULT);
        }

        static void Set(IntPtr h, int attr, int value)
        {
            try
            {
                Native.DwmSetWindowAttribute(h, attr, ref value, sizeof(int));
            }
            catch
            {
            }
        }

        public void Dispose() => Sync(false);
    }

    internal static class AppAccent
    {
        static readonly Dictionary<string, Color?> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static Color? For(string path, AppCatalog cat)
        {
            if (Cache.TryGetValue(path, out var known)) return known;
            Color? found = null;
            try
            {
                if (cat.Icon(path, 48, true) is BitmapSource src) found = Pick(src);
            }
            catch
            {
            }
            if (Cache.Count > 200) Cache.Clear();
            Cache[path] = found;
            return found;
        }

        static Color? Pick(BitmapSource src)
        {
            var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, weight = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] < 140) continue;
                double bb = px[i] / 255.0, gg = px[i + 1] / 255.0, rr = px[i + 2] / 255.0;
                double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                double sat = max <= 0 ? 0 : (max - min) / max;
                if (sat < 0.3 || max < 0.25) continue;
                double wt = sat * sat * max;
                r += rr * wt;
                g += gg * wt;
                b += bb * wt;
                weight += wt;
            }
            if (weight < 2) return null;
            r /= weight;
            g /= weight;
            b /= weight;
            double top = Math.Max(r, Math.Max(g, b));
            double lift = top < 0.75 ? 0.75 / Math.Max(0.01, top) : 1;
            return Color.FromRgb((byte)Math.Clamp(r * lift * 255, 0, 255), (byte)Math.Clamp(g * lift * 255, 0, 255), (byte)Math.Clamp(b * lift * 255, 0, 255));
        }
    }
}
