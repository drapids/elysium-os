using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Elysium.Ui
{
    internal static class Changelog
    {
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "0.6" : $"{v.Major}.{v.Minor}";
            }
        }

        public static readonly (string version, string[] lines)[] Notes =
        {
            ("0.6", new[]
            {
                "heft is elysium now. your settings came with you",
                "quick look got rebuilt: opens instantly, arrows flip through the folder, delete right from it",
                "right-click stuff on the shelf to delete it (or hit delete)",
                "AudioFlow's main volume actually works now",
                "minimize into the dock, apps zoom open and shrink closed",
                "window borders match your theme, and accent can follow the app you're in",
                "roll a window up into its title bar (give it a key in settings > keys)",
                "the bar pill is a dynamic island now: volume, recording, timers, all of it",
                "glass volume / brightness / caps lock popups",
                "hover an app in the dock to see its windows",
                "a calendar under the clock, with google and apple calendars",
                "desktop piles, a glass desktop menu, auto-tiling",
                "boot screen, shutdown screen, standby mode, wake animation",
                "themes can change your wallpaper and cursor, and there's a gallery now",
                "pick which screen notifications show up on"
            })
        };
    }

    internal sealed class WhatsNew : Window
    {
        readonly Border _surface;

        public WhatsNew()
        {
            Glass.Attach(this, 18);
            Title = "elysium what's new";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(18);
            var body = new StackPanel { Width = 420, Margin = new Thickness(24, 20, 24, 20) };
            var top = Kit.Text("what's new", 22, true);
            body.Children.Add(top);
            var ver = Kit.Caption("elysium " + Changelog.Version);
            ver.Margin = new Thickness(0, 2, 0, 14);
            body.Children.Add(ver);
            var lines = Changelog.Notes.FirstOrDefault(n => n.version == Changelog.Version).lines ?? Changelog.Notes[0].lines;
            var list = new StackPanel();
            foreach (var line in lines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 6, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                dot.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var t = Kit.Text(line, 13);
                t.TextWrapping = TextWrapping.Wrap;
                row.Children.Add(t);
                list.Children.Add(row);
            }
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var ok = Kit.Push("Nice", () => Glass.Dismiss(this, _surface), true);
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(ok);
            _surface.Child = body;
            Content = _surface;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Escape or Key.Enter)) return;
                e.Handled = true;
                Glass.Dismiss(this, _surface);
            };
        }

        public void Open()
        {
            var d = Displays.AtCursor();
            Glass.Present(this, _surface, () => Center(this, _surface, d), 12, 0.96, 260);
            Activate();
        }

        public static void Center(Window w, FrameworkElement surface, Display d)
        {
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = surface.DesiredSize;
            int pw = (int)(size.Width * d.Scale), ph = (int)(size.Height * d.Scale);
            Native.SetWindowPos(new WindowInteropHelper(w).Handle, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - pw) / 2, d.Work.Top + (d.Work.Height - ph) / 2, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    internal sealed class FirstRun : Window
    {
        readonly Settings _s;
        readonly Border _surface;
        readonly Grid _page = new() { Width = 540, MinHeight = 360 };
        readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        int _step;
        const int Steps = 5;

        public event Action? Finished;
        public Func<bool, bool>? StartWithWindows;

        public FirstRun(Settings s)
        {
            _s = s;
            Glass.Attach(this, 20);
            Title = "elysium setup";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(20);
            var body = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
            body.Children.Add(_page);
            body.Children.Add(_dots);
            _surface.Child = body;
            Content = _surface;
        }

        public void Open()
        {
            _step = 0;
            Build();
            var d = Displays.Primary(Displays.All());
            Glass.Present(this, _surface, () => WhatsNew.Center(this, _surface, d), 14, 0.95, 320);
            Activate();
        }

        void Go(int step)
        {
            _step = Math.Clamp(step, 0, Steps - 1);
            Build();
            Glass.FadeIn(_page, 8, 220);
        }

        void Build()
        {
            _page.Children.Clear();
            _dots.Children.Clear();
            for (int i = 0; i < Steps; i++)
            {
                var dot = new Border { Width = i == _step ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0) };
                dot.SetResourceReference(Border.BackgroundProperty, i == _step ? "AccentFill" : "Faint");
                _dots.Children.Add(dot);
            }
            var content = new StackPanel();
            var nav = new DockPanel { Margin = new Thickness(0, 20, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            if (_step > 0)
            {
                var back = Kit.Push("Back", () => Go(_step - 1));
                DockPanel.SetDock(back, Dock.Left);
                nav.Children.Add(back);
            }
            var next = Kit.Push(_step == 0 ? "Let's go" : _step == Steps - 1 ? "Done" : "Next", () =>
            {
                if (_step == Steps - 1) Finish();
                else Go(_step + 1);
            }, true);
            next.HorizontalAlignment = HorizontalAlignment.Right;
            nav.Children.Add(next);

            switch (_step)
            {
                case 0:
                    content.Children.Add(Big("welcome to elysium"));
                    content.Children.Add(Small("a whole new layer on top of windows. glass bar, dock, start, overview, themes, all of it"));
                    var who = Small("made by mason arcieri");
                    who.Margin = new Thickness(0, 18, 0, 0);
                    content.Children.Add(who);
                    content.Children.Add(Label("what should it call you?"));
                    var name = Kit.Field(_s.UserName.Length > 0 ? _s.UserName : Environment.UserName, "your name", v =>
                    {
                        _s.UserName = v.Trim();
                        _s.Touch();
                    });
                    content.Children.Add(name);
                    break;
                case 1:
                    content.Children.Add(Big("pick a look"));
                    content.Children.Add(Small("you can change all of this later in settings > look"));
                    content.Children.Add(Label("light or dark"));
                    content.Children.Add(Kit.Segments(new[] { "Dark", "Light", "Match Windows" }, new[] { "dark", "light", "system" }, _s.Theme, v =>
                    {
                        _s.Theme = v;
                        _s.Touch();
                    }));
                    content.Children.Add(Label("theme"));
                    var wrap = new WrapPanel();
                    wrap.Children.Add(Chip("elysium", null));
                    foreach (var p in Themes.All()) wrap.Children.Add(Chip(p.Name, p));
                    content.Children.Add(wrap);
                    break;
                case 2:
                    content.Children.Add(Big("accent color"));
                    content.Children.Add(Small("the color for highlights, toggles and glows"));
                    content.Children.Add(Kit.Segments(new[] { "elysium blue", "Windows", "Wallpaper", "The app I'm in" }, new[] { "default", "system", "wallpaper", "app" }, _s.AccentMode, v =>
                    {
                        _s.AccentMode = v;
                        _s.Touch();
                        Build();
                    }));
                    content.Children.Add(Label("wallpaper"));
                    var row = new WrapPanel();
                    var keep = Kit.Push("Keep mine", () => { });
                    row.Children.Add(keep);
                    var pick = Kit.Push("Pick a picture...", () =>
                    {
                        var dlg = new OpenFileDialog { Title = "Pick a wallpaper", Filter = "pictures|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                        if (dlg.ShowDialog(this) == true) Wallpaper.Set(dlg.FileName);
                    });
                    pick.Margin = new Thickness(8, 0, 0, 0);
                    row.Children.Add(pick);
                    content.Children.Add(row);
                    break;
                case 3:
                    content.Children.Add(Big("a couple things"));
                    content.Children.Add(Toggle("Start elysium with windows", "so it's there every time you sign in", _s.StartWithWindows, v =>
                    {
                        bool ok = StartWithWindows?.Invoke(v) ?? false;
                        _s.StartWithWindows = v && ok;
                        _s.Touch();
                    }));
                    content.Children.Add(Toggle("Copy my windows start pins", "so start has your apps right away", !_s.StartPinsSeeded || _s.StartPins.Count > 0, v =>
                    {
                        if (v)
                        {
                            _s.StartPins.Clear();
                            _s.StartPinsSeeded = false;
                        }
                        else
                        {
                            _s.StartPins.Clear();
                            _s.StartPinsSeeded = true;
                        }
                        _s.Touch();
                    }));
                    content.Children.Add(Toggle("Show what's new after updates", "", _s.ShowWhatsNew, v =>
                    {
                        _s.ShowWhatsNew = v;
                        _s.Touch();
                    }));
                    break;
                default:
                    content.Children.Add(Big("you're set"));
                    content.Children.Add(Small("tap the windows key for start\nalt + space for the launcher\nctrl + alt + w (or the top-left corner) for overview\nctrl + alt + y shows every shortcut"));
                    content.Children.Add(Small("everything new is off until you turn it on in settings (ctrl + alt + h)"));
                    break;
            }
            var stack = new DockPanel();
            DockPanel.SetDock(nav, Dock.Bottom);
            stack.Children.Add(nav);
            stack.Children.Add(content);
            _page.Children.Add(stack);
        }

        static TextBlock Big(string t)
        {
            var b = Kit.Text(t, 24, true);
            b.Margin = new Thickness(0, 0, 0, 6);
            return b;
        }

        static TextBlock Small(string t)
        {
            var s = Kit.Text(t, 13, false, "SubText");
            s.TextWrapping = TextWrapping.Wrap;
            s.Margin = new Thickness(0, 2, 0, 4);
            return s;
        }

        static TextBlock Label(string t)
        {
            var l = Kit.Text(t, 12.5, true);
            l.Margin = new Thickness(0, 16, 0, 6);
            return l;
        }

        static FrameworkElement Toggle(string title, string sub, bool on, Action<bool> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var cb = new CheckBox { Style = (Style)Application.Current.FindResource("Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (_, _) => set(true);
            cb.Unchecked += (_, _) => set(false);
            DockPanel.SetDock(cb, Dock.Right);
            row.Children.Add(cb);
            var text = new StackPanel();
            text.Children.Add(Kit.Text(title, 13.5, true));
            if (sub.Length > 0) text.Children.Add(Kit.Text(sub, 12, false, "SubText"));
            row.Children.Add(text);
            return row;
        }

        FrameworkElement Chip(string name, ThemePack? p)
        {
            bool on = (Themes.Active?.Id ?? "") == (p?.Id ?? "");
            var t = Kit.Text(name, 12.5, on);
            var b = new Border { Child = t, Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 8, 8), CornerRadius = Kit.Round(10), BorderThickness = new Thickness(on ? 2 : 1), Cursor = Cursors.Hand };
            b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            b.SetResourceReference(Border.BorderBrushProperty, on ? "AccentFill" : "GroupStroke");
            b.MouseLeftButtonUp += (_, _) =>
            {
                Themes.Pick(_s, p);
                Build();
            };
            return b;
        }

        void Finish()
        {
            _s.SetupSeen = true;
            _s.SeenVersion = Changelog.Version;
            _s.Touch();
            Glass.Dismiss(this, _surface);
            Finished?.Invoke();
        }
    }
}
