using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Elysium
{
    public sealed class CalFeed
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Color { get; set; } = "#0A84FF";
        public bool On { get; set; } = true;
    }

    internal sealed class CalEvent
    {
        public DateTime Start;
        public DateTime End;
        public bool AllDay;
        public string Title = "";
        public string Where = "";
        public string Color = "#0A84FF";
        public string Feed = "";
    }

    internal sealed class Calendars
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        readonly Settings _s;
        readonly Dictionary<string, List<CalEvent>> _cache = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
        DateTime _fetched = DateTime.MinValue;
        bool _busy;

        public event Action? Changed;

        public Calendars(Settings s) => _s = s;

        public string? Error(string url) => _errors.TryGetValue(url, out var e) ? e : null;

        public List<CalEvent> On(DateTime day)
        {
            var from = day.Date;
            var to = from.AddDays(1);
            var list = new List<CalEvent>();
            foreach (var f in _s.CalendarFeeds.Where(f => f.On))
                if (_cache.TryGetValue(f.Url, out var evs))
                    list.AddRange(evs.Where(e => e.Start < to && (e.End > from || (e.End <= e.Start && e.Start >= from))));
            return list.OrderBy(e => !e.AllDay).ThenBy(e => e.Start).ToList();
        }

        public HashSet<DateTime> Busy(DateTime month)
        {
            var set = new HashSet<DateTime>();
            var from = new DateTime(month.Year, month.Month, 1).AddDays(-7);
            var to = from.AddDays(50);
            foreach (var f in _s.CalendarFeeds.Where(f => f.On))
                if (_cache.TryGetValue(f.Url, out var evs))
                    foreach (var e in evs)
                    {
                        if (e.Start >= to || e.End < from) continue;
                        for (var d = e.Start.Date; d <= e.End.Date && d < to; d = d.AddDays(1))
                        {
                            if (e.AllDay && d == e.End.Date && e.End.TimeOfDay == TimeSpan.Zero && d != e.Start.Date) break;
                            set.Add(d);
                        }
                    }
            return set;
        }

        public void Refresh(bool force = false)
        {
            if (_busy || (!force && (DateTime.Now - _fetched).TotalMinutes < 20)) return;
            if (_s.CalendarFeeds.Count == 0) return;
            _busy = true;
            _fetched = DateTime.Now;
            var feeds = _s.CalendarFeeds.Where(f => f.On && f.Url.Length > 0).Select(f => (f.Url, f.Color, f.Name)).ToList();
            Task.Run(async () =>
            {
                var results = new List<(string url, List<CalEvent>? evs, string? err)>();
                foreach (var (url, color, name) in feeds)
                {
                    try
                    {
                        string fixedUrl = url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[9..] : url;
                        string text = await Http.GetStringAsync(fixedUrl);
                        if (!text.Contains("BEGIN:VCALENDAR")) throw new Exception("that link isn't a calendar (ical) link");
                        results.Add((url, Parse(text, color, name), null));
                    }
                    catch (Exception ex)
                    {
                        results.Add((url, null, ex.Message));
                    }
                }
                return results;
            }).ContinueWith(t =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    if (t.IsFaulted) return;
                    foreach (var (url, evs, err) in t.Result)
                    {
                        if (evs != null)
                        {
                            _cache[url] = evs;
                            _errors.Remove(url);
                        }
                        else if (err != null) _errors[url] = err;
                    }
                    Changed?.Invoke();
                }));
            });
        }

        static List<string> Unfold(string text)
        {
            var lines = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && lines.Count > 0) lines[^1] += raw[1..];
                else lines.Add(raw);
            }
            return lines;
        }

        static List<CalEvent> Parse(string text, string color, string feed)
        {
            var events = new List<CalEvent>();
            var winFrom = DateTime.Today.AddDays(-60);
            var winTo = DateTime.Today.AddDays(200);
            Dictionary<string, (string value, Dictionary<string, string> parms)>? cur = null;
            var exdates = new List<DateTime>();
            foreach (var line in Unfold(text))
            {
                if (line == "BEGIN:VEVENT")
                {
                    cur = new Dictionary<string, (string, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase);
                    exdates.Clear();
                    continue;
                }
                if (line == "END:VEVENT")
                {
                    if (cur != null) Expand(cur, exdates, color, feed, winFrom, winTo, events);
                    cur = null;
                    continue;
                }
                if (cur == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string head = line[..colon], value = line[(colon + 1)..];
                var parts = head.Split(';');
                var parms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in parts.Skip(1))
                {
                    int eq = p.IndexOf('=');
                    if (eq > 0) parms[p[..eq]] = p[(eq + 1)..].Trim('"');
                }
                string key = parts[0].ToUpperInvariant();
                if (key == "EXDATE")
                {
                    foreach (var v in value.Split(','))
                        if (When(v, parms, out var ex, out _)) exdates.Add(ex);
                    continue;
                }
                cur[key] = (value, parms);
            }
            return events;
        }

        static string Text(string v) => v.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

        static void Expand(Dictionary<string, (string value, Dictionary<string, string> parms)> e, List<DateTime> exdates, string color, string feed, DateTime winFrom, DateTime winTo, List<CalEvent> into)
        {
            if (e.TryGetValue("STATUS", out var status) && status.value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
            if (!e.TryGetValue("DTSTART", out var ds) || !When(ds.value, ds.parms, out var start, out bool allDay)) return;
            DateTime end = start;
            if (e.TryGetValue("DTEND", out var de) && When(de.value, de.parms, out var en, out _)) end = en;
            else if (e.TryGetValue("DURATION", out var du))
            {
                try
                {
                    end = start + System.Xml.XmlConvert.ToTimeSpan(du.value);
                }
                catch
                {
                    end = allDay ? start.AddDays(1) : start.AddHours(1);
                }
            }
            else end = allDay ? start.AddDays(1) : start;
            var length = end - start;
            string title = e.TryGetValue("SUMMARY", out var sm) ? Text(sm.value) : "(no title)";
            string where = e.TryGetValue("LOCATION", out var lo) ? Text(lo.value) : "";

            void Add(DateTime s)
            {
                if (exdates.Any(x => Math.Abs((x - s).TotalMinutes) < 1)) return;
                if (s + length < winFrom || s > winTo) return;
                into.Add(new CalEvent { Start = s, End = s + length, AllDay = allDay, Title = title, Where = where, Color = color, Feed = feed });
            }

            if (!e.TryGetValue("RRULE", out var rr))
            {
                Add(start);
                return;
            }
            var rule = rr.value.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);
            string freq = rule.TryGetValue("FREQ", out var f) ? f.ToUpperInvariant() : "";
            int interval = rule.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out int ivn) && ivn > 0 ? ivn : 1;
            int count = rule.TryGetValue("COUNT", out var cn) && int.TryParse(cn, out int cnt) ? cnt : int.MaxValue;
            DateTime until = DateTime.MaxValue;
            if (rule.TryGetValue("UNTIL", out var un) && When(un, new Dictionary<string, string>(), out var ut, out bool untilDay)) until = untilDay ? ut.AddDays(1).AddTicks(-1) : ut;
            var days = rule.TryGetValue("BYDAY", out var bd)
                ? bd.Split(',').Select(x => x.Trim().ToUpperInvariant()).Select(x => x.Length >= 2 ? x[^2..] : x).Select(Day).Where(x => x != null).Select(x => x!.Value).ToHashSet()
                : new HashSet<DayOfWeek>();
            int made = 0;
            int guard = 0;
            if (freq == "WEEKLY" && days.Count > 0)
            {
                var weekStart = start.Date.AddDays(-(int)start.DayOfWeek);
                for (var w = weekStart; w <= winTo && made < count && guard++ < 2000; w = w.AddDays(7 * interval))
                {
                    for (int i = 0; i < 7; i++)
                    {
                        var day = w.AddDays(i);
                        if (!days.Contains(day.DayOfWeek)) continue;
                        var s = day + start.TimeOfDay;
                        if (s < start) continue;
                        if (s > until || made >= count) return;
                        made++;
                        Add(s);
                    }
                }
                return;
            }
            for (var s = start; s <= winTo && made < count && guard++ < 3000;)
            {
                if (s > until) return;
                made++;
                Add(s);
                s = freq switch
                {
                    "DAILY" => s.AddDays(interval),
                    "WEEKLY" => s.AddDays(7 * interval),
                    "MONTHLY" => s.AddMonths(interval),
                    "YEARLY" => s.AddYears(interval),
                    _ => DateTime.MaxValue
                };
                if (s == DateTime.MaxValue) return;
            }
        }

        static DayOfWeek? Day(string d) => d switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            _ => null
        };

        static bool When(string v, Dictionary<string, string> parms, out DateTime when, out bool allDay)
        {
            when = default;
            allDay = false;
            v = v.Trim();
            if (v.Length == 8 || (parms.TryGetValue("VALUE", out var vt) && vt.Equals("DATE", StringComparison.OrdinalIgnoreCase)))
            {
                allDay = true;
                return DateTime.TryParseExact(v[..Math.Min(8, v.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
            }
            bool utc = v.EndsWith('Z');
            string core = v.TrimEnd('Z');
            if (!DateTime.TryParseExact(core, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return false;
            if (utc)
            {
                when = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
                return true;
            }
            if (parms.TryGetValue("TZID", out var tz))
            {
                try
                {
                    string id = tz;
                    if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tz, out var win)) id = win;
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                    when = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local);
                    return true;
                }
                catch
                {
                }
            }
            when = t;
            return true;
        }
    }
}
