using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Elysium
{
    internal static class SystemStats
    {
        static ulong _idle, _total;

        public static int Cpu()
        {
            if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            ulong i = idle.Value;
            ulong t = kernel.Value + user.Value;
            ulong di = i - _idle;
            ulong dt = t - _total;
            bool first = _total == 0;
            _idle = i;
            _total = t;
            if (first || dt == 0) return 0;
            double used = 1.0 - (double)di / dt;
            return (int)Math.Round(Math.Clamp(used, 0, 1) * 100);
        }

        public static int Ram()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            return Native.GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
        }
    }

    internal static class Audio
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class DeviceEnumerator
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig]
            int RegisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int UnregisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int GetChannelCount(out uint count);

            [PreserveSig]
            int SetMasterVolumeLevel(float level, ref Guid context);

            [PreserveSig]
            int SetMasterVolumeLevelScalar(float level, ref Guid context);

            [PreserveSig]
            int GetMasterVolumeLevel(out float level);

            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);

            [PreserveSig]
            int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

            [PreserveSig]
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

            [PreserveSig]
            int GetChannelVolumeLevel(uint channel, out float level);

            [PreserveSig]
            int GetChannelVolumeLevelScalar(uint channel, out float level);

            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IAudioEndpointVolume? Endpoint()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device == null) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0) return null;
                return obj as IAudioEndpointVolume;
            }
            catch
            {
                return null;
            }
        }

        public static int Volume(out bool muted)
        {
            muted = false;
            try
            {
                var ep = Endpoint();
                if (ep == null) return -1;
                ep.GetMasterVolumeLevelScalar(out float level);
                ep.GetMute(out muted);
                return (int)Math.Round(level * 100);
            }
            catch
            {
                return -1;
            }
        }

        public static int Nudge(int percent)
        {
            try
            {
                var ep = Endpoint();
                if (ep == null) return -1;
                ep.GetMasterVolumeLevelScalar(out float level);
                float next = Math.Clamp((float)Math.Round(level * 100 + percent) / 100f, 0f, 1f);
                var ctx = Guid.Empty;
                ep.SetMasterVolumeLevelScalar(next, ref ctx);
                if (percent > 0) ep.SetMute(false, ref ctx);
                return (int)Math.Round(next * 100);
            }
            catch
            {
                return -1;
            }
        }

        public static void ToggleMute()
        {
            try
            {
                var ep = Endpoint();
                if (ep == null) return;
                ep.GetMute(out bool muted);
                var ctx = Guid.Empty;
                ep.SetMute(!muted, ref ctx);
            }
            catch
            {
            }
        }
    }

    internal sealed class Brightness : IDisposable
    {
        readonly Thread _thread;
        readonly AutoResetEvent _wake = new(false);
        volatile bool _alive = true;
        int _pending;
        int _x, _y;

        public event Action<int>? Changed;

        public Brightness()
        {
            _thread = new Thread(Work) { IsBackground = true, Name = "elysium-brightness" };
            _thread.Start();
        }

        public void Nudge(int steps, int x, int y)
        {
            Interlocked.Add(ref _pending, steps);
            _x = x;
            _y = y;
            _wake.Set();
        }

        void Work()
        {
            while (_alive)
            {
                _wake.WaitOne();
                if (!_alive) break;
                int steps = Interlocked.Exchange(ref _pending, 0);
                if (steps == 0) continue;
                int result = -1;
                try
                {
                    result = Apply(steps);
                }
                catch
                {
                }
                Changed?.Invoke(result);
            }
        }

        int Apply(int steps)
        {
            IntPtr mon = Native.MonitorFromPoint(new Native.POINT(_x, _y), Native.MONITOR_DEFAULTTONEAREST);
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out uint count) || count == 0) return -1;
            var list = new Native.PHYSICAL_MONITOR[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(mon, count, list)) return -1;
            int result = -1;
            try
            {
                foreach (var pm in list)
                {
                    if (!Native.GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max) || max <= min) continue;
                    double range = max - min;
                    double pct = (cur - min) / range * 100 + steps * 5;
                    pct = Math.Clamp(Math.Round(pct / 5) * 5, 0, 100);
                    uint value = (uint)Math.Round(min + range * pct / 100);
                    if (Native.SetMonitorBrightness(pm.hPhysicalMonitor, value)) result = (int)pct;
                }
            }
            finally
            {
                Native.DestroyPhysicalMonitors(count, list);
            }
            return result;
        }

        public void Dispose()
        {
            _alive = false;
            _wake.Set();
        }
    }
}
