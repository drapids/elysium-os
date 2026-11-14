using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Heft
{
    internal sealed partial class AudioFlow
    {
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static readonly string[] HeadphoneWords = { "headphone", "headset", "earphone", "earbud", "buds", "airpods", "beats", "hyperx", "arctis", "kraken", "cloud", "blackshark", "wh-", "wf-", "bose", "jabra", "galaxy buds", "pixel buds" };

        public static bool LooksLikeHeadphones(string name)
        {
            string n = name.ToLowerInvariant();
            return HeadphoneWords.Any(w => n.Contains(w));
        }

        List<IAudioEndpointVolume> Mics()
        {
            var list = new List<IAudioEndpointVolume>();
            if (_enum == null) return list;
            try
            {
                if (_enum.EnumAudioEndpoints(1, 1, out var col) != 0) return list;
                col.GetCount(out uint count);
                var iid = typeof(IAudioEndpointVolume).GUID;
                for (uint i = 0; i < count; i++)
                {
                    if (col.Item(i, out var d) != 0 || d == null) continue;
                    if (d.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0 || obj is not IAudioEndpointVolume vol) continue;
                    list.Add(vol);
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("mic list failed " + ex.Message);
            }
            return list;
        }

        public bool HasMic => Mics().Count > 0;

        public bool MicMuted()
        {
            var mics = Mics();
            if (mics.Count == 0) return false;
            foreach (var m in mics)
            {
                try
                {
                    if (m.GetMute(out bool muted) == 0 && !muted) return false;
                }
                catch
                {
                }
            }
            return true;
        }

        public void SetMicMute(bool mute)
        {
            var ctx = Guid.Empty;
            foreach (var m in Mics())
            {
                try
                {
                    m.SetMute(mute, ref ctx);
                }
                catch
                {
                }
            }
        }

        public AudioDevice? DefaultOutput() => Devices(false).FirstOrDefault(d => d.Default);
    }
}
