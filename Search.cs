using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Elysium
{
    internal static class Fuzzy
    {
        static bool Start(string t, int i)
        {
            if (i == 0) return true;
            char p = t[i - 1], c = t[i];
            if (!char.IsLetterOrDigit(p)) return true;
            if (char.IsLower(p) && char.IsUpper(c)) return true;
            if (char.IsLetter(p) && char.IsDigit(c)) return true;
            return false;
        }

        static double Pass(string q, string t, bool preferStarts)
        {
            string tl = t.ToLowerInvariant();
            int ti = 0;
            double score = 0;
            int last = -2;
            foreach (char qc in q)
            {
                int found = -1;
                if (preferStarts)
                {
                    for (int j = ti; j < tl.Length; j++)
                    {
                        if (tl[j] == qc && Start(t, j))
                        {
                            found = j;
                            break;
                        }
                    }
                    if (found >= 0 && last >= 0 && tl.IndexOf(qc, ti) == last + 1) found = last + 1;
                }
                if (found < 0) found = tl.IndexOf(qc, ti);
                if (found < 0) return 0;

                double s = 1;
                if (Start(t, found)) s += 2.2;
                if (found == last + 1) s += 1.6;
                if (found == 0) s += 1.5;
                s -= Math.Min(found - ti, 8) * 0.06;
                score += s;
                last = found;
                ti = found + 1;
            }
            return score;
        }

        public static double Score(string query, string text)
        {
            if (string.IsNullOrEmpty(query)) return 1;
            if (string.IsNullOrEmpty(text)) return 0;
            string q = query.ToLowerInvariant().Replace(" ", "");
            if (q.Length == 0) return 1;
            double a = Pass(q, text, true);
            double b = Pass(q, text, false);
            double best = Math.Max(a, b);
            if (best <= 0) return 0;
            string tl = text.ToLowerInvariant();
            string ql = query.ToLowerInvariant().Trim();
            if (tl == ql) best += 12;
            else if (tl.StartsWith(ql)) best += 6;
            else if (tl.Contains(ql)) best += 3;
            return best / (1 + text.Length * 0.012);
        }
    }

    internal static class Calc
    {
        static readonly Dictionary<string, (string dim, double k)> Units = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mm"] = ("len", 0.001), ["cm"] = ("len", 0.01), ["m"] = ("len", 1), ["km"] = ("len", 1000),
            ["in"] = ("len", 0.0254), ["inch"] = ("len", 0.0254), ["inches"] = ("len", 0.0254), ["ft"] = ("len", 0.3048), ["feet"] = ("len", 0.3048),
            ["yd"] = ("len", 0.9144), ["mi"] = ("len", 1609.344), ["mile"] = ("len", 1609.344), ["miles"] = ("len", 1609.344),
            ["mg"] = ("mass", 0.001), ["g"] = ("mass", 1), ["kg"] = ("mass", 1000), ["oz"] = ("mass", 28.3495), ["lb"] = ("mass", 453.592), ["lbs"] = ("mass", 453.592),
            ["ml"] = ("vol", 0.001), ["l"] = ("vol", 1), ["gal"] = ("vol", 3.78541), ["cup"] = ("vol", 0.236588), ["cups"] = ("vol", 0.236588), ["floz"] = ("vol", 0.0295735),
            ["b"] = ("data", 1), ["kb"] = ("data", 1024), ["mb"] = ("data", 1024d * 1024), ["gb"] = ("data", 1024d * 1024 * 1024), ["tb"] = ("data", 1024d * 1024 * 1024 * 1024),
            ["ms"] = ("time", 0.001), ["s"] = ("time", 1), ["sec"] = ("time", 1), ["min"] = ("time", 60), ["h"] = ("time", 3600), ["hr"] = ("time", 3600), ["hours"] = ("time", 3600), ["day"] = ("time", 86400), ["days"] = ("time", 86400), ["week"] = ("time", 604800), ["weeks"] = ("time", 604800),
            ["mph"] = ("speed", 0.44704), ["kph"] = ("speed", 0.277778), ["kmh"] = ("speed", 0.277778), ["mps"] = ("speed", 1),
            ["c"] = ("temp", 0), ["f"] = ("temp", 0), ["k"] = ("temp", 0)
        };

        public static bool TryConvert(string q, out string result)
        {
            result = "";
            var m = Regex.Match(q.Trim(), @"^(-?[\d.,]+)\s*([a-zA-Z]+)\s+(?:to|in|as)\s+([a-zA-Z]+)$");
            if (!m.Success) return false;
            if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return false;
            string a = m.Groups[2].Value, b = m.Groups[3].Value;
            if (!Units.TryGetValue(a, out var ua) || !Units.TryGetValue(b, out var ub) || ua.dim != ub.dim) return false;
            double r;
            if (ua.dim == "temp")
            {
                double c = a.ToLowerInvariant() switch { "f" => (v - 32) * 5 / 9, "k" => v - 273.15, _ => v };
                r = b.ToLowerInvariant() switch { "f" => c * 9 / 5 + 32, "k" => c + 273.15, _ => c };
            }
            else
            {
                r = v * ua.k / ub.k;
            }
            result = $"{Format(r)} {b}";
            return true;
        }

        public static string Format(double v)
        {
            if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0)) return v.ToString("G6", CultureInfo.InvariantCulture);
            return Math.Round(v, 8).ToString("#,0.########", CultureInfo.InvariantCulture);
        }

        public static bool TryEval(string expr, out double value)
        {
            value = 0;
            string e = expr.Trim();
            if (e.StartsWith("=")) e = e[1..];
            if (e.Length == 0 || !Regex.IsMatch(e, @"\d")) return false;
            string words = Regex.Replace(e, @"\b(sqrt|sin|cos|tan|log|ln|abs|pi|round|floor|ceil)\b", "");
            if (Regex.IsMatch(words, @"[a-zA-Z]")) return false;
            if (!Regex.IsMatch(e, @"^[\d\s.+\-*/^%()a-zA-Z,]+$")) return false;
            if (!Regex.IsMatch(e, @"[+\-*/^%(]") && !Regex.IsMatch(e, @"[a-z]")) return false;
            try
            {
                int pos = 0;
                value = Expr(e.Replace(" ", "").ToLowerInvariant(), ref pos);
                return pos >= e.Replace(" ", "").Length && !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        static double Expr(string s, ref int p)
        {
            double v = Term(s, ref p);
            while (p < s.Length && (s[p] == '+' || s[p] == '-'))
            {
                char op = s[p++];
                double r = Term(s, ref p);
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        static double Term(string s, ref int p)
        {
            double v = Power(s, ref p);
            while (p < s.Length && (s[p] == '*' || s[p] == '/' || s[p] == '%' || s[p] == '(' || char.IsLetter(s[p])))
            {
                char op = s[p];
                if (op == '(' || char.IsLetter(op))
                {
                    v *= Power(s, ref p);
                    continue;
                }
                p++;
                double r = Power(s, ref p);
                v = op == '*' ? v * r : op == '/' ? v / r : v % r;
            }
            return v;
        }

        static double Power(string s, ref int p)
        {
            double b = Unary(s, ref p);
            if (p < s.Length && s[p] == '^')
            {
                p++;
                return Math.Pow(b, Power(s, ref p));
            }
            return b;
        }

        static double Unary(string s, ref int p)
        {
            if (p < s.Length && s[p] == '-')
            {
                p++;
                return -Unary(s, ref p);
            }
            if (p < s.Length && s[p] == '+')
            {
                p++;
                return Unary(s, ref p);
            }
            return Atom(s, ref p);
        }

        static double Atom(string s, ref int p)
        {
            if (p >= s.Length) throw new FormatException();
            if (s[p] == '(')
            {
                p++;
                double v = Expr(s, ref p);
                if (p < s.Length && s[p] == ')') p++;
                return v;
            }
            if (char.IsLetter(s[p]))
            {
                int start = p;
                while (p < s.Length && char.IsLetter(s[p])) p++;
                string name = s[start..p];
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                double arg = Atom(s, ref p);
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "sin" => Math.Sin(arg * Math.PI / 180),
                    "cos" => Math.Cos(arg * Math.PI / 180),
                    "tan" => Math.Tan(arg * Math.PI / 180),
                    "log" => Math.Log10(arg),
                    "ln" => Math.Log(arg),
                    "abs" => Math.Abs(arg),
                    "round" => Math.Round(arg),
                    "floor" => Math.Floor(arg),
                    "ceil" => Math.Ceiling(arg),
                    _ => throw new FormatException()
                };
            }
            int st = p;
            while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == ',')) p++;
            if (st == p) throw new FormatException();
            double num = double.Parse(s[st..p].Replace(",", ""), CultureInfo.InvariantCulture);
            if (p < s.Length && s[p] == '%' && (p + 1 >= s.Length || "+-*/)".Contains(s[p + 1])))
            {
                p++;
                num /= 100;
            }
            return num;
        }
    }

    internal static class When
    {
        static readonly Regex Rel = new(@"\bin\s+(\d+(?:\.\d+)?)\s*(s|sec|secs|seconds?|m|min|mins|minutes?|h|hr|hrs|hours?|d|days?)\b", RegexOptions.IgnoreCase);
        static readonly Regex At = new(@"\bat\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b", RegexOptions.IgnoreCase);
        static readonly Regex Tomorrow = new(@"\btomorrow\b", RegexOptions.IgnoreCase);

        public static bool TryReminder(string q, out DateTime due, out string text)
        {
            due = default;
            text = "";
            string s = q.Trim();
            var lead = Regex.Match(s, @"^(remind\s+me|reminder|remind)\b\s*", RegexOptions.IgnoreCase);
            bool explicitAsk = lead.Success;
            if (explicitAsk) s = s[lead.Length..];
            else if (!Regex.IsMatch(s, @"^in\s+\d", RegexOptions.IgnoreCase)) return false;

            var now = DateTime.Now;
            var rel = Rel.Match(s);
            var at = At.Match(s);
            if (rel.Success)
            {
                due = now.Add(Span(rel.Groups[1].Value, rel.Groups[2].Value));
                s = s.Remove(rel.Index, rel.Length);
            }
            else if (at.Success)
            {
                int h = int.Parse(at.Groups[1].Value);
                int m = at.Groups[2].Success ? int.Parse(at.Groups[2].Value) : 0;
                string ap = at.Groups[3].Value.ToLowerInvariant();
                if (ap == "pm" && h < 12) h += 12;
                if (ap == "am" && h == 12) h = 0;
                if (ap == "" && h < 12)
                {
                    var cand = now.Date.AddHours(h).AddMinutes(m);
                    if (cand <= now && now.Date.AddHours(h + 12).AddMinutes(m) > now) h += 12;
                }
                if (h > 23 || m > 59) return false;
                due = now.Date.AddHours(h).AddMinutes(m);
                if (due <= now) due = due.AddDays(1);
                s = s.Remove(at.Index, at.Length);
            }
            else
            {
                return false;
            }
            if (Tomorrow.IsMatch(s))
            {
                if (due.Date == now.Date) due = due.AddDays(1);
                s = Tomorrow.Replace(s, "");
            }
            text = Regex.Replace(s, @"^\s*(to|that|about)\s+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+(to)\s*$", "", RegexOptions.IgnoreCase).Trim(' ', ',', '.');
            if (text.Length == 0) text = "you asked me to remind you";
            return true;
        }

        public static bool TryTimer(string q, out TimeSpan span, out string label)
        {
            span = default;
            label = "";
            var m = Regex.Match(q.Trim(), @"^(timer|pomodoro)\s*(\d+(?:\.\d+)?)?\s*(s|sec|secs|seconds?|m|min|mins|minutes?|h|hr|hrs|hours?)?\s*(.*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            bool pomo = m.Groups[1].Value.Equals("pomodoro", StringComparison.OrdinalIgnoreCase);
            if (!m.Groups[2].Success)
            {
                if (!pomo) return false;
                span = TimeSpan.FromMinutes(25);
            }
            else
            {
                span = Span(m.Groups[2].Value, m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : "m");
            }
            label = m.Groups[4].Value.Trim();
            if (label.Length == 0) label = pomo ? "pomodoro" : "";
            return span.TotalSeconds >= 1;
        }

        static TimeSpan Span(string n, string unit)
        {
            double v = double.Parse(n, CultureInfo.InvariantCulture);
            char u = char.ToLowerInvariant(unit[0]);
            return u switch
            {
                's' => TimeSpan.FromSeconds(v),
                'h' => TimeSpan.FromHours(v),
                'd' => TimeSpan.FromDays(v),
                _ => TimeSpan.FromMinutes(v)
            };
        }
    }

    internal sealed class FileQuery
    {
        public string Name = "";
        public List<string> Exts = new();
        public string? Kind;
        public DateTime? After;
        public DateTime? Before;
        public long? MinSize;
        public long? MaxSize;
        public string? Scope;
        public string Describe = "";
        public bool Natural;

        static readonly Dictionary<string, (string? kind, string[] exts)> Types = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = (null, new[] { ".pdf" }),
            ["pdfs"] = (null, new[] { ".pdf" }),
            ["image"] = ("picture", Array.Empty<string>()),
            ["images"] = ("picture", Array.Empty<string>()),
            ["photos"] = ("picture", Array.Empty<string>()),
            ["pictures"] = ("picture", Array.Empty<string>()),
            ["screenshots"] = ("picture", Array.Empty<string>()),
            ["videos"] = ("video", Array.Empty<string>()),
            ["video"] = ("video", Array.Empty<string>()),
            ["clips"] = ("video", Array.Empty<string>()),
            ["music"] = ("music", Array.Empty<string>()),
            ["songs"] = ("music", Array.Empty<string>()),
            ["docs"] = ("document", Array.Empty<string>()),
            ["documents"] = ("document", Array.Empty<string>()),
            ["spreadsheets"] = (null, new[] { ".xlsx", ".xls", ".csv" }),
            ["slides"] = (null, new[] { ".pptx", ".ppt", ".key" }),
            ["presentations"] = (null, new[] { ".pptx", ".ppt", ".key" }),
            ["zips"] = (null, new[] { ".zip", ".rar", ".7z" }),
            ["archives"] = (null, new[] { ".zip", ".rar", ".7z", ".tar", ".gz" }),
            ["code"] = (null, new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".rs", ".go", ".lua", ".json", ".html", ".css" }),
            ["installers"] = (null, new[] { ".exe", ".msi" }),
            ["exes"] = (null, new[] { ".exe" }),
            ["text"] = (null, new[] { ".txt", ".md" }),
            ["models"] = (null, new[] { ".fbx", ".obj", ".blend", ".stl", ".glb", ".gltf" })
        };

        public static FileQuery Parse(string input, bool forced)
        {
            var q = new FileQuery();
            string s = " " + input.Trim() + " ";
            var now = DateTime.Now;
            var parts = new List<string>();

            var lead = Regex.Match(s, @"^\s*(find|show|search|get|list|look for|where are|where's)\s+(me\s+)?(all\s+)?(my\s+)?(the\s+)?", RegexOptions.IgnoreCase);
            if (lead.Success)
            {
                q.Natural = true;
                s = " " + s[lead.Length..];
            }

            foreach (var kv in Types)
            {
                var m = Regex.Match(s, $@"\s{Regex.Escape(kv.Key)}\s", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                q.Kind ??= kv.Value.kind;
                q.Exts.AddRange(kv.Value.exts);
                if (kv.Key.Equals("screenshots", StringComparison.OrdinalIgnoreCase)) q.Name = "screenshot";
                s = s.Remove(m.Index, m.Length).Insert(m.Index, " ");
                parts.Add(kv.Key.ToLowerInvariant());
                q.Natural = true;
                break;
            }
            var extM = Regex.Match(s, @"\s\.?(png|jpg|jpeg|gif|mp4|mov|mp3|wav|txt|md|docx|xlsx|pptx|psd|zip|cs|py|js|json|toe|tox)\s+files?\s", RegexOptions.IgnoreCase);
            if (extM.Success)
            {
                q.Exts.Add("." + extM.Groups[1].Value.ToLowerInvariant());
                s = s.Remove(extM.Index, extM.Length).Insert(extM.Index, " ");
                parts.Add(extM.Groups[1].Value.ToLowerInvariant() + " files");
                q.Natural = true;
            }

            (string pattern, Func<(DateTime? a, DateTime? b)> range, string label)[] times =
            {
                (@"\btoday\b", () => (now.Date, null), "today"),
                (@"\byesterday\b", () => (now.Date.AddDays(-1), now.Date), "yesterday"),
                (@"\bthis\s+week\b", () => (now.Date.AddDays(-(int)now.DayOfWeek), null), "this week"),
                (@"\blast\s+week\b", () => (now.Date.AddDays(-(int)now.DayOfWeek - 7), now.Date.AddDays(-(int)now.DayOfWeek)), "last week"),
                (@"\bthis\s+month\b", () => (new DateTime(now.Year, now.Month, 1), null), "this month"),
                (@"\blast\s+month\b", () => (new DateTime(now.Year, now.Month, 1).AddMonths(-1), new DateTime(now.Year, now.Month, 1)), "last month"),
                (@"\bthis\s+year\b", () => (new DateTime(now.Year, 1, 1), null), "this year"),
                (@"\brecent(ly)?\b", () => (now.AddDays(-3), null), "recently")
            };
            foreach (var t in times)
            {
                var m = Regex.Match(s, @"(\b(modified|changed|edited|made|created|saved|downloaded|from)\s+)?" + t.pattern, RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var (a, b) = t.range();
                q.After = a;
                q.Before = b;
                s = s.Remove(m.Index, m.Length);
                parts.Add(t.label);
                q.Natural = true;
                break;
            }
            var lastN = Regex.Match(s, @"\b(in\s+the\s+)?(last|past)\s+(\d+)\s+(day|days|week|weeks|month|months|hour|hours)\b", RegexOptions.IgnoreCase);
            if (lastN.Success)
            {
                int n = int.Parse(lastN.Groups[3].Value);
                string u = lastN.Groups[4].Value.ToLowerInvariant();
                q.After = u.StartsWith("hour") ? now.AddHours(-n) : u.StartsWith("week") ? now.AddDays(-7 * n) : u.StartsWith("month") ? now.AddMonths(-n) : now.AddDays(-n);
                s = s.Remove(lastN.Index, lastN.Length);
                parts.Add($"last {n} {u}");
                q.Natural = true;
            }

            var size = Regex.Match(s, @"\b(bigger|larger|over|more)\s+(than\s+)?(\d+(?:\.\d+)?)\s*(kb|mb|gb)\b", RegexOptions.IgnoreCase);
            if (size.Success)
            {
                q.MinSize = Bytes(size.Groups[3].Value, size.Groups[4].Value);
                s = s.Remove(size.Index, size.Length);
                parts.Add($"over {size.Groups[3].Value}{size.Groups[4].Value.ToUpperInvariant()}");
                q.Natural = true;
            }
            var small = Regex.Match(s, @"\b(smaller|less|under)\s+(than\s+)?(\d+(?:\.\d+)?)\s*(kb|mb|gb)\b", RegexOptions.IgnoreCase);
            if (small.Success)
            {
                q.MaxSize = Bytes(small.Groups[3].Value, small.Groups[4].Value);
                s = s.Remove(small.Index, small.Length);
                parts.Add($"under {small.Groups[3].Value}{small.Groups[4].Value.ToUpperInvariant()}");
            }
            if (Regex.IsMatch(s, @"\b(big|huge|large)\s+files?\b", RegexOptions.IgnoreCase))
            {
                q.MinSize ??= 500L * 1024 * 1024;
                s = Regex.Replace(s, @"\b(big|huge|large)\s+files?\b", " ", RegexOptions.IgnoreCase);
                parts.Add("big");
                q.Natural = true;
            }

            var scope = Regex.Match(s, @"\b(in|on|from)\s+(my\s+)?(downloads|desktop|documents|pictures|videos|music)\b", RegexOptions.IgnoreCase);
            if (scope.Success)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                q.Scope = scope.Groups[3].Value.ToLowerInvariant() switch
                {
                    "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    _ => Path.Combine(home, "Downloads")
                };
                s = s.Remove(scope.Index, scope.Length);
                parts.Add("in " + scope.Groups[3].Value.ToLowerInvariant());
            }

            var named = Regex.Match(s, @"\b(named|called|with|containing|about)\s+(.+)$", RegexOptions.IgnoreCase);
            string rest;
            if (named.Success)
            {
                rest = named.Groups[2].Value;
                s = s[..named.Index];
            }
            else
            {
                rest = Regex.Replace(s, @"\b(files?|stuff|things|that|were|was|i|my|the|all|me)\b", " ", RegexOptions.IgnoreCase);
            }
            rest = Regex.Replace(rest, @"\s+", " ").Trim();
            if (rest.Length > 0)
            {
                q.Name = q.Name.Length > 0 ? q.Name + " " + rest : rest;
            }
            if (q.Name.Length > 0) parts.Add($"\"{q.Name}\"");
            if (!q.Natural && !forced) return q;
            q.Describe = parts.Count > 0 ? string.Join(" · ", parts) : "files";
            return q;
        }

        static long Bytes(string n, string unit)
        {
            double v = double.Parse(n, CultureInfo.InvariantCulture);
            return (long)(v * unit.ToLowerInvariant() switch { "kb" => 1024, "gb" => 1024d * 1024 * 1024, _ => 1024d * 1024 });
        }

        static string Esc(string v) => v.Replace("'", "''");

        public string Sql(int top)
        {
            var where = new List<string> { "SCOPE='file:'" };
            if (Scope != null) where[0] = $"SCOPE='file:{Esc(Scope)}'";
            foreach (var word in Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))
                where.Add($"System.FileName LIKE '%{Esc(word.Replace("%", ""))}%'");
            if (Kind != null) where.Add($"System.Kind = '{Kind}'");
            if (Exts.Count > 0) where.Add("(" + string.Join(" OR ", Exts.Distinct().Select(e => $"System.FileExtension = '{Esc(e)}'")) + ")");
            if (After is DateTime a) where.Add($"System.DateModified >= '{a.ToUniversalTime():yyyy-MM-dd HH:mm:ss}'");
            if (Before is DateTime b) where.Add($"System.DateModified < '{b.ToUniversalTime():yyyy-MM-dd HH:mm:ss}'");
            if (MinSize is long mn) where.Add($"System.Size >= {mn}");
            if (MaxSize is long mx) where.Add($"System.Size <= {mx}");
            string order = MinSize != null ? "System.Size DESC" : "System.DateModified DESC";
            return $"SELECT TOP {top} System.ItemPathDisplay FROM SystemIndex WHERE {string.Join(" AND ", where)} ORDER BY {order}";
        }

        public static async Task<List<string>> Run(FileQuery q, int top, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var list = new List<string>();
                try
                {
                    using var con = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                    con.Open();
                    using var cmd = new OleDbCommand(q.Sql(top), con);
                    using var r = cmd.ExecuteReader();
                    while (r != null && r.Read())
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!r.IsDBNull(0)) list.Add(r.GetString(0));
                    }
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("search failed " + ex.Message);
                }
                return list;
            }, ct);
        }
    }
}
