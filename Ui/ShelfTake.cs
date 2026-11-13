using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Heft.Ui
{
    internal sealed partial class Shelf
    {
        sealed class Item
        {
            public FrameworkElement El = null!;
            public Entry E = null!;
            public Action<bool> Look = _ => { };
        }

        readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
        readonly List<Item> _items = new();
        readonly Canvas _bandLayer = new() { IsHitTestVisible = false };
        readonly System.Windows.Shapes.Rectangle _band = new() { RadiusX = 6, RadiusY = 6, StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
        readonly Border _selBar = new() { Margin = new Thickness(14, 0, 14, 10), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 6, 8, 6), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1) };
        readonly StackPanel _selRow = new() { Orientation = Orientation.Horizontal };
        readonly StackPanel _shelfTools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        Point _bandStart;
        bool _banding;
        int _anchor = -1;
        bool _emptyArmed;

        void SetupTake(Grid body, DockPanel root, Grid tools)
        {
            _band.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFill");
            _band.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentSoft");
            _bandLayer.Children.Add(_band);
            body.Children.Add(_bandLayer);

            _selBar.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            _selBar.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            _selBar.Child = _selRow;
            DockPanel.SetDock(_selBar, Dock.Bottom);
            root.Children.Insert(Math.Max(0, root.Children.Count - 1), _selBar);

            tools.Children.Add(_shelfTools);

            _content.Background = Brushes.Transparent;
            _content.MouseLeftButtonDown += (_, e) =>
            {
                if (IsItem(e.OriginalSource as DependencyObject)) return;
                e.Handled = true;
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Select(Array.Empty<string>());
                _banding = true;
                _bandStart = e.GetPosition(_content);
                _band.Width = 0;
                _band.Height = 0;
                _band.Visibility = Visibility.Visible;
                _content.CaptureMouse();
                PlaceBand(_bandStart);
            };
            _content.MouseMove += (_, e) =>
            {
                if (!_banding) return;
                PlaceBand(e.GetPosition(_content));
            };
            _content.MouseLeftButtonUp += (_, _) =>
            {
                if (!_banding) return;
                _banding = false;
                _band.Visibility = Visibility.Collapsed;
                _content.ReleaseMouseCapture();
            };
            _content.LostMouseCapture += (_, _) =>
            {
                _banding = false;
                _band.Visibility = Visibility.Collapsed;
            };
        }

        static bool IsItem(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is FrameworkElement fe && fe.Tag as string == "item") return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void PlaceBand(Point now)
        {
            var a = _content.TranslatePoint(_bandStart, _bandLayer);
            var b = _content.TranslatePoint(now, _bandLayer);
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            Canvas.SetLeft(_band, x);
            Canvas.SetTop(_band, y);
            _band.Width = Math.Abs(a.X - b.X);
            _band.Height = Math.Abs(a.Y - b.Y);
            var area = new Rect(_bandStart, now);
            if (area.Width < 3 && area.Height < 3) return;
            var hit = new List<string>();
            foreach (var it in _items)
            {
                try
                {
                    if (!it.El.IsVisible) continue;
                    var r = it.El.TransformToAncestor(_content).TransformBounds(new Rect(it.El.RenderSize));
                    if (r.IntersectsWith(area)) hit.Add(it.E.Path);
                }
                catch
                {
                }
            }
            Select(hit);
        }

        void Track(FrameworkElement el, Entry e, Action<bool> look)
        {
            el.Tag = "item";
            var it = new Item { El = el, E = e, Look = look };
            _items.Add(it);
            look(_selected.Contains(e.Path));
        }

        void Select(IEnumerable<string> paths)
        {
            _selected.Clear();
            foreach (var p in paths) _selected.Add(p);
            Repaint();
        }

        void Repaint()
        {
            foreach (var it in _items) it.Look(_selected.Contains(it.E.Path));
            BuildSelBar();
        }

        bool ClickSelect(Entry e)
        {
            var m = Keyboard.Modifiers;
            int idx = _items.FindIndex(x => x.E.Path.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
            if (m.HasFlag(ModifierKeys.Control))
            {
                if (!_selected.Remove(e.Path)) _selected.Add(e.Path);
                _anchor = idx;
                Repaint();
                return true;
            }
            if (m.HasFlag(ModifierKeys.Shift) && idx >= 0)
            {
                int from = _anchor >= 0 ? _anchor : idx;
                int lo = Math.Min(from, idx), hi = Math.Max(from, idx);
                for (int i = lo; i <= hi && i < _items.Count; i++) _selected.Add(_items[i].E.Path);
                Repaint();
                return true;
            }
            _anchor = idx;
            if (_selected.Count > 0)
            {
                _selected.Clear();
                Repaint();
            }
            return false;
        }

        void BuildSelBar()
        {
            _selRow.Children.Clear();
            var paths = _selected.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (_selected.Count == 0)
            {
                _selBar.Visibility = Visibility.Collapsed;
                return;
            }
            _selBar.Visibility = Visibility.Visible;
            var count = Kit.Text($"{_selected.Count} selected", 12.5, true);
            count.VerticalAlignment = VerticalAlignment.Center;
            count.Margin = new Thickness(0, 0, 10, 0);
            var grip = new Border { Child = Kit.Icon("\uE76F", 12, "SubText"), Padding = new Thickness(6, 4, 6, 4), Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = "drag these out" };
            grip.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                DragPaths(paths);
            };
            _selRow.Children.Add(grip);
            _selRow.Children.Add(count);
            void Act(string glyph, string label, Action run)
            {
                var b = Kit.Push(label, run);
                b.Margin = new Thickness(4, 0, 0, 0);
                b.Padding = new Thickness(10, 3, 10, 4);
                _selRow.Children.Add(b);
            }
            Act("\uE8DE", "Move to...", () => PickAndSend(paths, false));
            Act("\uE8C8", "Copy to...", () => PickAndSend(paths, true));
            Act("\uE8C8", "Copy", () =>
            {
                var list = new StringCollection();
                list.AddRange(paths.ToArray());
                try
                {
                    Clipboard.SetFileDropList(list);
                }
                catch
                {
                }
            });
            Act("\uF012", "Zip", () =>
            {
                var zip = Files.Zip(paths);
                if (zip != null) _notes.Post("Zipped", Path.GetFileName(zip), "\uF012", zip);
            });
            if (_actions.Share != null) Act("\uE8EA", "Send to phone", () => _actions.Share(paths));
            if (_tab == "shelf" && _trail.Count == 0)
            {
                Act("\uE711", "Take off shelf", () =>
                {
                    _s.ShelfItems.RemoveAll(x => _selected.Contains(x));
                    _selected.Clear();
                    _s.Touch();
                    Render();
                });
            }
            var clear = Kit.Plain("\uE8BB", () => Select(Array.Empty<string>()), "clear selection (esc)", 9);
            clear.Margin = new Thickness(8, 0, 0, 0);
            _selRow.Children.Add(clear);
        }

        void PickAndSend(List<string> paths, bool copy)
        {
            var dlg = new OpenFolderDialog { Title = copy ? "Copy them to..." : "Move them to..." };
            _suppress = true;
            bool ok;
            try
            {
                ok = dlg.ShowDialog(this) == true;
            }
            finally
            {
                _suppress = false;
            }
            Activate();
            if (!ok) return;
            MoveInto(dlg.FolderName, paths, copy);
            if (!copy)
            {
                _selected.Clear();
                Render();
            }
        }

        void BuildShelfTools(int count)
        {
            _shelfTools.Children.Clear();
            bool show = _tab == "shelf" && _trail.Count == 0 && count > 0;
            _shelfTools.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                _emptyArmed = false;
                return;
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var gi = Kit.Icon("\uE76F", 11, "SubText");
            gi.Margin = new Thickness(0, 0, 7, 0);
            sp.Children.Add(gi);
            sp.Children.Add(Kit.Text($"drag all {count}", 12, false, "SubText"));
            var all = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 10, 5), Cursor = Cursors.SizeAll, BorderThickness = new Thickness(1), ToolTip = "grab everything on the shelf at once (ctrl copies)" };
            all.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            all.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            all.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                DragPaths(_s.ShelfItems.Where(p => File.Exists(p) || Directory.Exists(p)).ToList());
            };
            _shelfTools.Children.Add(all);
            var empty = Kit.Plain(_emptyArmed ? "\uE73E" : "\uE74D", () =>
            {
                if (!_emptyArmed)
                {
                    _emptyArmed = true;
                    BuildShelfTools(count);
                    return;
                }
                _emptyArmed = false;
                _s.ShelfItems.Clear();
                _selected.Clear();
                _s.Touch();
                Render();
            }, _emptyArmed ? "click again to empty the shelf" : "empty the shelf (nothing gets deleted)", 10);
            empty.Margin = new Thickness(6, 0, 0, 0);
            _shelfTools.Children.Add(empty);
            if (_emptyArmed)
            {
                var sure = Kit.Text("sure?", 12, false, "SubText");
                sure.VerticalAlignment = VerticalAlignment.Center;
                sure.Margin = new Thickness(4, 0, 0, 0);
                _shelfTools.Children.Add(sure);
            }
        }

        void DragPaths(List<string> paths)
        {
            if (paths.Count == 0) return;
            _dragging = true;
            DragDropEffects result = DragDropEffects.None;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
                bool copy = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(copy ? 1 : 2)));
                result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch
            {
            }
            finally
            {
                _dragging = false;
                _drops.Visibility = Visibility.Collapsed;
            }
            bool changed = false;
            foreach (var p in paths)
            {
                bool gone = !File.Exists(p) && !Directory.Exists(p);
                bool takeOut = _s.ShelfTakeOut && result != DragDropEffects.None && _tab == "shelf" && _trail.Count == 0;
                if (gone || takeOut)
                {
                    if (_s.ShelfItems.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase)) > 0) changed = true;
                    _selected.Remove(p);
                }
            }
            if (changed)
            {
                _s.Touch();
                if (_open) Render();
            }
        }

        FrameworkElement Rack(List<Entry> files)
        {
            double width = _content.ActualWidth > 100 ? _content.ActualWidth - 6 : 1050;
            const double cw = 86, gap = 14;
            int per = Math.Max(1, (int)((width - 24) / (cw + gap)));
            var stack = new StackPanel { Width = width, Margin = new Thickness(0, 18, 0, 0) };
            for (int start = 0; start < files.Count; start += per)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 22) };
                var covers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 10), VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var e in files.Skip(start).Take(per)) covers.Children.Add(Cover(e, cw, gap));
                row.Children.Add(covers);

                var rail = new Border { Height = 9, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 34), CornerRadius = new CornerRadius(4.5), IsHitTestVisible = false };
                rail.Background = new LinearGradientBrush(Color.FromArgb(0x5A, 255, 255, 255), Color.FromArgb(0x22, 255, 255, 255), 90);
                rail.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255));
                rail.BorderThickness = new Thickness(0, 1, 0, 0);
                rail.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = Colors.Black };
                row.Children.Add(rail);

                var ledge = new Border { Height = 10, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
                ledge.Background = new LinearGradientBrush(Color.FromArgb(0x70, 255, 255, 255), Color.FromArgb(0x18, 255, 255, 255), 90);
                ledge.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = 0.4, Color = Colors.Black };
                row.Children.Add(ledge);
                stack.Children.Add(row);
            }
            return stack;
        }

        FrameworkElement Cover(Entry e, double cw, double gap)
        {
            double ch = cw * 1.3;
            var color = Files.ColorFor(_s, e.Cat);
            bool thumb = e.Cat is "image" or "video" or "doc";
            var face = new Grid { ClipToBounds = true };
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bg.GradientStops.Add(new GradientStop(Shade(color, 1.1), 0));
            bg.GradientStops.Add(new GradientStop(Shade(color, 0.55), 1));
            face.Background = bg;
            var img = new Image { Stretch = thumb ? Stretch.UniformToFill : Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (!thumb)
            {
                img.Width = 42;
                img.Height = 42;
                img.VerticalAlignment = VerticalAlignment.Top;
                img.Margin = new Thickness(0, 22, 0, 0);
            }
            if (_cat.Icons.TryPeek(e.Path, 96, !thumb, out var cached)) img.Source = cached;
            else _cat.Icons.Get(e.Path, 96, !thumb, s => img.Source = s);
            face.Children.Add(img);
            var band = new Border { VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(6, 5, 6, 6), Background = new LinearGradientBrush(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0xC8, 0, 0, 0), 90) };
            band.Child = new TextBlock { Text = e.Name, FontSize = 10, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 26 };
            face.Children.Add(band);
            var gloss = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Color.FromArgb(0x00, 255, 255, 255), new Point(0, 0), new Point(0.6, 0.6)) };
            face.Children.Add(gloss);

            var cover = new Border
            {
                Width = cw,
                Height = ch,
                CornerRadius = new CornerRadius(3, 5, 5, 3),
                Child = face,
                Margin = new Thickness(0, 0, gap, 0),
                Cursor = Cursors.Hand,
                ToolTip = e.Path,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
                AllowDrop = false,
                RenderTransformOrigin = new Point(0.5, 1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 280, Opacity = 0.45, Color = Colors.Black }
            };
            int hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(e.Path));
            double lean = (hash % 5 - 2) * 0.8;
            var rot = new RotateTransform(lean);
            var lift = new TranslateTransform();
            var sc = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(sc);
            tg.Children.Add(rot);
            tg.Children.Add(lift);
            cover.RenderTransform = tg;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            cover.MouseEnter += (_, _) =>
            {
                _hovered = cover;
                _hoveredEntry = e;
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                Panel.SetZIndex(cover, 20);
            };
            cover.MouseLeave += (_, _) =>
            {
                if (_hovered == cover)
                {
                    _hovered = null;
                    _hoveredEntry = null;
                }
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(lean, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                Panel.SetZIndex(cover, 0);
            };
            Interactive(cover, e, () => Files.Open(e.Path, _s));
            Track(cover, e, on =>
            {
                if (on) cover.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
                else cover.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
                cover.BorderThickness = new Thickness(on ? 2.5 : 1);
            });
            return cover;
        }
    }
}
