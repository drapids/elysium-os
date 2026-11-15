using System;
using System.Runtime.InteropServices;

namespace Elysium
{
    internal static class EdgeGuard
    {
        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public Native.POINT pt;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(ref CURSORINFO info);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        public static Func<bool>? Gaming;
        public static Func<bool>? OwnClip;

        static long _at;
        static int _last;

        public static bool Blocked() => Level() > 0;

        public static bool Hard() => Level() > 1;

        public static int Level()
        {
            long now = Environment.TickCount64;
            if (now - _at < 50) return _last;
            _at = now;
            _last = Check();
            return _last;
        }

        static int Check()
        {
            try
            {
                if (ForeignClip()) return 2;
                if (CursorHidden()) return Gaming?.Invoke() == true ? 2 : 1;
            }
            catch
            {
            }
            return 0;
        }

        public static bool CursorHidden()
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci)) return false;
            return (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero;
        }

        public static bool ForeignClip() => !(OwnClip?.Invoke() ?? false) && Clipped();

        static bool Clipped()
        {
            if (!Native.GetClipCursor(out var c)) return false;
            int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
            return c.Left > x || c.Top > y || c.Right < x + w || c.Bottom < y + h;
        }
    }
}
