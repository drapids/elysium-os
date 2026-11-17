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

        static readonly string[] NotGameExes = { "epicgameslauncher", "epicwebhelper", "riotclientservices", "riotclientux", "robloxstudiobeta", "eadesktop", "ubisoftconnect", "upc", "galaxyclient", "steamwebhelper", "unitycrashhandler64" };

        static readonly string[] GameExes = { "robloxplayerbeta", "minecraft.windows", "javaw" };

        static IntPtr _gameFg;
        static bool _gameHit;
        static readonly System.Collections.Generic.Dictionary<IntPtr, bool> Known = new();

        public static bool GameForeground()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg != _gameFg)
            {
                _gameFg = fg;
                _gameHit = IsGameWindow(fg);
            }
            return _gameHit || (Learned?.Invoke(fg) ?? false);
        }

        public static bool IsGameWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            if (Known.TryGetValue(h, out var known)) return known;
            bool hit = false;
            try
            {
                string path = (Native.ProcessPath(h) ?? "").ToLowerInvariant();
                string exe = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.Length > 0 && Array.IndexOf(NotGameExes, exe) < 0)
                {
                    if (exe == "javaw") hit = Native.Title(h).StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase);
                    else if (Array.IndexOf(GameExes, exe) >= 0) hit = true;
                    else foreach (var d in GameDirs)
                            if (path.Contains(d))
                            {
                                hit = true;
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
