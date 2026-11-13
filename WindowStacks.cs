using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Heft.Ui;

namespace Heft
{
    internal sealed class WindowStacks
    {
        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        class TaskbarList
        {
        }

        [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList
        {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        }

        sealed class Stack
        {
            public readonly List<IntPtr> Wins = new();
            public IntPtr Active;
            public TabStrip? Strip;
            public Native.RECT Frame;
            public bool Max;
            public string Sig = "";
        }

        const int Away = -25000;

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly List<Stack> _stacks = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        StackHint? _hint;

        public WindowStacks(Settings s, AppCatalog cat)
        {
            _s = s;
            _cat = cat;
            _timer.Tick += (_, _) => Tick();
        }

        public int Count => _stacks.Count;

        public void RestoreLeftovers()
        {
            if (_s.StackedAway.Count == 0) return;
            foreach (var e in _s.StackedAway.ToList())
            {
                var h = new IntPtr(e.Hwnd);
                if (!Native.IsWindow(h)) continue;
                Native.SetWindowPos(h, IntPtr.Zero, e.X, e.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                if (e.Max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
            }
            _s.StackedAway.Clear();
            _s.Touch();
        }

        public void Hover(IntPtr target, bool on)
        {
            try
            {
                if (!on || target == IntPtr.Zero || !Native.IsWindow(target))
                {
                    _hint?.Fade();
                    return;
                }
                _hint ??= new StackHint();
                _hint.Over(Native.FrameBounds(target));
                Sfx.Play("tick");
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("stack hint failed " + ex.Message);
            }
        }

        public void Join(IntPtr dragged, IntPtr target)
        {
            _hint?.Fade();
            if (dragged == target || !Native.IsWindow(dragged) || !Native.IsWindow(target)) return;
            var from = Find(dragged);
            if (from != null) Take(from, dragged, false);

            var st = Find(target);
            if (st == null)
            {
                st = new Stack { Active = target, Frame = Native.FrameBounds(target), Max = Native.IsZoomed(target) };
                st.Wins.Add(target);
                _stacks.Add(st);
            }
            else if (st.Active != target)
            {
                Activate(st, target, false);
            }
            if (!st.Wins.Contains(dragged)) st.Wins.Add(dragged);
            Activate(st, dragged, true);
            Sfx.Play("stack");
            _timer.Start();
            PhysicsEngine.Log($"stacked {st.Wins.Count} windows");
        }

        Stack? Find(IntPtr h) => _stacks.FirstOrDefault(s => s.Wins.Contains(h));

        public bool IsStacked(IntPtr h) => Find(h) != null;

        public void Cycle(IntPtr h, int dir)
        {
            var st = Find(h);
            if (st == null || st.Wins.Count < 2) return;
            int i = st.Wins.IndexOf(st.Active);
            int next = ((i + dir) % st.Wins.Count + st.Wins.Count) % st.Wins.Count;
            Activate(st, st.Wins[next], true);
            Sfx.Play("tick");
        }

        public void CycleForeground() => Cycle(Native.GetForegroundWindow(), 1);

        void Activate(Stack st, IntPtr h, bool focus)
        {
            if (!Native.IsWindow(h)) return;
            var old = st.Active;
            if (old != h && Native.IsWindow(old) && Visible(old))
            {
                st.Frame = Native.FrameBounds(old);
                st.Max = Native.IsZoomed(old);
            }

            if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Place(h, st.Frame);
            if (st.Max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
            Tab(h, true);

            if (old != h && Native.IsWindow(old))
            {
                if (Native.IsZoomed(old)) Native.ShowWindow(old, Native.SW_RESTORE);
                Native.SetWindowPos(old, IntPtr.Zero, Away, Away, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            st.Active = h;
            if (focus) Native.ForceForeground(h);
            Strip(st, true);
            Persist();
        }

        static bool Visible(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            var r = Native.FrameBounds(h);
            return r.Left > -10000 && r.Width > 0;
        }

        static void Place(IntPtr h, Native.RECT frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0) return;
            Native.GetWindowRect(h, out var wr);
            var fb = Native.FrameBounds(h);
            int padL = Math.Clamp(fb.Left - wr.Left, 0, 40), padT = Math.Clamp(fb.Top - wr.Top, 0, 40);
            int padR = Math.Clamp(wr.Right - fb.Right, 0, 40), padB = Math.Clamp(wr.Bottom - fb.Bottom, 0, 40);
            Native.SetWindowPos(h, IntPtr.Zero, frame.Left - padL, frame.Top - padT, frame.Width + padL + padR, frame.Height + padT + padB, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void Take(IntPtr h) => Take(Find(h), h, true);

        void Take(Stack? st, IntPtr h, bool focus)
        {
            if (st == null) return;
            if (st.Active == h && st.Wins.Count > 1)
            {
                int i = st.Wins.IndexOf(h);
                var next = st.Wins[(i + 1) % st.Wins.Count];
                Activate(st, next, false);
            }
            st.Wins.Remove(h);
            if (Native.IsWindow(h))
            {
                var f = st.Frame;
                int off = (int)(36 * Native.DpiScaleAt(f.Left + 10, f.Top + 10));
                var r = new Native.RECT { Left = f.Left + off, Top = f.Top + off, Right = f.Right + off, Bottom = f.Bottom + off };
                Place(h, r);
                Tab(h, true);
                if (focus) Native.ForceForeground(h);
            }
            if (st.Wins.Count < 2) Dissolve(st);
            else Strip(st, true);
            Persist();
        }

        void Dissolve(Stack st)
        {
            foreach (var w in st.Wins)
            {
                if (!Native.IsWindow(w)) continue;
                if (w != st.Active || !Visible(w))
                {
                    Place(w, st.Frame);
                    if (st.Max) Native.ShowWindow(w, Native.SW_MAXIMIZE);
                }
                Tab(w, true);
            }
            CloseStrip(st);
            _stacks.Remove(st);
            if (_stacks.Count == 0) _timer.Stop();
            Persist();
        }

        public void ReleaseAll()
        {
            foreach (var st in _stacks.ToList())
            {
                int n = 0;
                foreach (var w in st.Wins)
                {
                    if (!Native.IsWindow(w) || w == st.Active) continue;
                    n++;
                    int off = (int)(28 * n * Native.DpiScaleAt(st.Frame.Left + 10, st.Frame.Top + 10));
                    Place(w, new Native.RECT { Left = st.Frame.Left + off, Top = st.Frame.Top + off, Right = st.Frame.Right + off, Bottom = st.Frame.Bottom + off });
                    Tab(w, true);
                }
                CloseStrip(st);
            }
            _stacks.Clear();
            _timer.Stop();
            _s.StackedAway.Clear();
            _s.SaveNow();
            try
            {
                _hint?.Close();
            }
            catch
            {
            }
        }

        void Tick()
        {
            IntPtr fg = Native.GetForegroundWindow();
            foreach (var st in _stacks.ToList())
            {
                int before = st.Wins.Count;
                st.Wins.RemoveAll(w => !Native.IsWindow(w));
                if (st.Wins.Count < 2)
                {
                    Dissolve(st);
                    continue;
                }
                if (!st.Wins.Contains(st.Active))
                {
                    st.Active = IntPtr.Zero;
                    Activate(st, st.Wins[0], false);
                }
                else if (before != st.Wins.Count)
                {
                    Persist();
                }

                if (fg != st.Active && st.Wins.Contains(fg))
                {
                    Activate(st, fg, false);
                    continue;
                }

                if (Visible(st.Active))
                {
                    st.Frame = Native.FrameBounds(st.Active);
                    st.Max = Native.IsZoomed(st.Active);
                }
                Strip(st, false);
            }
        }

        void Strip(Stack st, bool rebuild)
        {
            try
            {
                if (st.Strip == null || st.Strip.Dead)
                {
                    st.Strip = new TabStrip(_cat);
                    st.Strip.Picked += h =>
                    {
                        var s2 = Find(h);
                        if (s2 != null)
                        {
                            Activate(s2, h, true);
                            Sfx.Play("tick");
                        }
                    };
                    st.Strip.Unstack += Take;
                    st.Strip.Scrolled += (h, dir) => Cycle(h, dir);
                    rebuild = true;
                }
                bool show = Native.IsWindow(st.Active) && Visible(st.Active);
                string sig = string.Join(",", st.Wins.Select(w => w.ToInt64() + ":" + Native.Title(w))) + "|" + st.Active.ToInt64();
                if (rebuild || sig != st.Sig)
                {
                    st.Sig = sig;
                    st.Strip.Build(st.Wins, st.Active);
                }
                st.Strip.Follow(st.Active, show);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("tab strip failed " + ex.Message);
                st.Strip = null;
            }
        }

        static void CloseStrip(Stack st)
        {
            try
            {
                st.Strip?.Kill();
            }
            catch
            {
            }
            st.Strip = null;
        }

        void Persist()
        {
            var list = new List<PocketEntry>();
            foreach (var st in _stacks)
            {
                foreach (var w in st.Wins)
                {
                    if (w == st.Active) continue;
                    list.Add(new PocketEntry { Hwnd = w.ToInt64(), X = st.Frame.Left, Y = st.Frame.Top, Max = st.Max, Title = Native.Title(w) });
                }
            }
            _s.StackedAway = list;
            _s.Touch();
        }

        ITaskbarList? _tb;
        bool _tbTried;

        void Tab(IntPtr h, bool add)
        {
            if (!_tbTried)
            {
                _tbTried = true;
                try
                {
                    _tb = (ITaskbarList)new TaskbarList();
                    _tb.HrInit();
                }
                catch
                {
                    _tb = null;
                }
            }
            try
            {
                if (add) _tb?.AddTab(h);
                else _tb?.DeleteTab(h);
            }
            catch
            {
            }
        }
    }

    internal sealed class TabStrip : Window
    {
        readonly AppCatalog _cat;
        readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        readonly Border _surface;
        IntPtr _owner;
        IntPtr _active;
        Native.RECT _last;
        bool _shownOnce;

        public bool Dead { get; private set; }
        public event Action<IntPtr>? Picked;
        public event Action<IntPtr>? Unstack;
        public event Action<IntPtr, int>? Scrolled;

        public TabStrip(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 10, true);
            Topmost = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "heft tabs";
            _surface = Glass.Surface(10);
            _surface.Child = _row;
            Content = _surface;
            Closed += (_, _) => Dead = true;
            MouseWheel += (_, e) =>
            {
                e.Handled = true;
                Scrolled?.Invoke(_active, e.Delta > 0 ? -1 : 1);
            };
        }

        public void Kill()
        {
            if (Dead) return;
            Dead = true;
            Close();
        }

        public void Build(List<IntPtr> wins, IntPtr active)
        {
            _active = active;
            _row.Children.Clear();
            foreach (var w in wins)
            {
                var h = w;
                bool on = h == active;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                var path = Native.ProcessPath(h);
                if (path != null) _cat.Icons.Get(path, 32, true, src => icon.Source = src);
                sp.Children.Add(icon);
                string title = Native.Title(h);
                if (title.Length == 0 && path != null) title = Path.GetFileNameWithoutExtension(path);
                var text = Kit.Text(title, 12, on, on ? "Text" : "SubText");
                text.MaxWidth = 170;
                sp.Children.Add(text);
                var tab = new Border { Child = sp, CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 5, 12, 6), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = title + "\nmiddle-click or right-click to take it out" };
                if (on) tab.SetResourceReference(Border.BackgroundProperty, "RowHover");
                else tab.Background = Brushes.Transparent;
                tab.MouseEnter += (_, _) =>
                {
                    if (!on) tab.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                };
                tab.MouseLeave += (_, _) =>
                {
                    if (!on) tab.Background = Brushes.Transparent;
                };
                tab.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Picked?.Invoke(h);
                };
                tab.MouseUp += (_, e) =>
                {
                    if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
                    e.Handled = true;
                    Unstack?.Invoke(h);
                };
                _row.Children.Add(tab);
            }
            _last = default;
        }

        public void Follow(IntPtr active, bool show)
        {
            if (Dead) return;
            var me = new WindowInteropHelper(this).EnsureHandle();
            if (!show)
            {
                if (IsVisible) Hide();
                return;
            }
            if (_owner != active)
            {
                _owner = active;
                Native.SetWindowLongPtr(me, Native.GWLP_HWNDPARENT, active);
                _last = default;
            }
            var f = Native.FrameBounds(active);
            if (f.Left == _last.Left && f.Top == _last.Top && f.Right == _last.Right && IsVisible) return;
            _last = f;
            if (!IsVisible)
            {
                if (!_shownOnce) _surface.Opacity = 0;
                Show();
            }
            UpdateLayout();
            Native.GetWindowRect(me, out var mine);
            int w = mine.Width, hgt = mine.Height;
            var work = Native.WorkAreaAt(f.Left + f.Width / 2, f.Top + 4);
            int x = f.Left + (int)(10 * Native.DpiScaleAt(f.Left + 10, f.Top + 10));
            int y = f.Top - hgt + 1;
            if (y < work.Top || Native.IsZoomed(active))
            {
                x = f.Left + (f.Width - w) / 2;
                y = f.Top + 3;
            }
            x = Math.Max(f.Left, Math.Min(x, f.Right - w));
            IntPtr prev = Native.GetWindow(active, 3);
            if (prev == me || (prev != IntPtr.Zero && (Native.ExStyle(prev) & Native.WS_EX_TOPMOST) != 0))
                Native.SetWindowPos(me, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            else
                Native.SetWindowPos(me, prev, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!_shownOnce)
            {
                _shownOnce = true;
                Glass.FadeIn(_surface, 6, 180);
                _surface.Opacity = 1;
            }
            Glass.SetBlur(this, true);
        }
    }

    internal sealed class StackHint : Window
    {
        readonly Border _box;

        public StackHint()
        {
            Glass.Attach(this, 12, true);
            Title = "heft stack hint";
            ShowActivated = false;
            IsHitTestVisible = false;
            _box = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2) };
            _box.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            _box.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var g = Kit.Icon("\uE8A9", 13, "AccentFill");
            g.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(g);
            sp.Children.Add(Kit.Text("let go to stack these as tabs", 12.5, true));
            _box.Child = sp;
            Content = _box;
            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                long ex = Native.ExStyle(h);
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | 0x20 | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE));
            };
        }

        public void Over(Native.RECT f)
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            double scale = Native.DpiScaleAt(f.Left + 10, f.Top + 10);
            int pad = (int)(6 * scale);
            int height = (int)(42 * scale);
            int width = Math.Max((int)(240 * scale), f.Width - pad * 2);
            Width = width / scale;
            Height = height / scale;
            if (!IsVisible)
            {
                _box.Opacity = 0;
                Show();
            }
            Native.SetWindowPos(h, Native.HWND_TOPMOST, f.Left + pad, f.Top + pad, width, height, Native.SWP_NOACTIVATE);
            _box.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        }

        public void Fade()
        {
            if (!IsVisible) return;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            a.Completed += (_, _) =>
            {
                if (_box.Opacity < 0.05) Hide();
            };
            _box.BeginAnimation(OpacityProperty, a);
        }
    }
}
