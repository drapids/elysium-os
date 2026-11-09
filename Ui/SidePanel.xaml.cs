using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Heft.Ui
{
    public partial class SidePanel : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(60) };
        bool _loading, _open, _suppress;

        public event Action? WheelRequested;
        public event Action? QuitRequested;

        internal SidePanel(Settings settings, AppCatalog catalog)
        {
            InitializeComponent();
            _s = settings;
            _cat = catalog;

            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, false);
            Deactivated += (_, _) =>
            {
                if (!_suppress) SlideOut();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) SlideOut();
            };
            _outside.Tick += (_, _) => CheckOutsideClick();

            close.Click += (_, _) => SlideOut();
            quit.Click += (_, _) => QuitRequested?.Invoke();
            openWheel.Click += (_, _) => WheelRequested?.Invoke();

            Wire();
            Load();
            _cat.Updated += () => Dispatcher.BeginInvoke(new Action(RefreshItems));
        }

        public bool IsOpen => _open;

        public void Toggle()
        {
            if (_open) SlideOut();
            else Open();
        }

        public void Open()
        {
            if (_open)
            {
                Activate();
                return;
            }
            _open = true;

            var wa = SystemParameters.WorkArea;
            Height = wa.Height;
            Top = wa.Top;
            Left = wa.Right - Width;

            Load();
            Show();
            Activate();
            _outside.Start();

            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(380, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        public void SlideOut()
        {
            if (!_open) return;
            _open = false;
            _outside.Stop();
            Keyboard.ClearFocus();

            var anim = new DoubleAnimation(380, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (_, _) =>
            {
                if (!_open) Hide();
            };
            slide.BeginAnimation(TranslateTransform.XProperty, anim);
            frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)));
        }

        void CheckOutsideClick()
        {
            if (!_open || _suppress || IsActive) return;
            if (!Native.MouseDown()) return;
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
            if (!r.Contains(p.X, p.Y)) SlideOut();
        }

        void Wire()
        {
            Slide(throwS, v => _s.Throw = v);
            Slide(frictionS, v => _s.Friction = v);
            Slide(bounceS, v => _s.Bounce = v);
            Slide(gravityS, v => _s.Gravity = v < 0.02 ? 0 : v);

            shakeSens.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                _s.ShakeSensitivity = e.NewValue;
                Labels();
                _s.Touch();
            };

            Toggle(enabled, v => _s.Enabled = v);
            Toggle(stackS, v => _s.Stacking = v);
            Toggle(shakeS, v => _s.ShakeToMinimize = v);
            Toggle(crossS, v => _s.CrossMonitors = v);
            Toggle(handleS, v => _s.ShowHandle = v);
            Toggle(startupS, v =>
            {
                _s.StartWithWindows = v;
                SetStartup(v);
            });

            foreach (var rb in new[] { presetChill, presetBouncy, presetChaos })
            {
                rb.Checked += (_, _) =>
                {
                    if (_loading) return;
                    _s.ApplyPreset((string)rb.Tag);
                    Load();
                };
            }

            foreach (var rb in new[] { themeSystem, themeLight, themeDark })
            {
                rb.Checked += (_, _) =>
                {
                    if (_loading) return;
                    _s.Theme = (string)rb.Tag;
                    Theme.Apply(_s.Theme);
                    _s.Touch();
                };
            }

            projects.LostKeyboardFocus += (_, _) =>
            {
                if (_loading || projects.Text == _s.ProjectNames) return;
                _s.ProjectNames = projects.Text;
                _s.Touch();
                _cat.Scan();
            };

            ignored.TextChanged += (_, _) =>
            {
                if (_loading) return;
                _s.IgnoredApps = ignored.Text;
                _s.Touch();
            };

            addApp.Click += (_, _) =>
            {
                var path = Pick("add to the wheel", "apps and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|anything|*.*");
                if (path != null) _cat.AddFile(path);
            };

            locate.Click += (_, _) =>
            {
                if (_cat.HangarPath != null && !_cat.IsPinned(_cat.HangarPath))
                {
                    _cat.SetHangar(_cat.HangarPath);
                    return;
                }
                var path = Pick("where's hangar29?", "hangar29|*.exe;*.lnk|anything|*.*");
                if (path != null) _cat.SetHangar(path);
            };

            rescan.Click += (_, _) => _cat.Scan();
        }

        void Slide(Slider slider, Action<double> set)
        {
            slider.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                set(e.NewValue);
                if (_s.Preset != "custom")
                {
                    _s.Preset = "custom";
                    _loading = true;
                    presetChill.IsChecked = presetBouncy.IsChecked = presetChaos.IsChecked = false;
                    _loading = false;
                }
                Labels();
                _s.Touch();
            };
        }

        void Toggle(CheckBox box, Action<bool> set)
        {
            box.Checked += (_, _) =>
            {
                if (_loading) return;
                set(true);
                Labels();
                _s.Touch();
            };
            box.Unchecked += (_, _) =>
            {
                if (_loading) return;
                set(false);
                Labels();
                _s.Touch();
            };
        }

        string? Pick(string title, string filter)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter, DereferenceLinks = false, CheckFileExists = true };
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
            return ok ? dlg.FileName : null;
        }

        static void SetStartup(bool on)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (on && Environment.ProcessPath != null) key.SetValue("heft", $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue("heft", false);
            }
            catch
            {
            }
        }

        void Load()
        {
            _loading = true;

            enabled.IsChecked = _s.Enabled;
            throwS.Value = _s.Throw;
            frictionS.Value = _s.Friction;
            bounceS.Value = _s.Bounce;
            gravityS.Value = _s.Gravity;
            stackS.IsChecked = _s.Stacking;
            shakeS.IsChecked = _s.ShakeToMinimize;
            shakeSens.Value = _s.ShakeSensitivity;
            crossS.IsChecked = _s.CrossMonitors;
            startupS.IsChecked = _s.StartWithWindows;
            handleS.IsChecked = _s.ShowHandle;

            presetChill.IsChecked = _s.Preset == "chill";
            presetBouncy.IsChecked = _s.Preset == "bouncy";
            presetChaos.IsChecked = _s.Preset == "chaos";

            themeSystem.IsChecked = _s.Theme != "light" && _s.Theme != "dark";
            themeLight.IsChecked = _s.Theme == "light";
            themeDark.IsChecked = _s.Theme == "dark";

            if (!projects.IsKeyboardFocused) projects.Text = _s.ProjectNames;
            if (!ignored.IsKeyboardFocused) ignored.Text = _s.IgnoredApps;

            _loading = false;
            Labels();
            RefreshItems();
        }

        static string Pct(double v) => $"{Math.Round(v * 100)}%";

        void Labels()
        {
            throwVal.Text = $"{_s.Throw:0.0}x";
            frictionVal.Text = Pct(_s.Friction);
            bounceVal.Text = Pct(_s.Bounce);
            gravityVal.Text = _s.Gravity < 0.001 ? "off" : Pct(_s.Gravity);
            shakeVal.Text = _s.ShakeSensitivity < 0.34 ? "low" : _s.ShakeSensitivity < 0.67 ? "medium" : "high";
            shakeRow.Visibility = _s.ShakeToMinimize ? Visibility.Visible : Visibility.Collapsed;
            status.Text = _s.Enabled ? "throw your windows around" : "paused";
        }

        void RefreshItems()
        {
            itemsHost.Children.Clear();
            var items = _cat.WheelItems();
            emptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var it in items) itemsHost.Children.Add(Row(it));

            rescan.IsEnabled = !_cat.Scanning;
            rescanText.Text = _cat.Scanning ? "scanning..." : "rescan";

            var hp = _cat.HangarPath;
            if (hp != null)
            {
                bool pinned = _cat.IsPinned(hp);
                hangarStatus.Text = pinned ? "pinned to the wheel" : "found it, not pinned";
                locate.Content = pinned ? "change" : "pin";
                var icon = _cat.Icon(hp);
                hangarIcon.Source = icon;
                hangarGlyph.Visibility = icon == null ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                hangarStatus.Text = _cat.Scanning || !_cat.Scanned ? "looking for it..." : "couldn't find it, point me to it";
                locate.Content = "locate";
                hangarIcon.Source = null;
                hangarGlyph.Visibility = Visibility.Visible;
            }
        }

        FrameworkElement Row(WheelItem item)
        {
            var row = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 2, 4), Background = Brushes.Transparent };
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "CardHover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement glyph;
            var icon = _cat.Icon(item.Path);
            if (icon != null)
            {
                glyph = new Image { Source = icon, Width = 22, Height = 22 };
            }
            else
            {
                var t = new TextBlock { Text = "", FontSize = 14 };
                t.Style = (Style)FindResource("Icon");
                glyph = t;
            }
            glyph.Margin = new Thickness(0, 0, 12, 0);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(glyph);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Name, Style = (Style)FindResource("Body") });
            text.Children.Add(new TextBlock
            {
                Text = item.Kind switch
                {
                    "favorite" => "favorite",
                    "project" => "one of yours",
                    _ => "you use this a lot"
                },
                Style = (Style)FindResource("Caption"),
                FontSize = 11
            });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var star = new CheckBox
            {
                Style = (Style)FindResource("Star"),
                IsChecked = item.Kind == "favorite",
                ToolTip = "favorite",
                VerticalAlignment = VerticalAlignment.Center
            };
            star.Checked += (_, _) => _cat.Pin(item);
            star.Unchecked += (_, _) => _cat.Unpin(item.Path);
            Grid.SetColumn(star, 2);
            g.Children.Add(star);

            var remove = new Button
            {
                Style = (Style)FindResource("Ghost"),
                Width = 28,
                Height = 28,
                ToolTip = "take it off the wheel",
                Content = new TextBlock { Text = "", FontSize = 10, Style = (Style)FindResource("Icon") }
            };
            remove.Click += (_, _) => _cat.Hide(item.Path);
            Grid.SetColumn(remove, 3);
            g.Children.Add(remove);

            row.Child = g;
            row.ToolTip = item.Path;
            return row;
        }
    }
}
