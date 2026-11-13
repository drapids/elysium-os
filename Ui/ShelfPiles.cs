using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Heft.Ui
{
    internal sealed partial class Shelf
    {
        string _pile = "";
        FrameworkElement? _pilePressed;
        Point _pileDown;

        static string PileOf(Entry e) => e.Dir ? "folder" : e.Cat;

        static string PileName(string id)
        {
            if (id == "folder") return "Folders";
            foreach (var c in Files.Categories)
                if (c.cat == id) return c.label;
            return "Other stuff";
        }

        bool Piles(List<Entry> list, string q) => _s.ShelfPiles && _tab == "shelf" && _trail.Count == 0 && q.Length == 0 && list.Count >= 8;

        void RenderPiles(List<Entry> list)
        {
            if (_pile.Length > 0)
            {
                var inside = list.Where(e => PileOf(e) == _pile).ToList();
                if (inside.Count > 0)
                {
                    _content.Children.Add(PileBack(_pile, inside.Count));
                    int i = 0;
                    foreach (var e in inside.Take(300))
                    {
                        var card = Card(e);
                        Deal(card, i++);
                        _content.Children.Add(card);
                    }
                    return;
                }
                _pile = "";
            }

            var counts = list.GroupBy(PileOf).ToDictionary(g => g.Key, g => g.ToList());
            var done = new HashSet<string>();
            foreach (var e in list.Take(300))
            {
                string id = PileOf(e);
                var members = counts[id];
                if (members.Count < 3)
                {
                    _content.Children.Add(Card(e));
                    continue;
                }
                if (!done.Add(id)) continue;
                _content.Children.Add(Pile(id, members));
            }
        }

        static void Deal(FrameworkElement el, int index)
        {
            var st = new ScaleTransform(0.7, 0.7);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(Math.Min(index, 24) * 16 * Theme.Speed);
            var dur = TimeSpan.FromMilliseconds(260 * Theme.Speed);
            var ease = new SpringEase();
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1, dur) { BeginTime = begin, EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, dur) { BeginTime = begin, EasingFunction = ease });
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160 * Theme.Speed)) { BeginTime = begin });
        }

        FrameworkElement PileBack(string id, int count)
        {
            var g = new Grid { Width = 92, Height = 108 };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var arrow = Kit.Icon("\uE72B", 18);
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(arrow);
            var label = Kit.Text(PileName(id), 11.5, true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(label);
            var sub = Kit.Text(count + " items · back", 10.5, false, "Faint");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(sub);
            g.Children.Add(stack);
            var tile = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(2), Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
            tile.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            tile.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            tile.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                _pile = "";
                Render(-1);
            };
            return tile;
        }

        FrameworkElement Pile(string id, List<Entry> members)
        {
            var g = new Grid { Width = 92 };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fan = new Grid { Width = 80, Height = 64 };
            var top = members.Take(3).ToList();
            var turns = new List<RotateTransform>();
            double[] rest = { -9, 7, 0 };
            for (int i = top.Count - 1; i >= 0; i--)
            {
                var e = top[i];
                var img = new Image { Width = 46, Height = 46, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                bool thumb = e.Cat is "image" or "video" or "doc";
                if (_cat.Icons.TryPeek(e.Path, 96, !thumb, out var cached)) img.Source = cached;
                else _cat.Icons.Get(e.Path, 96, !thumb, s => img.Source = s);
                var rt = new RotateTransform(top.Count == 1 ? 0 : rest[i]);
                img.RenderTransformOrigin = new Point(0.5, 0.9);
                img.RenderTransform = rt;
                turns.Add(rt);
                fan.Children.Add(img);
            }
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 2, 0) };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            badge.Child = Kit.Text(members.Count.ToString(), 10.5, true, "OnAccent");
            fan.Children.Add(badge);
            g.Children.Add(fan);

            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var name = Kit.Text(PileName(id), 11.5);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(name);
            long size = members.Sum(m => m.Size);
            var meta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            meta.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Files.ColorFor(_s, id)), Margin = new Thickness(0, 1, 5, 0) });
            meta.Children.Add(Kit.Text(id == "folder" ? "pile" : Kit.Size(size), 10.5, false, "Faint"));
            text.Children.Add(meta);
            Grid.SetRow(text, 1);
            g.Children.Add(text);

            var tile = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"{members.Count} {PileName(id).ToLowerInvariant()}. click to spread them out, drag to take them all" };
            void Spread(bool open)
            {
                var dur = TimeSpan.FromMilliseconds(220 * Theme.Speed);
                var ease = new SpringEase();
                for (int i = 0; i < turns.Count; i++)
                {
                    int slot = turns.Count - 1 - i;
                    double to = top.Count == 1 ? 0 : open ? new[] { -18, 14, 0 }[slot] : rest[slot];
                    turns[i].BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, dur) { EasingFunction = ease });
                }
            }
            tile.MouseEnter += (_, _) =>
            {
                tile.SetResourceReference(Border.BackgroundProperty, "RowHover");
                Spread(true);
            };
            tile.MouseLeave += (_, _) =>
            {
                tile.Background = Brushes.Transparent;
                Spread(false);
            };
            tile.PreviewMouseLeftButtonDown += (_, ev) =>
            {
                _pilePressed = tile;
                _pileDown = ev.GetPosition(this);
            };
            tile.MouseMove += (_, ev) =>
            {
                if (_pilePressed != tile || ev.LeftButton != MouseButtonState.Pressed) return;
                var p = ev.GetPosition(this);
                if (Math.Abs(p.X - _pileDown.X) < 3 && Math.Abs(p.Y - _pileDown.Y) < 3) return;
                _pilePressed = null;
                DragPaths(members.Select(m => m.Path).ToList());
            };
            tile.MouseLeftButtonUp += (_, ev) =>
            {
                if (_pilePressed != tile) return;
                _pilePressed = null;
                ev.Handled = true;
                _pile = id;
                Sfx.Play("tick");
                Render(1);
            };
            return tile;
        }
    }
}
