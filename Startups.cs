using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Elysium
{
    internal sealed class StartupItem
    {
        public string Name = "";
        public string Command = "";
        public string? Exe;
        public string Where = "";
        public string Publisher = "";
        public bool On = true;
        public RegistryHive Hive;
        public string Approved = "";
        public string Value = "";
    }

    internal static class Startups
    {
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> All()
        {
            var list = new List<StartupItem>();
            FromKey(list, RegistryHive.CurrentUser, Run, "Run", "you");
            FromKey(list, RegistryHive.LocalMachine, Run, "Run", "everyone");
            FromKey(list, RegistryHive.LocalMachine, Run32, "Run32", "everyone");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "startup folder");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "startup folder, everyone");
            foreach (var it in list)
            {
                if (it.Exe == null) continue;
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(it.Exe);
                    it.Publisher = (info.CompanyName ?? "").Trim();
                    string d = (info.FileDescription ?? "").Trim();
                    if (d.Length > 1 && d.Length < 60 && it.Name.Length > 24) it.Name = d;
                }
                catch
                {
                }
            }
            return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void FromKey(List<StartupItem> list, RegistryHive hive, string path, string approved, string who)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(path);
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    if (name.Length == 0) continue;
                    string cmd = key.GetValue(name)?.ToString() ?? "";
                    if (cmd.Length == 0) continue;
                    list.Add(new StartupItem
                    {
                        Name = name,
                        Command = cmd,
                        Exe = ExeFrom(cmd),
                        Where = "registry, " + who,
                        Hive = hive,
                        Approved = approved,
                        Value = name,
                        On = IsOn(hive, approved, name)
                    });
                }
            }
            catch
            {
            }
        }

        static void FromFolder(List<StartupItem> list, string dir, RegistryHive hive, string where)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    string file = Path.GetFileName(f);
                    if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string? exe = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShellLink.Target(f) : f;
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Command = f,
                        Exe = exe != null && File.Exists(exe) ? exe : null,
                        Where = where,
                        Hive = hive,
                        Approved = "StartupFolder",
                        Value = file,
                        On = IsOn(hive, "StartupFolder", file)
                    });
                }
            }
            catch
            {
            }
        }

        static string? ExeFrom(string cmd)
        {
            try
            {
                string c = Environment.ExpandEnvironmentVariables(cmd.Trim());
                string path;
                if (c.StartsWith('"'))
                {
                    int end = c.IndexOf('"', 1);
                    path = end > 1 ? c[1..end] : c.Trim('"');
                }
                else
                {
                    int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    path = exe > 0 ? c[..(exe + 4)] : c.Split(' ')[0];
                }
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        static bool IsOn(RegistryHive hive, string approved, string value)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(Approved + approved);
                if (key?.GetValue(value) is byte[] b && b.Length > 0) return (b[0] & 1) == 0;
            }
            catch
            {
            }
            return true;
        }

        public static string? Set(StartupItem it, bool on)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(it.Hive, RegistryView.Registry64);
                using var key = root.CreateSubKey(Approved + it.Approved, true);
                var data = new byte[12];
                data[0] = on ? (byte)0x02 : (byte)0x03;
                if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
                key.SetValue(it.Value, data, RegistryValueKind.Binary);
                it.On = on;
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "that one's set for every user, so windows wants admin to change it";
            }
            catch (System.Security.SecurityException)
            {
                return "that one's set for every user, so windows wants admin to change it";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
