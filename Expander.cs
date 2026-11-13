using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Heft
{
    public sealed class Snippet
    {
        public string Trigger { get; set; } = "";
        public string Text { get; set; } = "";
    }

    internal static class Expander
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int cap, uint flags, IntPtr hkl);

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint thread);

        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public static string CharFor(uint vk, uint scan)
        {
            try
            {
                var state = new byte[256];
                if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80;
                if ((Native.GetAsyncKeyState(0xA0) & 0x8000) != 0) state[0xA0] = 0x80;
                if ((Native.GetAsyncKeyState(0xA1) & 0x8000) != 0) state[0xA1] = 0x80;
                uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
                var hkl = GetKeyboardLayout(thread);
                var sb = new StringBuilder(8);
                int n = ToUnicodeEx(vk, scan, state, sb, sb.Capacity, 4, hkl);
                return n == 1 ? sb.ToString(0, 1) : "";
            }
            catch
            {
                return "";
            }
        }

        public static string Fill(string text)
        {
            var now = DateTime.Now;
            string clip = "";
            if (text.Contains("{clipboard}", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Clipboard.ContainsText()) clip = Clipboard.GetText();
                }
                catch
                {
                }
            }
            return text
                .Replace("{date}", now.ToString("MMMM d, yyyy"), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", now.ToString("h:mm tt"), StringComparison.OrdinalIgnoreCase)
                .Replace("{day}", now.ToString("dddd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{iso}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{clipboard}", clip, StringComparison.OrdinalIgnoreCase);
        }

        static INPUT Key(ushort vk, ushort scan, uint flags) => new()
        {
            type = 1,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
        };

        public static void Type(int backspaces, string text)
        {
            var list = new List<INPUT>();
            for (int i = 0; i < backspaces; i++)
            {
                list.Add(Key(0x08, 0, 0));
                list.Add(Key(0x08, 0, KEYEVENTF_KEYUP));
            }
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n')
                {
                    list.Add(Key(0x0D, 0, 0));
                    list.Add(Key(0x0D, 0, KEYEVENTF_KEYUP));
                    continue;
                }
                if (c == '\t')
                {
                    list.Add(Key(0x09, 0, 0));
                    list.Add(Key(0x09, 0, KEYEVENTF_KEYUP));
                    continue;
                }
                list.Add(Key(0, c, KEYEVENTF_UNICODE));
                list.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            if (list.Count == 0) return;
            var arr = list.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        }

        public static void RightClick()
        {
            var arr = new[]
            {
                new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTDOWN } } },
                new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTUP } } }
            };
            SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        }
    }
}
