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

        readonly object _gate = new();
        IntPtr _lerpReq;
        Native.POINT _lerpReqPt;
        bool _lerpEndReq;
        IntPtr _lerp;
        double _lx, _ly, _lvx, _lvy, _ltx, _lty, _lStart;
        int _loffX, _loffY, _lfx, _lfy, _lw, _lh;
        int _lLastX = int.MinValue, _lLastY = int.MinValue;
        bool _lerpHeld;

        IntPtr _stackTarget;
        double _stackSince, _stackCheckAt;
        bool _stackArmed;

        public event Action<IntPtr, bool>? StackHover;
        public event Action<IntPtr, IntPtr>? StackDrop;

        public PhysicsEngine(Settings settings)
        {
            _s = settings;
            _thread = new Thread(Loop) { IsBackground = true, Name = "heft-physics", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            Log("started");
        }

        double Now => _clock.Elapsed.TotalSeconds;

        readonly Dictionary<IntPtr, (int x, int y, double at, int px, int py)> _sent = new();
        double _gap = 1.0 / 60, _gapAt = -10;

        void Pace(Native.POINT c, double now)
        {
            if (now - _gapAt < 1) return;
            _gapAt = now;
            try
            {
                int hz = Native.RefreshRate(Displays.At(c.X, c.Y));
                _gap = 1.0 / Math.Clamp(hz, 30, 500);
            }
            catch
            {
                _gap = 1.0 / 60;
            }
            if (_sent.Count > 64) _sent.Clear();
        }

        bool Send(IntPtr h, int x, int y, double now, bool force)
        {
            int px = x, py = y;
            if (_sent.TryGetValue(h, out var last))
            {
                if (last.x == x && last.y == y) return true;
                double since = now - last.at;
                if (!force && since < _gap - 0.0006) return false;
                if (!force && since < 0.05 && (last.px != last.x || last.py != last.y) && Native.GetWindowRect(h, out var r) && r.Left == last.px && r.Top == last.py) return false;
                px = last.x;
                py = last.y;
            }
            else if (Native.GetWindowRect(h, out var first))
            {
                px = first.Left;
                py = first.Top;
            }
            _sent[h] = (x, y, now, px, py);
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
            return true;
        }

        public volatile bool Hush;

        public event Action<IntPtr>? LerpBegan;
        public event Action<IntPtr>? LerpDropped;

        public bool LerpHeld => _lerpHeld && _lerp != IntPtr.Zero;

        public void StopAll() => _clear = true;

        public bool LerpWanted => _s.Enabled && _s.Lerp;

        public bool CanLerp(IntPtr hwnd) => Eligible(hwnd);

        public void BeginLerp(IntPtr hwnd, Native.POINT pt)
        {
            lock (_gate)
            {
                _lerpReq = hwnd;
                _lerpReqPt = pt;
                _lerpEndReq = false;
            }
        }

        public void EndLerp()
        {
            lock (_gate) _lerpEndReq = true;
        }

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
                        _lerp = IntPtr.Zero;
                        _lerpHeld = false;
                    }
                    bool busy = _down || _bodies.Count > 0 || _lerp != IntPtr.Zero;
                    Thread.Sleep(busy ? 1 : 10);
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
            Pace(c, now);

            IntPtr startLerp;
            Native.POINT startPt;
            bool endLerp;
            lock (_gate)
            {
                startLerp = _lerpReq;
                startPt = _lerpReqPt;
                endLerp = _lerpEndReq;
                _lerpReq = IntPtr.Zero;
                _lerpEndReq = false;
            }

            if (!enabled)
            {
                _bodies.Clear();
                _cand = IntPtr.Zero;
                _dragging = false;
                _lerp = IntPtr.Zero;
                _down = down;
                ResetStack();
                return;
            }

            if (startLerp != IntPtr.Zero) StartLerp(startLerp, startPt, now);
            if (endLerp && _lerp != IntPtr.Zero && _lerpHeld) ReleaseLerp(c, now);
            if (_lerp != IntPtr.Zero)
            {
                _cand = IntPtr.Zero;
                _dragging = false;
                if (dt > 0) LerpStep(c, now, dt);
                _down = down;
                if (_bodies.Count > 0 && dt > 0) Step(dt, now);
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
            ResetStack();
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
            if (_cand != IntPtr.Zero) StackCheck(c, now, _cand);
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
                var target = _lerp != IntPtr.Zero ? _lerp : _cand;
                _cand = IntPtr.Zero;
                _dragging = false;
                _turns.Clear();
                if (_lerp != IntPtr.Zero) StopLerp();
                if (target == IntPtr.Zero) return;
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
            if (Hush)
            {
                ResetStack();
                return;
            }
            if (h != IntPtr.Zero && dragging && _stackArmed && _stackTarget != IntPtr.Zero)
            {
                var target = _stackTarget;
                ResetStack();
                StackDrop?.Invoke(h, target);
                return;
            }
            ResetStack();
            if (h == IntPtr.Zero || !dragging) return;
            if (!Native.IsWindow(h) || Native.IsZoomed(h) || Native.IsIconic(h)) return;

            var r = Native.FrameBounds(h);
            if (Math.Abs(r.Width - _startRect.Width) > 2 || Math.Abs(r.Height - _startRect.Height) > 2) return;

            var (vx, vy) = CursorVelocity(now);

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
            Sfx.Play("throw");
        }

        (double vx, double vy) CursorVelocity(double now)
        {
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
                double span = last.t - first.t;
                if (span > 0.01 && now - last.t < 0.05)
                {
                    vx = (last.x - first.x) / span;
                    vy = (last.y - first.y) / span;
                }
            }
            return (vx, vy);
        }

        void StartLerp(IntPtr h, Native.POINT p, double now)
        {
            if (!Native.IsWindow(h) || Native.IsZoomed(h) || Native.IsIconic(h)) return;
            _bodies.Remove(h);
            ResetStack();
            var r = Native.FrameBounds(h);
            Native.GetWindowRect(h, out var wr);
            _lerp = h;
            _lx = r.Left;
            _ly = r.Top;
            _lvx = 0;
            _lvy = 0;
            _loffX = p.X - r.Left;
            _loffY = p.Y - r.Top;
            _ltx = _lx;
            _lty = _ly;
            _lfx = r.Left - wr.Left;
            _lfy = r.Top - wr.Top;
            _lw = r.Width;
            _lh = r.Height;
            _lLastX = int.MinValue;
            _lLastY = int.MinValue;
            _lerpHeld = true;
            _lStart = now;
            _samples.Clear();
            _turns.Clear();
            _lastDir = 0;
            _extremeX = p.X;
            Native.ForceForeground(h);
            LerpBegan?.Invoke(h);
        }

        void StopLerp()
        {
            _lerp = IntPtr.Zero;
            _lerpHeld = false;
            ResetStack();
        }

        void LerpStep(Native.POINT c, double now, double dt)
        {
            var h = _lerp;
            if (!Native.IsWindow(h) || Native.IsIconic(h) || Native.IsZoomed(h))
            {
                StopLerp();
                return;
            }

            if (_lerpHeld)
            {
                _ltx = c.X - _loffX;
                _lty = c.Y - _loffY;
                _samples.Add((now, c.X, c.Y));
                while (_samples.Count > 2 && now - _samples[0].t > 0.25) _samples.RemoveAt(0);
                if (now - _lStart > 120)
                {
                    ReleaseLerp(c, now);
                    if (_lerp == IntPtr.Zero) return;
                }
            }

            double lag = Math.Clamp(_s.LerpLag, 0, 1);
            double k = 900 * Math.Pow(40.0 / 900.0, lag);
            double zeta = 1.0 - 0.65 * Math.Clamp(_s.LerpWobble, 0, 1);
            double damp = 2 * zeta * Math.Sqrt(k);
            double left = Math.Min(dt, 0.05);
            while (left > 1e-6)
            {
                double step = Math.Min(0.004, left);
                double ax = k * (_ltx - _lx) - damp * _lvx;
                double ay = k * (_lty - _ly) - damp * _lvy;
                _lvx += ax * step;
                _lvy += ay * step;
                _lx += _lvx * step;
                _ly += _lvy * step;
                left -= step;
            }

            bool done = false;
            if (!_lerpHeld && Math.Abs(_ltx - _lx) < 0.6 && Math.Abs(_lty - _ly) < 0.6 && Math.Abs(_lvx) < 25 && Math.Abs(_lvy) < 25)
            {
                _lx = _ltx;
                _ly = _lty;
                done = true;
            }

            int x = (int)Math.Round(_lx) - _lfx;
            int y = (int)Math.Round(_ly) - _lfy;
            if ((x != _lLastX || y != _lLastY) && Send(h, x, y, now, done))
            {
                _lLastX = x;
                _lLastY = y;
            }

            if (_lerpHeld)
            {
                Shake(c.X, now);
                if (_lerp == IntPtr.Zero) return;
                StackCheck(c, now, h);
            }
            if (done) StopLerp();
        }

        void ReleaseLerp(Native.POINT c, double now)
        {
            var h = _lerp;
            _lerpHeld = false;
            _ltx = c.X - _loffX;
            _lty = c.Y - _loffY;

            if (Hush)
            {
                ResetStack();
                StopLerp();
                LerpDropped?.Invoke(h);
                return;
            }

            if (_stackArmed && _stackTarget != IntPtr.Zero)
            {
                var target = _stackTarget;
                StopLerp();
                StackDrop?.Invoke(h, target);
                return;
            }
            ResetStack();

            if (_s.LerpSnap && Snap(h, c))
            {
                StopLerp();
                return;
            }

            var (vx, vy) = CursorVelocity(now);
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
            if (speed < 260 && !grav) return;

            var wr = new Native.RECT();
            Native.GetWindowRect(h, out wr);
            var fr = Native.FrameBounds(h);
            StopLerp();
            _bodies[h] = new Body
            {
                Hwnd = h,
                X = fr.Left,
                Y = fr.Top,
                W = fr.Width,
                H = fr.Height,
                OffX = fr.Left - wr.Left,
                OffY = fr.Top - wr.Top,
                Vx = vx,
                Vy = vy,
                Wait = 0
            };
            Log($"lerp threw {Describe(h)} at {speed:0} px/s");
            Sfx.Play("throw");
        }

        bool Snap(IntPtr h, Native.POINT c)
        {
            IntPtr mon = Native.MonitorFromPoint(c, Native.MONITOR_DEFAULTTONEAREST);
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(mon, ref info)) return false;
            var b = info.rcMonitor;
            var w = info.rcWork;
            if (c.Y <= b.Top + 1)
            {
                Native.ShowWindow(h, Native.SW_MAXIMIZE);
                return true;
            }
            bool left = c.X <= b.Left + 1;
            bool right = c.X >= b.Right - 2;
            if (!left && !right) return false;
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out var wr);
            var fr = Native.FrameBounds(h);
            int padL = fr.Left - wr.Left, padT = fr.Top - wr.Top, padR = wr.Right - fr.Right, padB = wr.Bottom - fr.Bottom;
            int half = w.Width / 2;
            int x = left ? w.Left : w.Left + half;
            int width = left ? half : w.Width - half;
            Native.SetWindowPos(h, IntPtr.Zero, x - padL, w.Top - padT, width + padL + padR, w.Height + padT + padB, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            return true;
        }

        void ResetStack()
        {
            bool was = _stackArmed;
            _stackArmed = false;
            _stackTarget = IntPtr.Zero;
            if (was) StackHover?.Invoke(IntPtr.Zero, false);
        }

        void StackCheck(Native.POINT c, double now, IntPtr dragged)
        {
            if (!_s.WindowStacks) return;
            if (now - _stackCheckAt < 0.06) return;
            _stackCheckAt = now;
            IntPtr under = WindowBelow(c, dragged);
            IntPtr t = under != IntPtr.Zero && CaptionAt(under, c) ? under : IntPtr.Zero;
            if (t != _stackTarget)
            {
                bool was = _stackArmed;
                _stackTarget = t;
                _stackSince = now;
                _stackArmed = false;
                if (was) StackHover?.Invoke(IntPtr.Zero, false);
                return;
            }
            if (t != IntPtr.Zero && !_stackArmed && now - _stackSince > 0.35)
            {
                _stackArmed = true;
                StackHover?.Invoke(t, true);
            }
        }

        IntPtr WindowBelow(Native.POINT c, IntPtr dragged)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, _) =>
            {
                if (h == dragged) return true;
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return true;
                if ((Native.ExStyle(h) & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if ((Native.Style(h) & Native.WS_CHILD) != 0) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == _pid) return true;
                var r = Native.FrameBounds(h);
                if (!r.Contains(c.X, c.Y)) return true;
                if (Native.GetWindowTextLength(h) == 0 || ShellClasses.Contains(Native.ClassName(h)))
                {
                    found = IntPtr.Zero;
                    return false;
                }
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static bool CaptionAt(IntPtr h, Native.POINT c)
        {
            var r = Native.FrameBounds(h);
            int packed = ((c.Y & 0xFFFF) << 16) | (c.X & 0xFFFF);
            IntPtr ok = Native.SendMessageTimeout(h, Native.WM_NCHITTEST, IntPtr.Zero, new IntPtr(packed), 2, 40, out IntPtr hit);
            if (ok != IntPtr.Zero && hit.ToInt64() == Native.HTCAPTION) return true;
            double scale = Native.DpiScaleAt(c.X, c.Y);
            return c.Y - r.Top < 30 * scale && c.Y >= r.Top;
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

                Apply(b, now, dead.Contains(b.Hwnd));
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

        void Apply(Body b, double now, bool last)
        {
            int x = (int)Math.Round(b.X) - b.OffX;
            int y = (int)Math.Round(b.Y) - b.OffY;
            if (x == b.LastX && y == b.LastY) return;
            if (!Send(b.Hwnd, x, y, now, last)) return;
            b.LastX = x;
            b.LastY = y;
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
