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
using Elysium.Ui;

namespace Elysium
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

        internal sealed class Stack
        {
            public readonly List<IntPtr> Wins = new();
            public IntPtr Active;
            public TabStrip? Strip;
            public WorkspacePill? Pill;
            public Native.RECT Frame;
            public bool Fill;
            public string Name = "";
            public string Layer = "normal";
            public bool Collapsed;
            public string Sig = "";
        }

        const int Away = -25000;

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly List<Stack> _stacks = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        readonly List<(string exe, Stack st, long until)> _waiting = new();
        StackHint? _hint;
        bool _wired;

        public Func<IReadOnlyList<(string name, string path)>>? Favorites;

        public WindowStacks(Settings s, AppCatalog cat)
        {
            _s = s;
            _cat = cat;
            _timer.Tick += (_, _) => Tick();
        }

        public int Count => _stacks.Count;

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            WinEvents.Moved += OnMoved;
            WinEvents.Foreground += OnForeground;
            WinEvents.Minimized += OnMinimized;
        }

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

        static string Exe(IntPtr h) => Path.GetFileNameWithoutExtension(Native.ProcessPath(h) ?? "");

        string Nice(IntPtr h)
        {
            var p = Native.ProcessPath(h);
            return p == null ? Native.Title(h) : _cat.DisplayName(p);
        }

        public void Join(IntPtr dragged, IntPtr target)
        {
            _hint?.Fade();
            if (dragged == target || !Native.IsWindow(dragged) || !Native.IsWindow(target)) return;
            Wire();
            var from = Find(dragged);
            if (from != null) Take(from, dragged, false);

            var st = Find(target);
            if (st == null)
            {
                st = new Stack { Active = target, Frame = Native.FrameBounds(target) };
                if (Native.IsZoomed(target)) MakeFill(st, target);
                st.Wins.Add(target);
                st.Name = Nice(target);
                _stacks.Add(st);
                WinEvents.Watch(target);
            }
            else if (st.Active != target)
            {
                Activate(st, target, false);
            }
            if (!st.Wins.Contains(dragged))
            {
                st.Wins.Add(dragged);
                WinEvents.Watch(dragged);
                CountPair(target, dragged);
                if (st.Wins.Count == 2 && st.Name == Nice(target)) st.Name = Nice(target) + " + " + Nice(dragged);
            }
            Activate(st, dragged, true);
            Sfx.Play("stack");
            _timer.Start();
            PhysicsEngine.Log($"stacked {st.Wins.Count} windows");
        }

        void CountPair(IntPtr a, IntPtr b)
        {
            string ea = Exe(a).ToLowerInvariant(), eb = Exe(b).ToLowerInvariant();
            if (ea.Length == 0 || eb.Length == 0 || ea == eb) return;
            string key = string.CompareOrdinal(ea, eb) < 0 ? ea + "|" + eb : eb + "|" + ea;
            _s.StackPairs[key] = _s.StackPairs.TryGetValue(key, out int n) ? n + 1 : 1;
            _s.Touch();
        }

        List<string> PairsFor(Stack st)
        {
            var mine = st.Wins.Select(w => Exe(w).ToLowerInvariant()).Where(x => x.Length > 0).ToHashSet();
            var scores = new Dictionary<string, int>();
            foreach (var kv in _s.StackPairs)
            {
                var parts = kv.Key.Split('|');
                if (parts.Length != 2) continue;
                if (mine.Contains(parts[0]) && !mine.Contains(parts[1])) scores[parts[1]] = scores.GetValueOrDefault(parts[1]) + kv.Value;
                if (mine.Contains(parts[1]) && !mine.Contains(parts[0])) scores[parts[0]] = scores.GetValueOrDefault(parts[0]) + kv.Value;
            }
            return scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(5).ToList();
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

        int StripPx(Native.RECT f) => (int)Math.Round(TabStrip.Tall * Native.DpiScaleAt(f.Left + 10, f.Top + 10));

        void MakeFill(Stack st, IntPtr h)
        {
            var fb = Native.FrameBounds(h);
            var work = Native.WorkAreaAt(fb.Left + fb.Width / 2, fb.Top + fb.Height / 2);
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            int top = StripPx(fb);
            st.Fill = true;
            st.Frame = new Native.RECT { Left = work.Left, Top = work.Top + top, Right = work.Right, Bottom = work.Bottom };
            SnapZones.Place(h, st.Frame);
        }

        void Activate(Stack st, IntPtr h, bool focus)
        {
            if (!Native.IsWindow(h)) return;
            var old = st.Active;
            if (old != h && Native.IsWindow(old) && Visible(old)) st.Frame = Native.FrameBounds(old);

            if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            SnapZones.Place(h, st.Frame);
            Tab(h, true);
            if (old != h && Native.IsWindow(old))
            {
                if (Native.IsZoomed(old)) Native.ShowWindow(old, Native.SW_RESTORE);
                if (st.Layer == "top") Native.SetWindowPos(old, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Native.SetWindowPos(old, IntPtr.Zero, Away, Away, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            st.Active = h;
            ApplyLayer(st);
            if (focus) Native.ForceForeground(h);
            Strip(st, true);
            Persist();
        }

        void ApplyLayer(Stack st)
        {
            var h = st.Active;
            if (!Native.IsWindow(h)) return;
            if (st.Layer == "top") Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            else if (st.Layer == "under") Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            else if ((Native.ExStyle(h) & Native.WS_EX_TOPMOST) != 0) Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        static bool Visible(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            var r = Native.FrameBounds(h);
            return r.Left > -10000 && r.Width > 0;
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
            WinEvents.Unwatch(h);
            if (Native.IsWindow(h))
            {
                var f = st.Frame;
                int off = (int)(36 * Native.DpiScaleAt(f.Left + 10, f.Top + 10));
                var r = new Native.RECT { Left = f.Left + off, Top = f.Top + off, Right = f.Right + off, Bottom = f.Bottom + off };
                SnapZones.Place(h, r);
                if ((Native.ExStyle(h) & Native.WS_EX_TOPMOST) != 0) Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
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
                WinEvents.Unwatch(w);
                if (!Native.IsWindow(w)) continue;
                if (w != st.Active || !Visible(w))
                {
                    if (Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_RESTORE);
                    SnapZones.Place(w, st.Frame);
                }
                if (st.Fill && w == st.Active) Native.ShowWindow(w, Native.SW_MAXIMIZE);
                if ((Native.ExStyle(w) & Native.WS_EX_TOPMOST) != 0) Native.SetWindowPos(w, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Tab(w, true);
            }
            CloseStrip(st);
            ClosePill(st);
            _stacks.Remove(st);
            if (_stacks.Count == 0) _timer.Stop();
            Persist();
            Sweep();
        }

        void Sweep()
        {
            foreach (var strip in TabStrip.Live.ToList())
                if (!_stacks.Any(x => x.Strip == strip)) strip.Kill();
            foreach (var st in _stacks)
                if (st.Pill != null && !st.Collapsed) ClosePill(st);
        }

        public void ReleaseAll()
        {
            foreach (var st in _stacks.ToList())
            {
                int n = 0;
                foreach (var w in st.Wins)
                {
                    if (!Native.IsWindow(w)) continue;
                    if (Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_RESTORE);
                    if (w == st.Active) continue;
                    n++;
                    int off = (int)(28 * n * Native.DpiScaleAt(st.Frame.Left + 10, st.Frame.Top + 10));
                    SnapZones.Place(w, new Native.RECT { Left = st.Frame.Left + off, Top = st.Frame.Top + off, Right = st.Frame.Right + off, Bottom = st.Frame.Bottom + off });
                    Tab(w, true);
                }
                CloseStrip(st);
                ClosePill(st);
            }
            _stacks.Clear();
            _timer.Stop();
            Sweep();
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

        void OnMoved(IntPtr h)
        {
            var st = Find(h);
            if (st == null || st.Active != h || st.Collapsed) return;
            if (Native.IsZoomed(h))
            {
                MakeFill(st, h);
                Strip(st, false);
                return;
            }
            if (Visible(h))
            {
                var f = Native.FrameBounds(h);
                if (st.Fill && (f.Left != st.Frame.Left || f.Top != st.Frame.Top || f.Width != st.Frame.Width)) st.Fill = false;
                st.Frame = f;
            }
            st.Strip?.Follow(h, Visible(h) && !st.Collapsed);
        }

        void OnForeground(IntPtr h)
        {
            foreach (var st in _stacks)
            {
                if (st.Collapsed) continue;
                if (st.Wins.Contains(h) && h != st.Active)
                {
                    Activate(st, h, false);
                    continue;
                }
                if (st.Layer == "under" && h != st.Active && Native.IsWindow(st.Active) && !st.Wins.Contains(h))
                    Native.SetWindowPos(st.Active, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        void OnMinimized(IntPtr h, bool min)
        {
            var st = Find(h);
            if (st == null || st.Active != h) return;
            if (st.Collapsed && !min)
            {
                Expand(st);
                return;
            }
            st.Strip?.Follow(h, !min && Visible(h));
        }

        void Tick()
        {
            Sweep();
            long now = Environment.TickCount64;
            _waiting.RemoveAll(w => w.until < now);
            foreach (var st in _stacks.ToList())
            {
                int before = st.Wins.Count;
                foreach (var dead in st.Wins.Where(w => !Native.IsWindow(w)).ToList())
                {
                    st.Wins.Remove(dead);
                    WinEvents.Unwatch(dead);
                }
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
                else if (before != st.Wins.Count) Persist();
                if (st.Collapsed) st.Pill?.Update(st.Name, st.Wins.Count);
                else Strip(st, false);
            }
        }

        public void Opened(IntPtr h, string path)
        {
            string exe = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            int i = _waiting.FindIndex(w => w.exe == exe);
            if (i < 0) return;
            var st = _waiting[i].st;
            _waiting.RemoveAt(i);
            if (!_stacks.Contains(st) || !Native.IsWindow(st.Active)) return;
            Join(h, st.Active);
        }

        void Launch(Stack st, string path)
        {
            string exe = WinList.ExeOf(path).ToLowerInvariant();
            if (exe.Length == 0) return;
            _waiting.RemoveAll(w => w.exe == exe);
            _waiting.Add((exe, st, Environment.TickCount64 + 20000));
            Elysium.Launch.Run(path);
        }

        void Strip(Stack st, bool rebuild)
        {
            try
            {
                if (st.Strip == null || st.Strip.Dead)
                {
                    var strip = new TabStrip(_cat);
                    st.Strip = strip;
                    strip.Picked += h =>
                    {
                        var s2 = Find(h);
                        if (s2 == null) return;
                        Activate(s2, h, true);
                        Sfx.Play("tick");
                    };
                    strip.Unstack += Take;
                    strip.CloseTab += h =>
                    {
                        Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    };
                    strip.Scrolled += (h, dir) => Cycle(h, dir);
                    strip.Plus += anchor =>
                    {
                        var s2 = _stacks.FirstOrDefault(x => x.Strip == strip);
                        if (s2 != null) GlassMenu.Show(anchor, PlusMenu(s2));
                    };
                    strip.More += anchor =>
                    {
                        var s2 = _stacks.FirstOrDefault(x => x.Strip == strip);
                        if (s2 != null) GlassMenu.Show(anchor, MoreMenu(s2));
                    };
                    strip.Renamed += name =>
                    {
                        var s2 = _stacks.FirstOrDefault(x => x.Strip == strip);
                        if (s2 == null || name.Trim().Length == 0) return;
                        s2.Name = name.Trim();
                        s2.Sig = "";
                        Strip(s2, true);
                    };
                    rebuild = true;
                }
                bool show = Native.IsWindow(st.Active) && Visible(st.Active) && !st.Collapsed;
                string sig = string.Join(",", st.Wins.Select(w => w.ToInt64() + ":" + Native.Title(w))) + "|" + st.Active.ToInt64() + "|" + st.Name + "|" + st.Layer;
                if (rebuild || sig != st.Sig)
                {
                    st.Sig = sig;
                    st.Strip.Build(st.Wins, st.Active, st.Name, st.Layer);
                }
                st.Strip.Follow(st.Active, show);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("tab strip failed " + ex.Message);
                st.Strip = null;
            }
        }

        List<MenuEntry> PlusMenu(Stack st)
        {
            var items = new List<MenuEntry>();
            var open = WinList.All().Where(w => !st.Wins.Contains(w.Hwnd) && Find(w.Hwnd) == null).Take(10).ToList();
            if (open.Count > 0)
            {
                items.Add(MenuEntry.Title("open right now"));
                foreach (var w in open)
                {
                    var win = w;
                    string t = win.Title.Length > 46 ? win.Title[..46] + "..." : win.Title;
                    items.Add(MenuEntry.Item("\uE8A7", t, () => Join(win.Hwnd, st.Active)));
                }
            }
            var pairs = PairsFor(st);
            var favs = Favorites?.Invoke() ?? Array.Empty<(string name, string path)>();
            var usual = new List<(string name, string path)>();
            foreach (var exe in pairs)
            {
                var hit = favs.FirstOrDefault(f => WinList.ExeOf(f.path).Equals(exe, StringComparison.OrdinalIgnoreCase));
                if (hit.path != null) usual.Add(hit);
            }
            if (usual.Count > 0)
            {
                items.Add(MenuEntry.Title("usually with this"));
                foreach (var (name, path) in usual)
                {
                    string p = path;
                    items.Add(MenuEntry.Item("\uE710", name, () => Launch(st, p)));
                }
            }
            var more = favs.Where(f => !usual.Any(u => u.path == f.path)).Take(6).ToList();
            if (more.Count > 0)
            {
                items.Add(MenuEntry.Title("open an app in here"));
                foreach (var (name, path) in more)
                {
                    string p = path;
                    items.Add(MenuEntry.Item("\uE710", name, () => Launch(st, p)));
                }
            }
            if (items.Count == 0) items.Add(MenuEntry.Title("nothing else open"));
            return items;
        }

        List<MenuEntry> MoreMenu(Stack st)
        {
            return new List<MenuEntry>
            {
                MenuEntry.Item("\uE8AC", "Rename", () => st.Strip?.StartRename()),
                MenuEntry.Item("\uE921", "Minimize to a pill", () => Collapse(st)),
                MenuEntry.Toggle("\uE718", "Keep on top of everything", st.Layer == "top", () =>
                {
                    st.Layer = st.Layer == "top" ? "normal" : "top";
                    ApplyLayer(st);
                    Strip(st, true);
                }),
                MenuEntry.Toggle("\uE8A1", "Keep under everything", st.Layer == "under", () =>
                {
                    st.Layer = st.Layer == "under" ? "normal" : "under";
                    ApplyLayer(st);
                    Strip(st, true);
                }),
                MenuEntry.Sep(),
                MenuEntry.Item("\uE8C6", "Ungroup", () => Dissolve(st)),
                MenuEntry.Item("\uE8BB", "Close every tab", () =>
                {
                    foreach (var w in st.Wins.ToList()) Native.PostMessage(w, 0x0010, IntPtr.Zero, IntPtr.Zero);
                }, true)
            };
        }

        void Collapse(Stack st)
        {
            if (st.Collapsed) return;
            st.Collapsed = true;
            st.Strip?.Follow(st.Active, false);
            if (Native.IsWindow(st.Active)) Native.ShowWindow(st.Active, Native.SW_MINIMIZE);
            var pill = st.Pill;
            if (pill == null || pill.Dead)
            {
                pill = new WorkspacePill(_cat);
                st.Pill = pill;
                pill.Restore += () => Expand(st);
                pill.Menu += anchor => GlassMenu.Show(anchor, new List<MenuEntry>
                {
                    MenuEntry.Item("\uE740", "Bring it back", () => Expand(st)),
                    MenuEntry.Item("\uE8C6", "Ungroup", () =>
                    {
                        Expand(st);
                        Dissolve(st);
                    }),
                    MenuEntry.Item("\uE8BB", "Close every tab", () =>
                    {
                        foreach (var w in st.Wins.ToList()) Native.PostMessage(w, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    }, true)
                });
            }
            int index = _stacks.Where(x => x.Collapsed).ToList().IndexOf(st);
            pill.Show(st.Name, st.Wins.Count, st.Wins.Select(w => Native.ProcessPath(w)).Where(p => p != null).Select(p => p!).Distinct().Take(3).ToList(), st.Frame, Math.Max(0, index));
            Sfx.Play("close");
        }

        void Expand(Stack st)
        {
            if (!st.Collapsed) return;
            st.Collapsed = false;
            ClosePill(st);
            if (Native.IsWindow(st.Active))
            {
                Native.ShowWindow(st.Active, Native.SW_RESTORE);
                SnapZones.Place(st.Active, st.Frame);
                ApplyLayer(st);
                Native.ForceForeground(st.Active);
            }
            Strip(st, true);
            Sfx.Play("open");
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

        static void ClosePill(Stack st)
        {
            try
            {
                st.Pill?.Kill();
            }
            catch
            {
            }
            st.Pill = null;
        }

        void Persist()
        {
            var list = new List<PocketEntry>();
            foreach (var st in _stacks)
            {
                foreach (var w in st.Wins)
                {
                    if (w == st.Active) continue;
                    list.Add(new PocketEntry { Hwnd = w.ToInt64(), X = st.Frame.Left, Y = st.Frame.Top, Max = st.Fill, Title = Native.Title(w) });
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
        public const double Tall = 36;

        readonly AppCatalog _cat;
        readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _surface;
        readonly TextBlock _name;
        readonly TextBox _rename;
        readonly TextBlock _layer;
        IntPtr _owner;
        IntPtr _active;
        Native.RECT _last;
        bool _shownOnce, _blurred;
        int _lastW;

        public bool Dead { get; private set; }
        public event Action<IntPtr>? Picked;
        public event Action<IntPtr>? Unstack;
        public event Action<IntPtr>? CloseTab;
        public event Action<IntPtr, int>? Scrolled;
        public event Action<UIElement>? Plus;
        public event Action<UIElement>? More;
        public event Action<string>? Renamed;

        public TabStrip(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 10, true);
            Topmost = false;
            ShowActivated = false;
            Title = "elysium tabs";
            Height = Tall;
            _surface = Glass.Surface(10);
            var dock = new DockPanel { Margin = new Thickness(4, 0, 4, 0), LastChildFill = true };

            var more = Kit.Plain("\uE712", () => More?.Invoke(this), "workspace stuff", 11);
            DockPanel.SetDock(more, Dock.Right);
            dock.Children.Add(more);
            _layer = Kit.Icon("", 11, "AccentFill");
            _layer.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(_layer, Dock.Right);
            dock.Children.Add(_layer);
            var nameHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            _name = Kit.Text("", 12, true, "SubText");
            _name.Cursor = Cursors.Hand;
            _name.ToolTip = "double-click to rename";
            _name.MaxWidth = 200;
            _name.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) StartRename();
            };
            _rename = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Width = 160, Visibility = Visibility.Collapsed, FontSize = 12 };
            _rename.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) EndRename(true);
                else if (e.Key == Key.Escape) EndRename(false);
            };
            _rename.LostKeyboardFocus += (_, _) => EndRename(true);
            nameHost.Children.Add(_name);
            nameHost.Children.Add(_rename);
            DockPanel.SetDock(nameHost, Dock.Right);
            dock.Children.Add(nameHost);

            var plus = Kit.Plain("\uE710", () => Plus?.Invoke(this), "add a tab", 11);
            plus.Margin = new Thickness(2, 0, 0, 0);
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(_row);
            left.Children.Add(plus);
            dock.Children.Add(left);

            _surface.Child = dock;
            Content = _surface;
            Live.Add(this);
            Closed += (_, _) =>
            {
                Dead = true;
                Live.Remove(this);
            };
            MouseWheel += (_, e) =>
            {
                e.Handled = true;
                Scrolled?.Invoke(_active, e.Delta > 0 ? -1 : 1);
            };
        }

        public void StartRename()
        {
            var me = new WindowInteropHelper(this).Handle;
            if (me != IntPtr.Zero) Native.SetWindowLongPtr(me, Native.GWL_EXSTYLE, new IntPtr(Native.ExStyle(me) & ~(long)Native.WS_EX_NOACTIVATE));
            ShowActivated = true;
            _rename.Text = _name.Text;
            _name.Visibility = Visibility.Collapsed;
            _rename.Visibility = Visibility.Visible;
            Activate();
            _rename.Focus();
            _rename.SelectAll();
        }

        void EndRename(bool keep)
        {
            if (_rename.Visibility != Visibility.Visible) return;
            _rename.Visibility = Visibility.Collapsed;
            _name.Visibility = Visibility.Visible;
            ShowActivated = false;
            var me = new WindowInteropHelper(this).Handle;
            if (me != IntPtr.Zero) Native.SetWindowLongPtr(me, Native.GWL_EXSTYLE, new IntPtr(Native.ExStyle(me) | Native.WS_EX_NOACTIVATE));
            if (keep && _rename.Text.Trim().Length > 0 && _rename.Text.Trim() != _name.Text) Renamed?.Invoke(_rename.Text.Trim());
            if (Native.IsWindow(_active)) Native.ForceForeground(_active);
        }

        public static readonly HashSet<TabStrip> Live = new();

        public void Kill()
        {
            if (Dead) return;
            Dead = true;
            Live.Remove(this);
            Vanish(this);
        }

        public static void Vanish(Window w)
        {
            var me = new WindowInteropHelper(w).Handle;
            if (me != IntPtr.Zero)
            {
                Native.SetWindowLongPtr(me, Native.GWLP_HWNDPARENT, IntPtr.Zero);
                Native.ShowWindow(me, Native.SW_HIDE);
            }
            try
            {
                w.Hide();
            }
            catch
            {
            }
            w.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    w.Close();
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("tab strip close failed " + ex.Message);
                }
            }), DispatcherPriority.Background);
        }

        public void Build(List<IntPtr> wins, IntPtr active, string name, string layer)
        {
            _active = active;
            _name.Text = name;
            _layer.Text = layer == "top" ? "\uE718" : layer == "under" ? "\uE8A1" : "";
            _layer.ToolTip = layer == "top" ? "kept on top" : layer == "under" ? "kept under everything" : null;
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
                var x = Kit.Plain("\uE8BB", () => CloseTab?.Invoke(h), "close this tab", 8);
                x.Margin = new Thickness(6, 0, -4, 0);
                x.Visibility = Visibility.Hidden;
                sp.Children.Add(x);
                var tab = new Border { Child = sp, CornerRadius = Kit.Round(7), Padding = new Thickness(10, 4, 8, 5), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = title + "\nmiddle-click or right-click to take it out", VerticalAlignment = VerticalAlignment.Center };
                if (on) tab.SetResourceReference(Border.BackgroundProperty, "RowHover");
                else tab.Background = Brushes.Transparent;
                tab.MouseEnter += (_, _) =>
                {
                    x.Visibility = Visibility.Visible;
                    if (!on) tab.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                };
                tab.MouseLeave += (_, _) =>
                {
                    x.Visibility = Visibility.Hidden;
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
            if (!show || !Native.IsWindow(active))
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
            double scale = Native.DpiScaleAt(f.Left + 10, f.Top + 10);
            int h = (int)Math.Round(Tall * scale);
            int w = Math.Max((int)(300 * scale), f.Width);
            var work = Native.WorkAreaAt(f.Left + f.Width / 2, f.Top + 4);
            int x = f.Left;
            int y = Math.Max(work.Top, f.Top - h);
            if (!IsVisible)
            {
                if (!_shownOnce) _surface.Opacity = 0;
                Show();
            }
            bool resized = w != _lastW;
            if (resized)
            {
                _lastW = w;
                Native.SetWindowPos(me, IntPtr.Zero, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            }
            else Native.SetWindowPos(me, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            if (!_shownOnce)
            {
                _shownOnce = true;
                Glass.FadeIn(_surface, 6, 180);
                _surface.Opacity = 1;
            }
            if (resized || !_blurred)
            {
                _blurred = true;
                Glass.SetBlur(this, true);
            }
        }
    }

    internal sealed class WorkspacePill : Window
    {
        readonly AppCatalog _cat;
        readonly Border _surface;
        readonly StackPanel _icons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name;
        readonly TextBlock _count;
        Point _down;
        bool _pressed, _moved;

        public bool Dead { get; private set; }
        public event Action? Restore;
        public event Action<UIElement>? Menu;

        public WorkspacePill(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 18, true);
            ShowActivated = false;
            Title = "elysium workspace";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(18);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 7, 14, 7), Background = Brushes.Transparent };
            row.Children.Add(_icons);
            _name = Kit.Text("", 12.5, true);
            _name.Margin = new Thickness(8, 0, 0, 0);
            _name.MaxWidth = 200;
            row.Children.Add(_name);
            _count = Kit.Text("", 11.5, false, "SubText");
            _count.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(_count);
            _surface.Child = row;
            _surface.Cursor = Cursors.Hand;
            _surface.ToolTip = "click to bring it back, drag to move";
            Content = _surface;
            Closed += (_, _) => Dead = true;
            _surface.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _pressed = true;
                _moved = false;
                Native.GetCursorPos(out var c);
                _down = new Point(c.X, c.Y);
                _surface.CaptureMouse();
            };
            _surface.MouseMove += (_, e) =>
            {
                if (!_pressed) return;
                Native.GetCursorPos(out var c);
                double dx = c.X - _down.X, dy = c.Y - _down.Y;
                if (!_moved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
                _moved = true;
                var h = new WindowInteropHelper(this).Handle;
                Native.GetWindowRect(h, out var r);
                Native.SetWindowPos(h, IntPtr.Zero, r.Left + (int)dx, r.Top + (int)dy, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                _down = new Point(c.X, c.Y);
            };
            _surface.MouseLeftButtonUp += (_, _) =>
            {
                if (!_pressed) return;
                _pressed = false;
                _surface.ReleaseMouseCapture();
                if (!_moved) Restore?.Invoke();
            };
            _surface.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                Menu?.Invoke(_surface);
            };
        }

        public void Update(string name, int count)
        {
            _name.Text = name;
            _count.Text = count + " tabs";
        }

        public void Show(string name, int count, List<string> paths, Native.RECT near, int index)
        {
            Update(name, count);
            _icons.Children.Clear();
            for (int i = 0; i < paths.Count; i++)
            {
                var img = new Image { Width = 20, Height = 20, Margin = new Thickness(i == 0 ? 0 : -6, 0, 0, 0) };
                _cat.Icons.Get(paths[i], 32, true, src => img.Source = src);
                _icons.Children.Add(img);
            }
            var work = Native.WorkAreaAt(near.Left + near.Width / 2, near.Top + near.Height / 2);
            double scale = Native.DpiScaleAt(work.Left + 10, work.Top + 10);
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _surface.DesiredSize;
                int w = (int)(size.Width * scale), h = (int)(size.Height * scale);
                int x = work.Left + (work.Width - w) / 2 + (int)(index * 230 * scale);
                int y = work.Bottom - h - (int)(18 * scale);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 10, 0.94, 240);
        }

        public void Kill()
        {
            if (Dead) return;
            Dead = true;
            bool gone = false;
            Glass.Dismiss(this, _surface, () =>
            {
                gone = true;
                TabStrip.Vanish(this);
            });
            var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                if (!gone) TabStrip.Vanish(this);
            };
            guard.Start();
        }
    }

    internal sealed class StackHint : Window
    {
        readonly Border _box;

        public StackHint()
        {
            Glass.Attach(this, 12, true);
            Title = "elysium stack hint";
            ShowActivated = false;
            IsHitTestVisible = false;
            _box = new Border { CornerRadius = Kit.Round(12), BorderThickness = new Thickness(2) };
            _box.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            _box.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var g = Kit.Icon("\uE8A9", 13, "AccentFill");
            g.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(g);
            sp.Children.Add(Kit.Text("let go to make these a workspace", 12.5, true));
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
