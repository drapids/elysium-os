using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Elysium.Ui
{
    internal sealed partial class SettingsPages
    {
        static readonly string[] GesturePatterns = { "U", "D", "L", "R", "UD", "DU", "LR", "RL", "DR", "DL", "UR", "UL" };
        string _gestureOpen = "";
        string _newTrigger = "", _newText = "";

        void SnippetsSection(Panel h)
        {
            h.Children.Add(Kit.Header("Text shortcuts"));
            var rows = new List<UIElement>
            {
                Kit.Row("Expand as you type", "type a trigger anywhere and it turns into the full text. {date} {time} {day} {iso} {clipboard} fill themselves in", Sw(_s.SnippetsOn, v => _s.SnippetsOn = v)),
                Kit.Row("Paste plain text", KeyHint("pasteplain") + " pastes without the fonts and colors, in any app", Sw(_s.PastePlain, v => _s.PastePlain = v))
            };
            foreach (var sn in _s.Snippets.ToList())
            {
                var snip = sn;
                var row = new DockPanel { Margin = new Thickness(14, 6, 10, 6) };
                var remove = Kit.Plain("\uE711", () =>
                {
                    _s.Snippets.Remove(snip);
                    Touch();
                    Rebuild();
                }, "remove", 10);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var trig = Kit.Field(snip.Trigger, ";trigger", v =>
                {
                    snip.Trigger = v.Trim();
                    Touch();
                });
                trig.Width = 96;
                trig.Margin = new Thickness(0, 0, 8, 0);
                trig.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
                DockPanel.SetDock(trig, Dock.Left);
                row.Children.Add(trig);
                var text = Kit.Field(snip.Text, "what it turns into", v =>
                {
                    snip.Text = v;
                    Touch();
                }, true);
                text.MinHeight = 34;
                row.Children.Add(text);
                rows.Add(row);
            }

            var add = new DockPanel { Margin = new Thickness(14, 8, 10, 10) };
            var newTrig = Kit.Field(_newTrigger, ";new", v => _newTrigger = v.Trim());
            newTrig.Width = 96;
            newTrig.Margin = new Thickness(0, 0, 8, 0);
            newTrig.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
            var newText = Kit.Field(_newText, "turns into...", v => _newText = v, true);
            newText.MinHeight = 34;
            var save = Kit.Push("Add", () =>
            {
                Keyboard.ClearFocus();
                string t = newTrig.Text.Trim(), body = newText.Text;
                if (t.Length < 2 || body.Length == 0) return;
                _s.Snippets.RemoveAll(x => x.Trigger == t);
                _s.Snippets.Add(new Snippet { Trigger = t, Text = body });
                _newTrigger = "";
                _newText = "";
                Touch();
                Rebuild();
            }, true);
            save.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(newTrig, Dock.Left);
            DockPanel.SetDock(save, Dock.Right);
            add.Children.Add(newTrig);
            add.Children.Add(save);
            add.Children.Add(newText);
            rows.Add(add);
            h.Children.Add(Kit.Group(rows.ToArray()));
            var cap = Kit.Caption("start triggers with something you never type normally, like ; or //");
            cap.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(cap);
        }

        void GesturesSection(Panel h)
        {
            h.Children.Add(Kit.Header("Mouse gestures"));
            var rows = new List<UIElement>
            {
                Kit.Row("Right-drag gestures", "hold right click, draw a line, let go. a plain right click still works like normal. off in fullscreen games and on file lists", Sw(_s.Gestures, v =>
                {
                    _s.Gestures = v;
                    Rebuild();
                }))
            };
            if (_s.Gestures)
            {
                foreach (var pat in GesturePatterns)
                {
                    string p = pat;
                    string cur = _s.GestureMap.TryGetValue(p, out var a) ? a : "none";
                    string label = App.GestureActions.FirstOrDefault(g => g.id == cur).label ?? cur;
                    bool open = _gestureOpen == p;
                    var box = new StackPanel { Margin = new Thickness(14, 6, 14, 6) };
                    var head = new DockPanel { Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
                    var val = Kit.Text(label, 12.5, false, cur == "none" ? "Faint" : "AccentFill");
                    DockPanel.SetDock(val, Dock.Right);
                    head.Children.Add(val);
                    var arrows = Kit.Text(App.Arrows(p), 15, true);
                    head.Children.Add(arrows);
                    head.MouseLeftButtonUp += (_, _) =>
                    {
                        _gestureOpen = open ? "" : p;
                        Rebuild();
                    };
                    box.Children.Add(head);
                    if (open)
                    {
                        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 2) };
                        foreach (var (id, name) in App.GestureActions)
                        {
                            string aid = id;
                            bool on = aid == cur;
                            var chip = new Border { Child = Kit.Text(name, 11.5, on, on ? "OnAccent" : "Text"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
                            chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentFill" : "GroupStroke");
                            chip.MouseLeftButtonUp += (_, _) =>
                            {
                                if (aid == "none") _s.GestureMap.Remove(p);
                                else _s.GestureMap[p] = aid;
                                _gestureOpen = "";
                                Touch();
                                Rebuild();
                            };
                            chips.Children.Add(chip);
                        }
                        box.Children.Add(chips);
                    }
                    rows.Add(box);
                }
            }
            h.Children.Add(Kit.Group(rows.ToArray()));
        }
    }
}
