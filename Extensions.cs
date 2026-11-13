using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Jint;
using Jint.Native;

namespace Heft
{
    internal sealed class ExtManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Main { get; set; } = "main.js";
        public List<string> Permissions { get; set; } = new();
    }

    internal sealed class ExtBarItem
    {
        public string Ext = "";
        public string Text = "";
        public string Glyph = "";
        public Action? Click;
    }

    internal sealed class ExtWidget
    {
        public string Ext = "";
        public string Title = "";
        public string Body = "";
    }

    internal sealed class Extension
    {
        public string Dir = "";
        public ExtManifest Manifest = new();
        public bool Running;
        public string Error = "";
        public Engine? Engine;
        public readonly List<string> Commands = new();
        public readonly List<DispatcherTimer> Timers = new();
        public readonly Dictionary<string, List<JsValue>> Handlers = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string name, JsValue fn)> Drops = new();
        public readonly List<(string spec, JsValue fn)> Keys = new();
        public ExtBarItem? Bar;
        public ExtWidget? Widget;
        public string Id => Manifest.Id;
        public bool Can(string perm) => Manifest.Permissions.Any(p => p.Equals(perm, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class ExtHost : IDisposable
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft", "extensions");

        public static readonly (string id, string label)[] PermissionNames =
        {
            ("notify", "show notifications"),
            ("commands", "add launcher commands"),
            ("keys", "add shortcuts"),
            ("bar", "put something in the top bar"),
            ("widgets", "add a Start widget"),
            ("drop", "add shelf drop actions"),
            ("watch", "see which app you're in, what's playing, downloads"),
            ("media", "read and control what's playing"),
            ("net", "use the internet"),
            ("run", "open apps, files and links"),
            ("clipboard", "copy to your clipboard")
        };

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

        readonly Settings _s;
        readonly Commands _cmds;
        readonly Notifier _notes;
        readonly NowPlaying _np;
        readonly Dispatcher _ui;
        readonly Dictionary<string, Extension> _all = new(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(450) };
        readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer _minute = new() { Interval = TimeSpan.FromSeconds(15) };
        FileSystemWatcher? _watch;
        int _lastMinute = -1;

        public event Action? Changed;
        public event Action? KeysUpdated;
        public event Action? BarChanged;

        public bool WantsMedia => _all.Values.Any(e => e.Running && e.Can("media"));

        internal void KeysChanged() => KeysUpdated?.Invoke();

        internal void DropsChanged() => Changed?.Invoke();
        public Func<string, Action, string, bool>? AddKey;

        static ExtHost()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("heft/1.0 (+https://github.com/drapids)");
        }

        public ExtHost(Settings s, Commands cmds, Notifier notes, NowPlaying np)
        {
            _s = s;
            _cmds = cmds;
            _notes = notes;
            _np = np;
            _ui = Dispatcher.CurrentDispatcher;
            _reload.Tick += (_, _) =>
            {
                _reload.Stop();
                var list = _dirty.ToList();
                _dirty.Clear();
                foreach (var id in list) Reload(id);
            };
            _minute.Tick += (_, _) =>
            {
                var now = DateTime.Now;
                int m = now.Hour * 60 + now.Minute;
                if (m == _lastMinute) return;
                _lastMinute = m;
                Emit("tick", now.Hour, now.Minute);
            };
        }

        public IEnumerable<Extension> All => _all.Values.OrderBy(e => e.Manifest.Name, StringComparer.CurrentCultureIgnoreCase);

        public IEnumerable<ExtBarItem> BarItems => _all.Values.Where(e => e.Running && e.Bar != null && e.Bar.Text.Length + e.Bar.Glyph.Length > 0).Select(e => e.Bar!);

        public IEnumerable<ExtWidget> Widgets => _all.Values.Where(e => e.Running && e.Widget != null && e.Widget.Title.Length + e.Widget.Body.Length > 0).Select(e => e.Widget!);

        public IEnumerable<(string name, Action<List<string>> run)> DropActions =>
            _all.Values.Where(e => e.Running).SelectMany(e => e.Drops.Select(d =>
            {
                var ext = e;
                var fn = d.fn;
                return (d.name, (Action<List<string>>)(paths =>
                {
                    var engine = ext.Engine;
                    if (engine == null) return;
                    Call(ext, fn, JsValue.FromObject(engine, paths.ToArray()));
                }));
            })).ToList();

        public IEnumerable<(string spec, Action run, string label)> Hotkeys =>
            _all.Values.Where(e => e.Running).SelectMany(e => e.Keys.Select(k =>
            {
                var ext = e;
                var fn = k.fn;
                return (k.spec, (Action)(() => Call(ext, fn)), ext.Manifest.Name);
            })).ToList();

        public bool Enabled(string id) => _s.ExtEnabled.TryGetValue(id, out bool on) && on;

        public void Start()
        {
            try
            {
                Directory.CreateDirectory(Root);
                Seed();
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("extensions folder failed " + ex.Message);
            }
            Scan();
            try
            {
                _watch = new FileSystemWatcher(Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName };
                _watch.Changed += (_, e) => Touched(e.FullPath);
                _watch.Created += (_, e) => Touched(e.FullPath);
                _watch.Deleted += (_, e) => Touched(e.FullPath);
                _watch.Renamed += (_, e) => Touched(e.FullPath);
                _watch.EnableRaisingEvents = true;
            }
            catch
            {
            }
            _minute.Start();
        }

        void Touched(string path)
        {
            string rel = Path.GetRelativePath(Root, path);
            string top = rel.Split(Path.DirectorySeparatorChar)[0];
            if (top.Length == 0 || top == "." || top.StartsWith("..")) return;
            if (rel.EndsWith("store.json", StringComparison.OrdinalIgnoreCase)) return;
            _ui.BeginInvoke(new Action(() =>
            {
                _dirty.Add(top);
                _reload.Stop();
                _reload.Start();
            }));
        }

        public void Scan()
        {
            foreach (var ext in _all.Values.ToList()) Stop(ext);
            _all.Clear();
            if (!Directory.Exists(Root)) return;
            foreach (var dir in Directory.EnumerateDirectories(Root)) LoadDir(dir);
            Changed?.Invoke();
        }

        void Reload(string folder)
        {
            string dir = Path.Combine(Root, folder);
            foreach (var old in _all.Values.Where(e => e.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Stop(old);
                _all.Remove(old.Id);
            }
            if (Directory.Exists(dir)) LoadDir(dir);
            Changed?.Invoke();
        }

        void LoadDir(string dir)
        {
            var ext = new Extension { Dir = dir };
            try
            {
                string mf = Path.Combine(dir, "manifest.json");
                if (!File.Exists(mf)) return;
                var m = JsonSerializer.Deserialize<ExtManifest>(File.ReadAllText(mf), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (m == null) return;
                if (m.Id.Length == 0) m.Id = Path.GetFileName(dir);
                if (m.Name.Length == 0) m.Name = m.Id;
                m.Permissions ??= new List<string>();
                ext.Manifest = m;
            }
            catch (Exception ex)
            {
                ext.Manifest = new ExtManifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) };
                ext.Error = "manifest.json is broken: " + ex.Message;
            }
            if (_all.ContainsKey(ext.Id)) return;
            _all[ext.Id] = ext;
            if (ext.Error.Length == 0 && Enabled(ext.Id)) Run(ext);
        }

        public void SetEnabled(string id, bool on)
        {
            _s.ExtEnabled[id] = on;
            _s.Touch();
            if (!_all.TryGetValue(id, out var ext)) return;
            if (on && !ext.Running) Run(ext);
            if (!on && ext.Running) Stop(ext);
            Changed?.Invoke();
        }

        public void Restart(string id)
        {
            if (!_all.TryGetValue(id, out var ext)) return;
            Reload(Path.GetFileName(ext.Dir));
        }

        void Run(Extension ext)
        {
            ext.Error = "";
            try
            {
                string main = Path.Combine(ext.Dir, ext.Manifest.Main);
                if (!File.Exists(main))
                {
                    ext.Error = $"can't find {ext.Manifest.Main}";
                    return;
                }
                var engine = new Engine(o =>
                {
                    o.TimeoutInterval(TimeSpan.FromSeconds(2));
                    o.LimitMemory(64_000_000);
                    o.LimitRecursion(256);
                    o.Strict(false);
                });
                ext.Engine = engine;
                ext.Running = true;
                engine.SetValue("heft", new ExtApi(this, ext));
                engine.Execute(File.ReadAllText(main), main);
                PhysicsEngine.Log("extension started " + ext.Id);
            }
            catch (Exception ex)
            {
                ext.Error = Clean(ex);
                PhysicsEngine.Log($"extension {ext.Id} failed: {ext.Error}");
                Stop(ext);
                ext.Error = Clean(ex);
            }
        }

        static string Clean(Exception ex)
        {
            string msg = ex.Message;
            return msg.Length > 240 ? msg[..240] : msg;
        }

        void Stop(Extension ext)
        {
            foreach (var id in ext.Commands) _cmds.Remove(id);
            ext.Commands.Clear();
            bool hadKeys = ext.Keys.Count > 0;
            foreach (var t in ext.Timers) t.Stop();
            ext.Timers.Clear();
            ext.Handlers.Clear();
            ext.Drops.Clear();
            ext.Keys.Clear();
            bool hadBar = ext.Bar != null;
            ext.Bar = null;
            ext.Widget = null;
            ext.Engine = null;
            ext.Running = false;
            if (hadKeys) KeysUpdated?.Invoke();
            if (hadBar) BarChanged?.Invoke();
        }

        internal void Call(Extension ext, JsValue fn, params object?[] args)
        {
            if (!ext.Running || ext.Engine == null) return;
            try
            {
                ext.Engine.Invoke(fn, args);
            }
            catch (Exception ex)
            {
                ext.Error = Clean(ex);
                PhysicsEngine.Log($"extension {ext.Id} error: {ext.Error}");
                Changed?.Invoke();
            }
        }

        public void Emit(string evt, params object?[] args)
        {
            foreach (var ext in _all.Values.Where(e => e.Running).ToList())
            {
                if (!ext.Handlers.TryGetValue(evt, out var list)) continue;
                foreach (var fn in list.ToList()) Call(ext, fn, args);
            }
        }

        internal void Need(Extension ext, string perm)
        {
            if (!ext.Can(perm)) throw new InvalidOperationException($"this extension needs the \"{perm}\" permission in its manifest.json");
        }

        internal void Notify(Extension ext, string title, string body) => _notes.Post(new Note { App = ext.Manifest.Name, Title = title, Body = body, Glyph = "\uEA86" });

        internal void Command(Extension ext, string id, string name, JsValue fn)
        {
            string full = "ext." + ext.Id + "." + id;
            _cmds.Remove(full);
            _cmds.Add(ext.Manifest.Name, full, name, "\uEA86", () => Call(ext, fn), ext.Manifest.Name + " extension");
            if (!ext.Commands.Contains(full)) ext.Commands.Add(full);
        }

        internal void SetBar(Extension ext, string text, string glyph, JsValue? click)
        {
            ext.Bar ??= new ExtBarItem { Ext = ext.Id };
            ext.Bar.Text = text;
            ext.Bar.Glyph = glyph;
            if (click != null) ext.Bar.Click = () => Call(ext, click);
            BarChanged?.Invoke();
        }

        internal void SetWidget(Extension ext, string title, string body)
        {
            ext.Widget ??= new ExtWidget { Ext = ext.Id };
            ext.Widget.Title = title;
            ext.Widget.Body = body;
        }

        internal void Every(Extension ext, double ms, JsValue fn, bool once)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(once ? 50 : 1000, ms)) };
            t.Tick += (_, _) =>
            {
                if (once)
                {
                    t.Stop();
                    ext.Timers.Remove(t);
                }
                Call(ext, fn);
            };
            ext.Timers.Add(t);
            t.Start();
        }

        internal void Fetch(Extension ext, string url, JsValue fn, Dictionary<string, string>? headers = null)
        {
            var engine = ext.Engine;
            Task.Run(async () =>
            {
                string text = "";
                int status = 0;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (headers != null)
                        foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    using var res = await Http.SendAsync(req);
                    status = (int)res.StatusCode;
                    text = await res.Content.ReadAsStringAsync();
                    if (text.Length > 2_000_000) text = text[..2_000_000];
                }
                catch (Exception ex)
                {
                    text = ex.Message;
                }
                await _ui.BeginInvoke(new Action(() =>
                {
                    if (ext.Engine != engine) return;
                    Call(ext, fn, text, status);
                }));
            });
        }

        internal string StorePath(Extension ext) => Path.Combine(ext.Dir, "store.json");

        internal Dictionary<string, string> Store(Extension ext)
        {
            try
            {
                string p = StorePath(ext);
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) ?? new();
            }
            catch
            {
            }
            return new();
        }

        internal void Save(Extension ext, Dictionary<string, string> data)
        {
            try
            {
                File.WriteAllText(StorePath(ext), JsonSerializer.Serialize(data));
            }
            catch
            {
            }
        }

        internal NowPlaying Media => _np;
        internal Settings Settings => _s;

        public string? Install(string file)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "heftext-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                ZipFile.ExtractToDirectory(file, tmp);
                string? mf = Directory.EnumerateFiles(tmp, "manifest.json", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
                if (mf == null) return "that file doesn't have a manifest.json in it";
                string src = Path.GetDirectoryName(mf)!;
                var m = JsonSerializer.Deserialize<ExtManifest>(File.ReadAllText(mf), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                string id = m?.Id is { Length: > 0 } x ? x : Path.GetFileNameWithoutExtension(file);
                foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '-');
                string dest = Path.Combine(Root, id);
                if (Directory.Exists(dest))
                {
                    var keep = Path.Combine(dest, "store.json");
                    string? kept = File.Exists(keep) ? File.ReadAllText(keep) : null;
                    Directory.Delete(dest, true);
                    Copy(src, dest);
                    if (kept != null) File.WriteAllText(keep, kept);
                }
                else Copy(src, dest);
                Reload(id);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try
                {
                    Directory.Delete(tmp, true);
                }
                catch
                {
                }
            }
        }

        static void Copy(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(dest, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to, true);
            }
        }

        public void Remove(string id)
        {
            if (!_all.TryGetValue(id, out var ext)) return;
            Stop(ext);
            _all.Remove(id);
            try
            {
                Directory.Delete(ext.Dir, true);
            }
            catch
            {
            }
            Changed?.Invoke();
        }

        public string Create(string name)
        {
            string id = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (id.Length == 0) id = "my-extension";
            string dir = Path.Combine(Root, id);
            int n = 2;
            while (Directory.Exists(dir)) dir = Path.Combine(Root, id + "-" + n++);
            Directory.CreateDirectory(dir);
            id = Path.GetFileName(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new ExtManifest
            {
                Id = id,
                Name = name,
                Author = "",
                Description = "what it does",
                Permissions = new List<string> { "notify", "commands", "bar" }
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            File.WriteAllText(Path.Combine(dir, "main.js"), StarterExtensions.Template);
            _s.ExtEnabled[id] = true;
            _s.Touch();
            Reload(id);
            return dir;
        }

        void Seed()
        {
            if (_s.ExtSeeded) return;
            foreach (var (id, manifest, code) in StarterExtensions.All)
            {
                string dir = Path.Combine(Root, id);
                if (Directory.Exists(dir)) continue;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest);
                File.WriteAllText(Path.Combine(dir, "main.js"), code);
            }
            _s.ExtSeeded = true;
            _s.Touch();
        }

        public void Dispose()
        {
            _minute.Stop();
            try
            {
                _watch?.Dispose();
            }
            catch
            {
            }
            foreach (var ext in _all.Values) Stop(ext);
        }
    }

    public sealed class ExtMedia
    {
        public string title { get; set; } = "";
        public string artist { get; set; } = "";
        public bool playing { get; set; }
        public double position { get; set; }
        public double duration { get; set; }
        public bool any { get; set; }
    }

    public sealed class ExtApi
    {
        readonly ExtHost _host;
        readonly Extension _ext;

        internal ExtApi(ExtHost host, Extension ext)
        {
            _host = host;
            _ext = ext;
        }

        public string id => _ext.Id;

        public void log(object? msg) => PhysicsEngine.Log($"[{_ext.Id}] {msg}");

        public void notify(string title, string? body = null)
        {
            _host.Need(_ext, "notify");
            _host.Notify(_ext, title ?? "", body ?? "");
        }

        public void command(string id, string name, JsValue fn)
        {
            _host.Need(_ext, "commands");
            _host.Command(_ext, id, name, fn);
        }

        public void key(string spec, JsValue fn)
        {
            _host.Need(_ext, "keys");
            _ext.Keys.Add((spec, fn));
            _host.KeysChanged();
        }

        public void bar(string text, string? glyph = null, JsValue? onClick = null)
        {
            _host.Need(_ext, "bar");
            bool real = onClick is not null && !onClick.IsUndefined() && !onClick.IsNull();
            _host.SetBar(_ext, text ?? "", glyph ?? "", real ? onClick : null);
        }

        public void widget(string title, string? body = null)
        {
            _host.Need(_ext, "widgets");
            _host.SetWidget(_ext, title ?? "", body ?? "");
        }

        public void dropAction(string name, JsValue fn)
        {
            _host.Need(_ext, "drop");
            _ext.Drops.Add((name, fn));
            _host.DropsChanged();
        }

        public void on(string evt, JsValue fn)
        {
            string e = evt.ToLowerInvariant();
            if (e is "window" or "download" or "clipboard" or "game") _host.Need(_ext, "watch");
            if (e == "song") _host.Need(_ext, "media");
            if (!_ext.Handlers.TryGetValue(e, out var list)) _ext.Handlers[e] = list = new List<JsValue>();
            list.Add(fn);
        }

        public void every(double ms, JsValue fn) => _host.Every(_ext, ms, fn, false);

        public void after(double ms, JsValue fn) => _host.Every(_ext, ms, fn, true);

        public void fetch(string url, JsValue fn)
        {
            _host.Need(_ext, "net");
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("fetch only does http and https");
            _host.Fetch(_ext, url, fn);
        }

        public void request(string url, string? headersJson, JsValue fn)
        {
            _host.Need(_ext, "net");
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("request only does http and https");
            Dictionary<string, string>? headers = null;
            if (!string.IsNullOrWhiteSpace(headersJson)) headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            _host.Fetch(_ext, url, fn, headers);
        }

        public string time(string zone, string? format = null)
        {
            var now = DateTime.UtcNow;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
                return TimeZoneInfo.ConvertTimeFromUtc(now, tz).ToString(format ?? "h:mm tt");
            }
            catch
            {
                return "?";
            }
        }

        public void open(string target)
        {
            _host.Need(_ext, "run");
            Launch.Run(target);
        }

        public void copy(string text)
        {
            _host.Need(_ext, "clipboard");
            Files.CopyText(text ?? "");
        }

        public string? load(string key) => _host.Store(_ext).TryGetValue(key, out var v) ? v : null;

        public void save(string key, string? value)
        {
            var data = _host.Store(_ext);
            if (value == null) data.Remove(key);
            else data[key] = value;
            _host.Save(_ext, data);
        }

        public string file(string name)
        {
            string p = Path.GetFullPath(Path.Combine(_ext.Dir, name));
            if (!p.StartsWith(Path.GetFullPath(_ext.Dir), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("extensions can only read their own folder");
            return File.Exists(p) ? File.ReadAllText(p) : "";
        }

        public ExtMedia media()
        {
            _host.Need(_ext, "media");
            var np = _host.Media;
            return new ExtMedia { title = np.Title, artist = np.Artist, playing = np.Playing, position = np.Position, duration = np.Duration, any = np.HasSession };
        }

        public void playPause()
        {
            _host.Need(_ext, "media");
            _ = _host.Media.Toggle();
        }

        public void next()
        {
            _host.Need(_ext, "media");
            _ = _host.Media.Next();
        }

        public void previous()
        {
            _host.Need(_ext, "media");
            _ = _host.Media.Previous();
        }
    }
}
