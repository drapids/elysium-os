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
        public static Func<IntPtr, bool>? Learned;

        static readonly string[] GameDirs =
        {
            @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\xboxgames\", @"\roblox\versions\",
            @"\ubisoft game launcher\games\", @"\ea games\", @"\origin games\", @"\gog galaxy\games\", @"\gog games\",
            @"\windowsapps\microsoft.minecraft", @"\itch\apps\", @"\battlestate games\", @"\rockstar games\"
        };

        static readonly string[] NotGameExes = { "epicgameslauncher", "epicwebhelper", "riotclientservices", "riotclientux", "robloxstudiobeta", "eadesktop", "ubisoftconnect", "upc", "galaxyclient", "steamwebhelper", "unitycrashhandler64", "blender", "wallpaper32", "wallpaper64", "ui32", "obs64", "steamvr", "vrmonitor" };

        static readonly string[] GameExes = { "robloxplayerbeta", "minecraft.windows", "javaw" };

        static readonly System.Collections.Generic.Dictionary<IntPtr, int> Known = new();

        public static bool GameForeground()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            return IsGameWindow(fg) || (Learned?.Invoke(fg) ?? false);
        }

        public static bool IsGameWindow(IntPtr h)
        {
            int kind = Kind(h);
            if (kind == 2) return true;
            return kind == 1 && !Native.IsZoomed(h) && CursorLock.IsFullscreen(h, out _);
        }

        static int Kind(IntPtr h)
        {
            if (h == IntPtr.Zero) return 0;
            if (Known.TryGetValue(h, out var known)) return known;
            int hit = 0;
            try
            {
                string path = (Native.ProcessPath(h) ?? "").ToLowerInvariant();
                string exe = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.Length > 0 && Array.IndexOf(NotGameExes, exe) < 0)
                {
                    if (exe == "javaw") hit = Native.Title(h).StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                    else if (Array.IndexOf(GameExes, exe) >= 0) hit = 2;
                    else foreach (var d in GameDirs)
                            if (path.Contains(d))
                            {
                                hit = 1;
                                break;
                            }
                }
            }
            catch
            {
            }
            if (Known.Count > 200) Known.Clear();
            Known[h] = hit;
            return hit;
        }

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
                if (GameForeground()) return 2;
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
