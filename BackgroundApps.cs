using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Elysium
{
    internal sealed class BackgroundApp
    {
        public string Exe = "";
        public string Path = "";
        public string Name = "";
        public List<uint> Pids = new();
    }

    internal static class BackgroundApps
    {
        static readonly string[] Skip =
        {
            "helper", "crash", "update", "service", "svc", "host", "broker", "runtime", "agent", "daemon", "elevat",
            "install", "setup", "webview", "renderer", "gpu", "utility", "plugin", "monitor", "watchdog", "container",
            "telemetry", "report", "elysium", "heft", "audiodg", "conhost", "dllhost", "rundll", "searchapp", "searchhost",
            "textinput", "widgets", "lockapp", "ctfmon", "sihost", "taskhost", "explorer", "applicationframe", "systemsettings",
            "startmenu", "shellexperience", "phoneexperience", "securityhealth", "smartscreen", "msedge", "rtkaud", "igfx", "nvdisplay"
        };

        public static List<BackgroundApp> Scan(ICollection<string> hidden)
        {
            var list = new Dictionary<string, BackgroundApp>(StringComparer.OrdinalIgnoreCase);
            var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owners = new HashSet<uint>();
            try
            {
                foreach (var w in WinList.All()) visible.Add(w.Exe);
                Native.EnumWindows((h, _) =>
                {
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    owners.Add(pid);
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int session = Process.GetCurrentProcess().SessionId;
            uint me = (uint)Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    uint pid = (uint)p.Id;
                    if (pid == me || p.SessionId != session || !owners.Contains(pid)) continue;
                    string? path = Native.ProcessPath(pid);
                    if (path == null || path.StartsWith(windir, StringComparison.OrdinalIgnoreCase)) continue;
                    string exe = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (visible.Contains(exe) || hidden.Contains(exe)) continue;
                    string low = exe.ToLowerInvariant();
                    if (Skip.Any(low.Contains)) continue;
                    if (!list.TryGetValue(exe, out var app))
                    {
                        string name = exe;
                        try
                        {
                            var info = FileVersionInfo.GetVersionInfo(path);
                            if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription.Trim();
                            else if (!string.IsNullOrWhiteSpace(info.ProductName)) name = info.ProductName.Trim();
                            else continue;
                        }
                        catch
                        {
                            continue;
                        }
                        app = new BackgroundApp { Exe = exe, Path = path, Name = name };
                        list[exe] = app;
                    }
                    app.Pids.Add(pid);
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            return list.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void Open(BackgroundApp a)
        {
            if (File.Exists(a.Path)) Launch.Run(a.Path);
        }

        public static void Quit(BackgroundApp a)
        {
            foreach (var pid in a.Pids)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + pid) { CreateNoWindow = true, UseShellExecute = false });
                }
                catch
                {
                }
            }
        }

        public static void Kill(BackgroundApp a)
        {
            foreach (var pid in a.Pids)
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    p.Kill();
                }
                catch
                {
                }
            }
        }
    }
}
