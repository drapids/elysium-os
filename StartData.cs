using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Heft
{
    internal sealed class Pin
    {
        public string Name = "";
        public string Target = "";
        public bool Uwp;
        public int W = 2;
        public int H = 2;
        public int Col;
        public int Row;

        public string IconPath => Uwp ? "shell:AppsFolder\\" + Target : Target;

        public void Launch()
        {
            if (Uwp) Heft.Launch.Uwp(Target);
            else Heft.Launch.Run(Target);
        }
    }

    internal sealed class PinGroup
    {
        public string Name = "";
        public List<Pin> Pins = new();
    }

    internal sealed class StartData
    {
        static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft", "startlayout.xml");

        readonly AppIndex _apps;
        bool _loading;

        public List<PinGroup> Groups { get; private set; } = new();
        public List<Pin> Taskbar { get; private set; } = new();
        public string Weather { get; private set; } = "";
        public string WeatherPlace { get; private set; } = "";
        public string WeatherGlyph { get; private set; } = "\uE706";
        DateTime _weatherAt = DateTime.MinValue;

        public event Action? Updated;

        public StartData(AppIndex apps)
        {
            _apps = apps;
            Parse();
            LoadTaskbar();
        }

        public async void Refresh()
        {
            LoadTaskbar();
            if (!_loading && (!File.Exists(Cache) || (DateTime.Now - File.GetLastWriteTime(Cache)).TotalMinutes > 10))
            {
                _loading = true;
                try
                {
                    await Task.Run(Export);
                    Parse();
                    Updated?.Invoke();
                }
                catch
                {
                }
                _loading = false;
            }
            if ((DateTime.Now - _weatherAt).TotalMinutes > 20) await LoadWeather();
        }

        static void Export()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
            var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Export-StartLayout -Path '{Cache}'\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p?.WaitForExit(15000);
        }

        void Parse()
        {
            try
            {
                if (!File.Exists(Cache)) return;
                var doc = XDocument.Load(Cache);
                var groups = new List<PinGroup>();
                foreach (var g in doc.Descendants().Where(e => e.Name.LocalName == "Group"))
                {
                    var group = new PinGroup { Name = (string?)g.Attribute("Name") ?? "" };
                    foreach (var t in g.Descendants())
                    {
                        string kind = t.Name.LocalName;
                        if (kind != "Tile" && kind != "DesktopApplicationTile" && kind != "SecondaryTile") continue;
                        var pin = new Pin();
                        string size = (string?)t.Attribute("Size") ?? "2x2";
                        var parts = size.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        {
                            pin.W = w;
                            pin.H = h;
                        }
                        int.TryParse((string?)t.Attribute("Column"), out pin.Col);
                        int.TryParse((string?)t.Attribute("Row"), out pin.Row);

                        string? aumid = (string?)t.Attribute("AppUserModelID") ?? (string?)t.Attribute("DesktopApplicationID");
                        string? link = (string?)t.Attribute("DesktopApplicationLinkPath");
                        if (!string.IsNullOrEmpty(link))
                        {
                            pin.Target = Environment.ExpandEnvironmentVariables(link);
                            pin.Name = Path.GetFileNameWithoutExtension(pin.Target);
                        }
                        else if (!string.IsNullOrEmpty(aumid))
                        {
                            pin.Target = aumid;
                            pin.Uwp = true;
                            pin.Name = _apps.Apps.FirstOrDefault(a => a.Id.Equals(aumid, StringComparison.OrdinalIgnoreCase))?.Name ?? Pretty(aumid);
                        }
                        else continue;
                        group.Pins.Add(pin);
                    }
                    if (group.Pins.Count > 0)
                    {
                        group.Pins = group.Pins.OrderBy(p => p.Row).ThenBy(p => p.Col).ToList();
                        groups.Add(group);
                    }
                }
                Groups = groups;
            }
            catch
            {
            }
        }

        public void Rename()
        {
            foreach (var p in Groups.SelectMany(g => g.Pins).Where(p => p.Uwp))
            {
                var a = _apps.Apps.FirstOrDefault(x => x.Id.Equals(p.Target, StringComparison.OrdinalIgnoreCase));
                if (a != null) p.Name = a.Name;
            }
        }

        static string Pretty(string aumid)
        {
            string s = aumid;
            int bang = s.IndexOf('!');
            if (bang > 0) s = s[..bang];
            int us = s.IndexOf('_');
            if (us > 0) s = s[..us];
            var last = s.Split('.').LastOrDefault(x => x.Length > 1) ?? s;
            return last;
        }

        void LoadTaskbar()
        {
            var list = new List<Pin>();
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.lnk").OrderBy(File.GetCreationTime))
                        list.Add(new Pin { Name = Path.GetFileNameWithoutExtension(f), Target = f, W = 1, H = 1 });
                }
            }
            catch
            {
            }
            Taskbar = list;
        }

        async Task LoadWeather()
        {
            _weatherAt = DateTime.Now;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("heft");
                var json = await http.GetStringAsync("https://wttr.in/?format=j1");
                using var doc = JsonDocument.Parse(json);
                var cur = doc.RootElement.GetProperty("current_condition")[0];
                string temp = cur.GetProperty("temp_F").GetString() ?? "";
                string desc = cur.GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "";
                int code = int.TryParse(cur.GetProperty("weatherCode").GetString(), out int c) ? c : 113;
                string place = "";
                try
                {
                    place = doc.RootElement.GetProperty("nearest_area")[0].GetProperty("areaName")[0].GetProperty("value").GetString() ?? "";
                }
                catch
                {
                }
                Weather = $"{temp}°  {desc.ToLowerInvariant()}";
                WeatherPlace = place;
                WeatherGlyph = code switch
                {
                    113 => "\uE706",
                    116 => "\uE9C3",
                    119 or 122 => "\uE753",
                    143 or 248 or 260 => "\uE753",
                    >= 176 and <= 200 => "\uE9C6",
                    >= 227 and <= 230 => "\uE9C8",
                    >= 263 and <= 359 => "\uE9C4",
                    >= 362 and <= 395 => "\uE9C8",
                    _ => "\uE753"
                };
                Updated?.Invoke();
            }
            catch
            {
                _weatherAt = DateTime.Now.AddMinutes(-15);
            }
        }
    }
}
