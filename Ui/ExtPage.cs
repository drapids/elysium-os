using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        public ExtHost? Ext;
        string _extRemoveArmed = "";
        string _extNote = "";

        static readonly (string call, string what)[] ExtCalls =
        {
            ("heft.command(id, name, fn)", "adds a launcher command"),
            ("heft.key('Ctrl+Alt+J', fn)", "adds a shortcut"),
            ("heft.bar(text, glyph, onClick)", "puts something in the top bar"),
            ("heft.widget(title, body)", "a card on Start"),
            ("heft.dropAction(name, paths => {})", "a shelf drop action"),
            ("heft.on('song' | 'window' | 'download' | 'clipboard' | 'monitor' | 'tick', fn)", "react to stuff"),
            ("heft.every(ms, fn) / heft.after(ms, fn)", "timers"),
            ("heft.fetch(url, (text, status) => {})", "get something from the web"),
            ("heft.request(url, headersJson, fn)", "same with headers"),
            ("heft.notify(title, body)", "a notification"),
            ("heft.open(target) / heft.copy(text)", "open stuff, copy text"),
            ("heft.media() / playPause() / next() / previous()", "what's playing"),
            ("heft.save(key, value) / heft.load(key)", "remember things"),
            ("heft.file(name) / heft.time(zone, format) / heft.log(msg)", "read your own files, other time zones, log")
        };

        void ExtensionsPage(Panel h)
        {
            var ext = Ext;
            if (ext == null)
            {
                h.Children.Add(Kit.Caption("extensions aren't loaded yet"));
                return;
            }

            if (_extNote.Length > 0)
            {
                var note = Kit.Caption(_extNote);
                note.Margin = new Thickness(14, 8, 14, 0);
                note.TextWrapping = TextWrapping.Wrap;
                note.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
                h.Children.Add(note);
                _extNote = "";
            }

            var top = new WrapPanel { Margin = new Thickness(14, 10, 14, 4) };
            var make = Kit.Push("+ New extension", () =>
            {
                string dir = ext.Create("My extension");
                _extNote = "made it in " + dir + ". edit main.js and heft reloads it when you save";
                Files.Open(dir);
                Rebuild();
            }, true);
            top.Children.Add(make);
            var install = Kit.Push("Install from file...", () => _dialog(() =>
            {
                var dlg = new OpenFileDialog { Title = "Install a heft extension", Filter = "heft extension|*.heftext;*.zip" };
                if (dlg.ShowDialog() != true) return;
                string? err = ext.Install(dlg.FileName);
                _extNote = err == null ? "installed. flip it on below" : "couldn't install it: " + err;
                Rebuild();
            }));
            install.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(install);
            var folder = Kit.Push("Open folder", () =>
            {
                Directory.CreateDirectory(ExtHost.Root);
                Files.Open(ExtHost.Root);
            });
            folder.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(folder);
            h.Children.Add(top);

            h.Children.Add(Kit.Header("Installed"));
            var list = ext.All.ToList();
            if (list.Count == 0)
            {
                var none = Kit.Caption("nothing here yet");
                none.Margin = new Thickness(14, 4, 14, 0);
                h.Children.Add(none);
            }
            foreach (var e in list)
            {
                var item = e;
                var box = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
                var head = new DockPanel();
                var sw = Sw(ext.Enabled(item.Id), v => ext.SetEnabled(item.Id, v));
                DockPanel.SetDock(sw, Dock.Right);
                head.Children.Add(sw);
                var names = new StackPanel();
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(Kit.Text(item.Manifest.Name, 13.5, true));
                var ver = Kit.Text("  " + item.Manifest.Version + (item.Manifest.Author.Length > 0 ? "  ·  " + item.Manifest.Author : ""), 11.5, false, "Faint");
                ver.VerticalAlignment = VerticalAlignment.Bottom;
                title.Children.Add(ver);
                names.Children.Add(title);
                if (item.Manifest.Description.Length > 0)
                {
                    var d = Kit.Caption(item.Manifest.Description);
                    d.TextWrapping = TextWrapping.Wrap;
                    d.Margin = new Thickness(0, 2, 10, 0);
                    names.Children.Add(d);
                }
                head.Children.Add(names);
                box.Children.Add(head);

                if (item.Manifest.Permissions.Count > 0)
                {
                    var perms = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                    foreach (var p in item.Manifest.Permissions)
                    {
                        string label = ExtHost.PermissionNames.FirstOrDefault(x => x.id.Equals(p, StringComparison.OrdinalIgnoreCase)).label ?? p;
                        var chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 5, 5), BorderThickness = new Thickness(1) };
                        chip.SetResourceReference(Border.BackgroundProperty, "GroupBg");
                        chip.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
                        chip.Child = Kit.Text(label, 11, false, "SubText");
                        perms.Children.Add(chip);
                    }
                    box.Children.Add(perms);
                }

                if (item.Error.Length > 0)
                {
                    var err = Kit.Caption(item.Error);
                    err.TextWrapping = TextWrapping.Wrap;
                    err.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E));
                    err.Margin = new Thickness(0, 6, 0, 0);
                    box.Children.Add(err);
                }

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var reload = Kit.Push("Reload", () =>
                {
                    ext.Restart(item.Id);
                    Rebuild();
                });
                actions.Children.Add(reload);
                var open = Kit.Push("Open folder", () => Files.Open(item.Dir));
                open.Margin = new Thickness(6, 0, 0, 0);
                actions.Children.Add(open);
                bool armed = _extRemoveArmed == item.Id;
                var remove = Kit.Push(armed ? "Click again to remove" : "Remove", () =>
                {
                    if (_extRemoveArmed != item.Id)
                    {
                        _extRemoveArmed = item.Id;
                        Rebuild();
                        return;
                    }
                    _extRemoveArmed = "";
                    ext.Remove(item.Id);
                    Rebuild();
                });
                remove.Margin = new Thickness(6, 0, 0, 0);
                actions.Children.Add(remove);
                box.Children.Add(actions);
                h.Children.Add(Kit.Group(box));
            }

            h.Children.Add(Kit.Header("Make your own"));
            var how = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            var intro = Kit.Text("an extension is a folder with a manifest.json and a main.js. it runs inside heft (no node needed) and can only do what its manifest asks for. save the file and heft reloads it. share it by zipping the folder and renaming it to .heftext", 12, false, "SubText");
            intro.TextWrapping = TextWrapping.Wrap;
            intro.Margin = new Thickness(0, 0, 0, 10);
            how.Children.Add(intro);
            foreach (var (call, what) in ExtCalls)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var w = Kit.Text(what, 11.5, false, "Faint");
                w.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(w, Dock.Right);
                row.Children.Add(w);
                var c = Kit.Text(call, 11.5);
                c.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                c.TextWrapping = TextWrapping.Wrap;
                row.Children.Add(c);
                how.Children.Add(row);
            }
            h.Children.Add(Kit.Group(how));
        }
    }
}
