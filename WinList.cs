using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Elysium
{
    internal sealed class AppWin
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string? Path;
        public uint Pid;
        public bool Min;
        public bool Hung;
        public Native.RECT Frame;

        public string Exe => Path == null ? "" : System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    internal static class WinList
    {
        static readonly uint Own = (uint)Environment.ProcessId;

        public static bool IsApp(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsCloaked(h)) return false;
            long ex = Native.ExStyle(h);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
            if ((Native.Style(h) & Native.WS_CHILD) != 0) return false;
            if (Native.GetWindow(h, 4) != IntPtr.Zero) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == Own) return false;
            string cls = Native.ClassName(h);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return false;
            return true;
        }

        public static List<AppWin> All(bool withMin = true)
        {
            var list = new List<AppWin>();
            Native.EnumWindows((h, _) =>
            {
                try
                {
                    if (!IsApp(h)) return true;
                    bool min = Native.IsIconic(h);
                    if (min && !withMin) return true;
                    var frame = Native.FrameBounds(h);
                    if (!min && frame.Left <= -10000) return true;
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    list.Add(new AppWin
                    {
                        Hwnd = h,
                        Title = Native.Title(h),
                        Path = Native.ProcessPath(pid),
                        Pid = pid,
                        Min = min,
                        Frame = frame,
                        Hung = Native.IsHungAppWindow(h)
                    });
                }
                catch
                {
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string ExeOf(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var t = ShellLink.Target(path);
                    if (!string.IsNullOrEmpty(t)) return System.IO.Path.GetFileNameWithoutExtension(t);
                }
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return "";
            }
        }

        public static void Switch(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.ForceForeground(h);
        }

        public static void Close(IntPtr h) => Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);

        public static bool Kill(IntPtr h)
        {
            try
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == 0 || pid == Own) return false;
                using var p = Process.GetProcessById((int)pid);
                string name = p.ProcessName.ToLowerInvariant();
                if (name is "explorer" or "csrss" or "winlogon" or "dwm" or "svchost" or "lsass" or "system") return false;
                p.Kill(true);
                return true;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("end task failed " + ex.Message);
                return false;
            }
        }
    }
}
