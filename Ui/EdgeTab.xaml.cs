using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    public partial class EdgeTab : Window
    {
        readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(110) };
        string _side = "right";
        Display? _display;

        public Display? Display => _display;
        public string Side => _side;

        public event Action? DockRequested;
        public event Action? WorkAreaChanged;
        public event Action? SettingsRequested;

        public EdgeTab()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea)) WorkAreaChanged?.Invoke();
            };

            _hover.Tick += (_, _) =>
            {
                _hover.Stop();
                DockRequested?.Invoke();
            };

            hit.MouseEnter += (_, _) =>
            {
                Grow(true);
                _hover.Start();
            };
            hit.MouseLeave += (_, _) =>
            {
                Grow(false);
                _hover.Stop();
            };
            hit.MouseLeftButtonUp += (_, _) =>
            {
                _hover.Stop();
                DockRequested?.Invoke();
            };
            hit.MouseRightButtonUp += (_, e) =>
            {
                _hover.Stop();
                e.Handled = true;
                SettingsRequested?.Invoke();
            };
        }

        bool Vertical => _side == "left" || _side == "right";

        public void SetSide(string side)
        {
            _side = side;
            Place();
        }

        public void SetEdge(Display d, string side)
        {
            _display = d;
            _side = side;
            Place();
        }

        public void Place()
        {
            pill.BeginAnimation(WidthProperty, null);
            pill.BeginAnimation(HeightProperty, null);
            bool vertical = Vertical;
            Width = vertical ? 10 : 160;
            Height = vertical ? 160 : 10;
            pill.Width = vertical ? 3 : 56;
            pill.Height = vertical ? 56 : 3;
            pill.HorizontalAlignment = vertical ? (_side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Center;
            pill.VerticalAlignment = vertical ? VerticalAlignment.Center : (_side == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom);
            pill.Margin = vertical ? new Thickness(3, 0, 3, 0) : new Thickness(0, 3, 0, 3);

            var d = _display ?? Displays.Primary();
            var wa = d.Work;
            int w = (int)Math.Round(Width * d.Scale), h = (int)Math.Round(Height * d.Scale);
            int x, y;
            switch (_side)
            {
                case "left":
                    x = wa.Left;
                    y = wa.Top + (wa.Height - h) / 2;
                    break;
                case "top":
                    x = wa.Left + (wa.Width - w) / 2;
                    y = wa.Top;
                    break;
                case "bottom":
                    x = wa.Left + (wa.Width - w) / 2;
                    y = wa.Bottom - h;
                    break;
                default:
                    x = wa.Right - w;
                    y = wa.Top + (wa.Height - h) / 2;
                    break;
            }
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Left = x / d.Scale;
                Top = y / d.Scale;
                return;
            }
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
        }

        void Grow(bool on)
        {
            var d = TimeSpan.FromMilliseconds(140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var along = Vertical ? HeightProperty : WidthProperty;
            pill.BeginAnimation(along, new DoubleAnimation(on ? 80 : 56, d) { EasingFunction = ease });
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.7 : 0.22, d));
        }
    }
}
