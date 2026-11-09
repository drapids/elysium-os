using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Heft.Ui
{
    public partial class EdgeTab : Window
    {
        readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(160) };

        public event Action? OpenRequested;
        public event Action? WheelRequested;

        public EdgeTab()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);
            Loaded += (_, _) => Place();
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(new Action(Place));
            };

            _hover.Tick += (_, _) =>
            {
                _hover.Stop();
                OpenRequested?.Invoke();
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
                OpenRequested?.Invoke();
            };
            hit.MouseRightButtonUp += (_, e) =>
            {
                _hover.Stop();
                e.Handled = true;
                WheelRequested?.Invoke();
            };
        }

        public void Place()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width;
            Top = wa.Top + (wa.Height - Height) / 2;
        }

        void Grow(bool on)
        {
            var d = TimeSpan.FromMilliseconds(140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 6 : 4, d) { EasingFunction = ease });
            pill.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 72 : 56, d) { EasingFunction = ease });
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.85 : 0.28, d));
        }
    }
}
