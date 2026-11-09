using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

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
            public double Still, Age;
        }

        static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
        };

        readonly Settings _s;
        readonly Native.WinEventProc _proc;
        readonly IntPtr _hook;
        readonly Dictionary<IntPtr, Body> _bodies = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<(double t, double x, double y)> _samples = new();
        readonly List<double> _turns = new();
        readonly uint _pid = (uint)Environment.ProcessId;

        List<Native.RECT> _work = new();
        List<(IntPtr h, Native.RECT r)> _others = new();
        double _workAt = -10, _othersAt = -10, _last;
        IntPtr _drag;
        int _dragW, _dragH, _lastDir;
        double _extremeX;
        bool _ticking;

        public PhysicsEngine(Settings settings)
        {
            _s = settings;
            _proc = OnEvent;
            _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        double Now => _clock.Elapsed.TotalSeconds;

        public int Moving => _bodies.Count;

        public void StopAll()
        {
            _bodies.Clear();
            _drag = IntPtr.Zero;
        }

        void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || !_s.Enabled) return;

            if (evt == Native.EVENT_SYSTEM_MOVESIZESTART)
            {
                if (!Eligible(hwnd)) return;
                _bodies.Remove(hwnd);
                var r = Native.FrameBounds(hwnd);
                _drag = hwnd;
                _dragW = r.Width;
                _dragH = r.Height;
                _samples.Clear();
                _turns.Clear();
                _lastDir = 0;
                _extremeX = r.Left;
                StartTicking();
                Sample();
            }
            else if (evt == Native.EVENT_SYSTEM_MOVESIZEEND && hwnd == _drag)
            {
                Sample();
                _drag = IntPtr.Zero;
                Release(hwnd);
            }
        }

        bool Eligible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) return false;
            if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd)) return false;
            if ((Native.Style(hwnd) & Native.WS_CHILD) != 0) return false;
            if (ShellClasses.Contains(Native.ClassName(hwnd))) return false;

            var ignored = _s.IgnoredSet();
            if (ignored.Count > 0)
            {
                var path = Native.ProcessPath(hwnd);
                if (path != null && ignored.Contains(Path.GetFileNameWithoutExtension(path))) return false;
            }
            return true;
        }

        void Sample()
        {
            if (_drag == IntPtr.Zero) return;
            var r = Native.FrameBounds(_drag);
            double t = Now;
            _samples.Add((t, r.Left, r.Top));
            while (_samples.Count > 2 && t - _samples[0].t > 0.25) _samples.RemoveAt(0);

            if (!_s.ShakeToMinimize) return;

            double x = r.Left;
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
                _turns.Add(t);
                _lastDir = -_lastDir;
                _extremeX = x;
            }

            _turns.RemoveAll(v => t - v > 0.9);
            int needed = (int)Math.Round(7 - 4 * _s.ShakeSensitivity);
            if (_turns.Count >= needed)
            {
                var target = _drag;
                _drag = IntPtr.Zero;
                _turns.Clear();
                Native.ShowWindow(target, Native.SW_MINIMIZE);
            }
        }

        void Release(IntPtr hwnd)
        {
            if (!Native.IsWindow(hwnd) || Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) return;
            var r = Native.FrameBounds(hwnd);
            if (Math.Abs(r.Width - _dragW) > 2 || Math.Abs(r.Height - _dragH) > 2) return;

            double vx = 0, vy = 0;
            if (_samples.Count >= 2)
            {
                var last = _samples[^1];
                var first = last;
                for (int i = _samples.Count - 1; i >= 0; i--)
                {
                    if (last.t - _samples[i].t > 0.09) break;
                    first = _samples[i];
                }
                double dt = last.t - first.t;
                if (dt > 0.012)
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
            if (speed < 250 && !grav) return;

            Native.GetWindowRect(hwnd, out var wr);
            _bodies[hwnd] = new Body
            {
                Hwnd = hwnd,
                X = r.Left,
                Y = r.Top,
                W = r.Width,
                H = r.Height,
                OffX = r.Left - wr.Left,
                OffY = r.Top - wr.Top,
                Vx = vx,
                Vy = vy
            };
            StartTicking();
        }

        void StartTicking()
        {
            if (_ticking) return;
            _ticking = true;
            _last = Now;
            CompositionTarget.Rendering += Tick;
        }

        void StopTicking()
        {
            if (!_ticking) return;
            _ticking = false;
            CompositionTarget.Rendering -= Tick;
        }

        void Tick(object? sender, EventArgs e)
        {
            double now = Now;
            double dt = Math.Min(now - _last, 1.0 / 30.0);
            _last = now;

            if (_drag != IntPtr.Zero)
            {
                if (Native.IsWindow(_drag)) Sample();
                else _drag = IntPtr.Zero;
            }

            if (dt > 0 && _bodies.Count > 0 && _s.Enabled) Step(dt, now);
            if (!_s.Enabled) _bodies.Clear();

            if (_drag == IntPtr.Zero && _bodies.Count == 0) StopTicking();
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

        public void Dispose()
        {
            StopTicking();
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        }
    }
}
