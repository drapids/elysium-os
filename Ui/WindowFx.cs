using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class FxLayer : Window
    {
        public readonly Image Pic = new() { Stretch = Stretch.Fill };
        public IntPtr Hwnd;
        public bool Busy;

        public FxLayer()
        {
            Overlay.ClickThrough(this);
            Title = "elysium fx";
            RenderOptions.SetBitmapScalingMode(Pic, BitmapScalingMode.LowQuality);
            Content = Pic;
            SourceInitialized += (_, _) => Hwnd = new WindowInteropHelper(this).Handle;
        }

        public void Cover(Native.RECT r)
        {
            if (Hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            Width = double.NaN;
            Height = double.NaN;
            Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height), Native.SWP_NOACTIVATE);
            if (!IsVisible) Show();
        }

        public void Park()
        {
            Pic.BeginAnimation(OpacityProperty, null);
            Pic.RenderTransform = null;
            Pic.Source = null;
            Pic.Opacity = 1;
            if (Hwnd != IntPtr.Zero) Native.SetWindowPos(Hwnd, IntPtr.Zero, -32000, -32000, 1, 1, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            Busy = false;
        }
    }

    internal sealed class WindowFx : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiAnim(uint action, uint size, ref ANIMATIONINFO info, uint winIni);

        const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        readonly Settings _s;
        readonly List<FxLayer> _pool = new();
        readonly Dictionary<IntPtr, Native.RECT> _rects = new();
        readonly HashSet<IntPtr> _seen = new();
        readonly HashSet<IntPtr> _parked = new();
        bool _min, _anim, _follow;
        int _savedMin = -1;
        int _running;
        (string exe, Native.RECT icon, long at)? _launch;
        (IntPtr h, BitmapSource shot, Native.RECT r, long at)? _armed;

        public WindowFx(Settings s)
        {
            _s = s;
            WinEvents.Minimized += OnMinimized;
            WinEvents.Foreground += Track;
            WinEvents.Moved += h =>
            {
                if (h == WinEvents.Current) Track(h);
            };
            WinEvents.Shown += OnShown;
            WinEvents.Gone += OnGone;
        }

        public void Sync()
        {
            bool min = _s.MinimizeToDock, anim = _s.WindowAnims;
            if (min != _min)
            {
                _min = min;
                SystemMinimize(!min);
            }
            if (anim != _anim)
            {
                _anim = anim;
                WinEvents.Lifecycle(anim);
                if (anim)
                    foreach (var w in WinList.All()) _seen.Add(w.Hwnd);
            }
            bool follow = min || anim;
            if (follow != _follow)
            {
                _follow = follow;
                WinEvents.FollowForeground(follow);
                if (follow) Track(Native.GetForegroundWindow());
            }
        }

        void SystemMinimize(bool on)
        {
            try
            {
                var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                if (!on)
                {
                    if (!SpiAnim(0x48, info.cbSize, ref info, 0)) return;
                    if (_savedMin < 0) _savedMin = info.iMinAnimate;
                    info.iMinAnimate = 0;
                    SpiAnim(0x49, info.cbSize, ref info, 2);
                }
                else if (_savedMin >= 0)
                {
                    info.iMinAnimate = _savedMin;
                    SpiAnim(0x49, info.cbSize, ref info, 2);
                    _savedMin = -1;
                }
            }
            catch
            {
            }
        }

        void Track(IntPtr h)
        {
            if (!_follow || h == IntPtr.Zero || Native.IsIconic(h) || _parked.Contains(h)) return;
            var r = Native.FrameBounds(h);
            if (r.Width < 60 || r.Height < 40 || r.Left <= -30000) return;
            _rects[h] = r;
            if (_rects.Count > 80)
                foreach (var dead in _rects.Keys.Where(k => !Native.IsWindow(k)).ToList()) _rects.Remove(dead);
        }

        public void Launched(string path, Rect iconPx)
        {
            string exe = Exe(path);
            if (exe.Length == 0) return;
            _launch = (exe, new Native.RECT { Left = (int)iconPx.Left, Top = (int)iconPx.Top, Right = (int)iconPx.Right, Bottom = (int)iconPx.Bottom }, Environment.TickCount64);
        }

        static string Exe(string path)
        {
            try
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var t = ShellLink.Target(path);
                    if (t != null) path = t;
                }
                return System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        FxLayer? Layer()
        {
            var free = _pool.FirstOrDefault(l => !l.Busy);
            if (free == null)
            {
                if (_pool.Count >= 4) return null;
                free = new FxLayer();
                _pool.Add(free);
            }
            free.Busy = true;
            return free;
        }

        Native.POINT Target(Native.RECT r)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            var all = Displays.All();
            var d = Displays.At(cx, cy, all);
            foreach (var (display, side) in Displays.DockEdges(_s, all))
            {
                if (display.Index != d.Index) continue;
                var b = display.Bounds;
                return side switch
                {
                    "left" => new Native.POINT { X = b.Left + 6, Y = Math.Clamp(cy, b.Top + 60, b.Bottom - 60) },
                    "top" => new Native.POINT { X = Math.Clamp(cx, b.Left + 60, b.Right - 60), Y = b.Top + 6 },
                    "bottom" => new Native.POINT { X = Math.Clamp(cx, b.Left + 60, b.Right - 60), Y = b.Bottom - 6 },
                    _ => new Native.POINT { X = b.Right - 6, Y = Math.Clamp(cy, b.Top + 60, b.Bottom - 60) }
                };
            }
            return new Native.POINT { X = d.Bounds.Left + d.Bounds.Width / 2, Y = d.Bounds.Bottom - 6 };
        }

        static Native.RECT Box(double cx, double cy, double w, double h) => new()
        {
            Left = (int)Math.Round(cx - w / 2),
            Top = (int)Math.Round(cy - h / 2),
            Right = (int)Math.Round(cx + w / 2),
            Bottom = (int)Math.Round(cy + h / 2)
        };

        static Native.RECT Union(Native.RECT a, Native.RECT b, int pad) => new()
        {
            Left = Math.Min(a.Left, b.Left) - pad,
            Top = Math.Min(a.Top, b.Top) - pad,
            Right = Math.Max(a.Right, b.Right) + pad,
            Bottom = Math.Max(a.Bottom, b.Bottom) + pad
        };

        void OnMinimized(IntPtr h, bool start)
        {
            if (!_min || _running > 3) return;
            try
            {
                if (start)
                {
                    if (!_rects.TryGetValue(h, out var r))
                    {
                        if (Native.IsIconic(h)) return;
                        r = Native.FrameBounds(h);
                    }
                    if (r.Width < 60 || r.Height < 40 || !WinList.IsApp(h)) return;
                    var p = Target(r);
                    Genie(h, r, p, true, null);
                }
                else
                {
                    if (Native.IsZoomed(h) || !WinList.IsApp(h)) return;
                    var r = Native.FrameBounds(h);
                    if (r.Width < 60 || r.Height < 40) return;
                    if (CursorLock.IsFullscreen(h, out _)) return;
                    var p = Target(r);
                    if (!Park(h)) return;
                    Genie(h, r, p, false, () => Unpark(h));
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("minimize fx " + ex.Message);
            }
        }

        bool Park(IntPtr h)
        {
            if (!Native.GetWindowRect(h, out var wr)) return false;
            var d = Displays.At(wr.Left + wr.Width / 2, wr.Top + wr.Height / 2);
            _parked.Add(h);
            _parkedAt[h] = wr;
            Native.SetWindowPos(h, IntPtr.Zero, wr.Left, d.Bounds.Bottom + 40, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            return true;
        }

        readonly Dictionary<IntPtr, Native.RECT> _parkedAt = new();

        void Unpark(IntPtr h)
        {
            if (!_parked.Remove(h)) return;
            if (!_parkedAt.Remove(h, out var wr)) return;
            if (Native.IsWindow(h) && !Native.IsIconic(h))
                Native.SetWindowPos(h, IntPtr.Zero, wr.Left, wr.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }

        void Genie(IntPtr h, Native.RECT r, Native.POINT p, bool shrink, Action? done)
        {
            var layer = Layer();
            if (layer == null)
            {
                done?.Invoke();
                return;
            }
            var end = new Native.RECT { Left = p.X - 2, Top = p.Y - 2, Right = p.X + 2, Bottom = p.Y + 2 };
            var bbox = Union(r, end, 8);
            layer.Cover(bbox);
            var thumb = new Thumb(layer.Hwnd, h);
            if (!thumb.Valid)
            {
                thumb.Dispose();
                layer.Park();
                done?.Invoke();
                return;
            }
            _running++;
            double rx = r.Left + r.Width / 2.0, ry = r.Top + r.Height / 2.0;
            double ms = (shrink ? 340 : 300) * Theme.Speed;
            Frames.Run(Math.Max(120, ms), t =>
            {
                double k = shrink ? t : 1 - t;
                double ex = k * k;
                double ey = 1 - (1 - k) * (1 - k);
                double size = 1 - 0.95 * (shrink ? k * k * (3 - 2 * k) : 1 - Math.Pow(1 - k, 1.6));
                double cx = rx + (p.X - rx) * (shrink ? ex : ey);
                double cy = ry + (p.Y - ry) * (shrink ? ey : ex);
                var box = Box(cx - bbox.Left, cy - bbox.Top, r.Width * size, r.Height * Math.Max(0.04, size * (1 - 0.25 * Math.Sin(Math.PI * k))));
                double fade = k < 0.6 ? 1 : 1 - (k - 0.6) / 0.4;
                thumb.Place(box, (byte)Math.Clamp(255 * fade, 0, 255));
            }, () =>
            {
                done?.Invoke();
                Finish(layer, thumb);
            });
        }

        void Finish(FxLayer layer, Thumb thumb)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(34) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                thumb.Dispose();
                layer.Park();
                _running = Math.Max(0, _running - 1);
            };
            t.Start();
        }

        void OnShown(IntPtr h, uint time)
        {
            if (!_anim || _seen.Contains(h)) return;
            try
            {
                if (!WinList.IsApp(h)) return;
                _seen.Add(h);
                if (_seen.Count > 400) _seen.RemoveWhere(x => !Native.IsWindow(x));
                uint age = unchecked((uint)Environment.TickCount - time);
                if (age > 60 || _running > 3) return;
                if (Native.IsZoomed(h) || CursorLock.IsFullscreen(h, out _)) return;
                var r = Native.FrameBounds(h);
                if (r.Width < 160 || r.Height < 100) return;
                Native.RECT from;
                string exe = Exe(Native.ProcessPath(h) ?? "");
                if (_launch is { } l && Environment.TickCount64 - l.at < 6000 && l.exe == exe)
                {
                    from = l.icon;
                    _launch = null;
                }
                else from = Box(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0, r.Width * 0.9, r.Height * 0.9);
                if (!Park(h)) return;
                Grow(h, from, r, () => Unpark(h));
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("open fx " + ex.Message);
            }
        }

        void Grow(IntPtr h, Native.RECT from, Native.RECT to, Action done)
        {
            var layer = Layer();
            if (layer == null)
            {
                done();
                return;
            }
            var bbox = Union(from, to, 8);
            layer.Cover(bbox);
            var thumb = new Thumb(layer.Hwnd, h);
            if (!thumb.Valid)
            {
                thumb.Dispose();
                layer.Park();
                done();
                return;
            }
            _running++;
            bool far = from.Width < to.Width * 0.5;
            Frames.Run(Math.Max(120, (far ? 300 : 210) * Theme.Speed), t =>
            {
                double e = far ? Frames.Quint(t) : Frames.Out(t);
                double cx = (from.Left + from.Width / 2.0) + ((to.Left + to.Width / 2.0) - (from.Left + from.Width / 2.0)) * e;
                double cy = (from.Top + from.Height / 2.0) + ((to.Top + to.Height / 2.0) - (from.Top + from.Height / 2.0)) * e;
                double w = from.Width + (to.Width - from.Width) * e, hh = from.Height + (to.Height - from.Height) * e;
                var box = Box(cx - bbox.Left, cy - bbox.Top, w, hh);
                thumb.Place(box, (byte)Math.Clamp(255 * Math.Min(1, t * (far ? 2.5 : 1.6)), 0, 255));
            }, () =>
            {
                done();
                Finish(layer, thumb);
            });
        }

        public void ArmClose(Native.POINT pt)
        {
            if (!_anim) return;
            try
            {
                var h = Native.WindowFromPoint(pt);
                if (h == IntPtr.Zero) return;
                h = Native.GetAncestor(h, 2);
                if (!WinList.IsApp(h) || Native.IsZoomed(h) && CursorLock.IsFullscreen(h, out _)) return;
                IntPtr lp = new IntPtr((pt.Y << 16) | (pt.X & 0xFFFF));
                if (Native.SendMessageTimeout(h, (uint)Native.WM_NCHITTEST, IntPtr.Zero, lp, 0x2, 40, out var hit) == IntPtr.Zero) return;
                if (hit.ToInt64() != 20) return;
                Snap(h);
            }
            catch
            {
            }
        }

        public void ArmForeground()
        {
            if (!_anim) return;
            var h = Native.GetForegroundWindow();
            if (!WinList.IsApp(h)) return;
            Snap(h);
        }

        void Snap(IntPtr h)
        {
            var r = Native.FrameBounds(h);
            if (r.Width < 120 || r.Height < 80 || r.Left <= -30000) return;
            var shot = Backdrop.Shot(r);
            if (shot == null) return;
            int off = 1;
            Native.DwmSetWindowAttribute(h, DWMWA_TRANSITIONS_FORCEDISABLED, ref off, sizeof(int));
            _armed = (h, shot, r, Environment.TickCount64);
        }

        void OnGone(IntPtr h)
        {
            _seen.Remove(h);
            _rects.Remove(h);
            if (_armed is not { } a || a.h != h) return;
            _armed = null;
            if (!_anim || Environment.TickCount64 - a.at > 6000) return;
            var layer = Layer();
            if (layer == null) return;
            layer.Pic.Source = a.shot;
            layer.Cover(a.r);
            _running++;
            var scale = new ScaleTransform(1, 1);
            layer.Pic.RenderTransformOrigin = new Point(0.5, 0.5);
            layer.Pic.RenderTransform = scale;
            Frames.Run(Math.Max(100, 190 * Theme.Speed), t =>
            {
                double e = Frames.Out(t);
                double s = 1 - 0.08 * e;
                scale.ScaleX = s;
                scale.ScaleY = s;
                layer.Pic.Opacity = 1 - e;
            }, () =>
            {
                layer.Park();
                _running = Math.Max(0, _running - 1);
            });
        }

        public void Dispose()
        {
            SystemMinimize(true);
            foreach (var h in _parked.ToList()) Unpark(h);
            foreach (var l in _pool)
            {
                try
                {
                    l.Close();
                }
                catch
                {
                }
            }
        }
    }
}
