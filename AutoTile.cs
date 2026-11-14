using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace Elysium
{
    internal sealed class AutoTile
    {
        readonly Settings _s;
        readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(450) };
        readonly HashSet<string> _dirty = new();
        readonly Dictionary<IntPtr, string> _where = new();

        public AutoTile(Settings s)
        {
            _s = s;
            _later.Tick += (_, _) =>
            {
                _later.Stop();
                foreach (var key in _dirty.ToList()) Arrange(key);
                _dirty.Clear();
            };
        }

        public void Opened(IntPtr h)
        {
            if (!_s.AutoTile || !Fits(h)) return;
            var f = Native.FrameBounds(h);
            var d = Displays.At(f.Left + f.Width / 2, f.Top + f.Height / 2);
            _where[h] = d.Key;
            Mark(d.Key);
        }

        public void Closed()
        {
            if (!_s.AutoTile) return;
            foreach (var kv in _where.Where(kv => !Native.IsWindow(kv.Key) || !Native.IsWindowVisible(kv.Key)).ToList())
            {
                _where.Remove(kv.Key);
                Mark(kv.Value);
            }
        }

        public void Minimized(IntPtr h)
        {
            if (!_s.AutoTile || !_where.TryGetValue(h, out var key)) return;
            Mark(key);
        }

        void Mark(string key)
        {
            _dirty.Add(key);
            _later.Stop();
            _later.Start();
        }

        public void Now()
        {
            foreach (var d in Displays.All()) Arrange(d.Key);
        }

        static bool Fits(IntPtr h)
        {
            if (!WinList.IsApp(h) || Native.IsIconic(h)) return false;
            if ((Native.Style(h) & Native.WS_THICKFRAME) == 0) return false;
            if (CursorLock.IsFullscreen(h, out _)) return false;
            var f = Native.FrameBounds(h);
            return f.Width >= 200 && f.Height >= 150;
        }

        void Arrange(string key)
        {
            var d = Displays.All().FirstOrDefault(x => x.Key == key);
            if (d == null) return;
            var wins = new List<IntPtr>();
            foreach (var w in WinList.All(false))
            {
                if (!Fits(w.Hwnd)) continue;
                var f = Native.FrameBounds(w.Hwnd);
                if (Displays.At(f.Left + f.Width / 2, f.Top + f.Height / 2).Key != key) continue;
                wins.Add(w.Hwnd);
                _where[w.Hwnd] = key;
            }
            if (wins.Count == 0) return;
            var fg = Native.GetForegroundWindow();
            wins = wins.OrderBy(h => h == fg ? 0 : 1).ToList();
            var rects = Layout(d, wins.Count);
            for (int i = 0; i < wins.Count; i++)
            {
                var h = wins[i];
                if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                SnapZones.Place(h, rects[i]);
            }
        }

        List<Native.RECT> Layout(Display d, int n)
        {
            int gap = (int)Math.Round(Math.Clamp(_s.TileGap, 0, 40) * d.Scale);
            var w = d.Work;
            var list = new List<Native.RECT>();
            Native.RECT R(int x, int y, int cw, int ch) => new() { Left = x, Top = y, Right = x + cw, Bottom = y + ch };
            int fullW = w.Width - gap * 2, fullH = w.Height - gap * 2;
            if (n == 1)
            {
                list.Add(R(w.Left + gap, w.Top + gap, fullW, fullH));
                return list;
            }
            int half = (fullW - gap) / 2;
            list.Add(R(w.Left + gap, w.Top + gap, half, fullH));
            int rest = n - 1;
            int cols = rest <= 3 ? 1 : 2;
            int rows = (int)Math.Ceiling(rest / (double)cols);
            int colW = (half - gap * (cols - 1)) / cols;
            int rowH = (fullH - gap * (rows - 1)) / rows;
            for (int i = 0; i < rest; i++)
            {
                int c = i / rows, r = i % rows;
                int x = w.Left + gap + half + gap + c * (colW + gap);
                int y = w.Top + gap + r * (rowH + gap);
                list.Add(R(x, y, colW, rowH));
            }
            return list;
        }
    }
}
