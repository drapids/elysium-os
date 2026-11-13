using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Heft
{
    internal static class VirtualDesktops
    {
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        static int _busy;

        public static (List<Guid> ids, int current) Read()
        {
            var ids = new List<Guid>();
            int current = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Root + @"\VirtualDesktops");
                if (key?.GetValue("VirtualDesktopIDs") is byte[] raw)
                    for (int i = 0; i + 16 <= raw.Length; i += 16) ids.Add(new Guid(new ReadOnlySpan<byte>(raw, i, 16)));
                byte[]? cur = key?.GetValue("CurrentVirtualDesktop") as byte[];
                if (cur == null)
                {
                    int session = Process.GetCurrentProcess().SessionId;
                    using var sk = Registry.CurrentUser.OpenSubKey($@"{Root}\SessionInfo\{session}\VirtualDesktops");
                    cur = sk?.GetValue("CurrentVirtualDesktop") as byte[];
                }
                if (cur != null && cur.Length == 16)
                {
                    int at = ids.IndexOf(new Guid(cur));
                    if (at >= 0) current = at;
                }
            }
            catch
            {
            }
            if (ids.Count == 0) ids.Add(Guid.Empty);
            return (ids, current);
        }

        public static string CurrentId()
        {
            var (ids, cur) = Read();
            return ids.Count > 1 ? ids[cur].ToString("N") : "";
        }

        public static void Go(int from, int to)
        {
            int steps = to - from;
            if (steps == 0) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            byte arrow = steps > 0 ? (byte)0x27 : (byte)0x25;
            int n = Math.Abs(steps);
            Task.Run(() =>
            {
                try
                {
                    Native.keybd_event(0xA2, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(0x5B, 0, 1, UIntPtr.Zero);
                    for (int i = 0; i < n; i++)
                    {
                        Native.keybd_event(arrow, 0, 1, UIntPtr.Zero);
                        Native.keybd_event(arrow, 0, 1 | 2, UIntPtr.Zero);
                        Thread.Sleep(n > 1 ? 140 : 10);
                    }
                    Native.keybd_event(0x5B, 0, 1 | 2, UIntPtr.Zero);
                    Native.keybd_event(0xA2, 0, 2, UIntPtr.Zero);
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }
    }
}
