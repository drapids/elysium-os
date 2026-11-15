using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Elysium
{
    internal sealed class ProcRow
    {
        public uint Pid;
        public uint Parent;
        public string Image = "";
        public string? Path;
        public double Cpu;
        public long Memory;
        public double Disk;
        public int Threads;
        public int Handles;
        public uint Session;
        public DateTime Started;
    }

    internal sealed class ProcGroup
    {
        public string Key = "";
        public string Name = "";
        public string Image = "";
        public string? Path;
        public bool App;
        public bool Hung;
        public string Title = "";
        public IntPtr Window;
        public double Cpu;
        public long Memory;
        public double Disk;
        public List<ProcRow> Rows = new();
    }

    internal sealed class ProcSnapshot
    {
        public List<ProcGroup> Groups = new();
        public double Cpu;
        public long MemUsed;
        public long MemTotal;
        public long Commit;
        public long CommitLimit;
        public double DiskRate;
        public int Processes;
        public int Threads;
        public int Handles;
    }

    internal sealed class Procs
    {
        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int needed);

        [StructLayout(LayoutKind.Sequential)]
        struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public int HandleCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, int size);

        sealed class Last
        {
            public long Cpu;
            public long Io;
            public string Image = "";
        }

        static readonly string[] Critical = { "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "memory compression", "secure system", "lsaiso", "fontdrvhost", "dwm" };

        readonly Dictionary<uint, Last> _last = new();
        readonly Dictionary<uint, (string image, string? path)> _paths = new();
        readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
        IntPtr _buf = IntPtr.Zero;
        int _size = 1 << 20;
        long _at;
        ulong _idle, _total;
        double _sysCpu;

        public static bool IsCritical(string image) => Critical.Contains(System.IO.Path.GetFileNameWithoutExtension(image).ToLowerInvariant());

        public ProcSnapshot Sample()
        {
            var snap = new ProcSnapshot();
            long now = Stopwatch.GetTimestamp();
            double secs = _at == 0 ? 0 : (now - _at) / (double)Stopwatch.Frequency;
            _at = now;
            snap.Cpu = SystemCpu();
            var rows = Read(secs);
            var wins = Windows();
            var seen = new HashSet<uint>(rows.Select(r => r.Pid));
            foreach (var k in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(k);
            foreach (var k in _paths.Keys.Where(k => !seen.Contains(k)).ToList()) _paths.Remove(k);
            uint own = (uint)Environment.ProcessId;
            foreach (var bunch in rows.Where(r => r.Pid != 0).GroupBy(r => r.Image.ToLowerInvariant()))
            {
                var list = bunch.OrderBy(r => r.Pid).ToList();
                var g = new ProcGroup { Key = bunch.Key, Image = list[0].Image, Rows = list };
                g.Path = list.Select(r => r.Path).FirstOrDefault(p => p != null);
                g.Name = Friendly(g.Image, g.Path);
                foreach (var r in list)
                {
                    g.Cpu += r.Cpu;
                    g.Memory += r.Memory;
                    g.Disk += r.Disk;
                    if (wins.TryGetValue(r.Pid, out var w) && !g.App)
                    {
                        g.App = true;
                        g.Window = w.h;
                        g.Title = w.title;
                        g.Hung = w.hung;
                    }
                }
                if (list.Any(r => r.Pid == own)) g.App = false;
                snap.Groups.Add(g);
                snap.DiskRate += g.Disk;
            }
            snap.Processes = rows.Count;
            snap.Threads = rows.Sum(r => r.Threads);
            snap.Handles = rows.Sum(r => r.Handles);
            try
            {
                if (GetPerformanceInfo(out var pi, Marshal.SizeOf<PERFORMANCE_INFORMATION>()))
                {
                    long page = pi.PageSize.ToInt64();
                    snap.MemTotal = pi.PhysicalTotal.ToInt64() * page;
                    snap.MemUsed = snap.MemTotal - pi.PhysicalAvailable.ToInt64() * page;
                    snap.Commit = pi.CommitTotal.ToInt64() * page;
                    snap.CommitLimit = pi.CommitLimit.ToInt64() * page;
                }
            }
            catch
            {
            }
            return snap;
        }

        double SystemCpu()
        {
            if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return _sysCpu;
            ulong i = idle.Value, t = kernel.Value + user.Value;
            ulong di = i - _idle, dt = t - _total;
            bool first = _total == 0;
            _idle = i;
            _total = t;
            if (first || dt == 0) return _sysCpu;
            _sysCpu = Math.Clamp(1.0 - (double)di / dt, 0, 1) * 100;
            return _sysCpu;
        }

        List<ProcRow> Read(double secs)
        {
            var list = new List<ProcRow>();
            if (IntPtr.Size != 8) return Fallback();
            int status = 0;
            for (int tries = 0; tries < 6; tries++)
            {
                if (_buf == IntPtr.Zero) _buf = Marshal.AllocHGlobal(_size);
                status = NtQuerySystemInformation(5, _buf, _size, out int needed);
                if (status != unchecked((int)0xC0000004)) break;
                Marshal.FreeHGlobal(_buf);
                _buf = IntPtr.Zero;
                _size = Math.Max(_size * 2, needed + (1 << 16));
            }
            if (status != 0 || _buf == IntPtr.Zero) return Fallback();
            int cores = Math.Max(1, Environment.ProcessorCount);
            long offset = 0;
            while (true)
            {
                IntPtr e = _buf + (int)offset;
                int next = Marshal.ReadInt32(e, 0x00);
                uint pid = (uint)Marshal.ReadIntPtr(e, 0x50).ToInt64();
                ushort nameLen = (ushort)Marshal.ReadInt16(e, 0x38);
                IntPtr namePtr = Marshal.ReadIntPtr(e, 0x40);
                string image = pid == 0 ? "System Idle Process" : nameLen > 0 && namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr, nameLen / 2) : pid == 4 ? "System" : "";
                long cpu = Marshal.ReadInt64(e, 0x28) + Marshal.ReadInt64(e, 0x30);
                long io = Marshal.ReadInt64(e, 0xE8) + Marshal.ReadInt64(e, 0xF0);
                var r = new ProcRow
                {
                    Pid = pid,
                    Parent = (uint)Marshal.ReadIntPtr(e, 0x58).ToInt64(),
                    Image = image,
                    Threads = Marshal.ReadInt32(e, 0x04),
                    Handles = Marshal.ReadInt32(e, 0x60),
                    Session = (uint)Marshal.ReadInt32(e, 0x64),
                    Memory = Marshal.ReadInt64(e, 0x08)
                };
                long created = Marshal.ReadInt64(e, 0x20);
                if (created > 0)
                {
                    try
                    {
                        r.Started = DateTime.FromFileTime(created);
                    }
                    catch
                    {
                    }
                }
                if (r.Memory <= 0) r.Memory = Marshal.ReadIntPtr(e, 0x90).ToInt64();
                if (_last.TryGetValue(pid, out var was) && was.Image == image && secs > 0)
                {
                    r.Cpu = Math.Clamp((cpu - was.Cpu) / (secs * 1e7 * cores) * 100, 0, 100);
                    r.Disk = Math.Max(0, (io - was.Io) / secs);
                }
                _last[pid] = new Last { Cpu = cpu, Io = io, Image = image };
                r.Path = PathOf(pid, image);
                list.Add(r);
                if (next == 0) break;
                offset += next;
            }
            return list;
        }

        List<ProcRow> Fallback()
        {
            var list = new List<ProcRow>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        var r = new ProcRow { Pid = (uint)p.Id, Image = p.ProcessName + ".exe", Memory = p.WorkingSet64, Threads = p.Threads.Count, Handles = p.HandleCount, Session = (uint)p.SessionId };
                        r.Path = PathOf(r.Pid, r.Image);
                        list.Add(r);
                    }
                    catch
                    {
                    }
                }
            }
            return list;
        }

        string? PathOf(uint pid, string image)
        {
            if (pid <= 4) return null;
            if (_paths.TryGetValue(pid, out var c) && c.image == image) return c.path;
            string? path = null;
            try
            {
                path = Native.ProcessPath(pid);
            }
            catch
            {
            }
            _paths[pid] = (image, path);
            return path;
        }

        string Friendly(string image, string? path)
        {
            string key = path ?? image;
            if (_names.TryGetValue(key, out var n)) return n;
            string name = System.IO.Path.GetFileNameWithoutExtension(image);
            if (image == "System Idle Process" || image == "System") name = image;
            if (path != null)
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    string d = (info.FileDescription ?? "").Trim();
                    if (d.Length > 1 && d.Length < 70 && !d.Equals("Application", StringComparison.OrdinalIgnoreCase)) name = d;
                    else if ((info.ProductName ?? "").Trim().Length > 1) name = info.ProductName!.Trim();
                }
                catch
                {
                }
            }
            if (name.Length == 0) name = image;
            _names[key] = name;
            return name;
        }

        static Dictionary<uint, (IntPtr h, string title, bool hung)> Windows()
        {
            var map = new Dictionary<uint, (IntPtr, string, bool)>();
            try
            {
                foreach (var w in WinList.All())
                    if (!map.ContainsKey(w.Pid)) map[w.Pid] = (w.Hwnd, w.Title, w.Hung);
            }
            catch
            {
            }
            return map;
        }

        public void Dispose()
        {
            if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
            _buf = IntPtr.Zero;
        }

        public static string? End(IEnumerable<uint> pids)
        {
            uint own = (uint)Environment.ProcessId;
            string? err = null;
            int done = 0;
            foreach (var pid in pids)
            {
                if (pid == own)
                {
                    err = "that's elysium itself. quit it from the tray instead";
                    continue;
                }
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    if (IsCritical(p.ProcessName))
                    {
                        err = "windows needs that one to stay alive";
                        continue;
                    }
                    p.Kill();
                    done++;
                }
                catch (ArgumentException)
                {
                    done++;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    err ??= "windows won't let elysium end that one. it's probably running as admin";
                }
                catch (InvalidOperationException)
                {
                    done++;
                }
                catch (Exception ex)
                {
                    err ??= ex.Message;
                }
            }
            return done > 0 && err != null && !err.StartsWith("that's") ? null : err;
        }

        public static string? Priority(IEnumerable<uint> pids, ProcessPriorityClass cls)
        {
            string? err = null;
            foreach (var pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    p.PriorityClass = cls;
                }
                catch
                {
                    err = "couldn't change priority on some of them";
                }
            }
            return err;
        }

        public static void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    using (p)
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                        catch
                        {
                        }
                    }
                }
                System.Threading.Thread.Sleep(400);
                if (Process.GetProcessesByName("explorer").Length == 0) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("restart explorer failed " + ex.Message);
            }
        }
    }
}
