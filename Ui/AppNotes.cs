using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Heft.Ui
{
    internal sealed class NoteCard : Window
    {
        readonly Border _surface;
        readonly TextBlock _app;
        readonly TextBlock _text;
        IntPtr _target;
        string _exe = "";

        public event Action<string>? Edit;
        public event Action<string>? Dismissed;

        public NoteCard()
        {
            Glass.Attach(this, 12, true);
            Title = "heft note";
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(12);
            _surface.Background = new LinearGradientBrush(Color.FromArgb(0xF2, 0xFF, 0xE8, 0x7A), Color.FromArgb(0xF2, 0xFF, 0xD8, 0x4A), 90);
            _surface.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x60, 0x00));
            var ink = new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x05));
            var box = new StackPanel { Margin = new Thickness(12, 8, 8, 10), MaxWidth = 260 };
            var head = new DockPanel();
            var close = new TextBlock { Text = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Foreground = ink, Cursor = Cursors.Hand, Margin = new Thickness(8, 2, 2, 0), ToolTip = "hide it for now" };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Dismissed?.Invoke(_exe);
                Hide();
            };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var edit = new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Foreground = ink, Cursor = Cursors.Hand, Margin = new Thickness(8, 1, 0, 0), ToolTip = "edit" };
            edit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Edit?.Invoke(_exe);
            };
            DockPanel.SetDock(edit, Dock.Right);
            head.Children.Add(edit);
            _app = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0x3A, 0x2E, 0x05)) };
            head.Children.Add(_app);
            box.Children.Add(head);
            _text = new TextBlock { FontSize = 12.5, Foreground = ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 4, 0) };
            box.Children.Add(_text);
            _surface.Child = box;
            Content = _surface;
            WinEvents.Moved += h =>
            {
                if (h == _target && IsVisible) Place();
            };
        }

        public void ShowOn(IntPtr target, string exe, string app, string text)
        {
            if (_target != IntPtr.Zero && _target != target) WinEvents.Unwatch(_target);
            if (_target != target) WinEvents.Watch(target);
            _target = target;
            _exe = exe;
            _app.Text = app.ToLowerInvariant();
            _text.Text = text;
            var me = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowLongPtr(me, Native.GWLP_HWNDPARENT, target);
            if (!IsVisible)
            {
                _surface.Opacity = 0;
                Show();
                Place();
                Glass.FadeIn(_surface, -6, 200);
                _surface.Opacity = 1;
            }
            else Place();
        }

        void Place()
        {
            if (!Native.IsWindow(_target) || Native.IsIconic(_target))
            {
                Hide();
                return;
            }
            var f = Native.FrameBounds(_target);
            double scale = Native.DpiScaleAt(f.Left + 10, f.Top + 10);
            UpdateLayout();
            int w = (int)Math.Ceiling(ActualWidth * scale);
            int x = f.Right - w - (int)(16 * scale);
            int y = f.Top + (int)(44 * scale);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void Away()
        {
            if (_target != IntPtr.Zero) WinEvents.Unwatch(_target);
            _target = IntPtr.Zero;
            if (IsVisible) Hide();
        }
    }

    internal sealed class NoteEditor : Window
    {
        readonly Border _surface;
        readonly TextBox _box;
        readonly TextBlock _title;
        string _exe = "";

        public event Action<string, string>? Saved;

        public NoteEditor()
        {
            Glass.Attach(this, 16);
            Title = "heft note editor";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(16);
            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 16), Width = 340 };
            _title = Kit.Text("", 14, true);
            root.Children.Add(_title);
            var cap = Kit.Caption("shows up every time you're in this app");
            cap.Margin = new Thickness(0, 2, 0, 10);
            root.Children.Add(cap);
            _box = new TextBox { Style = (Style)Application.Current.FindResource("Field"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = "remember to export at 1080p..." };
            root.Children.Add(_box);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var del = Kit.Push("Delete", () => Done(""));
            del.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(del);
            row.Children.Add(Kit.Push("Save", () => Done(_box.Text.Trim()), true));
            root.Children.Add(row);
            _surface.Child = root;
            Content = _surface;
            Deactivated += (_, _) => Glass.Dismiss(this, _surface);
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Glass.Dismiss(this, _surface);
                }
                else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    Done(_box.Text.Trim());
                }
            };
        }

        void Done(string text)
        {
            Saved?.Invoke(_exe, text);
            Glass.Dismiss(this, _surface);
        }

        public void Open(string exe, string app, string text)
        {
            _exe = exe;
            _title.Text = "note on " + app;
            _box.Text = text;
            var d = Displays.AtCursor();
            Glass.Present(this, _surface, () =>
            {
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _surface.DesiredSize;
                int w = (int)(size.Width * d.Scale), h = (int)(size.Height * d.Scale);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - w) / 2, d.Work.Top + (d.Work.Height - h) / 3, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            });
            Activate();
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
        }
    }
}
