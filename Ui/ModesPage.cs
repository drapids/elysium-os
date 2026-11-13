using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        public Func<string, Mode>? SnapshotMode;
        public Action<Mode>? UseMode;
        string _modeOpen = "";

        void ModesSection(Panel h)
        {
            h.Children.Add(Kit.Header("Modes"));
            var intro = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            var how = Kit.Caption("a mode flips a bunch of stuff at once: theme, accent, focus, sounds, audio setup. switch by hotkey, from the launcher, at a time of day or when an app opens");
            how.TextWrapping = TextWrapping.Wrap;
            how.Margin = new Thickness(0, 0, 0, 10);
            intro.Children.Add(how);
            var make = Kit.Push("+ New mode from how things are now", () =>
            {
                var m = SnapshotMode?.Invoke("Mode " + (_s.Modes.Count + 1)) ?? new Mode { Name = "Mode " + (_s.Modes.Count + 1) };
                _s.Modes.Add(m);
                _modeOpen = m.Id;
                Touch();
                Rebuild();
            }, true);
            make.HorizontalAlignment = HorizontalAlignment.Left;
            intro.Children.Add(make);
            h.Children.Add(Kit.Group(intro));

            foreach (var md in _s.Modes.ToList())
            {
                var m = md;
                bool open = _modeOpen == m.Id;
                var box = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
                var head = new DockPanel();
                var use = Kit.Push(_s.ActiveMode == m.Name ? "On" : "Switch", () =>
                {
                    UseMode?.Invoke(m);
                    Rebuild();
                }, _s.ActiveMode != m.Name);
                DockPanel.SetDock(use, Dock.Right);
                head.Children.Add(use);
                var edit = Kit.Plain(open ? "\uE70E" : "\uE70F", () =>
                {
                    _modeOpen = open ? "" : m.Id;
                    Rebuild();
                }, open ? "done" : "edit", 11);
                edit.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(edit, Dock.Right);
                head.Children.Add(edit);
                var names = new StackPanel();
                names.Children.Add(Kit.Text(m.Name.Length > 0 ? m.Name : "untitled", 13.5, true));
                var sum = Kit.Caption(Modes.Describe(m) + (m.Hotkey.Length > 0 ? ", " + m.Hotkey.ToLowerInvariant().Replace("+", " + ") : ""));
                sum.TextWrapping = TextWrapping.Wrap;
                names.Children.Add(sum);
                head.Children.Add(names);
                box.Children.Add(head);

                if (open) ModeEditor(box, m);
                h.Children.Add(Kit.Group(box));
            }
        }

        static UIElement Labeled(string label, UIElement content)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var t = Kit.Text(label, 12, false, "SubText");
            t.Width = 70;
            t.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(t, Dock.Left);
            row.Children.Add(t);
            row.Children.Add(content);
            return row;
        }

        void ModeEditor(StackPanel box, Mode m)
        {
            box.Children.Add(Labeled("Name", Kit.Field(m.Name, "Work, Gaming, Night...", v =>
            {
                string name = v.Trim();
                if (name == m.Name) return;
                if (_s.ActiveMode == m.Name) _s.ActiveMode = name;
                m.Name = name;
                Touch();
                Rebuild();
            })));
            box.Children.Add(Labeled("Theme", Kit.Segments(new[] { "Keep", "Auto", "Light", "Dark" }, new[] { "", "system", "light", "dark" }, m.Theme, v =>
            {
                m.Theme = v;
                Touch();
            })));

            bool hexAccent = m.Accent.StartsWith('#');
            box.Children.Add(Labeled("Accent", Kit.Segments(new[] { "Keep", "heft", "Windows", "Wallpaper", "Pick" }, new[] { "", "default", "system", "wallpaper", "#" }, hexAccent ? "#" : m.Accent, v =>
            {
                m.Accent = v == "#" ? (_s.AccentHex.Length > 0 ? _s.AccentHex : "#0A84FF") : v;
                Touch();
                Rebuild();
            })));
            if (hexAccent)
            {
                var swatches = new WrapPanel { Margin = new Thickness(70, 8, 0, 0) };
                foreach (var hex in AccentPresets)
                {
                    string hx = hex;
                    bool on = m.Accent.Equals(hx, StringComparison.OrdinalIgnoreCase);
                    var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hx)), Margin = new Thickness(0, 0, 7, 7), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(on ? 3 : 0), BorderBrush = Brushes.White };
                    dot.MouseLeftButtonUp += (_, _) =>
                    {
                        m.Accent = hx;
                        Touch();
                        Rebuild();
                    };
                    swatches.Children.Add(dot);
                }
                box.Children.Add(swatches);
            }

            box.Children.Add(Labeled("Focus", Kit.Segments(new[] { "Keep", "On", "Off" }, new[] { "", "on", "off" }, m.Focus, v =>
            {
                m.Focus = v;
                Touch();
            })));
            box.Children.Add(Labeled("Sounds", Kit.Segments(new[] { "Keep", "On", "Off" }, new[] { "", "on", "off" }, m.Sounds, v =>
            {
                m.Sounds = v;
                Touch();
            })));
            if (_s.AudioProfiles.Count > 0)
            {
                var names = new List<string> { "Keep" };
                var ids = new List<string> { "" };
                foreach (var p in _s.AudioProfiles.Take(4))
                {
                    names.Add(p.Name);
                    ids.Add(p.Name);
                }
                box.Children.Add(Labeled("Audio", Kit.Segments(names.ToArray(), ids.ToArray(), m.Audio, v =>
                {
                    m.Audio = v;
                    Touch();
                })));
            }

            var at = Kit.Field(m.At, "like 9:00 or 10pm", v =>
            {
                string t = v.Trim();
                if (t.Length > 0 && !Modes.ParseTime(t, out _)) return;
                m.At = t;
                Touch();
            });
            box.Children.Add(Labeled("At", at));
            var app = Kit.Field(m.App, "an app name, like valorant or code", v =>
            {
                m.App = v.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                Touch();
            });
            box.Children.Add(Labeled("When", app));
            var rec = Recorder(m.Hotkey, false, v =>
            {
                if (!CheckFree(v, m.Id, null))
                {
                    Rebuild();
                    return;
                }
                m.Hotkey = v;
                Touch();
                Rebuild();
            }, "no hotkey");
            rec.HorizontalAlignment = HorizontalAlignment.Left;
            box.Children.Add(Labeled("Hotkey", rec));
            if (_keyNote.Length > 0)
            {
                var note = Kit.Caption(_keyNote);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(70, 6, 0, 0);
                note.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                box.Children.Add(note);
                _keyNote = "";
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var snap = Kit.Push("Copy how things are now", () =>
            {
                var now = SnapshotMode?.Invoke(m.Name);
                if (now == null) return;
                m.Theme = now.Theme;
                m.Accent = now.Accent;
                m.Focus = now.Focus;
                m.Audio = now.Audio;
                m.Sounds = now.Sounds;
                Touch();
                Rebuild();
            });
            actions.Children.Add(snap);
            var remove = Kit.Push("Remove", () =>
            {
                _s.Modes.Remove(m);
                if (_s.ActiveMode == m.Name) _s.ActiveMode = "";
                _modeOpen = "";
                Touch();
                Rebuild();
            });
            remove.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(remove);
            box.Children.Add(actions);
        }
    }
}
