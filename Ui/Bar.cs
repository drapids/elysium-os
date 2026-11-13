using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Heft.Ui
{
    internal sealed class BarState
    {
        public string App = "";
        public string MediaTitle = "";
        public string MediaArtist = "";
        public bool MediaPlaying;
        public bool MediaAny;
        public BitmapSource? Art;
        public double MediaPos;
        public double MediaDur;
        public Transfer? Download;
        public Transfer? Copy;
        public string Attention = "";
        public Reminder? Timer;
        public int Cpu, Ram, Gpu = -1;
        public double Down, Up;
        public int Volume = -1;
        public bool Muted;
        public int Battery = -1;
        public bool Charging;
        public int Unseen;
        public string Clip = "";
        public bool Focus;
        public bool Locked;
        public int Stack = -1;
        public string Profile = "";
    }

    internal sealed class Bar : Window
    {
        readonly Settings _s;
        readonly Grid _root = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        readonly Border _tint = new();
        readonly TextBlock _app;
        readonly Border _pill;
        readonly Grid _pillContent = new();
        readonly TextBlock _flash;
        readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _stats;
        readonly TextBlock _clock;
        readonly Border _stack;
        readonly TextBlock _stackText;
        readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        readonly DispatcherTimer _edge = new() { Interval = TimeSpan.FromMilliseconds(90) };

        TextBlock? _clipIcon, _focusIcon, _lockIcon, _volIcon, _volText, _bellIcon, _batIcon, _batText;
        Border? _badge;
        IntPtr _hwnd;
        uint _callback;
        bool _registered, _revealed = true, _pressed, _dragged, _busy, _fullscreen;
        Point _down;
        string _mode = "";
        double _outside;
        long _lastApply;

        public Display Display { get; private set; }
        public Func<bool>? KeepOpen;

        public event Action<Bar>? PillClicked;
        public event Action<Bar, double>? AudioClicked;
        public event Action<Bar, double>? NotesClicked;
        public event Action? ClipClicked;
        public event Action? FocusClicked;
        public event Action? LockClicked;
        public event Action<Bar>? ShelfToggle;
        public event Action<Bar>? ShelfPull;
        public event Action? SettingsRequested;
        public event Action<IDataObject>? Dropped;
        public event Action? MediaToggle;
        public event Action? MediaNext;
        public event Action? MediaPrev;
        public event Action? AttentionClicked;
        public event Action? TimerClicked;
        public event Action? CopyClicked;
        public event Action? DownloadClicked;

        public Bar(Settings s, Display d)
        {
            _s = s;
            Display = d;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            AllowDrop = true;
            UseLayoutRounding = true;
            Title = "heft bar";
            SetResourceReference(FontFamilyProperty, "UiFont");

            _root.Children.Add(_tint);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            line.SetResourceReference(Border.BackgroundProperty, "GroupStroke");
            _root.Children.Add(line);

            var grid = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            _app = Kit.Text("", 12.5, true);
            _app.HorizontalAlignment = HorizontalAlignment.Left;
            _app.MaxWidth = 260;
            grid.Children.Add(_app);

            _pill = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _pillContent,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                MinWidth = 120
            };
            _pill.SetResourceReference(Border.BackgroundProperty, "GroupBg");
            _pill.SetResourceReference(Border.BorderBrushProperty, "GroupStroke");
            _pill.MouseLeftButtonUp += (_, e) =>
            {
                if (_dragged) return;
                e.Handled = true;
                _pressed = false;
                root_ReleaseCapture();
                if (_mode == "attention") AttentionClicked?.Invoke();
                else if (_mode == "copy") CopyClicked?.Invoke();
                else if (_mode == "download") DownloadClicked?.Invoke();
                else if (_mode == "timer") TimerClicked?.Invoke();
                else PillClicked?.Invoke(this);
            };
            grid.Children.Add(_pill);

            _flash = Kit.Text("", 12);
            _flash.Opacity = 0;
            _flash.Margin = new Thickness(0, 0, 14, 0);
            _flash.IsHitTestVisible = false;
            _right.Children.Add(_flash);

            _stackText = Kit.Text("", 11.5, true, "OnAccent");
            _stack = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 0, 12, 0), Child = _stackText, Visibility = Visibility.Collapsed, ToolTip = "paste stack. ctrl + alt + v pastes the next one" };
            _stack.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _right.Children.Add(_stack);

            _stats = Kit.Text("", 11.5, false, "SubText");
            _stats.Margin = new Thickness(0, 0, 12, 0);
            _right.Children.Add(_stats);

            _clipIcon = AddIcon("\uE77F", "clipboard. click for history", () => ClipClicked?.Invoke());
            _focusIcon = AddIcon("\uE708", "focus mode", () => FocusClicked?.Invoke());
            _lockIcon = AddIcon("\uE72E", "cursor lock (ctrl + alt + l)", () => LockClicked?.Invoke());

            var vol = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 6, 0), ToolTip = "AudioFlow. scroll the bar for volume" };
            _volIcon = Kit.Icon("\uE767", 12.5);
            _volIcon.Margin = new Thickness(0, 0, 5, 0);
            _volText = Kit.Text("", 11.5, false, "SubText");
            vol.Children.Add(_volIcon);
            vol.Children.Add(_volText);
            vol.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                AudioClicked?.Invoke(this, CenterX(vol));
            };
            _right.Children.Add(vol);

            var bat = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 6, 0), Visibility = Visibility.Collapsed };
            _batIcon = Kit.Icon("\uE83F", 13);
            _batIcon.Margin = new Thickness(0, 0, 5, 0);
            _batText = Kit.Text("", 11.5, false, "SubText");
            bat.Children.Add(_batIcon);
            bat.Children.Add(_batText);
            _right.Children.Add(bat);

            var bellHost = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(4, 0, 6, 0), ToolTip = "notifications" };
            _bellIcon = Kit.Icon("\uEA8F", 12.5);
            bellHost.Children.Add(_bellIcon);
            _badge = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -4, 0), Visibility = Visibility.Collapsed };
            _badge.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            bellHost.Children.Add(_badge);
            bellHost.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                NotesClicked?.Invoke(this, CenterX(bellHost));
            };
            _right.Children.Add(bellHost);

            _clock = Kit.Text("", 12.5, true);
            _clock.Margin = new Thickness(6, 0, 8, 0);
            _right.Children.Add(_clock);

            var shelf = Kit.Plain("\uE70D", () => ShelfToggle?.Invoke(this), "shelf (or drag down from the bar)", 10);
            shelf.Height = 22;
            _right.Children.Add(shelf);

            grid.Children.Add(_right);
            _root.Children.Add(grid);
            Content = _root;

            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd, true);
                _callback = Native.RegisterWindowMessage("heft.appbar");
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            };
            Theme.Applied += Paint;

            _flashTimer.Tick += (_, _) =>
            {
                _flashTimer.Stop();
                _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };
            _edge.Tick += (_, _) => EdgeTick();

            _root.MouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (!_s.GestureVolume) return;
                int v = Audio.Nudge(e.Delta > 0 ? 2 : -2);
                if (v < 0) return;
                _volText!.Text = v + "%";
                Flash($"volume {v}%");
            };
            _root.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle || !_s.GestureMedia) return;
                e.Handled = true;
                MediaToggle?.Invoke();
            };
            _root.MouseLeftButtonDown += (_, e) =>
            {
                _pressed = true;
                _dragged = false;
                _down = e.GetPosition(this);
            };
            _root.MouseLeave += (_, e) =>
            {
                if (!_pressed) return;
                _pressed = false;
                if (e.LeftButton != MouseButtonState.Pressed || e.GetPosition(this).Y < ActualHeight - 1) return;
                _dragged = true;
                ShelfPull?.Invoke(this);
            };
            _root.MouseLeftButtonUp += (_, _) =>
            {
                _pressed = false;
                root_ReleaseCapture();
            };
            _root.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                SettingsRequested?.Invoke();
            };

            DragEnter += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                ShelfPull?.Invoke(this);
            };
            DragOver += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            Drop += (_, e) =>
            {
                e.Handled = true;
                Dropped?.Invoke(e.Data);
            };
        }

        void root_ReleaseCapture()
        {
            if (_root.IsMouseCaptured) _root.ReleaseMouseCapture();
        }

        TextBlock AddIcon(string glyph, string tip, Action click)
        {
            var t = Kit.Icon(glyph, 12.5);
            var host = new Border { Child = t, Background = Brushes.Transparent, Padding = new Thickness(5, 2, 5, 2), Cursor = Cursors.Hand, ToolTip = tip, Visibility = Visibility.Collapsed };
            host.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            _right.Children.Add(host);
            return t;
        }

        static void SetVisible(TextBlock? icon, bool on)
        {
            if (icon?.Parent is FrameworkElement host) host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        double CenterX(FrameworkElement el)
        {
            try
            {
                var p = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight));
                return p.X;
            }
            catch
            {
                return Display.Bounds.Right - 200;
            }
        }

        public double BottomPx
        {
            get
            {
                if (!IsVisible || _hwnd == IntPtr.Zero) return Display.Work.Top;
                Native.GetWindowRect(_hwnd, out var r);
                return Math.Max(r.Bottom, Display.Bounds.Top + (int)(Height * Display.Scale));
            }
        }

        public bool Contains(int x, int y)
        {
            if (!IsVisible || _hwnd == IntPtr.Zero) return false;
            Native.GetWindowRect(_hwnd, out var r);
            return r.Contains(x, y);
        }

        public void Apply(Display d)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Display = d;
                if (!IsVisible)
                {
                    Left = -20000;
                    Show();
                }
                if (_hwnd == IntPtr.Zero) return;

                double height = Math.Clamp(_s.BarHeight, 22, 48);
                int px = (int)Math.Round(height * d.Scale);
                Height = height;
                Width = d.Bounds.Width / d.Scale;

                if (_s.BarAutoHide)
                {
                    Unregister();
                    int y = _revealed ? d.Bounds.Top : d.Bounds.Top - px + 2;
                    Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, d.Bounds.Left, y, d.Bounds.Width, px, Native.SWP_NOACTIVATE);
                    _edge.Start();
                }
                else
                {
                    _edge.Stop();
                    _revealed = true;
                    Register(d, px);
                }
                _lastApply = Environment.TickCount64;
                Paint();
            }
            finally
            {
                _busy = false;
            }
        }

        void Register(Display d, int px)
        {
            var abd = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _callback };
            if (!_registered)
            {
                Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
                _registered = true;
            }
            abd.uEdge = Native.ABE_TOP;
            abd.rc = new Native.RECT { Left = d.Bounds.Left, Top = d.Bounds.Top, Right = d.Bounds.Right, Bottom = d.Bounds.Top + px };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            abd.rc.Bottom = abd.rc.Top + px;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, px, Native.SWP_NOACTIVATE);
        }

        void Unregister()
        {
            if (!_registered || _hwnd == IntPtr.Zero) return;
            _registered = false;
            var abd = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        }

        public void Rebind()
        {
            _registered = false;
            Apply(Display);
        }

        public void Shutdown()
        {
            _edge.Stop();
            Unregister();
            Hide();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_callback == 0 || (uint)msg != _callback) return IntPtr.Zero;
            int code = wParam.ToInt32();
            if (code == Native.ABN_POSCHANGED)
            {
                if (Environment.TickCount64 - _lastApply > 500) Dispatcher.BeginInvoke(new Action(() => Apply(Display)));
            }
            else if (code == Native.ABN_FULLSCREENAPP)
            {
                _fullscreen = lParam != IntPtr.Zero;
                Topmost = !_fullscreen;
                if (_fullscreen) Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                else Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        void Paint()
        {
            byte alpha = (byte)Math.Round(Math.Clamp(_s.BarTint, 0, 1) * 255);
            var color = Theme.IsDark ? Color.FromArgb(alpha, 22, 22, 24) : Color.FromArgb(alpha, 246, 246, 248);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _tint.Background = brush;
            if (_hwnd != IntPtr.Zero) Native.SetAccent(_hwnd, _s.BarBlur ? 3 : 0, 0);
        }

        public void Flash(string text)
        {
            if (!IsVisible) return;
            _flash.Text = text;
            _flash.BeginAnimation(OpacityProperty, null);
            _flash.Opacity = 1;
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        static string Rate(double bps)
        {
            if (bps < 1024) return "0 KB/s";
            return Kit.Size((long)bps) + "/s";
        }

        public void Refresh(BarState st)
        {
            if (!IsVisible) return;
            _app.Visibility = _s.BarApp ? Visibility.Visible : Visibility.Collapsed;
            if (_app.Text != st.App) _app.Text = st.App;

            string mode = st.Copy != null ? "copy"
                : st.Download != null ? "download"
                : st.Attention.Length > 0 ? "attention"
                : st.Timer != null ? "timer"
                : st.MediaAny && st.MediaTitle.Length > 0 && _s.BarMedia ? "media"
                : "idle";

            if (mode != _mode)
            {
                _mode = mode;
                BuildPill(mode, st);
                var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
                var sc = new ScaleTransform(0.92, 0.92);
                _pill.RenderTransformOrigin = new Point(0.5, 0.5);
                _pill.RenderTransform = sc;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
                _pillContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            UpdatePill(st);

            _clock.Visibility = mode == "idle" ? Visibility.Collapsed : Visibility.Visible;
            _clock.Text = DateTime.Now.ToString(_s.Bar24h ? "HH:mm" : "h:mm tt");

            _stats.Visibility = _s.BarStats ? Visibility.Visible : Visibility.Collapsed;
            if (_s.BarStats) _stats.Text = st.Gpu >= 0 ? $"cpu {st.Cpu}%  gpu {st.Gpu}%  ram {st.Ram}%" : $"cpu {st.Cpu}%  ram {st.Ram}%";

            _stack.Visibility = st.Stack >= 0 ? Visibility.Visible : Visibility.Collapsed;
            _stackText.Text = st.Stack <= 0 ? "stack: copy stuff" : $"stack: {st.Stack}";

            SetVisible(_clipIcon, st.Clip.Length > 0);
            if (_clipIcon?.Parent is FrameworkElement ch) ch.ToolTip = st.Clip.Length > 0 ? "last copied: " + (st.Clip.Length > 80 ? st.Clip[..80] + "..." : st.Clip) : null;
            SetVisible(_focusIcon, st.Focus);
            if (_focusIcon != null) _focusIcon.SetResourceReference(TextBlock.ForegroundProperty, st.Focus ? "AccentFill" : "Text");
            SetVisible(_lockIcon, st.Locked);
            if (_lockIcon != null) _lockIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");

            if (_volText != null && _volIcon != null)
            {
                ((FrameworkElement)_volIcon.Parent).Visibility = _s.BarVolume ? Visibility.Visible : Visibility.Collapsed;
                _volText.Text = st.Volume < 0 ? "" : st.Muted ? "muted" : st.Volume + "%";
                _volIcon.Text = st.Muted || st.Volume == 0 ? "\uE74F" : "\uE767";
            }
            if (_batText != null && _batIcon != null)
            {
                ((FrameworkElement)_batIcon.Parent).Visibility = _s.BarBattery && st.Battery >= 0 ? Visibility.Visible : Visibility.Collapsed;
                _batText.Text = st.Battery + "%";
                _batIcon.Text = st.Charging ? "\uE945" : "\uE83F";
            }
            if (_badge != null) _badge.Visibility = st.Unseen > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        TextBlock? _pTitle, _pSub, _pTime;
        Border? _pBar, _pFill;
        Image? _pArt;
        TextBlock? _pPlay;

        void BuildPill(string mode, BarState st)
        {
            _pillContent.Children.Clear();
            _pTitle = _pSub = _pTime = _pPlay = null;
            _pBar = _pFill = null;
            _pArt = null;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _pillContent.Children.Add(row);
            double h = Math.Clamp(_s.BarHeight, 22, 48) - 8;
            _pill.Height = h;
            _pill.CornerRadius = new CornerRadius(h / 2);

            switch (mode)
            {
                case "idle":
                    if (_s.BarDate)
                    {
                        _pSub = Kit.Text("", 12, false, "SubText");
                        _pSub.Margin = new Thickness(0, 0, 8, 0);
                        row.Children.Add(_pSub);
                    }
                    _pTime = Kit.Text("", 12.5, true);
                    row.Children.Add(_pTime);
                    break;

                case "timer":
                    var tg = Kit.Icon("\uE916", 11, "AccentFill");
                    tg.Margin = new Thickness(0, 0, 7, 0);
                    row.Children.Add(tg);
                    _pTime = Kit.Text("", 12.5, true);
                    row.Children.Add(_pTime);
                    _pSub = Kit.Text("", 12, false, "SubText");
                    _pSub.Margin = new Thickness(8, 0, 0, 0);
                    row.Children.Add(_pSub);
                    break;

                case "media":
                    _pArt = new Image { Width = h - 6, Height = h - 6, Margin = new Thickness(-6, 0, 8, 0), Stretch = Stretch.UniformToFill };
                    _pArt.Clip = new RectangleGeometry(new Rect(0, 0, h - 6, h - 6), 5, 5);
                    row.Children.Add(_pArt);
                    _pTitle = Kit.Text("", 12, true);
                    _pTitle.MaxWidth = 220;
                    row.Children.Add(_pTitle);
                    _pSub = Kit.Text("", 12, false, "SubText");
                    _pSub.MaxWidth = 160;
                    _pSub.Margin = new Thickness(6, 0, 10, 0);
                    row.Children.Add(_pSub);
                    row.Children.Add(Mini("\uE892", () => MediaPrev?.Invoke()));
                    var play = Mini("\uE768", () => MediaToggle?.Invoke());
                    _pPlay = (TextBlock)play.Child;
                    row.Children.Add(play);
                    row.Children.Add(Mini("\uE893", () => MediaNext?.Invoke()));
                    AddBar(h);
                    break;

                case "download":
                case "copy":
                    var dg = Kit.Icon(mode == "copy" ? "\uE8C8" : "\uE896", 11, "AccentFill");
                    dg.Margin = new Thickness(0, 0, 8, 0);
                    row.Children.Add(dg);
                    _pTitle = Kit.Text("", 12, true);
                    _pTitle.MaxWidth = 340;
                    row.Children.Add(_pTitle);
                    _pSub = Kit.Text("", 11.5, false, "SubText");
                    _pSub.Margin = new Thickness(10, 0, 0, 0);
                    row.Children.Add(_pSub);
                    AddBar(h);
                    break;

                case "attention":
                    var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 8, 0) };
                    dot.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                    dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    row.Children.Add(dot);
                    _pTitle = Kit.Text("", 12, true);
                    row.Children.Add(_pTitle);
                    var sub = Kit.Text("wants you  ·  click", 12, false, "SubText");
                    sub.Margin = new Thickness(6, 0, 0, 0);
                    row.Children.Add(sub);
                    break;
            }
        }

        void AddBar(double h)
        {
            _pBar = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
            _pBar.SetResourceReference(Border.BackgroundProperty, "SliderTrack");
            _pFill = new Border { Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _pFill.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _pBar.Child = _pFill;
            _pillContent.Children.Add(_pBar);
        }

        Border Mini(string glyph, Action act)
        {
            var b = new Border { Width = 20, Height = 18, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = Kit.Icon(glyph, 9.5) };
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "RowHover");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                act();
            };
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            return b;
        }

        void SetFill(double frac)
        {
            if (_pBar == null || _pFill == null) return;
            double w = _pBar.ActualWidth;
            if (frac < 0)
            {
                double t = DateTime.Now.TimeOfDay.TotalSeconds % 1.6 / 1.6;
                _pFill.Width = w * 0.3;
                _pFill.Margin = new Thickness(w * 0.7 * t, 0, 0, 0);
            }
            else
            {
                _pFill.Margin = new Thickness(0);
                _pFill.Width = Math.Clamp(frac, 0, 1) * w;
            }
        }

        void UpdatePill(BarState st)
        {
            var now = DateTime.Now;
            switch (_mode)
            {
                case "idle":
                    if (_pSub != null) _pSub.Text = now.ToString("ddd MMM d");
                    if (_pTime != null) _pTime.Text = now.ToString(_s.Bar24h ? (_s.BarSeconds ? "HH:mm:ss" : "HH:mm") : (_s.BarSeconds ? "h:mm:ss tt" : "h:mm tt"));
                    break;
                case "timer":
                    if (st.Timer == null) break;
                    var left = st.Timer.Due - now;
                    if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                    if (_pTime != null) _pTime.Text = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");
                    if (_pSub != null) _pSub.Text = st.Timer.Text;
                    break;
                case "media":
                    if (_pTitle != null) _pTitle.Text = st.MediaTitle;
                    if (_pSub != null) _pSub.Text = st.MediaArtist;
                    if (_pArt != null)
                    {
                        _pArt.Source = st.Art;
                        _pArt.Visibility = st.Art != null ? Visibility.Visible : Visibility.Collapsed;
                    }
                    if (_pPlay != null) _pPlay.Text = st.MediaPlaying ? "\uE769" : "\uE768";
                    SetFill(st.MediaDur > 1 ? st.MediaPos / st.MediaDur : 0);
                    break;
                case "download":
                    if (st.Download == null) break;
                    if (_pTitle != null) _pTitle.Text = st.Download.Name;
                    if (_pSub != null) _pSub.Text = $"{Kit.Size(st.Download.Bytes)}  ·  {Rate(st.Download.Speed)}{(st.Download.Detail.Length > 0 ? "  ·  " + st.Download.Detail : "")}";
                    SetFill(-1);
                    break;
                case "copy":
                    if (st.Copy == null) break;
                    if (_pTitle != null) _pTitle.Text = st.Copy.Name;
                    if (_pSub != null) _pSub.Text = (st.Copy.Percent >= 0 ? $"{st.Copy.Percent:0}%" : "") + (st.Copy.Detail.Length > 0 ? "  \u00b7  " + st.Copy.Detail : "") + "  \u00b7  click for pause / cancel";
                    SetFill(st.Copy.Percent >= 0 ? st.Copy.Percent / 100 : -1);
                    break;
                case "attention":
                    if (_pTitle != null) _pTitle.Text = st.Attention;
                    break;
            }
        }

        void EdgeTick()
        {
            if (!_s.BarAutoHide || !IsVisible || _hwnd == IntPtr.Zero) return;
            var d = Display;
            Native.GetCursorPos(out var p);
            bool inX = p.X >= d.Bounds.Left && p.X < d.Bounds.Right;
            bool atTop = inX && p.Y <= d.Bounds.Top + 1 && p.Y >= d.Bounds.Top - 1;
            bool inside = inX && p.Y >= d.Bounds.Top && p.Y <= d.Bounds.Top + Height * d.Scale + 6;
            bool keep = inside || _pressed || (KeepOpen?.Invoke() ?? false);

            if (atTop && !_revealed) Reveal(true);
            else if (_revealed && !keep)
            {
                _outside += 0.09;
                if (_outside > 0.6) Reveal(false);
            }
            else _outside = 0;
        }

        public void Reveal(bool show)
        {
            if (!_s.BarAutoHide || _revealed == show || _hwnd == IntPtr.Zero) return;
            _revealed = show;
            _outside = 0;
            var d = Display;
            int px = (int)Math.Round(Height * d.Scale);
            int from = show ? d.Bounds.Top - px + 2 : d.Bounds.Top;
            int to = show ? d.Bounds.Top : d.Bounds.Top - px + 2;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            anim.Tick += (_, _) =>
            {
                double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / 170);
                double e = 1 - Math.Pow(1 - t, 3);
                int y = (int)Math.Round(from + (to - from) * e);
                Native.SetWindowPos(_hwnd, IntPtr.Zero, d.Bounds.Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                if (t >= 1) anim.Stop();
            };
            anim.Start();
        }
    }
}
