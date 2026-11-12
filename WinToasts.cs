using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Heft
{
    internal sealed class WinToasts : IDisposable
    {
        const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db");

        readonly Settings _s;
        readonly Action<Note> _post;
        readonly Thread _thread;
        readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
        volatile bool _alive = true;
        volatile bool _enabled;
        long _last = -1;
        DateTime _lastBannerSweep = DateTime.MinValue;

        public bool Working { get; private set; }
        public string Status { get; private set; } = "starting";

        public WinToasts(Settings s, Action<Note> post)
        {
            _s = s;
            _post = post;
            _enabled = s.TakeOverToasts;
            _thread = new Thread(Loop) { IsBackground = true, Name = "heft-wintoasts", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Sync()
        {
            bool want = _s.TakeOverToasts;
            if (want == _enabled) return;
            _enabled = want;
            if (!want) RestoreBanners();
            else _lastBannerSweep = DateTime.MinValue;
        }

        void Loop()
        {
            Thread.Sleep(1500);
            while (_alive)
            {
                try
                {
                    if (_enabled)
                    {
                        if ((DateTime.UtcNow - _lastBannerSweep).TotalSeconds > 20)
                        {
                            HideBanners();
                            _lastBannerSweep = DateTime.UtcNow;
                        }
                        Poll();
                    }
                }
                catch (Exception ex)
                {
                    Working = false;
                    Status = "can't read windows notifications: " + ex.Message;
                }
                Thread.Sleep(900);
            }
        }

        SqliteConnection Open()
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = DbPath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared }.ToString();
            var con = new SqliteConnection(cs);
            con.Open();
            return con;
        }

        void Poll()
        {
            if (!File.Exists(DbPath))
            {
                Status = "no notification database on this pc";
                return;
            }
            using var con = Open();
            if (_last < 0)
            {
                using var max = con.CreateCommand();
                max.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Notification";
                _last = Convert.ToInt64(max.ExecuteScalar());
                Working = true;
                Status = "on";
                return;
            }

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT n.Id, n.Payload, h.PrimaryId FROM Notification n
                                JOIN NotificationHandler h ON n.HandlerId = h.RecordId
                                WHERE n.Id > $last AND n.Type = 'toast' ORDER BY n.Id LIMIT 20";
            cmd.Parameters.AddWithValue("$last", _last);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long id = r.GetInt64(0);
                _last = Math.Max(_last, id);
                string xml = r.IsDBNull(1) ? "" : Payload(r.GetValue(1));
                string appId = r.IsDBNull(2) ? "" : r.GetString(2);
                var note = Parse(xml, appId);
                if (note == null) continue;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _post(note)));
            }
            Working = true;
            Status = "on";
        }

        static string Payload(object v)
        {
            if (v is byte[] bytes)
            {
                if (bytes.Length > 1 && bytes[1] == 0) return Encoding.Unicode.GetString(bytes);
                return Encoding.UTF8.GetString(bytes);
            }
            return v.ToString() ?? "";
        }

        Note? Parse(string xml, string appId)
        {
            if (xml.Length == 0) return null;
            try
            {
                var doc = XDocument.Parse(xml.Trim('\0'));
                var texts = doc.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value.Trim()).Where(t => t.Length > 0).ToList();
                if (texts.Count == 0) return null;
                string launch = doc.Root?.Attribute("launch")?.Value ?? "";
                string? open = null;
                if (Uri.TryCreate(launch, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")) open = launch;
                return new Note
                {
                    App = Name(appId),
                    AppId = appId,
                    Title = texts[0],
                    Body = string.Join("\n", texts.Skip(1)),
                    Source = "windows",
                    Glyph = "\uEA8F",
                    Open = open
                };
            }
            catch
            {
                return null;
            }
        }

        string Name(string appId)
        {
            if (_names.TryGetValue(appId, out var cached)) return cached;
            string name = "";
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{appId}");
                name = k?.GetValue("DisplayName") as string ?? "";
                if (name.StartsWith("@")) name = "";
            }
            catch
            {
            }
            if (name.Length == 0)
            {
                string id = appId;
                int bang = id.IndexOf('!');
                if (bang > 0) id = id[..bang];
                int us = id.IndexOf('_');
                if (us > 0) id = id[..us];
                if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
                string[] parts = id.Split('.', '\\', '/');
                name = parts.LastOrDefault(p => p.Length > 2 && !p.Equals("Windows", StringComparison.OrdinalIgnoreCase) && !p.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)) ?? id;
                if (id.Contains("Windows.SystemToast", StringComparison.OrdinalIgnoreCase)) name = "Windows";
            }
            if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..];
            _names[appId] = name;
            return name;
        }

        void HideBanners()
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey, true);
            if (root == null) return;
            bool changed = false;
            foreach (var app in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(app, true);
                    if (k == null) continue;
                    if (k.GetValue("Enabled") is int en && en == 0) continue;
                    var cur = k.GetValue("ShowBanner");
                    if (cur is int v && v == 0)
                    {
                        if (!_s.BannerBackup.ContainsKey(app)) continue;
                        continue;
                    }
                    lock (_s.BannerBackup) _s.BannerBackup[app] = cur is int old ? old : -1;
                    k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
                    changed = true;
                }
                catch
                {
                }
            }
            if (changed) System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(_s.Touch));
        }

        public void RestoreBanners() => Restore(_s);

        public static void Restore(Settings s)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(SettingsKey, true);
                if (root == null) return;
                Dictionary<string, int> backup;
                lock (s.BannerBackup) backup = new Dictionary<string, int>(s.BannerBackup);
                foreach (var kv in backup)
                {
                    try
                    {
                        using var k = root.OpenSubKey(kv.Key, true);
                        if (k == null) continue;
                        if (kv.Value < 0) k.DeleteValue("ShowBanner", false);
                        else k.SetValue("ShowBanner", kv.Value, RegistryValueKind.DWord);
                    }
                    catch
                    {
                    }
                }
                lock (s.BannerBackup) s.BannerBackup.Clear();
                s.SaveNow();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            _alive = false;
            if (_enabled) RestoreBanners();
        }
    }
}
