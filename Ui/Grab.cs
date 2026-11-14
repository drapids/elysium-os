using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Elysium.Ui
{
    internal sealed class Grab : Window
    {
        readonly AppCatalog _cat;
        readonly Border _surface;
        readonly Image _icon = new() { Width = 34, Height = 34, Stretch = Stretch.Uniform };
        readonly TextBlock _name;
        readonly TextBlock _meta;
        string _path = "";
        bool _pressed, _dragging;
        Point _down;

        public Grab(AppCatalog cat)
        {
            _cat = cat;
            Glass.Attach(this, 14);
            Title = "elysium grab";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(14);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 16, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
            row.Children.Add(_icon);
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260 };
            _name = Kit.Text("", 12.5, true);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _meta = Kit.Text("", 11, false, "SubText");
            text.Children.Add(_name);
            text.Children.Add(_meta);
            row.Children.Add(text);
            _surface.Child = row;
            Content = _surface;

            row.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _pressed = true;
                _down = e.GetPosition(this);
            };
            row.MouseMove += (_, e) =>
            {
                if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - _down.X) < 3 && Math.Abs(p.Y - _down.Y) < 3) return;
                _pressed = false;
                Drag();
            };
            row.MouseLeftButtonUp += (_, _) =>
            {
                if (!_pressed) return;
                _pressed = false;
                Files.Open(_path);
                Away();
            };
            row.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                Files.Reveal(_path);
                Away();
            };
            Deactivated += (_, _) =>
            {
                if (!_dragging) Away();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Away();
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    try
                    {
                        var list = new System.Collections.Specialized.StringCollection { _path };
                        Clipboard.SetFileDropList(list);
                    }
                    catch
                    {
                    }
                    Away();
                }
                else if (e.Key == Key.Enter)
                {
                    Files.Open(_path);
                    Away();
                }
            };
        }

        public static string? Newest(string root)
        {
            string? best = null;
            DateTime bestAt = DateTime.MinValue;
            void Look(string dir, int depth)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is ".crdownload" or ".part" or ".tmp" or ".partial" or ".download" or ".ini") continue;
                        var at = File.GetLastWriteTimeUtc(f);
                        var made = File.GetCreationTimeUtc(f);
                        if (made > at) at = made;
                        if (at > bestAt)
                        {
                            bestAt = at;
                            best = f;
                        }
                    }
                    if (depth > 0)
                        foreach (var d in Directory.EnumerateDirectories(dir)) Look(d, depth - 1);
                }
                catch
                {
                }
            }
            Look(root, 1);
            return best;
        }

        public void Show(string path)
        {
            _path = path;
            _name.Text = Path.GetFileName(path);
            string size = "";
            try
            {
                size = Kit.Size(new FileInfo(path).Length);
            }
            catch
            {
            }
            _meta.Text = (size.Length > 0 ? size + "  ·  " : "") + "drag it anywhere, click to open, ctrl + c copies";
            _icon.Source = null;
            _cat.Icons.Get(path, 96, false, s => _icon.Source = s);
            Native.GetCursorPos(out var c);
            var d = Displays.AtCursor();
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _surface.DesiredSize;
                int w = (int)(size.Width * d.Scale), h = (int)(size.Height * d.Scale);
                int x = Math.Clamp(c.X - w / 2, d.Work.Left + 8, d.Work.Right - w - 8);
                int y = Math.Clamp(c.Y - h - (int)(14 * d.Scale), d.Work.Top + 8, d.Work.Bottom - h - 8);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 6, 0.96, 200);
            Activate();
        }

        void Drag()
        {
            if (!File.Exists(_path)) return;
            _dragging = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { _path });
                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Link);
            }
            catch
            {
            }
            finally
            {
                _dragging = false;
            }
            Away();
        }

        public void Away()
        {
            if (IsVisible) Glass.Dismiss(this, _surface);
        }
    }
}
