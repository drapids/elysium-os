using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Interop;

namespace Heft
{
    internal sealed class Transfer
    {
        public string Name = "";
        public long Bytes;
        public double Speed;
        public double Percent = -1;
        public string Detail = "";
    }

    internal sealed class Activity : IDisposable
    {
        static readonly string[] Partial = { ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp" };

        readonly Settings _s;
        readonly Thread _thread;
        readonly HwndSource _shell;
        readonly uint _shellMsg;
        readonly ConcurrentDictionary<string, (long size, DateTime at, double speed)> _downloads = new(StringComparer.OrdinalIgnoreCase);
        volatile bool _alive = true;
        FileSystemWatcher? _watcher;
        string _downloadsDir = "";
        PerformanceCounter[] _gpu = Array.Empty<PerformanceCounter>();
        DateTime _gpuAt = DateTime.MinValue;
        long _netRx, _netTx;
        DateTime _netAt = DateTime.MinValue;

        public Transfer? Download { get; private set; }
        public Transfer? Copy { get; private set; }
        public IntPtr Attention { get; private set; }
        public string AttentionApp { get; private set; } = "";
        public int Cpu { get; private set; }
        public int Ram { get; private set; }
        public int Gpu { get; private set; } = -1;
        public double NetDown { get; private set; }
        public double NetUp { get; private set; }

        public event Action? Changed;
        public event Action<string>? Downloaded;

        public Activity(Settings settings)
        {
            _s = settings;
            var p = new HwndSourceParameters("heft-shellhook") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            _shell = new HwndSource(p);
            _shellMsg = Native.RegisterWindowMessage("SHELLHOOK");
            _shell.AddHook(ShellHook);
            Native.RegisterShellHookWindow(_shell.Handle);

            StartDownloads();
            _thread = new Thread(Loop) { IsBackground = true, Name = "heft-activity", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        IntPtr ShellHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_shellMsg == 0 || (uint)msg != _shellMsg) return IntPtr.Zero;
            int code = wParam.ToInt32() & 0x7FFF;
            if (code == 0x0006 && (wParam.ToInt32() & 0x8000) != 0)
            {
                if (lParam != Native.GetForegroundWindow())
                {
                    Attention = lParam;
                    var path = Native.ProcessPath(lParam);
                    AttentionApp = path != null ? Path.GetFileNameWithoutExtension(path) : Native.Title(lParam);
                    Changed?.Invoke();
                }
            }
            else if (code == 0x0004 && lParam == Attention)
            {
                ClearAttention();
            }
            return IntPtr.Zero;
        }

        public void ClearAttention()
        {
            if (Attention == IntPtr.Zero) return;
            Attention = IntPtr.Zero;
            AttentionApp = "";
            Changed?.Invoke();
        }

        public void FocusAttention()
        {
            var h = Attention;
            ClearAttention();
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return;
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }

        void StartDownloads()
        {
            try
            {
                _downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(_downloadsDir)) return;
                _watcher = new FileSystemWatcher(_downloadsDir)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                };
                _watcher.Created += (_, e) => Touch(e.FullPath);
                _watcher.Changed += (_, e) => Touch(e.FullPath);
                _watcher.Renamed += (_, e) =>
                {
                    if (IsPartial(e.OldFullPath) && !IsPartial(e.FullPath))
                    {
                        _downloads.TryRemove(e.OldFullPath, out var gone);
                        Downloaded?.Invoke(e.FullPath);
                    }
                    else
                    {
                        Touch(e.FullPath);
                    }
                };
                _watcher.Deleted += (_, e) => _downloads.TryRemove(e.FullPath, out var gone);
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
            }
        }

