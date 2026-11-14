using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed partial class Bar
    {
        BarState? _lastState;
        string _pulseGlyph = "", _pulseText = "";
        double _pulseLevel = -1;
        DateTime _pulseUntil = DateTime.MinValue;
        DispatcherTimer? _pulseTimer;
        Border? _pzTrack, _pzFill;
        TextBlock? _pzText, _pzGlyph;

        public event Action? RecClicked;
        public event Action<Bar, double>? ClockClicked;

        bool PulseOn => DateTime.Now < _pulseUntil;

        public void Pulse(string glyph, string text, double level = -1, int ms = 1600)
        {
            if (!IsVisible) return;
            bool same = _mode == "pulse" && _pulseGlyph == glyph && PulseOn;
            _pulseGlyph = glyph;
            _pulseText = text;
            _pulseLevel = level;
            _pulseUntil = DateTime.Now.AddMilliseconds(ms);
            if (_pulseTimer == null)
            {
                _pulseTimer = new DispatcherTimer();
                _pulseTimer.Tick += (_, _) =>
                {
                    if (PulseOn) return;
                    _pulseTimer.Stop();
                    if (_lastState != null) Refresh(_lastState);
                };
            }
            _pulseTimer.Stop();
            _pulseTimer.Interval = TimeSpan.FromMilliseconds(ms + 20);
            _pulseTimer.Start();
            if (same)
            {
                UpdatePulse();
                return;
            }
            if (_lastState != null) Refresh(_lastState);
        }

        void BuildIsland(string mode, StackPanel row, double h)
        {
            _pzTrack = _pzFill = null;
            _pzText = _pzGlyph = null;
            if (mode == "rec")
            {
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) };
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                row.Children.Add(dot);
                row.Children.Add(Kit.Text("recording", 12, true));
                _pTime = Kit.Text("", 12, false, "SubText");
                _pTime.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(_pTime);
                var stop = Kit.Text("click to stop", 11.5, false, "Faint");
                stop.Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(stop);
                return;
            }
            _pzGlyph = Kit.Icon(_pulseGlyph, 12, "Text");
            _pzGlyph.Margin = new Thickness(0, 0, 9, 0);
            row.Children.Add(_pzGlyph);
            _pzText = Kit.Text("", 12, true);
            row.Children.Add(_pzText);
            _pzTrack = new Border { Width = 96, Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(12, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            _pzTrack.SetResourceReference(Border.BackgroundProperty, "SliderTrack");
            _pzFill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _pzFill.SetResourceReference(Border.BackgroundProperty, "AccentFill");
            _pzTrack.Child = _pzFill;
            row.Children.Add(_pzTrack);
        }

        void UpdatePulse()
        {
            if (_pzText != null) _pzText.Text = _pulseText;
            if (_pzGlyph != null) _pzGlyph.Text = _pulseGlyph;
            if (_pzTrack == null || _pzFill == null) return;
            _pzTrack.Visibility = _pulseLevel >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_pulseLevel < 0) return;
            double to = Math.Clamp(_pulseLevel, 0, 1) * 96;
            double from = double.IsNaN(_pzFill.Width) ? 0 : _pzFill.Width;
            Frames.Run(Math.Max(60, 160 * Theme.Speed), t => _pzFill.Width = from + (to - from) * Frames.Out(t), null, _pzFill);
        }

        void Morph(double oldW)
        {
            _pill.Width = double.NaN;
            _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double newW = Math.Max(_pill.MinWidth, _pill.DesiredSize.Width);
            if (oldW <= 0 || Math.Abs(newW - oldW) < 1 || Theme.Speed <= 0.01)
            {
                Bump();
                return;
            }
            _pill.Width = oldW;
            Frames.Run(Math.Max(120, 420 * Theme.Speed), t =>
            {
                _pill.Width = Math.Max(_pill.MinWidth, oldW + (newW - oldW) * Frames.Spring(t));
            }, () => _pill.Width = double.NaN, _pill);
            Bump();
        }

        public void Intro()
        {
            if (_hwnd == IntPtr.Zero || !IsVisible || _want.Width <= 0) return;
            if (_s.BarAutoHide && !_revealed) return;
            var w = _want;
            int to = w.Top, from = w.Top - _px - 2;
            Frames.Run(Math.Max(150, 520 * Theme.Speed), t =>
            {
                int y = (int)Math.Round(from + (to - from) * Frames.Quint(t));
                Native.SetWindowPos(_hwnd, IntPtr.Zero, w.Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }, null, this);
        }

        Border? _glow;

        public void Glow(bool on)
        {
            if (on && _glow == null)
            {
                _glow = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Opacity = 0, Margin = new Thickness(80, 0, 80, 0), CornerRadius = new CornerRadius(1) };
                _glow.SetResourceReference(Border.BackgroundProperty, "AccentFill");
                _glow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
                _root.Children.Add(_glow);
            }
            if (_glow == null) return;
            if (on) _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            else _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        }

        void Bump()
        {
            var sc = new ScaleTransform(0.96, 0.96);
            _pill.RenderTransformOrigin = new Point(0.5, 0.5);
            _pill.RenderTransform = sc;
            Frames.Run(Math.Max(100, 320 * Theme.Speed), t =>
            {
                double s = 0.96 + 0.04 * Frames.Spring(t);
                sc.ScaleX = s;
                sc.ScaleY = s;
            }, null, sc);
        }
    }
}
