using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        public Commands? Cmds;
        public Action<bool>? PauseKeys;
        public Func<string, bool>? KeyFree;

        static readonly (string keys, string what)[] WindowsKeys =
        {
            ("Win+L", "locking your pc"), ("Win+D", "show desktop"), ("Win+E", "File Explorer"), ("Win+R", "Run"),
            ("Win+I", "Windows settings"), ("Win+A", "action center"), ("Win+S", "search"), ("Win+X", "the power menu"),
            ("Win+Tab", "task view"), ("Win+V", "clipboard history"), ("Win+P", "projecting"), ("Win+K", "connect"),
            ("Win+G", "the game bar"), ("Win+M", "minimizing everything"), ("Win+Up", "maximizing"), ("Win+Down", "minimizing"),
            ("Win+Left", "snapping left"), ("Win+Right", "snapping right"), ("Win+Shift+S", "snip & sketch"), ("Win+OemPeriod", "emoji"),
            ("Win+Ctrl+D", "new desktop"), ("Win+Ctrl+Left", "switching desktops"), ("Win+Ctrl+Right", "switching desktops"),
            ("Ctrl+Alt+Delete", "the security screen"), ("Ctrl+Shift+Escape", "Task Manager"), ("Alt+Tab", "switching windows"),
            ("Alt+F4", "closing windows"), ("Alt+Escape", "cycling windows"), ("Ctrl+Escape", "Start"),
            ("Ctrl+C", "copy"), ("Ctrl+V", "paste"), ("Ctrl+X", "cut"), ("Ctrl+Z", "undo"), ("Ctrl+Y", "redo"),
            ("Ctrl+A", "select all"), ("Ctrl+S", "save"), ("Ctrl+F", "find"), ("Ctrl+T", "new tab"), ("Ctrl+W", "closing tabs"),
            ("Ctrl+N", "new window"), ("Ctrl+P", "print"), ("Ctrl+Tab", "switching tabs")
        };

        static readonly (string id, string label, string glyph)[] Kinds =
        {
            ("app", "App", "\uE71D"),
            ("file", "File", "\uE8A5"),
            ("folder", "Folder", "\uE8B7"),
            ("url", "Website", "\uE774"),
            ("text", "Type text", "\uE8D2"),
            ("heft", "heft action", "\uE945"),
            ("script", "Steps", "\uE8FD"),
            ("volume", "App volume", "\uE767")
        };

        CustomKey? _editing;
        bool _editingNew;
        string _remapFrom = "", _remapTo = "";
        string _keyNote = "";

        void Keys(Panel h)
        {
            if (_keyNote.Length > 0)
            {
                var note = Kit.Caption(_keyNote);
                note.Margin = new Thickness(14, 8, 14, 0);
                note.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                note.TextWrapping = TextWrapping.Wrap;
                h.Children.Add(note);
                _keyNote = "";
            }

            h.Children.Add(Kit.Header("heft shortcuts"));
            var rows = new List<UIElement>();
            foreach (var d in Hotkeys.Defaults)
            {
                string id = d.id;
                string fallback = d.fallback;
                string cur = Hotkeys.Spec(_s, id);
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(Recorder(cur, false, v => SetBuiltIn(id, fallback, v, d.label)));
                bool changed = _s.Keys.ContainsKey(id);
                if (changed && fallback.Length > 0)
                {
                    var reset = Kit.Plain("\uE7A7", () =>
                    {
                        _s.Keys.Remove(id);
                        Touch();
                        Rebuild();
                    }, "back to " + fallback, 10);
                    reset.Margin = new Thickness(4, 0, 0, 0);
                    right.Children.Add(reset);
                }
                if (cur.Length > 0)
                {
                    var off = Kit.Plain("\uE711", () =>
                    {
                        _s.Keys[id] = "";
                        Touch();
                        Rebuild();
                    }, "turn this shortcut off", 10);
                    off.Margin = new Thickness(4, 0, 0, 0);
                    right.Children.Add(off);
                }
                rows.Add(Kit.Row(d.label, null, right));
            }
            h.Children.Add(Kit.Group(rows.ToArray()));
            var cap = Kit.Caption("click the keys and press a new combo. esc cancels, backspace clears it");
            cap.Margin = new Thickness(14, 6, 14, 0);
            h.Children.Add(cap);

            h.Children.Add(Kit.Header("Your shortcuts"));
            var mine = new List<UIElement>();
            foreach (var k in _s.CustomKeys.ToList())
            {
                var key = k;
                if (_editing == key && !_editingNew)
                {
                    mine.Add(Editor(key));
                    continue;
                }
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(Recorder(key.Keys, false, v =>
                {
                    if (!CheckFree(v, key.Id, null)) return;
                    key.Keys = v;
                    Touch();
                    Rebuild();
                }));
                var edit = Kit.Plain("\uE70F", () =>
                {
                    _editing = key;
                    _editingNew = false;
                    Rebuild();
                }, "edit", 10);
                edit.Margin = new Thickness(4, 0, 0, 0);
                right.Children.Add(edit);
                var del = Kit.Plain("\uE74D", () =>
                {
                    _s.CustomKeys.Remove(key);
                    Touch();
                    Rebuild();
                }, "delete", 10);
                del.Margin = new Thickness(2, 0, 0, 0);
                right.Children.Add(del);
                mine.Add(Kit.Row(KeyTitle(key), Describe(key), right));
            }
            if (_editing != null && _editingNew) mine.Add(Editor(_editing));
            else
            {
                var add = Kit.Push("+ New shortcut", () =>
                {
                    _editing = new CustomKey { Kind = "app" };
                    _editingNew = true;
                    Rebuild();
                }, true);
                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Margin = new Thickness(14, 10, 14, 10);
                mine.Add(add);
            }
            h.Children.Add(Kit.Group(mine.ToArray()));
            var cap2 = Kit.Caption("open an app, a file, a folder or a website, type out text, run any heft action, or chain steps like &focus ; spotify: ; wait 2");
            cap2.Margin = new Thickness(14, 6, 14, 0);
            cap2.TextWrapping = TextWrapping.Wrap;
            h.Children.Add(cap2);

            h.Children.Add(Kit.Header("Remap keys"));
            var remaps = new List<UIElement>();
            foreach (var kv in _s.Remaps.ToList())
            {
                string from = kv.Key;
                var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                line.Children.Add(Caps(from));
                var arrow = Kit.Icon("\uE72A", 11, "SubText");
                arrow.Margin = new Thickness(8, 0, 8, 0);
                line.Children.Add(arrow);
                line.Children.Add(Caps(kv.Value));
                var del = Kit.Plain("\uE74D", () =>
                {
                    _s.Remaps.Remove(from);
                    Touch();
                    Rebuild();
                }, "remove", 10);
                var grid = new DockPanel { Margin = new Thickness(14, 8, 10, 8) };
                DockPanel.SetDock(del, Dock.Right);
                grid.Children.Add(del);
                grid.Children.Add(line);
                if (remaps.Count > 0) remaps.Add(Sep());
                remaps.Add(grid);
            }
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 10) };
            addRow.Children.Add(Recorder(_remapFrom, true, v =>
            {
                _remapFrom = v;
                Rebuild();
            }, "this key"));
            var arrow2 = Kit.Icon("\uE72A", 11, "SubText");
            arrow2.Margin = new Thickness(10, 0, 10, 0);
            arrow2.VerticalAlignment = VerticalAlignment.Center;
            addRow.Children.Add(arrow2);
            addRow.Children.Add(Recorder(_remapTo, false, v =>
            {
                _remapTo = v;
                Rebuild();
            }, "acts like"));
            var save = Kit.Push("Add", () =>
            {
                if (_remapFrom.Length == 0 || _remapTo.Length == 0) return;
                _s.Remaps[_remapFrom] = _remapTo;
                _remapFrom = "";
                _remapTo = "";
                Touch();
                Rebuild();
            }, true);
            save.Margin = new Thickness(12, 0, 0, 0);
            save.IsEnabled = _remapFrom.Length > 0 && _remapTo.Length > 0;
            addRow.Children.Add(save);
            if (remaps.Count > 0) remaps.Add(Sep());
            remaps.Add(addRow);
            h.Children.Add(Kit.Group(remaps.ToArray()));
            var cap3 = Kit.Caption("make one key act like another key or a combo. like caps lock as escape, or a spare key as ctrl+shift+m");
            cap3.Margin = new Thickness(14, 6, 14, 0);
            cap3.TextWrapping = TextWrapping.Wrap;
            h.Children.Add(cap3);

            var errors = _keyErrors().ToList();
            if (errors.Count > 0)
            {
                h.Children.Add(Kit.Header("Didn't work"));
                h.Children.Add(Kit.Group(errors.Select(e => (UIElement)Kit.Row(e, null, null)).ToArray()));
            }
        }

        static Border Sep() => new() { Style = (Style)Application.Current.FindResource("Sep") };

        public static string VolumeTitle(string target)
        {
            var parts = target.Split('|');
            string app = parts.Length > 0 && parts[0].Length > 0 && parts[0] != "*" ? parts[0] : "focused app";
            string act = parts.Length > 1 ? parts[1] : "up";
            return act switch
            {
                "down" => app + " quieter",
                "mute" => "Mute " + app,
                _ => app + " louder"
            };
        }

        static string KeyTitle(CustomKey k)
        {
            if (k.Label.Length > 0) return k.Label;
            string t = k.Target;
            return k.Kind switch
            {
                "app" or "file" or "folder" => Path.GetFileNameWithoutExtension(t.TrimEnd('\\')) is { Length: > 0 } n ? n : t,
                "url" => t.Replace("https://", "").Replace("http://", "").TrimEnd('/'),
                "text" => "Type \"" + (t.Length > 24 ? t[..24] + "..." : t) + "\"",
                "volume" => VolumeTitle(t),
                _ => t.Length > 30 ? t[..30] + "..." : t
            };
        }

        string Describe(CustomKey k)
        {
            string kind = Kinds.FirstOrDefault(x => x.id == k.Kind).label ?? k.Kind;
            if (k.Kind == "heft")
            {
                var c = Cmds?.Get(k.Target);
                return kind + (c != null ? ": " + c.Name : "");
            }
            return kind.ToLowerInvariant();
        }

        UIElement Editor(CustomKey k)
        {
            var box = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            box.Children.Add(Kit.Text(_editingNew ? "New shortcut" : "Edit shortcut", 13.5, true));

            var kinds = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
            foreach (var (id, label, glyph) in Kinds)
            {
                string kid = id;
                bool on = k.Kind == kid;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Kit.Icon(glyph, 11, on ? "OnAccent" : "SubText");
                g.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(g);
                sp.Children.Add(Kit.Text(label, 12, on, on ? "OnAccent" : "Text"));
                var chip = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5, 10, 6), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentFill" : "GroupStroke");
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    if (k.Kind != kid) k.Target = "";
                    k.Kind = kid;
                    Rebuild();
                };
                kinds.Children.Add(chip);
            }
            box.Children.Add(kinds);

            string hint = k.Kind switch
            {
                "app" => "path to the app (.exe or a shortcut)",
                "file" => "path to the file",
                "folder" => "path to the folder",
                "url" => "https://...",
                "text" => "what it should type",
                "script" => "&focus ; spotify: ; wait 2 ; https://docs.google.com",
                _ => ""
            };

            if (k.Kind == "heft")
            {
                box.Children.Add(HeftPicker(k));
            }
            else if (k.Kind == "volume")
            {
                box.Children.Add(VolumePicker(k));
            }
            else
            {
                var row = new DockPanel();
                if (k.Kind is "app" or "file" or "folder")
                {
                    var browse = Kit.Push("Browse...", () => _dialog(() =>
                    {
                        if (k.Kind == "folder")
                        {
                            var fd = new OpenFolderDialog { Title = "Pick a folder" };
                            if (fd.ShowDialog() == true) k.Target = fd.FolderName;
                        }
                        else
                        {
                            var od = new OpenFileDialog { Title = k.Kind == "app" ? "Pick an app" : "Pick a file", Filter = k.Kind == "app" ? "Apps|*.exe;*.lnk;*.url;*.bat;*.cmd|Anything|*.*" : "Anything|*.*", DereferenceLinks = false };
                            if (od.ShowDialog() == true) k.Target = od.FileName;
                        }
                        Rebuild();
                    }));
                    browse.Margin = new Thickness(8, 0, 0, 0);
                    DockPanel.SetDock(browse, Dock.Right);
                    row.Children.Add(browse);
                }
                var field = Kit.Field(k.Target, hint, v => k.Target = v, k.Kind is "text" or "script");
                row.Children.Add(field);
                box.Children.Add(row);
            }

            var nameRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var nameLabel = Kit.Text("Name", 12, false, "SubText");
            nameLabel.Width = 60;
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            nameRow.Children.Add(Kit.Field(k.Label, "optional", v => k.Label = v.Trim()));
            box.Children.Add(nameRow);

            var keyRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var keyLabel = Kit.Text("Keys", 12, false, "SubText");
            keyLabel.Width = 60;
            keyLabel.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(keyLabel, Dock.Left);
            keyRow.Children.Add(keyLabel);
            var rec = Recorder(k.Keys, false, v =>
            {
                if (!CheckFree(v, k.Id, null)) return;
                k.Keys = v;
                Rebuild();
            });
            rec.HorizontalAlignment = HorizontalAlignment.Left;
            keyRow.Children.Add(rec);
            box.Children.Add(keyRow);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var ok = Kit.Push(_editingNew ? "Add" : "Save", () =>
            {
                if (k.Target.Trim().Length == 0)
                {
                    _keyNote = "pick what the shortcut should do first";
                    Rebuild();
                    return;
                }
                if (k.Keys.Length == 0)
                {
                    _keyNote = "press the keys you want first";
                    Rebuild();
                    return;
                }
                k.Target = k.Target.Trim();
                if (_editingNew) _s.CustomKeys.Add(k);
                _editing = null;
                _editingNew = false;
                Touch();
                Rebuild();
            }, true);
            buttons.Children.Add(ok);
            var cancel = Kit.Push("Cancel", () =>
            {
                _editing = null;
                _editingNew = false;
                Rebuild();
            });
            cancel.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(cancel);
            box.Children.Add(buttons);
            return box;
        }

        UIElement HeftPicker(CustomKey k)
        {
            var wrap = new StackPanel();
            var list = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var all = (Cmds?.All ?? Enumerable.Empty<Command>()).Where(c => !c.Danger).OrderBy(c => c.Name).ToList();
            void Fill(string q)
            {
                list.Children.Clear();
                var shown = q.Length == 0
                    ? all.Where(c => c.Id == k.Target).Concat(all.Where(c => c.Group == "heft" && c.Id != k.Target)).Take(18)
                    : all.Select(c => (c, score: Math.Max(Fuzzy.Score(q, c.Name), Fuzzy.Score(q, c.Keywords) * 0.6))).Where(x => x.score > 0).OrderByDescending(x => x.score).Select(x => x.c).Take(18);
                foreach (var c in shown)
                {
                    var cmd = c;
                    bool on = cmd.Id == k.Target;
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    var g = Kit.Icon(cmd.Glyph, 11, on ? "OnAccent" : "SubText");
                    g.Margin = new Thickness(0, 0, 6, 0);
                    sp.Children.Add(g);
                    sp.Children.Add(Kit.Text(cmd.Name, 12, on, on ? "OnAccent" : "Text"));
                    var chip = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5, 10, 6), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
                    chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        k.Target = cmd.Id;
                        if (k.Label.Length == 0) k.Label = cmd.Name;
                        Rebuild();
                    };
                    list.Children.Add(chip);
                }
            }
            var search = Kit.Field("", "search heft actions and commands", Fill);
            wrap.Children.Add(search);
            wrap.Children.Add(list);
            Fill("");
            return wrap;
        }

        void SetBuiltIn(string id, string fallback, string v, string label)
        {
            if (v.Length > 0 && !CheckFree(v, null, id)) return;
            if (v.Equals(fallback, StringComparison.OrdinalIgnoreCase)) _s.Keys.Remove(id);
            else _s.Keys[id] = v;
            Touch();
            Rebuild();
        }

        static bool Same(string a, string b)
        {
            if (!Hotkeys.TryParse(a, out uint ma, out uint va) || !Hotkeys.TryParse(b, out uint mb, out uint vb)) return false;
            return ma == mb && va == vb;
        }

        bool CheckFree(string spec, string? customId, string? builtInId)
        {
            if (spec.Length == 0) return true;
            foreach (var d in Hotkeys.Defaults)
            {
                if (d.id == builtInId) continue;
                if (Same(Hotkeys.Spec(_s, d.id), spec))
                {
                    _keyNote = $"{Pretty(spec)} is already {d.label.ToLowerInvariant()}. change that one first";
                    Rebuild();
                    return false;
                }
            }
            foreach (var c in _s.CustomKeys)
            {
                if (c.Id == customId) continue;
                if (Same(c.Keys, spec))
                {
                    _keyNote = $"{Pretty(spec)} is already used by your shortcut \"{KeyTitle(c)}\"";
                    Rebuild();
                    return false;
                }
            }
            foreach (var p in _s.AudioProfiles)
            {
                if (Same(p.Hotkey, spec))
                {
                    _keyNote = $"{Pretty(spec)} already switches to the {p.Name} audio profile";
                    Rebuild();
                    return false;
                }
            }
            foreach (var (keys, what) in WindowsKeys)
            {
                if (Same(keys, spec))
                {
                    _keyNote = $"heads up, Windows or most apps use {Pretty(spec)} for {what}. heft will take it over";
                    return true;
                }
            }
            if (KeyFree != null && !KeyFree(spec)) _keyNote = $"another app already grabbed {Pretty(spec)}, so it might not work";
            return true;
        }

        static string Pretty(string spec) => string.Join(" + ", spec.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(KeyName));

        static string KeyName(string part)
        {
            return part.Trim() switch
            {
                "OemPeriod" => ".",
                "OemComma" => ",",
                "OemMinus" => "-",
                "OemPlus" => "=",
                "OemQuestion" => "/",
                "OemSemicolon" => ";",
                "OemQuotes" => "'",
                "OemOpenBrackets" => "[",
                "OemCloseBrackets" or "Oem6" => "]",
                "OemPipe" or "Oem5" => "\\",
                "OemTilde" or "Oem3" => "`",
                "Capital" or "CapsLock" => "Caps Lock",
                "Return" or "Enter" => "Enter",
                "Escape" => "Esc",
                "Next" or "PageDown" => "Page Down",
                "Prior" or "PageUp" => "Page Up",
                "Snapshot" or "PrintScreen" => "Print Screen",
                "Back" => "Backspace",
                "Space" => "Space",
                var x when x.Length == 2 && x[0] == 'D' && char.IsDigit(x[1]) => x[1].ToString(),
                var x when x.StartsWith("NumPad") => "Num " + x[6..],
                var x => x
            };
        }

        static UIElement Caps(string spec)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (spec.Length == 0)
            {
                sp.Children.Add(Kit.Text("off", 12, false, "Faint"));
                return sp;
            }
            foreach (var part in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var b = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 0, 4, 0), MinWidth = 22 };
                b.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                b.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
                var t = Kit.Text(KeyName(part), 11.5, true);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                b.Child = t;
                sp.Children.Add(b);
            }
            return sp;
        }

        FrameworkElement Recorder(string current, bool singleKey, Action<string> set, string empty = "set keys")
        {
            var host = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4), Cursor = Cursors.Hand, Focusable = true, MinWidth = 90, MinHeight = 28, Background = Brushes.Transparent, ToolTip = "click, then press the keys" };
            host.SetResourceReference(Border.BorderBrushProperty, "FieldStroke");
            UIElement Idle() => current.Length > 0 ? Caps(current) : Kit.Text(empty, 12, false, "Faint");
            host.Child = Idle();
            bool recording = false;
            string mods = "";

            void Stop(string? result)
            {
                if (!recording) return;
                recording = false;
                PauseKeys?.Invoke(false);
                host.SetResourceReference(Border.BorderBrushProperty, "FieldStroke");
                if (result == null)
                {
                    host.Child = Idle();
                    return;
                }
                set(result);
            }

            void Live()
            {
                var t = Kit.Text(mods.Length > 0 ? Pretty(mods) + " + ..." : "press keys...", 12, false, "AccentFill");
                host.Child = t;
            }

            host.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (recording) return;
                recording = true;
                mods = "";
                PauseKeys?.Invoke(true);
                host.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
                Live();
                host.Focus();
                Keyboard.Focus(host);
            };
            host.LostKeyboardFocus += (_, _) => Stop(null);
            host.PreviewKeyDown += (_, e) =>
            {
                if (!recording) return;
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
                var m = Keyboard.Modifiers;
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                if (key == Key.Escape && m == ModifierKeys.None)
                {
                    Stop(null);
                    return;
                }
                if ((key == Key.Back || key == Key.Delete) && m == ModifierKeys.None && !singleKey)
                {
                    Stop("");
                    return;
                }
                var parts = new List<string>();
                if (!singleKey)
                {
                    if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                    if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                    if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                    if (win || m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                }
                bool isMod = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
                if (isMod && !singleKey)
                {
                    mods = string.Join("+", parts);
                    Live();
                    return;
                }
                string name = key switch
                {
                    >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
                    Key.Capital => "CapsLock",
                    Key.Return => "Enter",
                    Key.Next => "PageDown",
                    Key.Prior => "PageUp",
                    Key.Snapshot => "PrintScreen",
                    Key.Oem3 => "OemTilde",
                    Key.LeftCtrl or Key.RightCtrl => "LeftCtrl",
                    Key.LeftAlt or Key.RightAlt => "LeftAlt",
                    Key.LeftShift or Key.RightShift => "LeftShift",
                    _ => key.ToString()
                };
                parts.Add(name);
                string spec = string.Join("+", parts);
                if (!Hotkeys.TryParse(spec, out uint checkMods, out uint checkVk))
                {
                    _keyNote = $"heft can't use {Pretty(spec)}, try another key";
                    Stop(null);
                    Rebuild();
                    return;
                }
                Stop(spec);
            };
            host.PreviewKeyUp += (_, e) =>
            {
                if (!recording || singleKey) return;
                e.Handled = true;
                var m = Keyboard.Modifiers;
                var parts = new List<string>();
                if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                mods = string.Join("+", parts);
                Live();
            };
            return host;
        }

        public Func<IEnumerable<string>>? AudioApps;

        UIElement VolumePicker(CustomKey k)
        {
            var parts = k.Target.Split('|');
            string app = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "*";
            string act = parts.Length > 1 ? parts[1] : "up";
            if (k.Target.Length == 0) k.Target = app + "|" + act;
            var box = new StackPanel();
            var apps = new List<string> { "*" };
            try
            {
                if (AudioApps != null) apps.AddRange(AudioApps().Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
            }
            if (!apps.Contains(app, StringComparer.OrdinalIgnoreCase)) apps.Add(app);
            var chips = new WrapPanel();
            foreach (var a in apps.Take(14))
            {
                string id = a;
                bool on = id.Equals(app, StringComparison.OrdinalIgnoreCase);
                var chip = new Border { Child = Kit.Text(id == "*" ? "whatever's focused" : id, 12, on, on ? "OnAccent" : "Text"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentFill" : "GroupStroke");
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    k.Target = id + "|" + act;
                    Rebuild();
                };
                chips.Children.Add(chip);
            }
            box.Children.Add(chips);
            var other = Kit.Field(app == "*" ? "" : app, "or type an app name (spotify, discord, chrome)", v =>
            {
                string t = v.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                k.Target = (t.Length == 0 ? "*" : t) + "|" + (k.Target.Split('|').ElementAtOrDefault(1) ?? "up");
            });
            other.Margin = new Thickness(0, 2, 0, 8);
            box.Children.Add(other);
            box.Children.Add(Kit.Segments(new[] { "Louder", "Quieter", "Mute" }, new[] { "up", "down", "mute" }, act, v =>
            {
                k.Target = (k.Target.Split('|').ElementAtOrDefault(0) ?? "*") + "|" + v;
            }));
            return box;
        }
    }
}