        static bool IsPartial(string path)
        {
            string ext = Path.GetExtension(path);
            return Partial.Any(p => p.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        void Touch(string path)
        {
            if (!IsPartial(path)) return;
            if (!_downloads.ContainsKey(path)) _downloads[path] = (0, DateTime.UtcNow, 0);
        }

        void Loop()
        {
            int tick = 0;
            while (_alive)
            {
                try
                {
                    Poll(tick);
                }
                catch
                {
                }
                tick++;
                Thread.Sleep(500);
            }
        }

        void Poll(int tick)
        {
            var dl = PollDownloads();
            var cp = PollCopy();

            if (tick % 2 == 0)
            {
                Cpu = SystemStats.Cpu();
                Ram = SystemStats.Ram();
                PollNet();
                if (tick % 4 == 0) PollGpu();
            }

            Download = dl;
            Copy = cp;
            Changed?.Invoke();
        }

        Transfer? PollDownloads()
        {
            if (_downloads.IsEmpty) return null;
            Transfer? best = null;
            var now = DateTime.UtcNow;
            foreach (var kv in _downloads.ToArray())
            {
                long size;
                try
                {
                    var fi = new FileInfo(kv.Key);
                    if (!fi.Exists)
                    {
                        _downloads.TryRemove(kv.Key, out _);
                        continue;
                    }
                    size = fi.Length;
                }
                catch
                {
                    continue;
                }

                var (last, at, speed) = kv.Value;
                double dt = (now - at).TotalSeconds;
                if (dt > 0.3)
                {
                    double inst = Math.Max(0, (size - last) / dt);
                    speed = speed <= 0 ? inst : speed * 0.6 + inst * 0.4;
                    _downloads[kv.Key] = (size, now, speed);
                }

                if (size == last && dt > 30)
                {
                    _downloads.TryRemove(kv.Key, out _);
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(kv.Key);
                if (name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) name = "download";
                var t = new Transfer { Name = name, Bytes = size, Speed = speed };
                if (best == null || t.Speed > best.Speed) best = t;
            }

            if (best != null && _downloads.Count > 1) best.Detail = $"+{_downloads.Count - 1} more";
            return best;
        }

        IntPtr _copyWindow;

        public void FocusCopy()
        {
            var h = _copyWindow != IntPtr.Zero && Native.IsWindow(_copyWindow) ? _copyWindow : Native.FindWindow("OperationStatusWindow", null);
            if (h != IntPtr.Zero) Native.Focus(h);
        }

        Transfer? PollCopy()
        {
            IntPtr h = Native.FindWindow("OperationStatusWindow", null);
            _copyWindow = h;
            if (h == IntPtr.Zero) return null;
            var t = new Transfer { Name = "copying files" };
            try
            {
                var el = AutomationElement.FromHandle(h);
                var bar = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar));
                if (bar != null && bar.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pat))
                {
                    var rv = (RangeValuePattern)pat;
                    double max = rv.Current.Maximum;
                    if (max > 0) t.Percent = rv.Current.Value / max * 100;
                }
                var texts = el.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                foreach (AutomationElement x in texts)
                {
                    string name = x.Current.Name ?? "";
                    if (name.Length == 0) continue;
                    string low = name.ToLowerInvariant();
                    if (low.StartsWith("copying") || low.StartsWith("moving") || low.StartsWith("deleting") || low.StartsWith("extracting") || low.StartsWith("compressing"))
                    {
                        t.Name = name;
                        break;
                    }
                }
                if (t.Percent < 0)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Native.Title(h), @"(\d+)%");
                    if (m.Success) t.Percent = double.Parse(m.Groups[1].Value);
                }
                if (Native.Title(h).Contains("paused", StringComparison.OrdinalIgnoreCase)) t.Detail = "paused";
            }
            catch
            {
            }
            return t;
        }

        void PollNet()
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var st = ni.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
                catch
                {
                }
            }
            var now = DateTime.UtcNow;
            if (_netAt != DateTime.MinValue)
            {
                double dt = (now - _netAt).TotalSeconds;
                if (dt > 0)
                {
                    NetDown = Math.Max(0, (rx - _netRx) / dt);
                    NetUp = Math.Max(0, (tx - _netTx) / dt);
                }
            }
            _netRx = rx;
            _netTx = tx;
            _netAt = now;
        }

        void PollGpu()
        {
            try
            {
                if ((DateTime.UtcNow - _gpuAt).TotalSeconds > 30 || _gpu.Length == 0)
                {
                    foreach (var c in _gpu) c.Dispose();
                    _gpuAt = DateTime.UtcNow;
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    _gpu = cat.GetInstanceNames()
                        .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        .Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true))
                        .ToArray();
                    foreach (var c in _gpu) c.NextValue();
                    return;
                }
                double sum = 0;
                foreach (var c in _gpu)
                {
                    try
                    {
                        sum += c.NextValue();
                    }
                    catch
                    {
                    }
                }
                Gpu = (int)Math.Round(Math.Clamp(sum, 0, 100));
            }
            catch
            {
                Gpu = -1;
            }
        }

        public void Dispose()
        {
            _alive = false;
            try
            {
                Native.DeregisterShellHookWindow(_shell.Handle);
                _shell.Dispose();
                _watcher?.Dispose();
                foreach (var c in _gpu) c.Dispose();
            }
            catch
            {
            }
        }
    }
}
