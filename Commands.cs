using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Elysium
{
    internal sealed class Command
    {
        public string Id = "";
        public string Group = "elysium";
        public string Name = "";
        public string Glyph = "\uE945";
        public string Keywords = "";
        public bool Danger;
        public Action Run = () => { };
        public Func<string?>? State;
    }

    internal sealed class Commands
    {
        readonly Dictionary<string, Command> _map = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<Command> All => _map.Values;

        public Command Add(string group, string id, string name, string glyph, Action run, string keywords = "", bool danger = false, Func<string?>? state = null)
        {
            var c = new Command { Group = group, Id = id, Name = name, Glyph = glyph, Run = run, Keywords = keywords, Danger = danger, State = state };
            _map[id] = c;
            return c;
        }

        public Command? Get(string id) => _map.TryGetValue(id, out var c) ? c : null;

        public void Remove(string id) => _map.Remove(id);

        public bool Run(string id)
        {
            var c = Get(id);
            if (c == null) return false;
            try
            {
                c.Run();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log($"command {id} failed: {ex.Message}");
            }
            return true;
        }

        public async Task Script(string script)
        {
            foreach (var raw in script.Split(';'))
            {
                string step = raw.Trim();
                if (step.Length == 0) continue;
                try
                {
                    if (step.StartsWith("wait ", StringComparison.OrdinalIgnoreCase) && double.TryParse(step[5..], out double secs))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(secs, 0, 120)));
                    }
                    else if (step.StartsWith("&") || step.StartsWith("!"))
                    {
                        Run(step[1..].Trim());
                    }
                    else if (step.StartsWith(">"))
                    {
                        Shell(step[1..].Trim(), false);
                    }
                    else if (step.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
                    {
                        Clips.PasteNow(step[5..]);
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(step)) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log($"script step '{step}' failed: {ex.Message}");
                }
            }
        }

        public static void Shell(string command, bool keepOpen)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", (keepOpen ? "/k " : "/c ") + command)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                });
            }
            catch
            {
            }
        }
    }

    internal sealed class AppIndex
    {
        public sealed class App
        {
            public string Name = "";
            public string Id = "";
            public bool IsPath;
        }

        public List<App> Apps { get; private set; } = new();
        public bool Loaded { get; private set; }

        public void Load()
        {
            var list = new List<App>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type != null)
                {
                    dynamic shell = Activator.CreateInstance(type)!;
                    dynamic folder = shell.NameSpace("shell:AppsFolder");
                    foreach (dynamic item in folder.Items())
                    {
                        string name = item.Name;
                        string id = item.Path;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                        string low = name.ToLowerInvariant();
                        if (low.StartsWith("uninstall") || low.Contains("readme") || low.EndsWith(" help") || low.Contains("release notes")) continue;
                        if (!seen.Add(name)) continue;
                        list.Add(new App { Name = name, Id = id, IsPath = id.Contains(":\\") });
                    }
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("app index failed " + ex.Message);
            }

            if (list.Count == 0)
            {
                foreach (var root in new[]
                         {
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs")
                         })
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                        {
                            string name = Path.GetFileNameWithoutExtension(f);
                            if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) || !seen.Add(name)) continue;
                            list.Add(new App { Name = name, Id = f, IsPath = true });
                        }
                    }
                    catch
                    {
                    }
                }
            }
            Apps = list;
            Loaded = true;
        }

        public static string IconPath(App a) => a.IsPath ? a.Id : "shell:AppsFolder\\" + a.Id;

        public static void Launch(App a)
        {
            if (a.IsPath && File.Exists(a.Id)) Elysium.Launch.Run(a.Id);
            else Elysium.Launch.Uwp(a.Id);
        }
    }
}
