using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace Elysium
{
    internal static class Startup
    {
        const string TaskName = "elysium";
        const string OldName = "heft";

        public static bool CanSet
        {
            get
            {
                string? exe = Environment.ProcessPath;
                return exe != null && !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool Set(bool on) => Set(on, true);

        public static bool Set(bool on, bool early)
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return false;
            if (exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;

            RunKey(false, exe);
            Schtasks($"/Delete /TN \"{OldName}\" /F");
            if (!on || !early) Schtasks($"/Delete /TN \"{TaskName}\" /F");
            if (!on) return true;
            if (!early)
            {
                RunKey(true, exe);
                return true;
            }

            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>starts elysium right when you sign in</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author""><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--startup</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "")}</WorkingDirectory></Exec>
  </Actions>
</Task>";
            string file = Path.Combine(Path.GetTempPath(), "elysium-task.xml");
            try
            {
                File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
                bool ok = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
                if (!ok) RunKey(true, exe);
                return ok;
            }
            catch
            {
                RunKey(true, exe);
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }

        static bool Schtasks(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                if (p == null) return false;
                p.WaitForExit(8000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void RunKey(bool on, string exe)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                key.DeleteValue(OldName, false);
                if (on) key.SetValue("elysium", $"\"{exe}\" --startup");
                else key.DeleteValue("elysium", false);
            }
            catch
            {
            }
        }
    }

    internal static class SharePack
    {
        static readonly HashSet<string> Personal = new()
        {
            nameof(Settings.Usage), nameof(Settings.FileOpens), nameof(Settings.CommandHistory), nameof(Settings.BannerBackup),
            nameof(Settings.Pocketed), nameof(Settings.Reminders), nameof(Settings.ShelfItems), nameof(Settings.PinnedClips),
            nameof(Settings.Places), nameof(Settings.ShelfFolders), nameof(Settings.Pinned), nameof(Settings.Hidden),
            nameof(Settings.Collections), nameof(Settings.Bookmarks), nameof(Settings.DevicePriority), nameof(Settings.AppRoutes),
            nameof(Settings.HangarPinned), nameof(Settings.StartWithWindows), nameof(Settings.ActiveProfile), nameof(Settings.FocusActive),
            nameof(Settings.PinnedCommands), nameof(Settings.StartupPath), nameof(Settings.StackedAway), nameof(Settings.VersionFolders),
            nameof(Settings.StartEarly), nameof(Settings.ShelfPinned), nameof(Settings.CustomKeys), nameof(Settings.CaptureFolder), nameof(Settings.ExtSeeded), nameof(Settings.RecentlyClosed), nameof(Settings.ActiveMode), nameof(Settings.ScreenTimeDay), nameof(Settings.ScreenTimeToday), nameof(Settings.ThemesSeeded), nameof(Settings.StackPairs), nameof(Settings.SeenVersion), nameof(Settings.SetupSeen), nameof(Settings.WallpaperBefore), nameof(Settings.LookBefore), nameof(Settings.CursorsBefore), nameof(Settings.CalendarFeeds), nameof(Settings.UserName), nameof(Settings.ThemesSeedVersion), nameof(Settings.HiddenTrayApps)
        };

        static IEnumerable<PropertyInfo> Shared() => typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !Personal.Contains(p.Name));

        public static void Export(Settings s, string path)
        {
            var map = new Dictionary<string, object?>();
            foreach (var p in Shared()) map[p.Name] = p.GetValue(s);
            var profiles = s.AudioProfiles.Select(a => new AudioProfile { Name = a.Name, Volumes = a.Volumes, Muted = a.Muted, Hotkey = a.Hotkey }).ToList();
            map[nameof(Settings.AudioProfiles)] = profiles;
            var pack = new Dictionary<string, object?> { ["elysiumpack"] = 1, ["made"] = DateTime.Now, ["settings"] = map };
            File.WriteAllText(path, JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static int Import(Settings s, string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("settings", out var map)) throw new InvalidDataException("that isn't an elysium setup file");
            int n = 0;
            foreach (var p in Shared())
            {
                if (!map.TryGetProperty(p.Name, out var v)) continue;
                try
                {
                    var value = v.Deserialize(p.PropertyType);
                    if (value == null) continue;
                    p.SetValue(s, value);
                    n++;
                }
                catch
                {
                }
            }
            s.Touch();
            return n;
        }
    }
}
