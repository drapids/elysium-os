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

namespace Heft.Ui
{
    public partial class SidePanel : Window
    {
        const double Gap = 10;

        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(60) };
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
                var h = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(h, false);
                Theme.Glass(this);
            };
            Theme.Applied += () => Theme.Glass(this);

            Deactivated += (_, _) =>
            {
                if (!_suppress) SlideOut();
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
            _outside.Tick += (_, _) => CheckOutsideClick();

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

        void BuildTabs()
        {
            tabs.Children.Clear();
            foreach (var (id, label) in SettingsPages.Tabs)
            {
                string tid = id;
                bool on = _tab == id;
                var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 11, 5), Margin = new Thickness(0, 0, 5, 6), Cursor = Cursors.Hand };
                chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentFill" : "GroupBg");
                chip.Child = Kit.Text(label, 12, on, on ? "OnAccent" : "Text");
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
                Activate();
                return;
            }
            _open = true;

            var d = Displays.AtCursor();
            _display = d;
            double scale = d.Scale;
            int gap = (int)(Gap * scale);
            int wpx = (int)Math.Round(Width * scale);
            int hpx = d.Work.Height - gap * 2;
            int target = d.Work.Right - wpx - gap;
            int y = d.Work.Top + gap;

            Load();
            frame.Opacity = 0;
            if (!IsVisible)
            {
                Left = -20000;
                Show();
            }
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(h, IntPtr.Zero, d.Work.Left + 20, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(h, IntPtr.Zero, d.Work.Left + 20, y, wpx, hpx, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Activate();
            _outside.Start();

            SlideTo(d.Bounds.Right, target, y, wpx, hpx, 280, true, null);
            frame.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        }

        Display? _display;
        DispatcherTimer? _slide;

        void SlideTo(int from, int to, int y, int wpx, int hpx, int ms, bool easeOut, Action? done)
        {
            _slide?.Stop();
            var h = new WindowInteropHelper(this).Handle;
            int edge = _display?.Bounds.Right ?? to + wpx;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(7) };
            _slide = timer;
            void Step()
            {
                double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
                double e = easeOut ? 1 - Math.Pow(1 - t, 5) : t * t * t;
                int x = (int)Math.Round(from + (to - from) * e);
                int visible = Math.Clamp(edge - x, 0, wpx);
                Native.SetWindowRgn(h, visible >= wpx ? IntPtr.Zero : Native.CreateRectRgn(0, 0, visible, hpx), true);
                Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                if (t < 1) return;
                timer.Stop();
                done?.Invoke();
            }
            timer.Tick += (_, _) => Step();
            Step();
            timer.Start();
        }

        public void SlideOut()
        {
            if (!_open) return;
            _open = false;
            _outside.Stop();
            Keyboard.ClearFocus();

            var h = new WindowInteropHelper(this).Handle;
            Native.GetWindowRect(h, out var r);
            int edge = _display?.Bounds.Right ?? r.Right;
            SlideTo(r.Left, edge, r.Top, r.Width, r.Height, 200, false, () =>
            {
                if (_open) return;
                Hide();
                Native.SetWindowRgn(h, IntPtr.Zero, false);
            });
            frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
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
            status.Text = _s.Enabled ? "throw your windows around" : "physics off, dock still works";
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
