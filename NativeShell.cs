using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Heft
{
    internal static partial class Native
    {
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public uint dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public int fVisible;
            public int fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx, cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint Low, High;
            public ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        public const int WH_MOUSE_LL = 14;
        public const int WM_QUIT = 0x0012;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_THEMECHANGED = 0x031A;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int HTCAPTION = 2;

        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_TOPMOST = 0x00000008L;

        public const int SW_HIDE = 0;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;

        public const uint SWP_NOMOVE = 0x0002;
        public const uint GUI_INMOVESIZE = 0x0002;
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;

        public const uint ABM_NEW = 0;
        public const uint ABM_REMOVE = 1;
        public const uint ABM_QUERYPOS = 2;
        public const uint ABM_SETPOS = 3;
        public const uint ABM_GETSTATE = 4;
        public const uint ABM_SETSTATE = 10;
        public const uint ABE_TOP = 1;
        public const int ABN_POSCHANGED = 1;
        public const int ABN_FULLSCREENAPP = 2;
        public const int ABS_AUTOHIDE = 1;
        public const int ABS_ALWAYSONTOP = 2;

        public const byte VK_MEDIA_NEXT = 0xB0;
        public const byte VK_MEDIA_PREV = 0xB1;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string? cls, string? title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? name);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        [DllImport("shell32.dll")]
        public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("dxva2.dll")]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll")]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        public static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

        [DllImport("dxva2.dll")]
        public static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

        [DllImport("dxva2.dll")]
        public static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [DllImport("user32.dll")]
        public static extern bool ClipCursor(ref RECT rect);

        [DllImport("user32.dll")]
        public static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll")]
        public static extern bool GetClipCursor(out RECT rect);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool RegisterShellHookWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool DeregisterShellHookWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        [DllImport("powrprof.dll")]
        public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint size);

        public static string? AssocApp(string ext)
        {
            try
            {
                uint size = 0;
                AssocQueryString(0x20, 4, ext, null, null, ref size);
                if (size == 0) return null;
                var sb = new StringBuilder((int)size);
                if (AssocQueryString(0x20, 4, ext, null, sb, ref size) != 0) return null;
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static bool ModifiersDown()
        {
            foreach (int vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            return false;
        }

        public static async System.Threading.Tasks.Task WaitForModifiersUp()
        {
            for (int i = 0; i < 60 && ModifiersDown(); i++) await System.Threading.Tasks.Task.Delay(20);
            await System.Threading.Tasks.Task.Delay(30);
        }

        public static void Combo(params byte[] keys)
        {
            foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2, UIntPtr.Zero);
        }

        public static void Focus(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            BringWindowToTop(hwnd);
        }

        public static void Tap(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
        }

        public static void SetAccent(IntPtr hwnd, int state, uint abgr)
        {
            var accent = new AccentPolicy { AccentState = state, AccentFlags = state == 0 ? 0 : 2, GradientColor = abgr };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static string Title(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static RECT PrimaryMonitor()
        {
            IntPtr mon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(mon, ref info);
            return info.rcMonitor;
        }

        public static RECT MonitorAt(int x, int y)
        {
            IntPtr mon = MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(mon, ref info);
            return info.rcMonitor;
        }

        public static bool FileOp(bool copy, IEnumerable<string> from, string toDir)
        {
            string src = string.Join("\0", from) + "\0\0";
            string dst = toDir + "\0\0";
            IntPtr pFrom = Marshal.StringToHGlobalUni(src);
            IntPtr pTo = Marshal.StringToHGlobalUni(dst);
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    wFunc = copy ? 2u : 1u,
                    pFrom = pFrom,
                    pTo = pTo,
                    fFlags = 0x0040 | 0x0200
                };
                return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(pFrom);
                Marshal.FreeHGlobal(pTo);
            }
        }
    }
}
