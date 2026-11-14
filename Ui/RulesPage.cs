using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Elysium.Ui
{
    internal sealed partial class SettingsPages
    {
        public Func<WindowRule?>? CaptureRule;
        public Func<string>? LastWindow;

        void RulesSection(Panel h)
        {
            h.Children.Add(Kit.Header("Window rules"));
            string last = LastWindow?.Invoke() ?? "";
            var add = Kit.Push(last.Length > 0 ? "Remember " + last : "Remember a window", () =>
            {
                CaptureRule?.Invoke();
                Rebuild();
            }, true);
            add.IsEnabled = last.Length > 0;
            var top = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            var how = Kit.Caption("put a window where you want it, then hit this (or run \"remember where this window goes\" from the launcher). next time it opens it goes right there");
            how.TextWrapping = TextWrapping.Wrap;
            how.Margin = new Thickness(0, 0, 0, 10);
            top.Children.Add(how);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            top.Children.Add(add);
            h.Children.Add(Kit.Group(top));

            if (_s.WindowRules.Count == 0) return;
            var ids = WindowRules.Places.Select(p => p.id).ToArray();
            var labels = WindowRules.Places.Select(p => p.label).ToArray();
            foreach (var r in _s.WindowRules.ToList())
            {
                var rule = r;
                var box = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
                var head = new DockPanel();
                var remove = Kit.Push("Remove", () =>
                {
                    _s.WindowRules.Remove(rule);
                    Touch();
                    Rebuild();
                });
                DockPanel.SetDock(remove, Dock.Right);
                head.Children.Add(remove);
                var on = Sw(rule.On, v => rule.On = v);
                on.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(on, Dock.Left);
                head.Children.Add(on);
                var name = Kit.Text(rule.Exe, 13.5, true);
                name.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(name);
                box.Children.Add(head);

                string where = rule.Place switch
                {
                    "max" => "maximized",
                    "center" => "centered",
                    "none" => "wherever it wants",
                    _ => $"{Math.Round(rule.W * 100)}% × {Math.Round(rule.H * 100)}% of the screen"
                };
                var cap = Kit.Caption($"monitor {Math.Max(0, rule.MonitorIndex) + 1}, {where}");
                cap.Margin = new Thickness(0, 4, 0, 8);
                box.Children.Add(cap);
                box.Children.Add(Kit.Segments(labels, ids, rule.Place, v =>
                {
                    rule.Place = v;
                    Touch();
                }));
                var pin = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                var pinSw = Sw(rule.Topmost, v => rule.Topmost = v);
                DockPanel.SetDock(pinSw, Dock.Right);
                pin.Children.Add(pinSw);
                pin.Children.Add(Kit.Text("Always on top", 12.5));
                box.Children.Add(pin);
                h.Children.Add(Kit.Group(box));
            }
        }
    }
}
