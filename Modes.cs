using System;
using System.Collections.Generic;
using System.Linq;

namespace Heft
{
    public sealed class Mode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";
        public string Theme { get; set; } = "";
        public string Accent { get; set; } = "";
        public string Focus { get; set; } = "";
        public string Audio { get; set; } = "";
        public string Sounds { get; set; } = "";
        public string At { get; set; } = "";
        public string App { get; set; } = "";
        public string Hotkey { get; set; } = "";
    }

    internal static class Modes
    {
        public static string Describe(Mode m)
        {
            var bits = new List<string>();
            if (m.Theme.Length > 0) bits.Add(m.Theme == "system" ? "auto theme" : m.Theme);
            if (m.Accent.Length > 0) bits.Add("accent " + m.Accent);
            if (m.Focus == "on") bits.Add("focus on");
            else if (m.Focus == "off") bits.Add("focus off");
            if (m.Audio.Length > 0) bits.Add("audio: " + m.Audio);
            if (m.Sounds == "on") bits.Add("sounds on");
            else if (m.Sounds == "off") bits.Add("sounds off");
            if (m.At.Length > 0) bits.Add("at " + m.At);
            if (m.App.Length > 0) bits.Add("when " + m.App + " opens");
            return bits.Count == 0 ? "doesn't change anything yet" : string.Join(", ", bits);
        }

        public static bool ParseTime(string text, out TimeSpan t)
        {
            t = default;
            string s = text.Trim().ToLowerInvariant().Replace(" ", "");
            if (s.Length == 0) return false;
            bool pm = s.EndsWith("pm"), am = s.EndsWith("am");
            if (pm || am) s = s[..^2];
            var parts = s.Split(':');
            if (!int.TryParse(parts[0], out int h)) return false;
            int m = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], out m)) return false;
            if (pm && h < 12) h += 12;
            if (am && h == 12) h = 0;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            t = new TimeSpan(h, m, 0);
            return true;
        }

        public static Mode? Due(Settings s, DateTime now, ref string lastKey)
        {
            string key = now.ToString("yyyyMMddHHmm");
            if (key == lastKey) return null;
            lastKey = key;
            foreach (var m in s.Modes)
            {
                if (!ParseTime(m.At, out var t)) continue;
                if (now.Hour == t.Hours && now.Minute == t.Minutes) return m;
            }
            return null;
        }
    }
}
