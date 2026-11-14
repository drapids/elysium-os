using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Elysium
{
    internal static class Sfx
    {
        public static readonly (string id, string label)[] Events =
        {
            ("open", "Panels opening"),
            ("close", "Panels closing"),
            ("drop", "Dropping files"),
            ("notify", "Notifications"),
            ("chime", "Reminders and timers"),
            ("throw", "Throwing a window"),
            ("stack", "Stacking windows"),
            ("tick", "Little clicks (tabs, corners, switching)")
        };

        public static readonly (string id, string label)[] Packs =
        {
            ("glass", "Glass"),
            ("soft", "Soft"),
            ("retro", "Retro")
        };

        [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
        static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

        const uint SND_ASYNC = 0x0001;
        const uint SND_NODEFAULT = 0x0002;
        const uint SND_MEMORY = 0x0004;

        static Settings? _s;
        public static volatile bool Quiet;
        static Func<bool>? _music;
        static readonly Dictionary<string, byte[]?> Raw = new(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new();
        static GCHandle _pinned;
        static string _lastId = "";
        static long _lastAt;

        public static void Init(Settings s, Func<bool> musicPlaying)
        {
            _s = s;
            _music = musicPlaying;
        }

        public static bool Enabled(string id)
        {
            if (_s == null || !_s.SoundsOn) return false;
            return !_s.SoundEvents.TryGetValue(id, out bool on) || on;
        }

        public static void Play(string id) => Play(id, false);

        public static void Test(string id) => Play(id, true);

        static void Play(string id, bool force)
        {
            var s = _s;
            if (s == null) return;
            if (!force && (Quiet || !Enabled(id))) return;
            try
            {
                lock (Gate)
                {
                    long now = Environment.TickCount64;
                    if (id == _lastId && now - _lastAt < (force ? 90 : 70)) return;
                    _lastId = id;
                    _lastAt = now;

                    var wav = Load(s.SoundPack, id);
                    if (wav == null) return;
                    double vol = Math.Clamp(s.SoundVolume, 0, 1);
                    double gain = vol * vol;
                    bool duck = false;
                    try
                    {
                        duck = s.SoundDuck && _music != null && _music();
                    }
                    catch
                    {
                    }
                    if (duck) gain *= 0.4;
                    if (gain <= 0.0005) return;

                    var buf = Scale(wav, gain);
                    var next = GCHandle.Alloc(buf, GCHandleType.Pinned);
                    PlaySound(next.AddrOfPinnedObject(), IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
                    if (_pinned.IsAllocated) _pinned.Free();
                    _pinned = next;
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("sound failed " + ex.Message);
            }
        }

        public static void Blip(bool up)
        {
            var s = _s;
            if (s == null || !s.MicSounds) return;
            try
            {
                double vol = Math.Clamp(Math.Max(s.SoundVolume, 0.45), 0, 1);
                var wav = Tone(up ? new[] { 660.0, 990.0 } : new[] { 880.0, 520.0 }, 55, vol * vol * 0.55);
                lock (Gate)
                {
                    var next = GCHandle.Alloc(wav, GCHandleType.Pinned);
                    PlaySound(next.AddrOfPinnedObject(), IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
                    if (_pinned.IsAllocated) _pinned.Free();
                    _pinned = next;
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("blip failed " + ex.Message);
            }
        }

        static byte[] Tone(double[] freqs, int msEach, double gain)
        {
            const int rate = 44100;
            int seg = rate * msEach / 1000;
            int total = seg * freqs.Length;
            int fade = rate * 6 / 1000;
            var pcm = new byte[total * 2];
            double phase = 0;
            for (int i = 0; i < total; i++)
            {
                double f = freqs[Math.Min(freqs.Length - 1, i / seg)];
                phase += 2 * Math.PI * f / rate;
                int inSeg = i % seg;
                double env = Math.Min(1, Math.Min(inSeg / (double)fade, (seg - inSeg) / (double)fade));
                short v = (short)Math.Round(Math.Sin(phase) * env * gain * 32767);
                pcm[i * 2] = (byte)(v & 0xFF);
                pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + pcm.Length);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(pcm.Length);
            w.Write(pcm);
            w.Flush();
            return ms.ToArray();
        }

        public static void Stop()
        {
            lock (Gate)
            {
                try
                {
                    PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
                }
                catch
                {
                }
                if (_pinned.IsAllocated) _pinned.Free();
            }
        }

        public static readonly string CustomRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "elysium", "sounds");

        public static List<(string id, string label)> AllPacks()
        {
            var list = Packs.ToList();
            try
            {
                if (Directory.Exists(CustomRoot))
                    foreach (var d in Directory.EnumerateDirectories(CustomRoot))
                    {
                        string name = Path.GetFileName(d);
                        if (!list.Any(p => p.id.Equals(name, StringComparison.OrdinalIgnoreCase))) list.Add((name, name));
                    }
            }
            catch
            {
            }
            return list;
        }

        public static void Forget()
        {
            lock (Gate) Raw.Clear();
        }

        static byte[]? Load(string pack, string id)
        {
            string key = pack + "-" + id;
            if (Raw.TryGetValue(key, out var cached)) return cached;
            byte[]? data = null;
            try
            {
                string custom = Path.Combine(CustomRoot, pack, id + ".wav");
                if (File.Exists(custom))
                {
                    data = File.ReadAllBytes(custom);
                    Raw[key] = data;
                    return data;
                }
                if (Directory.Exists(Path.Combine(CustomRoot, pack)) && !Packs.Any(p => p.id == pack))
                {
                    Raw[key] = null;
                    return Load("glass", id);
                }
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("elysium.sound." + key);
                if (stream == null && pack != "glass")
                {
                    Raw[key] = null;
                    return Load("glass", id);
                }
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }
            }
            catch
            {
                data = null;
            }
            Raw[key] = data;
            return data;
        }

        static byte[] Scale(byte[] wav, double gain)
        {
            var copy = (byte[])wav.Clone();
            int pos = 12;
            while (pos + 8 <= copy.Length)
            {
                int size = BitConverter.ToInt32(copy, pos + 4);
                bool isData = copy[pos] == (byte)'d' && copy[pos + 1] == (byte)'a' && copy[pos + 2] == (byte)'t' && copy[pos + 3] == (byte)'a';
                if (isData)
                {
                    int start = pos + 8;
                    int end = Math.Min(copy.Length, start + Math.Max(0, size));
                    for (int i = start; i + 1 < end; i += 2)
                    {
                        int v = (short)(copy[i] | (copy[i + 1] << 8));
                        int scaled = (int)Math.Round(v * gain);
                        if (scaled > short.MaxValue) scaled = short.MaxValue;
                        if (scaled < short.MinValue) scaled = short.MinValue;
                        copy[i] = (byte)(scaled & 0xFF);
                        copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
                    }
                    break;
                }
                if (size < 0) break;
                pos += 8 + size + (size & 1);
            }
            return copy;
        }
    }
}
