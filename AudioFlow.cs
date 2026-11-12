using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Heft
{
    public sealed class AudioProfile
    {
        public string Name { get; set; } = "";
        public string Output { get; set; } = "";
        public string Input { get; set; } = "";
        public Dictionary<string, float> Volumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Muted { get; set; } = new();
        public string Hotkey { get; set; } = "";
    }

    internal sealed class AudioDevice
    {
        public string Id = "";
        public string Name = "";
        public bool Input;
        public bool Default;
    }

    internal sealed class AudioApp
    {
        public uint Pid;
        public string Exe = "";
        public string Name = "";
        public string? Path;
        public float Volume;
        public bool Muted;
        public float Peak;
        public bool System;
    }

    internal sealed class AudioFlow : IDisposable
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public int pid;
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);

        [StructLayout(LayoutKind.Explicit)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointer;
            [FieldOffset(16)] public IntPtr pad;
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        }

        [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMNotificationClient
        {
            void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
            void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class PolicyConfigClient
        {
        }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
        }

        sealed class Watcher : IMMNotificationClient
        {
            readonly AudioFlow _owner;
            public Watcher(AudioFlow owner) => _owner = owner;
            public void OnDeviceStateChanged(string id, int state) => _owner.DeviceEvent(id, state == 1);
            public void OnDeviceAdded(string id) => _owner.DeviceEvent(id, true);
            public void OnDeviceRemoved(string id) => _owner.DeviceEvent(id, false);
            public void OnDefaultDeviceChanged(int flow, int role, string id) =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _owner.DevicesChanged?.Invoke()));
            public void OnPropertyValueChanged(string id, PROPERTYKEY key)
            {
            }
        }

        static readonly PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        readonly Settings _s;
        readonly IMMDeviceEnumerator? _enum;
        readonly Watcher? _watcher;
        readonly Dictionary<string, bool> _focusMuted = new(StringComparer.OrdinalIgnoreCase);

        public event Action? DevicesChanged;

        public AudioFlow(Settings settings)
        {
            _s = settings;
            try
            {
                _enum = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                _watcher = new Watcher(this);
                _enum.RegisterEndpointNotificationCallback(_watcher);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("audio init failed " + ex.Message);
            }
        }

        void DeviceEvent(string id, bool arrived)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => HandleDevice(id, arrived)));
        }

        void HandleDevice(string id, bool arrived)
        {
            DevicesChanged?.Invoke();
            if (!arrived || !_s.AudioAutoSwitch) return;
            try
            {
                var all = Devices(false).Concat(Devices(true)).ToList();
                var dev = all.FirstOrDefault(d => d.Id == id);
                if (dev == null) return;
                var list = _s.DevicePriority;
                int rank = list.IndexOf(id);
                if (rank < 0) return;
                var current = all.FirstOrDefault(d => d.Input == dev.Input && d.Default);
                int currentRank = current == null ? int.MaxValue : list.IndexOf(current.Id);
                if (currentRank < 0) currentRank = int.MaxValue;
                if (rank < currentRank)
                {
                    SetDefault(id);
                    Switched?.Invoke(dev.Name);
                }
            }
            catch
            {
            }
        }

        public event Action<string>? Switched;

        static string Name(IMMDevice d)
        {
            try
            {
                if (d.OpenPropertyStore(0, out var store) != 0) return "";
                var key = FriendlyName;
                if (store.GetValue(ref key, out var v) != 0) return "";
                string name = v.vt == 31 && v.pointer != IntPtr.Zero ? Marshal.PtrToStringUni(v.pointer) ?? "" : "";
                PropVariantClear(ref v);
                return name;
            }
            catch
            {
                return "";
            }
        }

        public List<AudioDevice> Devices(bool input)
        {
            var list = new List<AudioDevice>();
            if (_enum == null) return list;
            try
            {
                int flow = input ? 1 : 0;
                string defId = "";
                if (_enum.GetDefaultAudioEndpoint(flow, 1, out var def) == 0 && def != null) def.GetId(out defId);
                if (_enum.EnumAudioEndpoints(flow, 1, out var col) != 0) return list;
                col.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (col.Item(i, out var d) != 0) continue;
                    d.GetId(out string id);
                    list.Add(new AudioDevice { Id = id, Name = Name(d), Input = input, Default = id == defId });
                }
            }
            catch
            {
            }
            return list;
        }

        public bool SetDefault(string id)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClient();
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= policy.SetDefaultEndpoint(id, role) == 0;
                DevicesChanged?.Invoke();
                return ok;
            }
            catch
            {
                return false;
            }
        }

        public string? CycleOutput()
        {
            var list = Devices(false);
            if (list.Count < 2) return null;
            int cur = list.FindIndex(d => d.Default);
            var next = list[(cur + 1) % list.Count];
            return SetDefault(next.Id) ? next.Name : null;
        }

        IAudioSessionEnumerator? Sessions(string? deviceId = null)
        {
            if (_enum == null) return null;
            IMMDevice? dev;
            if (deviceId == null)
            {
                if (_enum.GetDefaultAudioEndpoint(0, 1, out dev) != 0) return null;
            }
            else if (_enum.GetDevice(deviceId, out dev) != 0)
            {
                return null;
            }
            if (dev == null) return null;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0) return null;
            var mgr = (IAudioSessionManager2)obj;
            return mgr.GetSessionEnumerator(out var e) == 0 ? e : null;
        }

        public List<AudioApp> Apps()
        {
            var map = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var e = Sessions();
                if (e == null) return new List<AudioApp>();
                e.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (e.GetSession(i, out var s) != 0 || s == null) continue;
                    s.GetState(out int state);
                    if (state == 2) continue;
                    s.GetProcessId(out uint pid);
                    bool system = s.IsSystemSoundsSession() == 0;
                    var vol = (ISimpleAudioVolume)s;
                    vol.GetMasterVolume(out float level);
                    vol.GetMute(out bool muted);
                    float peak = 0;
                    try
                    {
                        ((IAudioMeterInformation)s).GetPeakValue(out peak);
                    }
                    catch
                    {
                    }

                    string exe, name;
                    string? path = null;
                    if (system)
                    {
                        exe = "system";
                        name = "System sounds";
                    }
                    else
                    {
                        path = Native.ProcessPath(pid);
                        exe = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : $"pid {pid}";
                        name = exe;
                        try
                        {
                            if (path != null)
                            {
                                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                                if (!string.IsNullOrWhiteSpace(desc) && desc.Length < 32) name = desc.Trim();
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (map.TryGetValue(exe, out var existing))
                    {
                        existing.Peak = Math.Max(existing.Peak, peak);
                        continue;
                    }
                    map[exe] = new AudioApp { Pid = pid, Exe = exe, Name = name, Path = path, Volume = level, Muted = muted, Peak = peak, System = system };
                }
            }
            catch
            {
            }
            return map.Values.OrderBy(a => a.System).ThenByDescending(a => a.Peak > 0.001).ThenBy(a => a.Name).ToList();
        }

        void ForEachSession(string exe, Action<ISimpleAudioVolume> act)
        {
            var e = Sessions();
            if (e == null) return;
            e.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (e.GetSession(i, out var s) != 0 || s == null) continue;
                string name;
                if (s.IsSystemSoundsSession() == 0) name = "system";
                else
                {
                    s.GetProcessId(out uint pid);
                    var path = Native.ProcessPath(pid);
                    name = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : "";
                }
                if (!name.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
                act((ISimpleAudioVolume)s);
            }
        }

        public void SetVolume(string exe, float level)
        {
            try
            {
                var ctx = Guid.Empty;
                ForEachSession(exe, v => v.SetMasterVolume(Math.Clamp(level, 0f, 1f), ref ctx));
            }
            catch
            {
            }
        }

        public void SetMute(string exe, bool mute)
        {
            try
            {
                var ctx = Guid.Empty;
                ForEachSession(exe, v => v.SetMute(mute, ref ctx));
            }
            catch
            {
            }
        }

        public AudioProfile Capture(string name)
        {
            var p = new AudioProfile { Name = name };
            p.Output = Devices(false).FirstOrDefault(d => d.Default)?.Id ?? "";
            p.Input = Devices(true).FirstOrDefault(d => d.Default)?.Id ?? "";
            foreach (var a in Apps())
            {
                p.Volumes[a.Exe] = a.Volume;
                if (a.Muted) p.Muted.Add(a.Exe);
            }
            return p;
        }

        public void Apply(AudioProfile p)
        {
            if (!string.IsNullOrEmpty(p.Output)) SetDefault(p.Output);
            if (!string.IsNullOrEmpty(p.Input)) SetDefault(p.Input);
            foreach (var a in Apps())
            {
                if (p.Volumes.TryGetValue(a.Exe, out float v)) SetVolume(a.Exe, v);
                SetMute(a.Exe, p.Muted.Contains(a.Exe, StringComparer.OrdinalIgnoreCase));
            }
            _s.ActiveProfile = p.Name;
            _s.Touch();
        }

        public void Focus(bool on, IEnumerable<string> apps)
        {
            if (on)
            {
                _focusMuted.Clear();
                foreach (var a in Apps())
                {
                    if (!apps.Any(x => a.Exe.Contains(x, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                    _focusMuted[a.Exe] = a.Muted;
                    SetMute(a.Exe, true);
                }
            }
            else
            {
                foreach (var kv in _focusMuted) SetMute(kv.Key, kv.Value);
                _focusMuted.Clear();
            }
        }

        public bool Route(string exe, string? deviceId, out string error)
        {
            error = "";
            try
            {
                var pids = Process.GetProcessesByName(exe).Select(p =>
                {
                    uint id = (uint)p.Id;
                    p.Dispose();
                    return id;
                }).ToList();
                if (pids.Count == 0)
                {
                    error = "app isn't running";
                    return false;
                }
                bool ok = true;
                foreach (var pid in pids) ok &= AppRouting.Set(pid, deviceId);
                if (!ok) error = "windows didn't accept it";
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_enum != null && _watcher != null) _enum.UnregisterEndpointNotificationCallback(_watcher);
            }
            catch
            {
            }
        }
    }

    internal static class AppRouting
    {
        [DllImport("combase.dll", PreserveSig = false)]
        static extern void RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", PreserveSig = false)]
        static extern void WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, int length, out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = false)]
        static extern void WindowsDeleteString(IntPtr hstring);

        [ComImport, Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioPolicyConfigFactory
        {
            [PreserveSig] int GetIids(IntPtr a, IntPtr b);
            [PreserveSig] int GetRuntimeClassName(IntPtr a);
            [PreserveSig] int GetTrustLevel(IntPtr a);
            [PreserveSig] int add_CtxVolumeChange(IntPtr a, IntPtr b);
            [PreserveSig] int remove_CtxVolumeChanged(IntPtr a);
            [PreserveSig] int add_RingerVibrateStateChanged(IntPtr a, IntPtr b);
            [PreserveSig] int remove_RingerVibrateStateChange(IntPtr a);
            [PreserveSig] int SetVolumeGroupGainForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupGainForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetActiveVolumeGroupForEndpointId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupsForEndpoint(IntPtr a, IntPtr b);
            [PreserveSig] int GetCurrentVolumeContext(IntPtr a);
            [PreserveSig] int SetVolumeGroupMuteForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupMuteForId(IntPtr a, IntPtr b);
            [PreserveSig] int SetRingerVibrateState(IntPtr a);
            [PreserveSig] int GetRingerVibrateState(IntPtr a);
            [PreserveSig] int SetPreferredChatApplication(IntPtr a);
            [PreserveSig] int ResetPreferredChatApplication();
            [PreserveSig] int GetPreferredChatApplication(IntPtr a);
            [PreserveSig] int GetCurrentChatApplications(IntPtr a);
            [PreserveSig] int add_ChatContextChanged(IntPtr a, IntPtr b);
            [PreserveSig] int remove_ChatContextChanged(IntPtr a);
            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, IntPtr deviceId);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, out IntPtr deviceId);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        [ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioPolicyConfigFactory21H2
        {
            [PreserveSig] int GetIids(IntPtr a, IntPtr b);
            [PreserveSig] int GetRuntimeClassName(IntPtr a);
            [PreserveSig] int GetTrustLevel(IntPtr a);
            [PreserveSig] int add_CtxVolumeChange(IntPtr a, IntPtr b);
            [PreserveSig] int remove_CtxVolumeChanged(IntPtr a);
            [PreserveSig] int add_RingerVibrateStateChanged(IntPtr a, IntPtr b);
            [PreserveSig] int remove_RingerVibrateStateChange(IntPtr a);
            [PreserveSig] int SetVolumeGroupGainForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupGainForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetActiveVolumeGroupForEndpointId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupsForEndpoint(IntPtr a, IntPtr b);
            [PreserveSig] int GetCurrentVolumeContext(IntPtr a);
            [PreserveSig] int SetVolumeGroupMuteForId(IntPtr a, IntPtr b);
            [PreserveSig] int GetVolumeGroupMuteForId(IntPtr a, IntPtr b);
            [PreserveSig] int SetRingerVibrateState(IntPtr a);
            [PreserveSig] int GetRingerVibrateState(IntPtr a);
            [PreserveSig] int SetPreferredChatApplication(IntPtr a);
            [PreserveSig] int ResetPreferredChatApplication();
            [PreserveSig] int GetPreferredChatApplication(IntPtr a);
            [PreserveSig] int GetCurrentChatApplications(IntPtr a);
            [PreserveSig] int add_ChatContextChanged(IntPtr a, IntPtr b);
            [PreserveSig] int remove_ChatContextChanged(IntPtr a);
            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, IntPtr deviceId);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, out IntPtr deviceId);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

        public static bool Set(uint pid, string? deviceId)
        {
            IntPtr cls = IntPtr.Zero, dev = IntPtr.Zero, factory = IntPtr.Zero;
            try
            {
                const string name = "Windows.Media.Internal.AudioPolicyConfig";
                WindowsCreateString(name, name.Length, out cls);
                if (!string.IsNullOrEmpty(deviceId))
                {
                    string full = $"\\\\?\\SWD#MMDEVAPI#{deviceId}{RenderSuffix}";
                    WindowsCreateString(full, full.Length, out dev);
                }

                bool newer = Environment.OSVersion.Version.Build >= 21390;
                var iid = newer ? typeof(IAudioPolicyConfigFactory21H2).GUID : typeof(IAudioPolicyConfigFactory).GUID;
                RoGetActivationFactory(cls, ref iid, out factory);
                object obj = Marshal.GetObjectForIUnknown(factory);
                int hr = 0;
                for (int role = 0; role < 2; role++)
                {
                    hr |= newer
                        ? ((IAudioPolicyConfigFactory21H2)obj).SetPersistedDefaultAudioEndpoint(pid, 0, role, dev)
                        : ((IAudioPolicyConfigFactory)obj).SetPersistedDefaultAudioEndpoint(pid, 0, role, dev);
                }
                return hr == 0;
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("routing failed " + ex.Message);
                return false;
            }
            finally
            {
                if (factory != IntPtr.Zero) Marshal.Release(factory);
                if (cls != IntPtr.Zero) WindowsDeleteString(cls);
                if (dev != IntPtr.Zero) WindowsDeleteString(dev);
            }
        }
    }
}
