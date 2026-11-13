using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Heft.Ui
{
    internal sealed class ShortcutSheet : Window
    {
        sealed class Item
        {
            public string Group = "";
            public string Label = "";
            public string Keys = "";
            public Action? Run;
        }

        readonly Settings _s;
        readonly Func<string, bool> _run;
        readonly Border _surface;
        readonly WrapPanel _cols = new() { Orientation = Orientation.Horizontal };
        readonly TextBox _filter;
        bool _shown;

        public Action? OpenKeys;

        public ShortcutSheet(Settings s, Func<string, bool> run)
        {
            _s = s;
            _run = run;
            Glass.Attach(this, 18);
            Title = "heft shortcuts";
            SizeToContent = SizeToContent.Height;
            Width = 940;

            _surface = Glass.Surface(18);
            var root = new DockPanel { Margin = new Thickness(22, 18, 22, 20) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            _filter = Kit.Field("", "type to find one", _ => { });
            _filter.Width = 220;
            DockPanel.SetDock(_filter, Dock.Right);
            head.Children.Add(_filter);
            var titles = new StackPanel();
            titles.Children.Add(Kit.Text("everything you can press", 19, true));
            var sub = Kit.Caption("click one to do it. change them in settings > keys");
            sub.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(sub);
            head.Children.Add(titles);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _cols, Focusable = false, MaxHeight = 640 };
            root.Children.Add(scroll);
            _surface.Child = root;
            Content = _surface;

            _filter.TextChanged += (_, _) => Build();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (_filter.Text.Length > 0) _filter.Text = "";
                    else Close2();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    var first = Items().FirstOrDefault(i => i.Run != null && Match(i));
                    if (first != null) Fire(first);
                    e.Handled = true;
                }
            };
            Deactivated += (_, _) => Close2();
        }

        public bool Shown => _shown;

        public void Toggle()
        {
            if (_shown) Close2();
            else Open();
        }

        public void Open()
        {
            _shown = true;
            _filter.Text = "";
            Build();
            var d = Displays.AtCursor();
            Width = Math.Min(940, d.Work.Width / d.Scale - 80);
            Glass.Present(this, _surface, () =>
            {
                var h = new WindowInteropHelper(this).Handle;
                UpdateLayout();
                int wpx = (int)Math.Round(Width * d.Scale);
                int hpx = (int)Math.Round(Math.Max(ActualHeight, 300) * d.Scale);
                Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - wpx) / 2, d.Work.Top + Math.Max(20, (d.Work.Height - hpx) / 2), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 8, 0.97, 220);
            Activate();
            if (!IsActive) Native.Focus(new WindowInteropHelper(this).Handle);
            _filter.Focus();
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            Glass.Dismiss(this, _surface);
        }

        List<Item> Items()
        {
            var list = new List<Item>();
            foreach (var d in Hotkeys.Defaults)
            {
                string id = d.id;
                if (id == "sheet") continue;
                string spec = Hotkeys.Spec(_s, id);
                if (id == "start" && spec.Length == 0 && _s.WinKeyLauncher) spec = "Win";
                list.Add(new Item { Group = "keys", Label = d.label, Keys = spec.Length > 0 ? spec : "not set", Run = () => _run(id) });
            }
            foreach (var p in _s.AudioProfiles.Where(p => p.Hotkey.Length > 0))
            {
                string name = p.Name;
                list.Add(new Item { Group = "keys", Label = "Audio profile: " + name, Keys = p.Hotkey, Run = () => _run("profile:" + name) });
            }

            list.Add(new Item { Group = "mouse", Label = "Make a window see-through", Keys = "scroll its title bar", Run = null });
            list.Add(new Item { Group = "mouse", Label = "Pin a window on top", Keys = "middle-click its title bar" });
            list.Add(new Item { Group = "mouse", Label = "Stack two windows into tabs", Keys = "drag a title bar onto another, hold" });
            list.Add(new Item { Group = "mouse", Label = "Switch tabs in a stack", Keys = "click / scroll the tab strip" });
            list.Add(new Item { Group = "mouse", Label = "Take a window out of its stack", Keys = "middle-click its tab" });
            list.Add(new Item { Group = "mouse", Label = "Volume", Keys = "scroll the top bar", Run = () => _run("audio") });
            list.Add(new Item { Group = "mouse", Label = "Play / pause", Keys = "middle-click the top bar" });
            list.Add(new Item { Group = "mouse", Label = "Brightness", Keys = "scroll the screen edge" });
            list.Add(new Item { Group = "mouse", Label = "Shelf", Keys = "drag down from the top bar", Run = () => _run("shelf") });
            list.Add(new Item { Group = "mouse", Label = "Drop slit", Keys = "drag a file to the top bar" });
            list.Add(new Item { Group = "mouse", Label = "Peek at desktop icons", Keys = "double-click the desktop", Run = () => _run("peek") });
            list.Add(new Item { Group = "mouse", Label = "Inspect in File Explorer", Keys = "Space", Run = () => _run("inspect") });
            foreach (var corner in new[] { ("tl", "top-left"), ("tr", "top-right"), ("bl", "bottom-left"), ("br", "bottom-right") })
            {
                string act = _s.HotCorners.TryGetValue(corner.Item1, out var a) ? a : "none";
                if (act == "none") continue;
                string target = act;
                list.Add(new Item { Group = "mouse", Label = "Hot corner: " + act, Keys = corner.Item2 + " corner", Run = () => _run("corner:" + target) });
            }

            list.Add(new Item { Group = "launcher", Label = "Web search", Keys = "?" });
            list.Add(new Item { Group = "launcher", Label = "Run a command", Keys = ">" });
            list.Add(new Item { Group = "launcher", Label = "Find files", Keys = "r" });
            list.Add(new Item { Group = "launcher", Label = "Apps", Keys = "@" });
            list.Add(new Item { Group = "launcher", Label = "Windows settings", Keys = "#" });
            list.Add(new Item { Group = "launcher", Label = "Calculator + units", Keys = "=" });
            list.Add(new Item { Group = "launcher", Label = "System stuff", Keys = "!" });
            list.Add(new Item { Group = "launcher", Label = "Clipboard history", Keys = ":" });
            list.Add(new Item { Group = "launcher", Label = "heft stuff", Keys = "&" });
            list.Add(new Item { Group = "launcher", Label = "Walk folders", Keys = "/" });
            list.Add(new Item { Group = "launcher", Label = "Pipe results into an action", Keys = "results > action" });
            list.Add(new Item { Group = "launcher", Label = "Rename a bunch of files", Keys = "> rename trip-{n}" });
            list.Add(new Item { Group = "launcher", Label = "Send files to your phone", Keys = "> share" });
            list.Add(new Item { Group = "launcher", Label = "Pin a result", Keys = "Ctrl+P" });
            return list;
        }

        bool Match(Item i)
        {
            string f = _filter.Text.Trim();
            if (f.Length == 0) return true;
            return Fuzzy.Score(f, i.Label) > 0 || Fuzzy.Score(f, i.Keys) > 0;
        }

        void Build()
        {
            _cols.Children.Clear();
            var all = Items().Where(Match).ToList();
            foreach (var (gid, gname) in new[] { ("keys", "Keys"), ("mouse", "Mouse & gestures"), ("launcher", "Launcher (alt + space)") })
            {
                var items = all.Where(i => i.Group == gid).ToList();
                if (items.Count == 0) continue;
                var col = new StackPanel { Width = 286, Margin = new Thickness(0, 0, 14, 10) };
                var h = Kit.Header(gname);
                h.Margin = new Thickness(4, 0, 0, 6);
                col.Children.Add(h);
                foreach (var it in items) col.Children.Add(Row(it));
                _cols.Children.Add(col);
            }
            if (_cols.Children.Count == 0)
            {
                var none = Kit.Text("nothing like that", 13, false, "SubText");
                none.Margin = new Thickness(4, 10, 0, 10);
                _cols.Children.Add(none);
            }
        }

        UIElement Row(Item it)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Kit.Text(it.Label, 12.5, false, it.Run != null ? "Text" : "SubText");
            label.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(label);
            var caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bool plain = it.Keys.Contains(' ') && !it.Keys.Contains('+');
            if (plain)
            {
                var t = Kit.Text(it.Keys, 11, false, "Faint");
                t.MaxWidth = 150;
                caps.Children.Add(t);
            }
            else
            {
                foreach (var part in it.Keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) caps.Children.Add(Cap(part));
            }
            Grid.SetColumn(caps, 1);
            grid.Children.Add(caps);
            var host = Kit.Hover(grid, it.Run != null ? () => Fire(it) : null, 8);
            host.Padding = new Thickness(6, 5, 6, 5);
            return host;
        }

        static Border Cap(string text)
        {
            var b = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(3, 0, 0, 0), MinWidth = 20 };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            var t = Kit.Text(text, 10.5, true, "SubText");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = t;
            return b;
        }

        void Fire(Item it)
        {
            var act = it.Run;
            if (act == null) return;
            Close2();
            Sfx.Play("tick");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    act();
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("sheet action failed " + ex.Message);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
