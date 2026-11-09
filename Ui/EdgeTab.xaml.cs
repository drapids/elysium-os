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

        public event Action? DockRequested;
        public event Action? SettingsRequested;

        public EdgeTab()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(new Action(Place));
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

        public void Place()
        {
            var wa = SystemParameters.WorkArea;
            pill.BeginAnimation(WidthProperty, null);
            pill.BeginAnimation(HeightProperty, null);
            if (Vertical)
            {
                Width = 10;
                Height = 160;
                Left = _side == "left" ? wa.Left : wa.Right - Width;
                Top = wa.Top + (wa.Height - Height) / 2;
                pill.Width = 3;
                pill.Height = 56;
                pill.HorizontalAlignment = _side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                pill.VerticalAlignment = VerticalAlignment.Center;
                pill.Margin = new Thickness(3, 0, 3, 0);
            }
            else
            {
                Width = 160;
                Height = 10;
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = _side == "top" ? wa.Top : wa.Bottom - Height;
                pill.Width = 56;
                pill.Height = 3;
                pill.HorizontalAlignment = HorizontalAlignment.Center;
                pill.VerticalAlignment = _side == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                pill.Margin = new Thickness(0, 3, 0, 3);
            }
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
