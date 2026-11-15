using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Elysium.Ui
{
    internal sealed partial class SettingsPages
    {
        public Action? WhatsNewRequested;
        public Action? LockLookRequested;
        public Action? TaskManagerRequested;
        public Action? CalendarsChanged;
        string _calName = "", _calUrl = "";

        void WindowMore(Panel h)
        {
            h.Children.Add(Kit.Header("Window effects"));
            h.Children.Add(Kit.Group(
                Kit.Row("Minimize into the dock", "windows shrink into the dock edge and grow back out. turns off windows' own minimize animation while it's on", Sw(_s.MinimizeToDock, v => _s.MinimizeToDock = v)),
                Kit.Row("Open and close animations", "apps zoom open (out of the dock icon if you launched them there) and shrink away when you close them", Sw(_s.WindowAnims, v => _s.WindowAnims = v)),
                Kit.Row("Theme every window's border", "the window you're in gets an accent border, title bars match light / dark. windows 11", Sw(_s.ThemedBorders, v => _s.ThemedBorders = v)),
                Kit.Row("Roll up a window", KeyHint("shade") + " rolls the window you're in up into its title bar. click the little tab to drop it back down", null)));

            h.Children.Add(Kit.Header("Auto-tiling"));
            h.Children.Add(Kit.Group(
                Kit.Row("Tile new windows", "every app you open gets arranged: big one on the left, the rest stacked on the right", Sw(_s.AutoTile, v => _s.AutoTile = v)),
                Kit.SliderRow("Gap", 0, 30, _s.TileGap, v => Math.Round(v) + " px", v =>
                {
                    _s.TileGap = Math.Round(v);
                    Touch();
                })));
        }

        void ShellMore(Panel h)
        {
            h.Children.Add(Kit.Header("Dynamic island"));
            h.Children.Add(Kit.Group(
                Kit.Block("Volume / brightness / caps lock popups", null, Kit.Segments(new[] { "Windows'", "In the bar", "Glass popup" }, new[] { "off", "island", "popup" }, _s.OsdStyle, v =>
                {
                    _s.OsdStyle = v;
                    Touch();
                })),
                Kit.Row("Hide windows' volume popup", "so you don't get both. might not work on the newest windows 11", Sw(_s.HideWinOsd, v => _s.HideWinOsd = v)),
                Kit.Row("Bounce when an app wants you", "its icon pops out at the dock edge and the bar glows", Sw(_s.AttentionBounce, v => _s.AttentionBounce = v)),
                Kit.Row("Dock previews", "hover an open app in the dock to see its windows", Sw(_s.DockPreviews, v => _s.DockPreviews = v))));

            h.Children.Add(Kit.Header("Desktop"));
            h.Children.Add(Kit.Group(
                Kit.Row("Glass desktop menu", "right-click the desktop for elysium's menu. right-click an icon and you still get windows' one", Sw(_s.DesktopMenu, v => _s.DesktopMenu = v)),
                Kit.Row("Desktop piles", "your desktop files get stacked by type on the right. click a pile to fan it out", Sw(_s.DesktopPiles, v => _s.DesktopPiles = v)),
                Kit.Row("Background apps in the bar", "the stuff in your tray (discord, steam...) in a menu. turn on the slot in the bar editor above", null)));

            h.Children.Add(Kit.Header("Start extras"));
            h.Children.Add(Kit.Group(
                Kit.Row("Greeting", "good morning + your name + the weather up top", Sw(_s.StartGreeting, v => _s.StartGreeting = v)),
                Kit.Row("Your name", "", Kit.Field(_s.UserName, Environment.UserName, v =>
                {
                    _s.UserName = v.Trim();
                    Touch();
                })),
                Kit.Row("Live pins", "pins show unread counts and what spotify's playing", Sw(_s.LivePins, v => _s.LivePins = v))));

            h.Children.Add(Kit.Header("Standby"));
            h.Children.Add(Kit.Group(
                Kit.Row("Standby mode", "after you're away for a bit, the screen turns into a big clock with your music, weather and notifications. move the mouse and it's gone", Sw(_s.StandbyOn, v => _s.StandbyOn = v)),
                Kit.SliderRow("Starts after", 1, 30, _s.StandbyMinutes, v => Math.Round(v) + " min", v =>
                {
                    _s.StandbyMinutes = Math.Round(v);
                    Touch();
                }),
                Kit.Block("Clock", null, Kit.Segments(new[] { "Big", "Minimal" }, new[] { "big", "minimal" }, _s.StandbyClock, v =>
                {
                    _s.StandbyClock = v;
                    Touch();
                })),
                Kit.Block("Background", null, Kit.Segments(new[] { "Blurred screen", "Album art", "Black" }, new[] { "wallpaper", "art", "black" }, _s.StandbyBg, v =>
                {
                    _s.StandbyBg = v;
                    Touch();
                })),
                Kit.SliderRow("Dim", 0, 0.85, _s.StandbyDim, Pct, v =>
                {
                    _s.StandbyDim = v;
                    Touch();
                }),
                Kit.Block("Screens", null, Kit.Segments(new[] { "Main", "All" }, new[] { "main", "all" }, _s.StandbyScreens, v =>
                {
                    _s.StandbyScreens = v;
                    Touch();
                })),
                Kit.Row("Show music", "", Sw(_s.StandbyMusic, v => _s.StandbyMusic = v)),
                Kit.Row("Show weather", "", Sw(_s.StandbyWeather, v => _s.StandbyWeather = v)),
                Kit.Row("Show notifications", "", Sw(_s.StandbyNotes, v => _s.StandbyNotes = v)),
                Kit.Row("Show next calendar thing", "", Sw(_s.StandbyCalendar, v => _s.StandbyCalendar = v)),
                Kit.Row("Start it now", KeyHint("standby"), null)));
        }

        void GeneralMore(Panel h)
        {
            h.Children.Add(Kit.Header("Task manager"));
            h.Children.Add(Kit.Group(
                Kit.Row("Ctrl + Shift + Esc opens elysium's", "instead of the windows one. processes, performance graphs and startup apps", Sw(_s.TaskManagerKeys, v => _s.TaskManagerKeys = v)),
                Kit.Row("Open it", KeyHint("taskmgr"), Kit.Push("Open", () => TaskManagerRequested?.Invoke()))));
            h.Children.Add(Kit.Header("Moments"));
            h.Children.Add(Kit.Group(
                Kit.Block("Boot screen", null, Kit.Segments(new[] { "Off", "When the pc starts", "Every time" }, new[] { "off", "startup", "always" }, _s.BootScreen, v =>
                {
                    _s.BootScreen = v;
                    Touch();
                })),
                Kit.Row("Shutdown screen", "a glass \"shutting down\" when you shut down or restart from elysium", Sw(_s.ShutdownScreen, v => _s.ShutdownScreen = v)),
                Kit.Row("Lock animation", "a quick glass lock when you lock from elysium", Sw(_s.LockLook, v => _s.LockLook = v)),
                Kit.Row("Match the lock screen", "puts a themed version of your wallpaper on windows' lock screen", Kit.Push("Do it", () => LockLookRequested?.Invoke())),
                Kit.Row("Wake animation", "the bar and dock slide back in when the pc wakes up", Sw(_s.WakeAnim, v => _s.WakeAnim = v)),
                Kit.Row("What's new after updates", "", Sw(_s.ShowWhatsNew, v => _s.ShowWhatsNew = v))));
        }

        void AlertsMore(Panel h)
        {
            h.Children.Add(Kit.Header("Where notifications show"));
            var names = new List<string> { "Mouse screen", "Main", "All" };
            var values = new List<string> { "mouse", "main", "all" };
            var all = Displays.All();
            if (all.Count > 1)
                foreach (var d in all)
                {
                    names.Add("Screen " + (d.Index + 1));
                    values.Add(d.Index.ToString());
                }
            h.Children.Add(Kit.Group(
                Kit.Block("Show them on", null, Kit.Segments(names.ToArray(), values.ToArray(), _s.NotifyScreen, v =>
                {
                    _s.NotifyScreen = v;
                    Touch();
                }))));
        }

        void CalendarPage(Panel h)
        {
            h.Children.Add(Kit.Header("Calendar"));
            h.Children.Add(Kit.Group(
                Kit.Row("Calendar under the clock", "click the clock in the bar for a month view with your calendars, reminders and timers", Sw(_s.CalendarOn, v => _s.CalendarOn = v))));

            h.Children.Add(Kit.Header("Your calendars"));
            if (_s.CalendarFeeds.Count > 0)
            {
                var rows = _s.CalendarFeeds.Select(f =>
                {
                    var feed = f;
                    var box = new StackPanel { Orientation = Orientation.Horizontal };
                    box.Children.Add(Sw(feed.On, v =>
                    {
                        feed.On = v;
                        CalendarsChanged?.Invoke();
                    }));
                    var remove = Kit.Push("Remove", () =>
                    {
                        _s.CalendarFeeds.Remove(feed);
                        Touch();
                        CalendarsChanged?.Invoke();
                        Rebuild();
                    });
                    remove.Margin = new Thickness(8, 0, 0, 0);
                    box.Children.Add(remove);
                    return (UIElement)Kit.Row(feed.Name, feed.Url.Length > 60 ? feed.Url[..60] + "..." : feed.Url, box);
                }).ToArray();
                h.Children.Add(Kit.Group(rows));
            }

            h.Children.Add(Kit.Header("Add one"));
            var name = Kit.Field(_calName, "name it (school, work...)", v => _calName = v);
            var url = Kit.Field(_calUrl, "paste the ical link", v => _calUrl = v);
            url.Margin = new Thickness(0, 8, 0, 0);
            var colors = new[] { "#0A84FF", "#30D158", "#FF9F0A", "#FF375F", "#BF5AF2", "#64D2FF" };
            var add = Kit.Push("Add calendar", () =>
            {
                string link = _calUrl.Trim();
                if (link.Length == 0) return;
                _s.CalendarFeeds.Add(new CalFeed { Name = _calName.Trim().Length > 0 ? _calName.Trim() : "calendar", Url = link, Color = colors[_s.CalendarFeeds.Count % colors.Length] });
                _calName = "";
                _calUrl = "";
                _s.CalendarOn = true;
                Touch();
                CalendarsChanged?.Invoke();
                Rebuild();
            }, true);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 10, 0, 0);
            var form = new StackPanel { Margin = new Thickness(14, 4, 14, 8) };
            form.Children.Add(name);
            form.Children.Add(url);
            form.Children.Add(add);
            h.Children.Add(form);

            var how = Kit.Caption("google: calendar.google.com > settings > click your calendar > \"secret address in ical format\" > copy\napple: icloud.com/calendar > the share icon next to a calendar > public calendar > copy link\noutlook: outlook.com > settings > calendar > shared calendars > publish > ics link\nit's read-only and only lives on your pc");
            how.TextWrapping = TextWrapping.Wrap;
            how.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(how);
        }

        void AboutPage(Panel h)
        {
            var top = new StackPanel { Margin = new Thickness(14, 10, 14, 6) };
            var title = Kit.Text("elysium", 30, true);
            top.Children.Add(title);
            top.Children.Add(Kit.Text("version " + Changelog.Version + "  ·  made by mason arcieri", 13, false, "SubText"));
            h.Children.Add(top);
            var buttons = new WrapPanel { Margin = new Thickness(14, 6, 14, 6) };
            buttons.Children.Add(Kit.Push("What's new", () => WhatsNewRequested?.Invoke(), true));
            var gh = Kit.Push("GitHub", () => Launch.Run("https://github.com/drapids"));
            gh.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(gh);
            h.Children.Add(buttons);

            h.Children.Add(Kit.Header("This pc"));
            var rows = Specs().Select(kv => (UIElement)Kit.Row(kv.Key, kv.Value, null)).ToArray();
            h.Children.Add(Kit.Group(rows));
        }

        static List<KeyValuePair<string, string>> Specs()
        {
            var list = new List<KeyValuePair<string, string>>();
            void Add(string k, string? v)
            {
                if (!string.IsNullOrWhiteSpace(v)) list.Add(new KeyValuePair<string, string>(k, v.Trim()));
            }
            try
            {
                using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                Add("Processor", cpu?.GetValue("ProcessorNameString")?.ToString());
            }
            catch
            {
            }
            try
            {
                using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (cls != null)
                {
                    var gpus = new List<string>();
                    foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
                    {
                        using var k = cls.OpenSubKey(sub);
                        var desc = k?.GetValue("DriverDesc")?.ToString();
                        if (!string.IsNullOrWhiteSpace(desc) && !gpus.Contains(desc) && !desc.Contains("Basic", StringComparison.OrdinalIgnoreCase)) gpus.Add(desc);
                    }
                    Add("Graphics", string.Join(", ", gpus));
                }
            }
            catch
            {
            }
            try
            {
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
                if (Native.GlobalMemoryStatusEx(ref m)) Add("Memory", Math.Round(m.ullTotalPhys / 1024.0 / 1024 / 1024) + " GB");
            }
            catch
            {
            }
            try
            {
                using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                int build = int.TryParse(nt?.GetValue("CurrentBuildNumber")?.ToString(), out int b) ? b : 0;
                string product = nt?.GetValue("ProductName")?.ToString() ?? "Windows";
                if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
                Add("Windows", $"{product} {nt?.GetValue("DisplayVersion")} (build {build})");
            }
            catch
            {
            }
            try
            {
                var screens = Displays.All().Select(d => $"{d.Bounds.Width}x{d.Bounds.Height} @ {Native.RefreshRate(d)}hz ({Math.Round(d.Scale * 100)}%)");
                Add("Screens", string.Join(", ", screens));
            }
            catch
            {
            }
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            Add("On for", up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m");
            Add("Name", Environment.MachineName);
            return list;
        }
    }
}
