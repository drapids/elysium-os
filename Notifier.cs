using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace Elysium
{
    public sealed class Note
    {
        public long Id { get; set; }
        public string App { get; set; } = "elysium";
        public string AppId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime Time { get; set; } = DateTime.Now;
        public string Source { get; set; } = "elysium";
        public string? Open { get; set; }
        public string Glyph { get; set; } = "\uE7E7";
        public bool Seen { get; set; }
    }

    public sealed class Reminder
    {
        public DateTime Due { get; set; }
        public string Text { get; set; } = "";
        public bool Timer { get; set; }
    }

    internal sealed class Notifier
    {
        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium", "notes.json");

        readonly Settings _s;
        readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromSeconds(2) };
        long _nextId;

        public List<Note> History { get; } = new();
        public event Action<Note>? Toast;
        public event Action? Changed;

        public Notifier(Settings settings)
        {
            _s = settings;
            Load();
            _clock.Tick += (_, _) => CheckReminders();
            _clock.Start();
            _save.Tick += (_, _) =>
            {
                _save.Stop();
                Save();
            };
        }

        public int Unseen => History.Count(n => !n.Seen);

        public Reminder? ActiveTimer => _s.Reminders.Where(r => r.Timer).OrderBy(r => r.Due).FirstOrDefault();

        public string Rule(string app)
        {
            return _s.NoteRules.TryGetValue(app, out var r) ? r : "normal";
        }

        public void Post(Note n)
        {
            n.Id = ++_nextId;
            if (n.Time == default) n.Time = DateTime.Now;
            string rule = Rule(n.App);
            if (rule == "block") return;

            History.Insert(0, n);
            if (History.Count > 400) History.RemoveRange(400, History.Count - 400);
            Changed?.Invoke();
            _save.Stop();
            _s.Touch();
            _save.Start();

            bool quiet = rule == "silent" || (_s.FocusActive && !Allowed(n.App));
            if (!quiet) Toast?.Invoke(n);
        }

        public void Post(string title, string body, string glyph = "\uE7E7", string? open = null)
        {
            Post(new Note { Title = title, Body = body, Glyph = glyph, Open = open });
        }

        bool Allowed(string app)
        {
            if (app == "elysium") return true;
            foreach (var a in _s.FocusAllow.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (app.Contains(a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void MarkSeen()
        {
            bool changed = false;
            foreach (var n in History)
            {
                if (n.Seen) continue;
                n.Seen = true;
                changed = true;
            }
            if (!changed) return;
            Changed?.Invoke();
            _save.Start();
        }

        public void Remove(Note n)
        {
            History.Remove(n);
            Changed?.Invoke();
            _save.Start();
        }

        public void Clear(string? app = null)
        {
            if (app == null) History.Clear();
            else History.RemoveAll(n => n.App == app);
            Changed?.Invoke();
            _save.Start();
        }

        public void Remind(DateTime due, string text, bool timer = false)
        {
            _s.Reminders.Add(new Reminder { Due = due, Text = text, Timer = timer });
            _s.Touch();
            Changed?.Invoke();
        }

        public void CancelTimer()
        {
            _s.Reminders.RemoveAll(r => r.Timer);
            _s.Touch();
            Changed?.Invoke();
        }

        void CheckReminders()
        {
            if (_s.Reminders.Count == 0) return;
            var now = DateTime.Now;
            var due = _s.Reminders.Where(r => r.Due <= now).ToList();
            if (due.Count == 0) return;
            foreach (var r in due)
            {
                _s.Reminders.Remove(r);
                if (r.Timer) Post(new Note { Title = "Timer done", Body = string.IsNullOrWhiteSpace(r.Text) ? "time's up" : r.Text, Glyph = "\uE916" });
                else Post(new Note { Title = "Reminder", Body = r.Text, Glyph = "\uE787" });
            }
            _s.Touch();
            Changed?.Invoke();
        }

        void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var list = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath));
                if (list == null) return;
                History.AddRange(list);
                _nextId = History.Count == 0 ? 0 : History.Max(n => n.Id);
            }
            catch
            {
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(History));
            }
            catch
            {
            }
        }
    }
}
