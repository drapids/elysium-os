using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Elysium.Ui
{
    internal sealed class StandbyInfo
    {
        public string Title = "", Artist = "";
        public BitmapSource? Art;
        public double Pos, Dur;
        public bool Playing;
        public string Weather = "", WeatherGlyph = "\uE706";
        public int Unseen;
        public List<string> Notes = new();
        public string NextEvent = "";
    }

    internal sealed class Standby
    {
        readonly Settings _s;
        readonly List<StandbyScreen> _screens = new();
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

        public Func<StandbyInfo>? Info;
        public bool On => _screens.Count > 0;

        public Standby(Settings s)
        {
            _s = s;
            _tick.Tick += (_, _) => Update();
        }

        public void Enter()
        {
            if (On) return;
            var all = Displays.All();
            var list = _s.StandbyScreens == "all" ? all : new List<Display> { Displays.Primary(all) };
            foreach (var d in list)
            {
                var screen = new StandbyScreen(_s, d, d.Index == list[0].Index);
                screen.Woke += Leave;
                _screens.Add(screen);
                screen.Enter();
            }
            Update();
            _tick.Start();
        }

        public void Leave()
        {
            _tick.Stop();
            foreach (var s in _screens.ToList()) s.Leave();
            _screens.Clear();
        }

        void Update()
        {
            var info = Info?.Invoke() ?? new StandbyInfo();
            foreach (var s in _screens) s.Update(info);
        }
    }

    internal sealed class StandbyScreen : Window
    {
        readonly Settings _s;
        readonly Display _d;
        readonly bool _main;
        readonly Grid _stage = new() { Opacity = 0 };
        readonly Image _bg = new() { Stretch = Stretch.UniformToFill, Opacity = 0.9 };
        readonly Border _dim = new();
        readonly TextBlock _clock = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _date = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
        Point? _start;
        bool _leaving;
        string _sig = "";

        public event Action? Woke;

        public StandbyScreen(Settings s, Display d, bool main)
        {
            _s = s;
            _d = d;
            _main = main;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            Background = Brushes.Black;
            Title = "elysium standby";
            Cursor = Cursors.None;
            RenderOptions.SetBitmapScalingMode(_bg, BitmapScalingMode.HighQuality);
            _stage.Children.Add(_bg);
            _dim.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(_s.StandbyDim, 0, 0.9) * 255), 0, 0, 0));
            _stage.Children.Add(_dim);
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            bool big = _s.StandbyClock != "minimal";
            _clock.FontSize = big ? Math.Min(220, d.Bounds.Height / d.Scale * 0.24) : 72;
            _clock.FontWeight = big ? FontWeights.Thin : FontWeights.Light;
            _clock.Foreground = Brushes.White;
            _clock.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            _clock.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.35, Color = Colors.Black };
            center.Children.Add(_clock);
            _date.FontSize = big ? 26 : 18;
            _date.FontWeight = FontWeights.Light;
            _date.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 255, 255, 255));
            _date.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            center.Children.Add(_date);
            if (main) center.Children.Add(_cards);
            _stage.Children.Add(center);
            Content = _stage;
            SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, true);
            PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                Wake();
            };
            PreviewMouseDown += (_, e) =>
            {
                e.Handled = true;
                Wake();
            };
            MouseMove += (_, e) =>
            {
                var p = e.GetPosition(this);
                _start ??= p;
                if (Math.Abs(p.X - _start.Value.X) + Math.Abs(p.Y - _start.Value.Y) > 14) Wake();
            };
        }

        public void Enter()
        {
            if (_s.StandbyBg == "wallpaper")
            {
                var (_, soft) = Backdrop.Grab(_d.Bounds, 6, 3);
                _bg.Source = soft;
            }
            var h = new WindowInteropHelper(this).EnsureHandle();
            Native.Cloak(h, true);
            Show();
            Width = double.NaN;
            Height = double.NaN;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, _d.Bounds.Left, _d.Bounds.Top, _d.Bounds.Width, _d.Bounds.Height, 0);
            Activate();
            Glass.Unveil(this);
            Frames.Run(Math.Max(200, 700 * Theme.Speed), t => _stage.Opacity = Frames.Out(t), null, _stage);
        }

        void Wake()
        {
            if (_leaving) return;
            Woke?.Invoke();
        }

        public void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            double from = _stage.Opacity;
            Frames.Run(Math.Max(80, 240 * Theme.Speed), t => _stage.Opacity = from * (1 - Frames.Out(t)), () =>
            {
                try
                {
                    Close();
                }
                catch
                {
                }
            }, _stage);
        }

        public void Update(StandbyInfo info)
        {
            var now = DateTime.Now;
            _clock.Text = now.ToString(_s.Bar24h ? "HH:mm" : "h:mm");
            _date.Text = now.ToString("dddd, MMMM d").ToLowerInvariant();
            if (_s.StandbyBg == "art" && info.Art != null && _bg.Source != info.Art)
            {
                _bg.Source = info.Art;
                _bg.Effect = new BlurEffect { Radius = 60 };
                _bg.Opacity = 0.75;
            }
            if (!_main) return;
            string sig = $"{info.Title}|{info.Artist}|{info.Playing}|{info.Weather}|{info.Unseen}|{string.Join(",", info.Notes)}|{info.NextEvent}|{info.Art?.GetHashCode()}";
            if (sig == _sig) return;
            _sig = sig;
            _cards.Children.Clear();
            if (_s.StandbyMusic && info.Title.Length > 0) _cards.Children.Add(Music(info));
            if (_s.StandbyWeather && info.Weather.Length > 0) _cards.Children.Add(Card(info.WeatherGlyph, info.Weather, "outside"));
            if (_s.StandbyCalendar && info.NextEvent.Length > 0) _cards.Children.Add(Card("\uE787", info.NextEvent, "up next"));
            if (_s.StandbyNotes && info.Unseen > 0) _cards.Children.Add(Card("\uEA8F", info.Unseen + " new", string.Join("\n", info.Notes.Take(3))));
        }

        Border Shell(UIElement child) => new()
        {
            Child = child,
            Padding = new Thickness(18, 14, 20, 14),
            Margin = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top
        };

        static TextBlock T(string text, double size, bool bold = false, byte alpha = 0xFF) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260
        };

        Border Music(StandbyInfo info)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (info.Art != null)
            {
                var art = new Image { Source = info.Art, Width = 64, Height = 64, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 14, 0) };
                art.Clip = new RectangleGeometry(new Rect(0, 0, 64, 64), 10, 10);
                row.Children.Add(art);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(info.Title, 16, true));
            text.Children.Add(T(info.Artist, 13, false, 0xC0));
            text.Children.Add(T(info.Playing ? "playing" : "paused", 11.5, false, 0x90));
            row.Children.Add(text);
            return Shell(row);
        }

        Border Card(string glyph, string title, string sub)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 26, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            row.Children.Add(g);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(title, 16, true));
            if (sub.Length > 0)
            {
                var s = T(sub, 12.5, false, 0xB0);
                s.TextWrapping = TextWrapping.Wrap;
                s.MaxHeight = 60;
                text.Children.Add(s);
            }
            row.Children.Add(text);
            return Shell(row);
        }
    }
}
