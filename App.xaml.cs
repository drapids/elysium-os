using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Heft.Ui;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Heft
{
    public partial class App : Application
    {
        const int HotkeyDock = 1;
        const int HotkeyPanel = 2;

        Mutex? _single;
        Settings _s = null!;
        PhysicsEngine _physics = null!;
        AppCatalog _catalog = null!;
        EdgeTab _tab = null!;
        SidePanel _panel = null!;
        ArcDock _dock = null!;
        HwndSource? _hotkeys;
        Forms.NotifyIcon? _tray;
        Forms.ToolStripMenuItem? _pauseItem;
        DispatcherTimer? _autosave;
        bool _quitting, _cleaned;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _single = new Mutex(true, "heft.single.instance", out bool fresh);
            if (!fresh)
            {
                Shutdown();
                return;
            }

            _s = Settings.Load();
            Theme.Apply(_s.Theme);
            SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => Theme.Apply(_s.Theme)));

            _physics = new PhysicsEngine(_s);
            _catalog = new AppCatalog(_s);
            _tab = new EdgeTab();
            _panel = new SidePanel(_s, _catalog);
            _dock = new ArcDock(_s, _catalog);

            _tab.DockRequested += () => _dock.ShowDock(true, false);
            _tab.SettingsRequested += () => _panel.Open();
            _dock.SettingsRequested += () => _panel.Open();
            _panel.DockRequested += () => _dock.ShowDock(false, false);
            _panel.QuitRequested += Quit;

            _s.Changed += Sync;
            Sync();

            SetupHotkeys();
            SetupTray();

            _autosave = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _autosave.Tick += (_, _) =>
            {
                _catalog.Flush();
                _s.SaveNow();
            };
            _autosave.Start();

            _catalog.Scan();
        }

        void Sync()
        {
            _tab.SetSide(_s.DockSide);
            if (_s.ShowHandle && !_tab.IsVisible) _tab.Show();
            else if (!_s.ShowHandle && _tab.IsVisible) _tab.Hide();

            if (!_s.Enabled) _physics.StopAll();
            if (_pauseItem != null) _pauseItem.Checked = !_s.Enabled;
            if (_tray != null) _tray.Text = _s.Enabled ? "heft" : "heft (paused)";
        }

        void SetupHotkeys()
        {
            var p = new HwndSourceParameters("heft-hotkeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3)
            };
            _hotkeys = new HwndSource(p);
            _hotkeys.AddHook(HotkeyHook);
            const uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
            Native.RegisterHotKey(_hotkeys.Handle, HotkeyDock, mods, 0x20);
            Native.RegisterHotKey(_hotkeys.Handle, HotkeyPanel, mods, 0x48);
        }

        IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
            switch (wParam.ToInt32())
            {
                case HotkeyDock:
                    _dock.ShowDock(false, true);
                    handled = true;
                    break;
                case HotkeyPanel:
                    _panel.Toggle();
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("open panel", null, (_, _) => _panel.Open());
            menu.Items.Add("open dock", null, (_, _) => _dock.ShowDock(false, false));
            menu.Items.Add(new Forms.ToolStripSeparator());
            _pauseItem = new Forms.ToolStripMenuItem("pause physics") { Checked = !_s.Enabled };
            _pauseItem.Click += (_, _) =>
            {
                _s.Enabled = !_s.Enabled;
                _s.Touch();
            };
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("quit", null, (_, _) => Quit());

            _tray = new Forms.NotifyIcon
            {
                Icon = MakeIcon(),
                Text = "heft",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) _panel.Toggle();
            };
        }

        static Drawing.Icon MakeIcon()
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var pen = new Drawing.Pen(Drawing.Color.White, 2.6f)
                {
                    StartCap = Drawing.Drawing2D.LineCap.Round,
                    EndCap = Drawing.Drawing2D.LineCap.Round
                };
                using var path = new Drawing.Drawing2D.GraphicsPath();
                const float x = 12, y = 7, w = 17, h = 15, r = 4;
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
                g.DrawLine(pen, 3, 11, 8, 11);
                g.DrawLine(pen, 5, 17, 8, 17);
                g.DrawLine(pen, 7, 26, 23, 26);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            Cleanup();
            Shutdown();
        }

        void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;
            _autosave?.Stop();
            _catalog?.Dispose();
            _s?.SaveNow();
            _physics?.Dispose();
            if (_hotkeys != null)
            {
                Native.UnregisterHotKey(_hotkeys.Handle, HotkeyDock);
                Native.UnregisterHotKey(_hotkeys.Handle, HotkeyPanel);
                _hotkeys.Dispose();
                _hotkeys = null;
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            Cleanup();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!_quitting && _s != null) Cleanup();
            _single?.Dispose();
            base.OnExit(e);
        }
    }
}
