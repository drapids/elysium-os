using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    public partial class TopBar : Window
    {
        readonly Settings _s;
        readonly AppCatalog _cat;
        readonly NowPlaying _np = new();
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _edge = new() { Interval = TimeSpan.FromMilliseconds(100) };
        readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };

        IntPtr _hwnd;
        uint _callback;
        bool _registered, _revealed = true, _pressed, _dragged, _downOnMedia, _downOnVol, _busy;
        Point _down;
        int _count;
        double _outside;
        long _lastApply;

        public Func<bool>? ShelfIsOpen;
        public event Action? ShelfToggle;
        public event Action? ShelfPull;
        public event Action? SettingsRequested;
        public event Action<IDataObject>? Dropped;

        internal TopBar(Settings settings, AppCatalog catalog)
        {
            InitializeComponent();
            _s = settings;
            _cat = catalog;

            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd, true);
                _callback = Native.RegisterWindowMessage("heft.appbar");
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            };

            Theme.Applied += Paint;
            _cat.Foreground += OnForeground;

            _tick.Tick += (_, _) => Update();
            _edge.Tick += (_, _) => EdgeTick();
            _flashTimer.Tick += (_, _) =>
            {
                _flashTimer.Stop();
                flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };

            shelfBtn.Click += (_, _) => ShelfToggle?.Invoke();

            root.MouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (!_s.GestureVolume) return;
                int v = Audio.Nudge(e.Delta > 0 ? 2 : -2);
                if (v < 0) return;
                volText.Text = v + "%";
                volGlyph.Text = v == 0 ? "" : "";
                Flash($"volume {v}%");
            };

            root.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle || !_s.GestureMedia) return;
                e.Handled = true;
                PlayPause();
            };

            root.MouseLeftButtonDown += (_, e) =>
            {
                _pressed = true;
                _dragged = false;
                _downOnMedia = media.IsMouseOver;
                _downOnVol = vol.IsMouseOver;
                _down = e.GetPosition(this);
                root.CaptureMouse();
            };

            root.MouseMove += (_, e) =>
            {
                if (!_pressed) return;
                if (e.GetPosition(this).Y - _down.Y < 8) return;
                _pressed = false;
                _dragged = true;
                root.ReleaseMouseCapture();
                ShelfPull?.Invoke();
            };

            root.MouseLeftButtonUp += (_, _) =>
            {
                if (!_pressed) return;
                _pressed = false;
                root.ReleaseMouseCapture();
                if (_dragged) return;
                if (_downOnMedia) PlayPause();
                else if (_downOnVol)
                {
                    Audio.ToggleMute();
                    Levels();
                }
            };

            root.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                SettingsRequested?.Invoke();
            };

            DragEnter += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                ShelfPull?.Invoke();
            };
            DragOver += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            Drop += (_, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                Dropped?.Invoke(e.Data);
            };
        }

        public double BottomEdge
        {
            get
            {
                if (!_s.BarEnabled || !IsVisible) return SystemParameters.WorkArea.Top;
                var mon = Native.PrimaryMonitor();
                double scale = Native.DpiScaleAt(mon.Left + 1, mon.Top + 1);
                return _s.BarAutoHide ? mon.Top / scale + Height : Top + Height;
            }
        }

        public bool Contains(int x, int y)
        {
            if (!IsVisible || _hwnd == IntPtr.Zero) return false;
            Native.GetWindowRect(_hwnd, out var r);
            return r.Contains(x, y);
        }

        public void Apply()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (!_s.BarEnabled)
                {
                    Unregister();
                    _tick.Stop();
                    _edge.Stop();
                    if (IsVisible) Hide();
                    return;
                }

                if (!IsVisible) Show();
                if (_hwnd == IntPtr.Zero) return;

                var mon = Native.PrimaryMonitor();
                double scale = Native.DpiScaleAt(mon.Left + 1, mon.Top + 1);
                double height = Math.Clamp(_s.BarHeight, 22, 48);
                int px = (int)Math.Round(height * scale);
                Height = height;
                Width = mon.Width / scale;

                if (_s.BarAutoHide)
                {
                    Unregister();
                    Left = mon.Left / scale;
                    BeginAnimation(TopProperty, null);
                    Top = _revealed ? mon.Top / scale : mon.Top / scale - height + 2;
                    _edge.Start();
                }
                else
                {
                    _edge.Stop();
                    _revealed = true;
                    Register(mon, px, scale);
                }

                _lastApply = Environment.TickCount64;
                Paint();
                Modules();
                _tick.Start();
                _count = 0;
                Update();
            }
            finally
            {
                _busy = false;
            }
        }

        void Register(Native.RECT mon, int px, double scale)
        {
            var abd = new Native.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(),
                hWnd = _hwnd,
                uCallbackMessage = _callback
            };
            if (!_registered)
            {
                Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
                _registered = true;
            }
            abd.uEdge = Native.ABE_TOP;
            abd.rc = new Native.RECT { Left = mon.Left, Top = mon.Top, Right = mon.Right, Bottom = mon.Top + px };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            abd.rc.Bottom = abd.rc.Top + px;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);

            BeginAnimation(TopProperty, null);
            Left = abd.rc.Left / scale;
            Top = abd.rc.Top / scale;
        }

        void Unregister()
        {
            if (!_registered || _hwnd == IntPtr.Zero) return;
            _registered = false;
            var abd = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        }

        public void Shutdown()
        {
            _tick.Stop();
            _edge.Stop();
            Unregister();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_callback == 0 || (uint)msg != _callback) return IntPtr.Zero;
            int code = wParam.ToInt32();
            if (code == Native.ABN_POSCHANGED)
            {
                if (Environment.TickCount64 - _lastApply > 500) Dispatcher.BeginInvoke(new Action(Apply));
            }
            else if (code == Native.ABN_FULLSCREENAPP)
            {
                bool full = lParam != IntPtr.Zero;
                Topmost = !full;
                if (full)
                    Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        void Paint()
        {
            byte alpha = (byte)Math.Round(Math.Clamp(_s.BarTint, 0, 1) * 255);
            var color = Theme.IsDark ? Color.FromArgb(alpha, 22, 22, 24) : Color.FromArgb(alpha, 246, 246, 248);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            tint.Background = brush;
            if (_hwnd != IntPtr.Zero) Native.SetAccent(_hwnd, _s.BarBlur ? 3 : 0, 0);
        }

        void Modules()
        {
            appName.Visibility = _s.BarApp ? Visibility.Visible : Visibility.Collapsed;
            date.Visibility = _s.BarDate ? Visibility.Visible : Visibility.Collapsed;
            stats.Visibility = _s.BarStats ? Visibility.Visible : Visibility.Collapsed;
            vol.Visibility = _s.BarVolume ? Visibility.Visible : Visibility.Collapsed;
            if (!_s.BarMedia) media.Visibility = Visibility.Collapsed;
            if (!_s.BarBattery) bat.Visibility = Visibility.Collapsed;
        }

        void OnForeground(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file.Equals("heft", StringComparison.OrdinalIgnoreCase)) return;
            appName.Text = file.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "Desktop" : _cat.DisplayName(path);
        }

        void Update()
        {
            var now = DateTime.Now;
            string format = _s.Bar24h ? (_s.BarSeconds ? "HH:mm:ss" : "HH:mm") : (_s.BarSeconds ? "h:mm:ss tt" : "h:mm tt");
            clock.Text = now.ToString(format);
            if (_s.BarDate) date.Text = now.ToString("ddd MMM d");

            if (_count % 2 == 0)
            {
                if (_s.BarStats) stats.Text = $"cpu {SystemStats.Cpu()}%   ram {SystemStats.Ram()}%";
                Levels();
                if (_s.BarMedia) RefreshMedia();
            }
            _count++;
        }

        void Levels()
        {
            if (_s.BarVolume)
            {
                int v = Audio.Volume(out bool muted);
                if (v >= 0)
                {
                    volText.Text = muted ? "muted" : v + "%";
                    volGlyph.Text = muted || v == 0 ? "" : "";
                }
            }

            if (!_s.BarBattery) return;
            try
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool none = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                            || power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Unknown);
                bat.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
                if (none) return;
                bool charging = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                batText.Text = $"{Math.Round(power.BatteryLifePercent * 100)}%";
                batGlyph.Text = charging ? "" : "";
            }
            catch
            {
                bat.Visibility = Visibility.Collapsed;
            }
        }

        async void RefreshMedia()
        {
            try
            {
                await _np.Refresh();
                bool show = _s.BarMedia && _np.HasSession && _np.Title.Length > 0;
                media.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) return;
                mediaText.Text = _np.Artist.Length > 0 ? $"{_np.Title}  ·  {_np.Artist}" : _np.Title;
                mediaGlyph.Text = _np.Playing ? "" : "";
            }
            catch
            {
            }
        }

        async void PlayPause()
        {
            Native.Tap(Native.VK_MEDIA_PLAY_PAUSE);
            try
            {
                await System.Threading.Tasks.Task.Delay(350);
                RefreshMedia();
            }
            catch
            {
            }
        }

        public void Flash(string text)
        {
            if (!IsVisible) return;
            flash.Text = text;
            flash.BeginAnimation(OpacityProperty, null);
            flash.Opacity = 1;
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        public void SetStack(bool active, int count)
        {
            stackPill.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            stackText.Text = count == 0 ? "stack: copy stuff" : $"stack: {count}";
        }

        void EdgeTick()
        {
            if (!_s.BarAutoHide || !IsVisible) return;
            var mon = Native.PrimaryMonitor();
            double scale = Native.DpiScaleAt(mon.Left + 1, mon.Top + 1);
            Native.GetCursorPos(out var p);
            bool inX = p.X >= mon.Left && p.X < mon.Right;
            bool atTop = inX && p.Y <= mon.Top + 1;
            bool inside = inX && p.Y <= mon.Top + Height * scale + 6;
            bool keep = inside || _pressed || (ShelfIsOpen?.Invoke() ?? false);

            if (atTop && !_revealed)
            {
                Reveal(true);
            }
            else if (_revealed && !keep)
            {
                _outside += 0.1;
                if (_outside > 0.6) Reveal(false);
            }
            else
            {
                _outside = 0;
            }
        }

        public void Reveal(bool show)
        {
            if (!_s.BarAutoHide || !IsVisible) return;
            if (_revealed == show) return;
            _revealed = show;
            _outside = 0;
            var mon = Native.PrimaryMonitor();
            double scale = Native.DpiScaleAt(mon.Left + 1, mon.Top + 1);
            double to = show ? mon.Top / scale : mon.Top / scale - Height + 2;
            BeginAnimation(TopProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }
}
