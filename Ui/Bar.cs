using System;
using System.Collections.Generic;
using System.Linq;
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
        public bool MicMuted;
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

    internal sealed partial class Bar : Window
    {
        readonly Settings _s;
        readonly Grid _root = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        readonly Border _tint = new();
        readonly Border _gloss = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
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
        readonly Border _groove = new() { Height = 6, Width = 0, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        readonly Border _grooveGlow = new() { Height = 12, Width = 0, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0 };
        readonly DispatcherTimer _slitWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
        readonly DispatcherTimer _slitSpring = new() { Interval = TimeSpan.FromMilliseconds(900) };
        bool _slitOpen, _dragInside;
        long _lastDrag;

        readonly StackPanel _ext = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Dictionary<string, (Border host, TextBlock glyph, TextBlock text)> _extItems = new();
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
        public event Action<Bar>? DragNear;
        public event Action<Bar>? EmptyClicked;
        public event Action<Bar>? SlitSpring;
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
            _root.Children.Add(_gloss);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            line.SetResourceReference(Border.BackgroundProperty, "BarLine");
            _root.Children.Add(line);

            var grid = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            _app = Kit.Text("", 12.5, true);
            _app.HorizontalAlignment = HorizontalAlignment.Left;
            _app.MaxWidth = 260;
            _left.Children.Add(_app);
            grid.Children.Add(_left);

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
            _groove.Background = new LinearGradientBrush(Color.FromArgb(0xF2, 0, 0, 0), Color.FromArgb(0xB0, 0, 0, 0), 90);
            _groove.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 90, Opacity = 0.5, Color = Colors.White };
            _grooveGlow.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _grooveGlow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 9 };
            grid.Children.Add(_grooveGlow);
            grid.Children.Add(_groove);
            _slitWatch.Tick += (_, _) =>
            {
                if (!_slitOpen) return;
                if (_dragInside || IsMouseOver) return;
                if (Environment.TickCount64 - _lastDrag > 1400) CloseSlit();
            };
            _slitSpring.Tick += (_, _) =>
            {
                _slitSpring.Stop();
                if (_dragInside && _slitOpen) SlitSpring?.Invoke(this);
            };

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
            _right.Children.Add(_ext);
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
            BuildSlots(_ext, _stats, (FrameworkElement)_clipIcon!.Parent, (FrameworkElement)_focusIcon!.Parent, (FrameworkElement)_lockIcon!.Parent, vol, bat, bellHost, _clock, shelf);

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
                bool click = _pressed && !_dragged;
                _pressed = false;
                root_ReleaseCapture();
                if (click) EmptyClicked?.Invoke(this);
            };
            _root.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                SettingsRequested?.Invoke();
            };

            DragEnter += (_, e) =>
            {
                if (_selfDrag)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                _dragInside = true;
                _lastDrag = Environment.TickCount64;
                if (DragNear != null) DragNear.Invoke(this);
                else ShelfPull?.Invoke(this);
                if (_slitOpen)
                {
                    Glow(0.9);
                    _slitSpring.Stop();
                    _slitSpring.Start();
                }
            };
            DragOver += (_, e) =>
            {
                if (_selfDrag)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                _dragInside = true;
                _lastDrag = Environment.TickCount64;
            };
            DragLeave += (_, _) =>
            {
                _dragInside = false;
                _lastDrag = Environment.TickCount64;
                _slitSpring.Stop();
                if (_slitOpen) Glow(0);
            };
            Drop += (_, e) =>
            {
                e.Handled = true;
                _dragInside = false;
                _slitSpring.Stop();
                Dropped?.Invoke(e.Data);
            };
        }

        public void SetExt(IEnumerable<ExtBarItem> items)
        {
            var seen = new HashSet<string>();
            int index = 0;
            foreach (var it in items)
            {
                var item = it;
                seen.Add(item.Ext);
                if (!_extItems.TryGetValue(item.Ext, out var parts))
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    var g = Kit.Icon("", 12, "SubText");
                    g.VerticalAlignment = VerticalAlignment.Center;
                    var t = Kit.Text("", 12);
                    t.MaxWidth = 340;
                    t.TextTrimming = TextTrimming.CharacterEllipsis;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    sp.Children.Add(g);
                    sp.Children.Add(t);
                    var host = new Border { Child = sp, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent };
                    host.MouseEnter += (_, _) => host.SetResourceReference(Border.BackgroundProperty, "RowHover");
                    host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
                    host.MouseLeftButtonUp += (_, e) =>
                    {
                        if (host.Tag is not ExtBarItem bi || bi.Click == null) return;
                        e.Handled = true;
                        bi.Click();
                    };
                    parts = (host, g, t);
                    _extItems[item.Ext] = parts;
                    _ext.Children.Insert(Math.Min(index, _ext.Children.Count), host);
                    Glass.FadeIn(host, 0, 200);
                }
                parts.glyph.Text = item.Glyph;
                parts.glyph.Visibility = item.Glyph.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                parts.glyph.Margin = new Thickness(0, 0, item.Text.Length > 0 && item.Glyph.Length > 0 ? 6 : 0, 0);
                parts.text.Text = item.Text;
                parts.text.Visibility = item.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                parts.host.Cursor = item.Click != null ? Cursors.Hand : null;
                parts.host.Tag = item;
                index++;
            }
            foreach (var key in _extItems.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _ext.Children.Remove(_extItems[key].host);
                _extItems.Remove(key);
            }
        }

        public bool SlitOpen => _slitOpen;

        public Native.POINT SlotPx
        {
            get
            {
                Native.GetWindowRect(_hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle, out var r);
                return new Native.POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 + 1 };
            }
        }

        public void OpenSlit()
        {
            _lastDrag = Environment.TickCount64;
            if (_slitOpen) return;
            _slitOpen = true;
            if (!_revealed) Reveal(true);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            _pill.IsHitTestVisible = false;
            _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
            _app.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(200)));
            _groove.BeginAnimation(WidthProperty, new DoubleAnimation(0, 220, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(60) });
            _grooveGlow.BeginAnimation(WidthProperty, new DoubleAnimation(0, 236, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(60) });
            Glow(_dragInside ? 0.9 : 0.35);
            _slitWatch.Start();
        }

        public void CloseSlit()
        {
            if (!_slitOpen) return;
            _slitOpen = false;
            _slitWatch.Stop();
            _slitSpring.Stop();
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            _groove.BeginAnimation(WidthProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            _grooveGlow.BeginAnimation(WidthProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            Glow(0);
            _pill.IsHitTestVisible = true;
            _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(160) });
            _app.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(160) });
        }

        public void Gulp()
        {
            if (!_slitOpen) return;
            _lastDrag = Environment.TickCount64;
            var bump = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(420) };
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(250, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(220, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }));
            _groove.BeginAnimation(WidthProperty, bump);
            var flash = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(400) };
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
            _grooveGlow.BeginAnimation(OpacityProperty, flash);
            var close = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            close.Tick += (_, _) =>
            {
                close.Stop();
                if (!_dragInside && Environment.TickCount64 - _lastDrag > 1200) CloseSlit();
            };
            close.Start();
        }

        void Glow(double to)
        {
            _grooveGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)));
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
            if (_s.MonitorTints.TryGetValue(Display.Key, out var hex) && hex.Length > 0)
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    color = Color.FromArgb(alpha, c.R, c.G, c.B);
                }
                catch
                {
                }
            }
            Brush brush = new SolidColorBrush(color);
            brush.Freeze();
            var look = Themes.Active?.Bar;
            bool custom = _s.MonitorTints.TryGetValue(Display.Key, out var tintHex) && tintHex.Length > 0;
            if (!custom && Theme.Paint(look?.Fill) is Brush fill)
            {
                brush = fill.Clone();
                brush.Opacity = Math.Clamp(0.35 + _s.BarTint * 0.65, 0.35, 1);
                brush.Freeze();
            }
            _tint.Background = brush;
            if (look != null && look.Gloss)
            {
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 255, 255, 255), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 255, 255, 255), 0.48));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.52));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 255, 255, 255), 1));
                g.Freeze();
                _gloss.Background = g;
                _gloss.Visibility = Visibility.Visible;
            }
            else _gloss.Visibility = Visibility.Collapsed;
            if (Theme.Paint(look?.Text) is Brush ink)
            {
                _root.Resources["Text"] = ink;
                var sub = ink.Clone();
                sub.Opacity = 0.7;
                sub.Freeze();
                _root.Resources["SubText"] = sub;
            }
            else
            {
                _root.Resources.Remove("Text");
                _root.Resources.Remove("SubText");
            }
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
            SetMic(st.MicMuted);
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
            _sweeping = false;
            _fillTarget = -2;
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

        bool _sweeping;
        double _fillTarget = -2;

        void SetFill(double frac)
        {
            if (_pBar == null || _pFill == null) return;
            double w = _pBar.ActualWidth;
            if (w <= 0) return;
            if (frac < 0)
            {
                if (_sweeping) return;
                _sweeping = true;
                _fillTarget = -2;
                _pFill.BeginAnimation(WidthProperty, null);
                _pFill.Width = w * 0.3;
                var tt = new TranslateTransform();
                _pFill.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-w * 0.3, w, TimeSpan.FromMilliseconds(1300))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                _pBar.ClipToBounds = true;
                return;
            }
            if (_sweeping)
            {
                _sweeping = false;
                _pFill.RenderTransform = null;
            }
            double target = Math.Clamp(frac, 0, 1) * w;
            if (Math.Abs(target - _fillTarget) < 0.5) return;
            _fillTarget = target;
            _pFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
            Native.GetWindowRect(_hwnd, out var now);
            if (now.Top != from && now.Top >= Math.Min(from, to) && now.Top <= Math.Max(from, to)) from = now.Top;
            int lastY = int.MinValue;
            Frames.Run(Math.Max(60, (show ? 220 : 180) * Theme.Speed), t =>
            {
                double e = show ? Frames.Quint(t) : Frames.Out(t);
                int y = (int)Math.Round(from + (to - from) * e);
                if (y == lastY) return;
                lastY = y;
                Native.SetWindowPos(_hwnd, IntPtr.Zero, d.Bounds.Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }, null, this);
        }
    }
}
