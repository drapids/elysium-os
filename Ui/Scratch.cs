using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed partial class Panels
    {
        DispatcherTimer? _scratchSave;
        bool _scratchArmed;

        void BuildScratch()
        {
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 10) };
            var count = Kit.Text("", 11, false, "Faint");
            count.VerticalAlignment = VerticalAlignment.Center;
            count.Margin = new Thickness(0, 0, 8, 0);
            var copy = Kit.Plain("\uE8C8", () =>
            {
                try
                {
                    if (_s.Scratch.Length > 0) Clipboard.SetText(_s.Scratch);
                }
                catch
                {
                }
                Close2();
            }, "copy all", 12);
            var box = new TextBox
            {
                Style = (Style)Application.Current.FindResource("Field"),
                Text = _s.Scratch,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 240,
                MaxHeight = 480,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 13,
                Tag = "anything. it stays here"
            };
            _scratchArmed = false;
            var clear = Kit.Plain("\uE74D", () => { }, "clear (click twice)", 12);
            clear.Click += (_, _) =>
            {
                if (!_scratchArmed)
                {
                    _scratchArmed = true;
                    clear.ToolTip = "click again to clear";
                    clear.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E));
                    return;
                }
                _scratchArmed = false;
                box.Text = "";
                box.Focus();
            };
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(copy, Dock.Right);
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(clear);
            head.Children.Add(copy);
            head.Children.Add(count);
            head.Children.Add(Kit.Text("Scratchpad", 15, true));
            _body.Children.Add(head);

            void Count() => count.Text = box.Text.Length == 0 ? "" : box.Text.Length + " chars";
            Count();
            var saver = _scratchSave;
            if (saver == null)
            {
                saver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                var self = saver;
                self.Tick += (_, _) =>
                {
                    self.Stop();
                    _s.Touch();
                };
                _scratchSave = saver;
            }
            box.TextChanged += (_, _) =>
            {
                _s.Scratch = box.Text;
                Count();
                saver.Stop();
                saver.Start();
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close2();
            };
            _body.Children.Add(box);
            var hint = Kit.Caption("saves as you type. ctrl + alt + q opens it anywhere");
            hint.Margin = new Thickness(2, 8, 0, 0);
            _body.Children.Add(hint);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.CaretIndex = box.Text.Length;
                box.ScrollToEnd();
            }), DispatcherPriority.Input);
        }
    }
}
