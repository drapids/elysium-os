using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal sealed class CalendarPanel : Window
    {
        readonly Settings _s;
        readonly Calendars _cals;
        readonly Border _surface;
        readonly StackPanel _body = new() { Margin = new Thickness(16, 14, 16, 14), Width = 312 };
        DateTime _month = DateTime.Today;
        DateTime _pick = DateTime.Today;
        bool _shown;

        public Func<List<Reminder>>? Reminders;
        public Action? OpenSettings;

        public CalendarPanel(Settings s, Calendars cals)
        {
            _s = s;
            _cals = cals;
            Glass.Attach(this, 16);
            Title = "elysium calendar";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(16);
            _surface.Child = _body;
            Content = _surface;
            Deactivated += (_, _) => Close2();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close2();
                else if (e.Key == Key.Left) Move(-1);
                else if (e.Key == Key.Right) Move(1);
                else return;
                e.Handled = true;
            };
            _cals.Changed += () =>
            {
                if (_shown) Build();
            };
        }

        public bool Shown => _shown;

        public void Toggle(Display d, double topPx, double anchorX)
        {
            if (_shown)
            {
                Close2();
                return;
            }
            _month = DateTime.Today;
            _pick = DateTime.Today;
            _cals.Refresh();
            Build();
            _shown = true;
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                int w = (int)Math.Ceiling(_surface.DesiredSize.Width * d.Scale);
                int x = Math.Clamp((int)(anchorX - w / 2.0), d.Bounds.Left + 8, d.Bounds.Right - w - 8);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, (int)topPx + (int)(8 * d.Scale), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, -10, 0.985, 220);
            Activate();
        }

        public void Close2()
        {
            if (!_shown) return;
            _shown = false;
            Glass.Dismiss(this, _surface);
        }

        void Move(int months)
        {
            _month = _month.AddMonths(months);
            Build();
        }

        void Build()
        {
            _body.Children.Clear();
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 10) };
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Children.Add(Kit.Plain("\uE76B", () => Move(-1), "last month", 11));
            nav.Children.Add(Kit.Plain("\uE8D1", () =>
            {
                _month = DateTime.Today;
                _pick = DateTime.Today;
                Build();
            }, "today", 11));
            nav.Children.Add(Kit.Plain("\uE76C", () => Move(1), "next month", 11));
            DockPanel.SetDock(nav, Dock.Right);
            head.Children.Add(nav);
            var title = Kit.Text(_month.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToLowerInvariant(), 15, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            _body.Children.Add(head);

            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
            var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
            int first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var n = Kit.Text(names[(first + i) % 7].ToLowerInvariant(), 11, false, "Faint");
                n.HorizontalAlignment = HorizontalAlignment.Center;
                n.Margin = new Thickness(0, 0, 0, 4);
                grid.Children.Add(n);
            }
            var start = new DateTime(_month.Year, _month.Month, 1);
            int offset = ((int)start.DayOfWeek - first + 7) % 7;
            var busy = _cals.Busy(_month);
            var mine = Reminders?.Invoke() ?? new List<Reminder>();
            foreach (var r in mine) busy.Add(r.Due.Date);
            var day = start.AddDays(-offset);
            for (int i = 0; i < 42; i++, day = day.AddDays(1))
            {
                var d = day;
                bool inMonth = d.Month == _month.Month;
                bool today = d == DateTime.Today;
                bool picked = d == _pick;
                var cell = new Grid { Height = 36 };
                var circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                if (today) circle.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                else if (picked) circle.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                cell.Children.Add(circle);
                var num = Kit.Text(d.Day.ToString(), 12.5, today, today ? "OnAccent" : inMonth ? "Text" : "Faint");
                num.HorizontalAlignment = HorizontalAlignment.Center;
                num.Margin = new Thickness(0, 6, 0, 0);
                cell.Children.Add(num);
                if (busy.Contains(d))
                {
                    var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
                    dot.SetResourceReference(Border.BackgroundProperty, today ? "Text" : "AccentFill");
                    cell.Children.Add(dot);
                }
                var hit = new Border { Background = Brushes.Transparent, Child = cell, Cursor = Cursors.Hand };
                hit.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _pick = d;
                    if (d.Month != _month.Month) _month = d;
                    Build();
                };
                grid.Children.Add(hit);
            }
            _body.Children.Add(grid);

            var sep = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10) };
            sep.SetResourceReference(Border.BackgroundProperty, "Separator");
            _body.Children.Add(sep);
            var dayTitle = Kit.Text(_pick == DateTime.Today ? "today" : _pick == DateTime.Today.AddDays(1) ? "tomorrow" : _pick.ToString("dddd, MMM d").ToLowerInvariant(), 12.5, true);
            dayTitle.Margin = new Thickness(2, 0, 0, 6);
            _body.Children.Add(dayTitle);

            var list = new StackPanel();
            foreach (var e in _cals.On(_pick)) list.Children.Add(Row(e.Color, e.AllDay ? "all day" : e.Start.ToString("h:mm tt").ToLowerInvariant(), e.Title, e.Where.Length > 0 ? e.Where : e.Feed));
            foreach (var r in mine.Where(r => r.Due.Date == _pick).OrderBy(r => r.Due))
                list.Children.Add(Row(null, r.Due.ToString("h:mm tt").ToLowerInvariant(), r.Text.Length > 0 ? r.Text : r.Timer ? "timer" : "reminder", r.Timer ? "timer" : "reminder"));
            if (list.Children.Count == 0)
            {
                var none = Kit.Caption(_s.CalendarFeeds.Count == 0 ? "nothing here. add your google or apple calendar in settings" : "nothing planned");
                none.Margin = new Thickness(2, 0, 0, 0);
                list.Children.Add(none);
            }
            _body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var errors = _s.CalendarFeeds.Select(f => (f.Name, err: _cals.Error(f.Url))).Where(x => x.err != null).ToList();
            foreach (var (name, err) in errors)
            {
                var t = Kit.Caption($"couldn't load {name}: {err}");
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(2, 8, 0, 0);
                t.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                _body.Children.Add(t);
            }
            var gear = Kit.Push("Calendars...", () =>
            {
                Close2();
                OpenSettings?.Invoke();
            });
            gear.HorizontalAlignment = HorizontalAlignment.Right;
            gear.Margin = new Thickness(0, 10, 0, 0);
            _body.Children.Add(gear);
        }

        FrameworkElement Row(string? color, string when, string title, string sub)
        {
            var g = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 1, 9, 1) };
            if (color != null)
            {
                try
                {
                    bar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                }
                catch
                {
                    bar.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                }
            }
            else bar.SetResourceReference(Border.BackgroundProperty, "SubText");
            DockPanel.SetDock(bar, Dock.Left);
            g.Children.Add(bar);
            var w = Kit.Text(when, 11.5, false, "SubText");
            w.Width = 62;
            DockPanel.SetDock(w, Dock.Left);
            g.Children.Add(w);
            var text = new StackPanel();
            var t = Kit.Text(title, 12.5);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(t);
            if (sub.Length > 0)
            {
                var s = Kit.Text(sub, 11, false, "Faint");
                s.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(s);
            }
            g.Children.Add(text);
            return g;
        }
    }
}
