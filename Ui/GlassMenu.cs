using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal sealed class MenuEntry
    {
        public string Glyph = "";
        public string Label = "";
        public Action? Run;
        public bool Danger;
        public bool Check;
        public bool Header;
        public bool Line;
        public List<MenuEntry>? Kids;

        public static MenuEntry Item(string glyph, string label, Action run, bool danger = false) => new() { Glyph = glyph, Label = label, Run = run, Danger = danger };

        public static MenuEntry Toggle(string glyph, string label, bool on, Action run) => new() { Glyph = glyph, Label = label, Run = run, Check = on };

        public static MenuEntry Sub(string glyph, string label, List<MenuEntry> kids) => new() { Glyph = glyph, Label = label, Kids = kids };

        public static MenuEntry Title(string label) => new() { Label = label, Header = true };

        public static MenuEntry Sep() => new() { Line = true };
    }

    internal static class GlassMenu
    {
        static readonly List<Popup> Open = new();

        public static bool Showing => Open.Count > 0;

        public static void CloseAll()
        {
            foreach (var p in Open.ToArray()) p.IsOpen = false;
            Open.Clear();
        }

        public static void Show(UIElement owner, List<MenuEntry> items)
        {
            CloseAll();
            var popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                PlacementTarget = owner,
                Placement = PlacementMode.MousePoint,
                PopupAnimation = PopupAnimation.None,
                Focusable = false
            };
            var list = new StackPanel { Margin = new Thickness(5) };
            Fill(list, items, 0);
            var surface = Glass.Surface(12);
            surface.Child = list;
            surface.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            popup.Child = new Border { Padding = new Thickness(10, 4, 10, 16), Child = surface };
            popup.Closed += (_, _) => Open.Remove(popup);
            Open.Add(popup);
            popup.IsOpen = true;
            Glass.FadeIn(surface, -4, 120);
        }

        static void Fill(StackPanel list, List<MenuEntry> items, int depth)
        {
            foreach (var e in items)
            {
                var entry = e;
                if (entry.Line)
                {
                    var sep = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
                    sep.SetResourceReference(Border.BackgroundProperty, "Separator");
                    list.Children.Add(sep);
                    continue;
                }
                if (entry.Header)
                {
                    var t = Kit.Text(entry.Label, 11, true, "SubText");
                    t.Margin = new Thickness(10 + depth * 18, 6, 10, 3);
                    list.Children.Add(t);
                    continue;
                }
                var row = new DockPanel { LastChildFill = true };
                var g = Kit.Icon(entry.Check ? "\uE73E" : entry.Glyph, 12.5, entry.Danger ? "Danger" : entry.Check ? "AccentFill" : "SubText");
                g.Width = 22;
                g.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(g, Dock.Left);
                row.Children.Add(g);
                TextBlock? arrow = null;
                if (entry.Kids != null)
                {
                    arrow = Kit.Icon("\uE70D", 10, "Faint");
                    arrow.Margin = new Thickness(14, 0, 0, 0);
                    DockPanel.SetDock(arrow, Dock.Right);
                    row.Children.Add(arrow);
                }
                row.Children.Add(Kit.Text(entry.Label, 12.5, false, entry.Danger ? "Danger" : "Text"));
                var host = new Border { Child = row, Padding = new Thickness(8 + depth * 18, 6, 12, 6), CornerRadius = Kit.Round(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, MinWidth = 200 };
                host.MouseEnter += (_, _) => host.SetResourceReference(Border.BackgroundProperty, "RowHover");
                host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
                list.Children.Add(host);
                StackPanel? sub = null;
                host.MouseLeftButtonUp += (_, ev) =>
                {
                    ev.Handled = true;
                    if (entry.Kids != null)
                    {
                        if (sub == null)
                        {
                            sub = new StackPanel();
                            Fill(sub, entry.Kids, depth + 1);
                            list.Children.Insert(list.Children.IndexOf(host) + 1, sub);
                            if (arrow != null) arrow.Text = "\uE70E";
                            Glass.FadeIn(sub, -4, 120);
                        }
                        else
                        {
                            list.Children.Remove(sub);
                            sub = null;
                            if (arrow != null) arrow.Text = "\uE70D";
                        }
                        return;
                    }
                    CloseAll();
                    try
                    {
                        entry.Run?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        PhysicsEngine.Log("menu action failed " + ex.Message);
                    }
                };
            }
        }
    }
}
