using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Heft
{
    public sealed class WindowRule
    {
        public string Exe { get; set; } = "";
        public string Place { get; set; } = "spot";
        public string Monitor { get; set; } = "";
        public int MonitorIndex { get; set; } = -1;
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; } = 0.5;
        public double H { get; set; } = 1;
        public bool Topmost { get; set; }
        public bool On { get; set; } = true;
    }

    internal sealed class WindowRules
    {
        public static readonly (string id, string label)[] Places =
        {
            ("spot", "Same spot"),
            ("max", "Maximized"),
            ("center", "Centered"),
            ("none", "Leave it")
        };

        readonly Settings _s;
        IntPtr _last;
        string _lastPath = "";

        public WindowRules(Settings s)
        {
            _s = s;
        }

        public void Saw(IntPtr hwnd, string? path)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
            string exe = Path.GetFileNameWithoutExtension(path);
            if (exe.Equals("heft", StringComparison.OrdinalIgnoreCase) || exe.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return;
            _last = hwnd;
            _lastPath = path;
        }

        public IntPtr LastHwnd => _last != IntPtr.Zero && Native.IsWindow(_last) ? _last : IntPtr.Zero;

        public string LastExe => _lastPath.Length == 0 ? "" : Path.GetFileNameWithoutExtension(_lastPath);

        public WindowRule? Capture()
        {
            var h = _last;
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return null;
            string exe = LastExe;
            if (exe.Length == 0) return null;
            var rule = _s.WindowRules.FirstOrDefault(r => r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
            bool fresh = rule == null;
            rule ??= new WindowRule { Exe = exe };
            var fb = Native.FrameBounds(h);
            var all = Displays.All();
            var d = all.FirstOrDefault(x => x.Bounds.Contains(fb.Left + fb.Width / 2, fb.Top + fb.Height / 2)) ?? Displays.Primary();
            rule.Monitor = d.Key;
            rule.MonitorIndex = d.Index;
            if (Native.IsZoomed(h))
            {
                rule.Place = "max";
            }
            else
            {
                rule.Place = "spot";
                var w = d.Work;
                rule.X = Math.Round((double)(fb.Left - w.Left) / Math.Max(1, w.Width), 4);
                rule.Y = Math.Round((double)(fb.Top - w.Top) / Math.Max(1, w.Height), 4);
                rule.W = Math.Round((double)fb.Width / Math.Max(1, w.Width), 4);
                rule.H = Math.Round((double)fb.Height / Math.Max(1, w.Height), 4);
            }
            if (fresh) _s.WindowRules.Add(rule);
            _s.Touch();
            return rule;
        }

        static Display Pick(WindowRule r, List<Display> all)
        {
            return all.FirstOrDefault(d => d.Key == r.Monitor)
                ?? all.FirstOrDefault(d => d.Index == r.MonitorIndex)
                ?? Displays.Primary();
        }

        public void Opened(IntPtr hwnd, string path)
        {
            if (_s.WindowRules.Count == 0) return;
            string exe = Path.GetFileNameWithoutExtension(path);
            var rule = _s.WindowRules.FirstOrDefault(r => r.On && r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
            if (rule == null) return;
            Apply(hwnd, rule);
            var again = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            again.Tick += (_, _) =>
            {
                again.Stop();
                if (Native.IsWindow(hwnd)) Apply(hwnd, rule, true);
            };
            again.Start();
        }

        public static void Apply(IntPtr h, WindowRule rule, bool gentle = false)
        {
            if (!Native.IsWindow(h) || Native.IsIconic(h)) return;
            var d = Pick(rule, Displays.All());
            var w = d.Work;
            switch (rule.Place)
            {
                case "max":
                {
                    var fb = Native.FrameBounds(h);
                    bool there = d.Bounds.Contains(fb.Left + fb.Width / 2, fb.Top + fb.Height / 2);
                    if (Native.IsZoomed(h) && there) break;
                    if (gentle && Native.IsZoomed(h)) break;
                    if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                    var fit = new Native.RECT { Left = w.Left + w.Width / 8, Top = w.Top + w.Height / 8, Right = w.Right - w.Width / 8, Bottom = w.Bottom - w.Height / 8 };
                    SnapZones.Place(h, fit);
                    Native.ShowWindow(h, Native.SW_MAXIMIZE);
                    break;
                }
                case "center":
                {
                    if (Native.IsZoomed(h)) break;
                    var fb = Native.FrameBounds(h);
                    int cw = Math.Min(fb.Width, w.Width), ch = Math.Min(fb.Height, w.Height);
                    int l = w.Left + (w.Width - cw) / 2, t = w.Top + (w.Height - ch) / 2;
                    if (gentle && Math.Abs(fb.Left - l) < 4 && Math.Abs(fb.Top - t) < 4) break;
                    SnapZones.Place(h, new Native.RECT { Left = l, Top = t, Right = l + cw, Bottom = t + ch });
                    break;
                }
                case "spot":
                {
                    if (Native.IsZoomed(h) && gentle) break;
                    var target = new Native.RECT
                    {
                        Left = w.Left + (int)Math.Round(rule.X * w.Width),
                        Top = w.Top + (int)Math.Round(rule.Y * w.Height),
                        Right = w.Left + (int)Math.Round((rule.X + rule.W) * w.Width),
                        Bottom = w.Top + (int)Math.Round((rule.Y + rule.H) * w.Height)
                    };
                    if (target.Width < 120 || target.Height < 80) break;
                    var fb = Native.FrameBounds(h);
                    if (gentle && Math.Abs(fb.Left - target.Left) < 4 && Math.Abs(fb.Top - target.Top) < 4 && Math.Abs(fb.Width - target.Width) < 4) break;
                    SnapZones.Place(h, target);
                    break;
                }
            }
            if (rule.Topmost) Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }
}
