using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        void SaveBar(List<(string id, string side)> layout)
        {
            _s.BarLayout = layout.Select(x => x.id + ":" + x.side).ToList();
            Touch();
            Rebuild();
        }

        void BarEditor(Panel h)
        {
            h.Children.Add(Kit.Header("Bar items"));
            var layout = Bar.SlotLayout(_s);
            var rows = new List<UIElement>();
            for (int i = 0; i < layout.Count; i++)
            {
                int at = i;
                var (id, side) = layout[i];
                string label = Bar.SlotDefaults.First(d => d.id == id).label;
                var row = new DockPanel { Margin = new Thickness(14, 7, 10, 7) };
                var moves = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
                var up = Kit.Plain("\uE76B", () =>
                {
                    if (at == 0) return;
                    (layout[at - 1], layout[at]) = (layout[at], layout[at - 1]);
                    SaveBar(layout);
                }, "move left", 10);
                up.IsEnabled = at > 0;
                var down = Kit.Plain("\uE76C", () =>
                {
                    if (at >= layout.Count - 1) return;
                    (layout[at + 1], layout[at]) = (layout[at], layout[at + 1]);
                    SaveBar(layout);
                }, "move right", 10);
                down.IsEnabled = at < layout.Count - 1;
                moves.Children.Add(up);
                moves.Children.Add(down);
                DockPanel.SetDock(moves, Dock.Right);
                row.Children.Add(moves);
                var seg = Kit.Segments(new[] { "Left", "Right", "Off" }, new[] { "left", "right", "off" }, side, v =>
                {
                    layout[at] = (id, v);
                    SaveBar(layout);
                });
                seg.Width = 168;
                DockPanel.SetDock(seg, Dock.Right);
                row.Children.Add(seg);
                var name = Kit.Text(label, 12.5);
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                rows.Add(row);
            }
            var reset = Kit.Push("Reset", () =>
            {
                _s.BarLayout = new List<string>();
                Touch();
                Rebuild();
            });
            rows.Add(Kit.Row("Back to normal", "order goes left to right", reset));
            h.Children.Add(Kit.Group(rows.ToArray()));

            h.Children.Add(Kit.Header("Desktops"));
            h.Children.Add(Kit.Group(
                Kit.Row("A space for each desktop", "spaces opens the canvas that goes with the virtual desktop you're on. the dots in the bar switch desktops", Sw(_s.SpacePerDesktop, v => _s.SpacePerDesktop = v))));
        }
    }
}
