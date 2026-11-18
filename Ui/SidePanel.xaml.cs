using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Elysium.Ui
{
    public partial class SidePanel : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        bool _loading, _open, _suppress;

        string _tab = "dock";
        readonly DispatcherTimer _findWait = new() { Interval = TimeSpan.FromMilliseconds(160) };
        bool _quietFind;
        SettingsPages? _pages;

        public event Action? DockRequested;
        public event Action? QuitRequested;

        internal SidePanel(Settings settings, AppCatalog catalog)
        {
            InitializeComponent();
            _s = settings;
            _cat = catalog;

            SourceInitialized += (_, _) =>
            {
                Theme.Solid(this);
            };
            Theme.Applied += () => Theme.Solid(this);

            dragSide.MouseLeftButtonDown += (_, e) => Drag(e);
            dragHead.MouseLeftButtonDown += (_, e) => Drag(e);
            Closing += (_, e) =>
            {
                if (_quitting) return;
                e.Cancel = true;
                SlideOut();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (find.Text.Length > 0)
                {
                    find.Clear();
                    e.Handled = true;
                    return;
                }
                SlideOut();
            };
            _findWait.Tick += (_, _) =>
            {
                _findWait.Stop();
                RunFind();
            };
            find.TextChanged += (_, _) =>
            {
                if (_quietFind) return;
                _findWait.Stop();
                if (find.Text.Trim().Length == 0) ShowTab(_tab);
                else _findWait.Start();
            };

            close.Click += (_, _) => SlideOut();
            quit.Click += (_, _) => QuitRequested?.Invoke();
            openDock.Click += (_, _) =>
            {
                SlideOut();
                DockRequested?.Invoke();
            };

            Wire();
            Load();
            BuildTabs();
            _cat.Updated += () => Dispatcher.BeginInvoke(new Action(RefreshItems));
        }

        public bool IsOpen => _open;

        internal void Attach(SettingsPages pages)
        {
            _pages = pages;
            ShowTab(_tab);
        }

        public void OpenTab(string tab)
        {
            Open();
            ShowTab(tab);
        }

        internal void Dialog(Action a)
        {
            _suppress = true;
            try
            {
                a();
            }
            finally
            {
                _suppress = false;
            }
            Activate();
        }

        bool _quitting;

        public void Quit()
        {
            _quitting = true;
            Close();
        }

        void Drag(MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is Button) return;
            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        static string TabGlyph(string id) => id switch
        {
            "general" => "\uE713",
            "look" => "\uE790",
            "dock" => "\uE8A9",
            "windows" => "\uE737",
            "files" => "\uE8B7",
            "shell" => "\uE7F4",
            "audio" => "\uE767",
            "alerts" => "\uEA8F",
            "calendar" => "\uE787",
            "search" => "\uE721",
            "extensions" => "\uEA86",
            "keys" => "\uE765",
            "share" => "\uE72D",
            "about" => "\uE946",
            _ => "\uE713"
        };

        void BuildTabs()
        {
            tabs.Children.Clear();
            foreach (var (id, label) in SettingsPages.Tabs)
            {
                string tid = id;
                bool on = _tab == id;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var badge = new Border { Width = 26, Height = 26, CornerRadius = Kit.Round(7), Margin = new Thickness(0, 0, 11, 0) };
                badge.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "TileBg");
                var g = Kit.Icon(TabGlyph(id), 13, on ? "OnAccent" : "Text");
                g.HorizontalAlignment = HorizontalAlignment.Center;
                g.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = g;
                row.Children.Add(badge);
                var t = Kit.Text(label, 13, on);
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                var chip = new Border { CornerRadius = Kit.Round(9), Padding = new Thickness(6, 5, 8, 5), Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand, Child = row };
                if (on) chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                else
                {
                    chip.Background = Brushes.Transparent;
                    chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "RowHover");
                    chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
                }
                chip.MouseLeftButtonUp += (_, _) => ShowTab(tid);
                tabs.Children.Add(chip);
            }
        }

        void RunFind()
        {
            string q = find.Text.Trim();
            if (q.Length == 0 || _pages == null) return;
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string classicText = SettingsPages.Words(classic);
            bool classicHit = words.All(w => classicText.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            classic.Visibility = Visibility.Collapsed;
            pages.Visibility = Visibility.Visible;
            pageTitle.Text = "results for \"" + q + "\"";
            _pages.Find(q, pages, ShowTab, classicHit);
            scroll.ScrollToTop();
        }

        void ShowTab(string tab)
        {
            if (find.Text.Length > 0)
            {
                _quietFind = true;
                find.Clear();
                _quietFind = false;
                _findWait.Stop();
            }
            _tab = tab;
            BuildTabs();
            pageTitle.Text = SettingsPages.Tabs.FirstOrDefault(t => t.id == tab).label?.ToLowerInvariant() ?? "";
            bool classicTab = tab == "dock" || _pages == null;
            classic.Visibility = classicTab ? Visibility.Visible : Visibility.Collapsed;
            if (classicTab && _open) Load();
            pages.Visibility = classicTab ? Visibility.Collapsed : Visibility.Visible;
            if (!classicTab) _pages!.Build(tab, pages);
            scroll.ScrollToTop();
        }

        public void Toggle()
        {
            if (_open) SlideOut();
            else Open();
        }

        public void Open()
        {
            if (_open)
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                return;
            }
            _open = true;

            var d = Displays.AtCursor();
            _display = d;
            double scale = d.Scale;
            int wpx = (int)Math.Round(Math.Min(Width, d.Work.Width / scale - 60) * scale);
            int hpx = (int)Math.Round(Math.Min(Height, d.Work.Height / scale - 60) * scale);
            int x = d.Work.Left + (d.Work.Width - wpx) / 2;
            int y = d.Work.Top + (d.Work.Height - hpx) / 2;

            Load();
            frame.BeginAnimation(OpacityProperty, null);
            frame.Opacity = 0;
            bool fresh = !IsVisible;
            var h = new WindowInteropHelper(this).EnsureHandle();
            if (fresh) Native.Cloak(h, true);
            if (fresh || !_placed)
            {
                Native.SetWindowPos(h, IntPtr.Zero, x, y, wpx, hpx, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                _placed = true;
            }
            if (fresh) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            var lift = new TranslateTransform(0, 10);
            frame.RenderTransform = lift;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(Math.Max(120, 260 * Theme.Speed));
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
            frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(90, 180 * Theme.Speed))));
            if (fresh) Glass.Unveil(this);
        }

        Display? _display;
        bool _placed;

        public void SlideOut()
        {
            if (!_open) return;
            _open = false;
            Keyboard.ClearFocus();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Math.Max(80, 140 * Theme.Speed)));
            fade.Completed += (_, _) =>
            {
                if (!_open) Hide();
            };
            frame.BeginAnimation(OpacityProperty, fade);
        }

        void Wire()
        {
            Slide(throwS, v => _s.Throw = v, true);
            Slide(frictionS, v => _s.Friction = v, true);
            Slide(bounceS, v => _s.Bounce = v, true);
            Slide(gravityS, v => _s.Gravity = v < 0.02 ? 0 : v, true);
            Slide(shakeSens, v => _s.ShakeSensitivity = v, false);
            Slide(sizeS, v => _s.IconSize = Math.Round(v), false);
            Slide(magS, v => _s.Magnify = v, false);

            Toggle(enabled, v => _s.Enabled = v);
            Toggle(stackS, v => _s.Stacking = v);
            Toggle(shakeS, v => _s.ShakeToMinimize = v);
            Toggle(crossS, v => _s.CrossMonitors = v);
            Toggle(handleS, v => _s.ShowHandle = v);
            Toggle(drivesS, v => _s.ShowDrives = v);
            Toggle(foldersFirstS, v => _s.FoldersFirst = v);
            Toggle(hiddenS, v => _s.ShowHidden = v);
            Toggle(filterS, v => _s.TypeToFilter = v);

            foreach (var rb in new[] { sortName, sortRecent, sortType, sortSize })
            {
                rb.Checked += (_, _) =>
                {
                    if (_loading) return;
                    _s.FolderSort = (string)rb.Tag;
                    _s.Touch();
                };
            }
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

            foreach (var rb in new[] { sideLeft, sideRight, sideTop, sideBottom })
            {
                rb.Checked += (_, _) =>
                {
                    if (_loading) return;
                    _s.DockSide = (string)rb.Tag;
                    _s.Touch();
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
                var path = PickFile("Add to the dock", "Apps and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|Anything|*.*");
                if (path != null) _cat.AddFile(path);
            };

            addPlace.Click += (_, _) =>
            {
                var path = PickFolder();
                if (path == null || _s.Places.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
                _s.Places.Add(path);
                _s.Touch();
                RefreshPlaces();
            };

            locate.Click += (_, _) =>
            {
                if (_cat.HangarPath != null && !_cat.IsPinned(_cat.HangarPath))
                {
                    _cat.SetHangar(_cat.HangarPath);
                    return;
                }
                var path = PickFile("Where's hangar29?", "hangar29|*.exe;*.lnk|Anything|*.*");
                if (path != null) _cat.SetHangar(path);
            };

            rescan.Click += (_, _) => _cat.Scan();
        }

        void Slide(Slider slider, Action<double> set, bool physics)
        {
            slider.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                set(e.NewValue);
                if (physics && _s.Preset != "custom")
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

        string? PickFile(string title, string filter)
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

        string? PickFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Add a place" };
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
            return ok ? dlg.FolderName : null;
        }

        void SetStartup(bool on)
        {
            Startup.Set(on, _s.StartEarly);
            _s.StartupPath = on ? Environment.ProcessPath ?? "" : "";
            _s.Touch();
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
            drivesS.IsChecked = _s.ShowDrives;
            foldersFirstS.IsChecked = _s.FoldersFirst;
            hiddenS.IsChecked = _s.ShowHidden;
            filterS.IsChecked = _s.TypeToFilter;
            sortRecent.IsChecked = _s.FolderSort == "recent";
            sortType.IsChecked = _s.FolderSort == "type";
            sortSize.IsChecked = _s.FolderSort == "size";
            sortName.IsChecked = _s.FolderSort is not ("recent" or "type" or "size");
            sizeS.Value = _s.IconSize;
            magS.Value = _s.Magnify;

            presetChill.IsChecked = _s.Preset == "chill";
            presetBouncy.IsChecked = _s.Preset == "bouncy";
            presetChaos.IsChecked = _s.Preset == "chaos";

            sideLeft.IsChecked = _s.DockSide == "left";
            sideTop.IsChecked = _s.DockSide == "top";
            sideBottom.IsChecked = _s.DockSide == "bottom";
            sideRight.IsChecked = _s.DockSide != "left" && _s.DockSide != "top" && _s.DockSide != "bottom";

            themeSystem.IsChecked = _s.Theme != "light" && _s.Theme != "dark";
            themeLight.IsChecked = _s.Theme == "light";
            themeDark.IsChecked = _s.Theme == "dark";

            if (!projects.IsKeyboardFocused) projects.Text = _s.ProjectNames;
            if (!ignored.IsKeyboardFocused) ignored.Text = _s.IgnoredApps;

            _loading = false;
            Labels();
            RefreshItems();
            RefreshPlaces();
        }

        static string Pct(double v) => $"{Math.Round(v * 100)}%";

        void Labels()
        {
            throwVal.Text = $"{_s.Throw:0.0}x";
            frictionVal.Text = Pct(_s.Friction);
            bounceVal.Text = Pct(_s.Bounce);
            gravityVal.Text = _s.Gravity < 0.001 ? "Off" : Pct(_s.Gravity);
            shakeVal.Text = _s.ShakeSensitivity < 0.34 ? "Low" : _s.ShakeSensitivity < 0.67 ? "Mid" : "High";
            sizeVal.Text = $"{_s.IconSize:0}";
            magVal.Text = _s.Magnify < 0.01 ? "Off" : Pct(_s.Magnify);
            shakeRow.Visibility = _s.ShakeToMinimize ? Visibility.Visible : Visibility.Collapsed;
            status.Text = "elysium " + Changelog.Version;
            physicsCaption.Text = _s.Enabled ? "fling windows and they keep going" : "off. windows move like normal";
            physicsBody.IsEnabled = _s.Enabled;
            physicsBody.Opacity = _s.Enabled ? 1 : 0.45;
            behaviorGroup.IsEnabled = _s.Enabled;
            behaviorGroup.Opacity = _s.Enabled ? 1 : 0.45;
        }

        void RefreshItems()
        {
            itemsHost.Children.Clear();
            var items = _cat.WheelItems(24);
            emptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            itemsHost.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var it in items) itemsHost.Children.Add(Row(it, false));

            rescan.IsEnabled = !_cat.Scanning;
            rescanText.Text = _cat.Scanning ? "Scanning..." : "Rescan";

            var hp = _cat.HangarPath;
            if (hp != null)
            {
                bool pinned = _cat.IsPinned(hp);
                hangarStatus.Text = pinned ? "pinned to the dock" : "found it, not pinned";
                locate.Content = pinned ? "Change" : "Pin";
                hangarIcon.ToolTip = hp;
                _cat.Icons.Get(hp, 64, true, img =>
                {
                    hangarIcon.Source = img;
                    hangarGlyph.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else
            {
                hangarStatus.Text = _cat.Scanning || !_cat.Scanned ? "looking for it..." : "couldn't find it, point me to it";
                locate.Content = "Locate";
                hangarIcon.Source = null;
                hangarGlyph.Visibility = Visibility.Visible;
            }
        }

        void RefreshPlaces()
        {
            placesHost.Children.Clear();
            foreach (var p in _cat.PlaceItems()) placesHost.Children.Add(Row(p, true));
        }

        FrameworkElement Row(WheelItem item, bool place)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 4, 5), Background = Brushes.Transparent };
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "RowHover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyphText = new TextBlock { Text = place ? "\uE8B7" : "\uE71D", FontSize = 15, Style = (Style)FindResource("Icon") };
            var image = new Image { Width = 26, Height = 26 };
            var glyph = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.Children.Add(glyphText);
            glyph.Children.Add(image);
            _cat.Icons.Get(item.Path, 64, !place, img =>
            {
                if (img == null) return;
                image.Source = img;
                glyphText.Visibility = Visibility.Collapsed;
            });
            g.Children.Add(glyph);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Name, Style = (Style)FindResource("Body") });
            text.Children.Add(new TextBlock
            {
                Text = item.Kind switch
                {
                    "favorite" => "favorite",
                    "project" => "one of yours",
                    "drive" => "drive",
                    "place" => item.Path,
                    _ => "you use this a lot"
                },
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            if (!place)
            {
                var star = new CheckBox
                {
                    Style = (Style)FindResource("Star"),
                    IsChecked = item.Kind == "favorite",
                    ToolTip = "favorite"
                };
                star.Checked += (_, _) => _cat.Pin(item);
                star.Unchecked += (_, _) => _cat.Unpin(item.Path);
                Grid.SetColumn(star, 2);
                g.Children.Add(star);
            }

            bool removable = !place || _s.Places.Contains(item.Path, StringComparer.OrdinalIgnoreCase);
            if (removable)
            {
                var remove = new Button
                {
                    Style = (Style)FindResource("Plain"),
                    ToolTip = place ? "remove this place" : "take it off the dock",
                    Content = new TextBlock { Text = "\uE711", FontSize = 10, Style = (Style)FindResource("Icon"), Foreground = null }
                };
                if (remove.Content is TextBlock tb) tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = remove });
                remove.Click += (_, _) =>
                {
                    if (place)
                    {
                        _s.Places.RemoveAll(p => string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase));
                        _s.Touch();
                        RefreshPlaces();
                    }
                    else
                    {
                        _cat.Hide(item.Path);
                    }
                };
                Grid.SetColumn(remove, 3);
                g.Children.Add(remove);
            }

            row.Child = g;
            row.ToolTip = item.Path;
            return row;
        }
    }
}
