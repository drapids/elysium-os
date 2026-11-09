using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Heft
{
    internal sealed class PhysicsEngine : IDisposable
    {
        sealed class Body
        {
            public IntPtr Hwnd;
            public double X, Y, Vx, Vy;
            public int W, H, OffX, OffY;
            public int LastX = int.MinValue, LastY = int.MinValue;
            public double Still, Age, Wait;
        }

        static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow",
            "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "#32768"
        };

        static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heft", "log.txt");

        readonly Settings _s;
        readonly Thread _thread;
        readonly Dictionary<IntPtr, Body> _bodies = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<(double t, double x, double y)> _samples = new();
        readonly List<double> _turns = new();
        readonly uint _pid = (uint)Environment.ProcessId;

        volatile bool _alive = true;
        volatile bool _clear;

        List<Native.RECT> _work = new();
        List<(IntPtr h, Native.RECT r)> _others = new();
        double _workAt = -10, _othersAt = -10, _last, _swapAt = -10;
        int _button = 0x01;
        bool _down, _dragging;
        IntPtr _cand;
        Native.RECT _startRect;
        int _offX, _offY, _lastDir;
        double _extremeX;

        public PhysicsEngine(Settings settings)
        {
            _s = settings;
            _thread = new Thread(Loop) { IsBackground = true, Name = "heft-physics", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            Log("started");
        }

        double Now => _clock.Elapsed.TotalSeconds;

        public void StopAll() => _clear = true;

        void Loop()
        {
            Native.timeBeginPeriod(1);
            try
            {
                _last = Now;
                while (_alive)
                {
                    try
                    {
                        Frame();
                    }
                    catch (Exception ex)
                    {
                        Log("error " + ex.GetType().Name + ": " + ex.Message);
                        _bodies.Clear();
                        _cand = IntPtr.Zero;
                        _dragging = false;
                    }
                    bool busy = _down || _bodies.Count > 0;
                    Thread.Sleep(busy ? 3 : 10);
                }
            }
            finally
            {
                Native.timeEndPeriod(1);
            }
        }

        void Frame()
        {
            double now = Now;
            double dt = Math.Min(now - _last, 1.0 / 30.0);
            _last = now;

            if (_clear)
            {
                _clear = false;
                _bodies.Clear();
            }

            if (now - _swapAt > 2)
            {
                _swapAt = now;
                _button = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? 0x02 : 0x01;
            }

            bool enabled = _s.Enabled;
            bool down = (Native.GetAsyncKeyState(_button) & 0x8000) != 0;
            Native.GetCursorPos(out var c);

            if (!enabled)
            {
                _bodies.Clear();
                _cand = IntPtr.Zero;
                _dragging = false;
                _down = down;
                return;
            }

            if (down && !_down) Press(c, now);
            else if (down && _down) Hold(c, now);
            else if (!down && _down) Release(now);
            _down = down;

            if (_bodies.Count > 0 && dt > 0) Step(dt, now);
        }

        bool Eligible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) return false;
            if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd)) return false;
            if ((Native.Style(hwnd) & Native.WS_CHILD) != 0) return false;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _pid) return false;
            if (ShellClasses.Contains(Native.ClassName(hwnd))) return false;

            var ignored = _s.IgnoredSet();
            if (ignored.Count > 0)
            {
                var path = Native.ProcessPath(pid);
                if (path != null && ignored.Contains(Path.GetFileNameWithoutExtension(path))) return false;
            }
            return true;
        }

        void Press(Native.POINT c, double now)
        {
            _cand = IntPtr.Zero;
            _dragging = false;
            _samples.Clear();
            _turns.Clear();
            _lastDir = 0;

            var h = Native.WindowFromPoint(c);
            if (h == IntPtr.Zero) return;
            h = Native.GetAncestor(h, Native.GA_ROOT);
            if (_bodies.Remove(h)) Log("caught a flying window");
            if (!Eligible(h)) return;

            var r = Native.FrameBounds(h);
            _cand = h;
            _startRect = r;
            _offX = c.X - r.Left;
            _offY = c.Y - r.Top;
            _extremeX = c.X;
        }

        void Hold(Native.POINT c, double now)
        {
            if (_cand == IntPtr.Zero) return;
            if (!Native.IsWindow(_cand))
            {
                _cand = IntPtr.Zero;
                return;
            }

            var r = Native.FrameBounds(_cand);
            if (Math.Abs(r.Width - _startRect.Width) > 2 || Math.Abs(r.Height - _startRect.Height) > 2)
            {
                _cand = IntPtr.Zero;
                _dragging = false;
                return;
            }

            if (!_dragging)
            {
                bool moved = r.Left != _startRect.Left || r.Top != _startRect.Top;
                if (!moved) return;
                bool follows = Math.Abs(c.X - r.Left - _offX) < 48 && Math.Abs(c.Y - r.Top - _offY) < 48;
                if (!follows)
                {
                    _cand = IntPtr.Zero;
                    return;
                }
                _dragging = true;
                Log("drag " + Describe(_cand));
            }

            _samples.Add((now, c.X, c.Y));
            while (_samples.Count > 2 && now - _samples[0].t > 0.25) _samples.RemoveAt(0);
            Shake(c.X, now);
        }

        void Shake(double x, double now)
        {
            if (!_s.ShakeToMinimize) return;
            double delta = x - _extremeX;
            const double reach = 26;

            if (_lastDir == 0)
            {
                if (Math.Abs(delta) > reach)
                {
                    _lastDir = Math.Sign(delta);
                    _extremeX = x;
                }
            }
            else if (Math.Sign(delta) == _lastDir)
            {
                _extremeX = x;
            }
            else if (Math.Abs(delta) > reach)
            {
                _turns.Add(now);
                _lastDir = -_lastDir;
                _extremeX = x;
            }

            _turns.RemoveAll(v => now - v > 0.9);
            int needed = (int)Math.Round(7 - 4 * _s.ShakeSensitivity);
            if (_turns.Count >= needed)
            {
                var target = _cand;
                _cand = IntPtr.Zero;
                _dragging = false;
                _turns.Clear();
                Native.ShowWindow(target, Native.SW_MINIMIZE);
                Log("shake minimized " + Describe(target));
            }
        }

        void Release(double now)
        {
            var h = _cand;
            bool dragging = _dragging;
            _cand = IntPtr.Zero;
            _dragging = false;
            if (h == IntPtr.Zero || !dragging) return;
            if (!Native.IsWindow(h) || Native.IsZoomed(h) || Native.IsIconic(h)) return;

            var r = Native.FrameBounds(h);
            if (Math.Abs(r.Width - _startRect.Width) > 2 || Math.Abs(r.Height - _startRect.Height) > 2) return;

            double vx = 0, vy = 0;
            if (_samples.Count >= 2)
            {
                var last = _samples[^1];
                var first = last;
                for (int i = _samples.Count - 1; i >= 0; i--)
                {
                    if (now - _samples[i].t > 0.08) break;
                    first = _samples[i];
                }
                double dt = last.t - first.t;
                if (dt > 0.01 && now - last.t < 0.05)
                {
                    vx = (last.x - first.x) / dt;
                    vy = (last.y - first.y) / dt;
                }
            }

            vx *= _s.Throw;
            vy *= _s.Throw;
            double speed = Math.Sqrt(vx * vx + vy * vy);
            const double max = 9000;
            if (speed > max)
            {
                vx *= max / speed;
                vy *= max / speed;
                speed = max;
            }

            bool grav = _s.Gravity > 0.001;
            if (speed < 220 && !grav)
            {
                Log($"dropped, too slow to throw ({speed:0} px/s)");
                return;
            }

            Native.GetWindowRect(h, out var wr);
            _bodies[h] = new Body
            {
                Hwnd = h,
                X = r.Left,
                Y = r.Top,
                W = r.Width,
                H = r.Height,
                OffX = r.Left - wr.Left,
                OffY = r.Top - wr.Top,
                Vx = vx,
                Vy = vy,
                Wait = 0.03
            };
            Log($"threw {Describe(h)} at {speed:0} px/s");
        }

        void Step(double dt, double now)
        {
            RefreshWork(now);
            bool grav = _s.Gravity > 0.001;
            double g = _s.Gravity * 6500;
            double drag = _s.Friction * 4.0;
            double bounce = _s.Bounce;
            if (grav && _s.Stacking) RefreshOthers(now);

            var dead = new List<IntPtr>();
            foreach (var b in _bodies.Values)
            {
                b.Age += dt;
                if (b.Age > 15 || !Native.IsWindow(b.Hwnd) || !Native.IsWindowVisible(b.Hwnd) || Native.IsIconic(b.Hwnd) || Native.IsZoomed(b.Hwnd))
                {
                    dead.Add(b.Hwnd);
                    continue;
                }

                if (b.Wait > 0)
                {
                    b.Wait -= dt;
                    if (b.Wait <= 0)
                    {
                        var r = Native.FrameBounds(b.Hwnd);
                        if (Math.Abs(r.Width - b.W) > 2 || Math.Abs(r.Height - b.H) > 2)
                        {
                            dead.Add(b.Hwnd);
                            continue;
                        }
                        b.X = r.Left;
                        b.Y = r.Top;
                    }
                    continue;
                }

                double prevBottom = b.Y + b.H;
                if (grav) b.Vy += g * dt;
                b.Vx *= Math.Exp(-drag * dt);
                b.Vy *= Math.Exp(-(grav ? drag * 0.25 : drag) * dt);
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;

                bool grounded = Collide(b, prevBottom, bounce, grav);
                if (grounded) b.Vx *= Math.Exp(-(drag * 2 + 2.5) * dt);

                double speed = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                bool settling = grav ? grounded && speed < 30 : speed < 30;
                if (settling)
                {
                    b.Still += dt;
                    if (b.Still > 0.08) dead.Add(b.Hwnd);
                }
                else
                {
                    b.Still = 0;
                }

                Apply(b);
            }

            foreach (var h in dead) _bodies.Remove(h);
        }

        bool Collide(Body b, double prevBottom, double bounce, bool grav)
        {
            int cx = (int)(b.X + b.W / 2.0);
            int cy = (int)(b.Y + b.H / 2.0);
            var a = AreaFor(cx, cy);
            bool grounded = false;

            if (b.W >= a.Width)
            {
                b.X = a.Left;
                b.Vx = 0;
            }
            else
            {
                if (b.X < a.Left && !Open(a.Left - 2, cy))
                {
                    b.X = a.Left;
                    if (b.Vx < 0) b.Vx = -b.Vx * bounce;
                }
                if (b.X + b.W > a.Right && !Open(a.Right + 1, cy))
                {
                    b.X = a.Right - b.W;
                    if (b.Vx > 0) b.Vx = -b.Vx * bounce;
                }
            }

            if (b.H >= a.Height)
            {
                b.Y = a.Top;
                b.Vy = 0;
                return true;
            }

            if (b.Y < a.Top && !Open(cx, a.Top - 2))
            {
                b.Y = a.Top;
                if (b.Vy < 0) b.Vy = -b.Vy * bounce;
            }
            if (b.Y + b.H >= a.Bottom && !Open(cx, a.Bottom + 1))
            {
                b.Y = a.Bottom - b.H;
                if (b.Vy > 0)
                {
                    b.Vy = -b.Vy * bounce;
                    if (grav && Math.Abs(b.Vy) < 140) b.Vy = 0;
                }
                grounded = true;
            }

            if (grav && _s.Stacking && b.Vy >= 0 && !grounded)
            {
                double bottom = b.Y + b.H;
                foreach (var (h, r0) in _others)
                {
                    if (h == b.Hwnd) continue;
                    var r = r0;
                    if (_bodies.TryGetValue(h, out var other))
                    {
                        r.Left = (int)other.X;
                        r.Top = (int)other.Y;
                        r.Right = r.Left + other.W;
                        r.Bottom = r.Top + other.H;
                    }
                    double overlap = Math.Min(b.X + b.W, r.Right) - Math.Max(b.X, r.Left);
                    if (overlap < 60) continue;
                    if (prevBottom <= r.Top + 2 && bottom >= r.Top)
                    {
                        b.Y = r.Top - b.H;
                        b.Vy = -b.Vy * bounce * 0.6;
                        if (Math.Abs(b.Vy) < 160) b.Vy = 0;
                        grounded = true;
                        break;
                    }
                }
            }

            return grounded;
        }

        void Apply(Body b)
        {
            int x = (int)Math.Round(b.X) - b.OffX;
            int y = (int)Math.Round(b.Y) - b.OffY;
            if (x == b.LastX && y == b.LastY) return;
            b.LastX = x;
            b.LastY = y;
            Native.SetWindowPos(b.Hwnd, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
        }

        bool Open(int x, int y)
        {
            if (!_s.CrossMonitors) return false;
            foreach (var w in _work)
                if (w.Contains(x, y)) return true;
            return false;
        }

        Native.RECT AreaFor(int x, int y)
        {
            foreach (var w in _work)
                if (w.Contains(x, y)) return w;
            return Native.WorkAreaAt(x, y);
        }

        void RefreshWork(double now)
        {
            if (now - _workAt < 1.0 && _work.Count > 0) return;
            _workAt = now;
            var list = new List<Native.RECT>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr hdc, ref Native.RECT rect, IntPtr data) =>
            {
                var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
                if (Native.GetMonitorInfo(mon, ref info)) list.Add(info.rcWork);
                return true;
            }, IntPtr.Zero);
            _work = list;
        }

        void RefreshOthers(double now)
        {
            if (now - _othersAt < 0.25) return;
            _othersAt = now;
            var list = new List<(IntPtr, Native.RECT)>();
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h)) return true;
                if (Native.IsCloaked(h)) return true;
                if ((Native.ExStyle(h) & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if ((Native.Style(h) & Native.WS_CHILD) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == _pid) return true;
                if (ShellClasses.Contains(Native.ClassName(h))) return true;
                var r = Native.FrameBounds(h);
                if (r.Width < 120 || r.Height < 80) return true;
                list.Add((h, r));
                return true;
            }, IntPtr.Zero);
            _others = list;
        }

        static string Describe(IntPtr h)
        {
            var path = Native.ProcessPath(h);
            return (path != null ? Path.GetFileName(path) : "?") + " / " + Native.ClassName(h);
        }

        static readonly object LogLock = new();

        public static void Log(string line)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    var info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > 256 * 1024) info.Delete();
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            _alive = false;
            _thread.Join(500);
        }
    }
}
