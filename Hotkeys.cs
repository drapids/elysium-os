using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace Heft
{
    internal sealed class Hotkeys : IDisposable
    {
        public static readonly (string id, string label, string fallback)[] Defaults =
        {
            ("launcher", "Launcher", "Alt+Space"),
            ("dock", "Dock", "Ctrl+Alt+Space"),
            ("settings", "Settings", "Ctrl+Alt+H"),
            ("shelf", "Shelf", "Ctrl+Alt+S"),
            ("audio", "AudioFlow", "Ctrl+Alt+A"),
            ("notes", "Notifications", "Ctrl+Alt+N"),
            ("center", "Control center", "Ctrl+Alt+K"),
            ("lock", "Cursor lock", "Ctrl+Alt+L"),
            ("focus", "Focus mode", "Ctrl+Alt+F"),
            ("pocket", "Pocket window", "Ctrl+Alt+P"),
            ("stack", "Paste stack on/off", "Ctrl+Alt+C"),
            ("paste", "Paste next from stack", "Ctrl+Alt+V"),
            ("inspect", "Inspect Explorer selection", "Ctrl+Alt+I"),
            ("device", "Next audio output", "Ctrl+Alt+D"),
            ("peek", "Peek desktop icons", "Ctrl+Alt+E"),
            ("overview", "Overview (all windows)", "Ctrl+Alt+W"),
            ("spaces", "Spaces (zoomable desktop)", "Ctrl+Alt+G"),
            ("pip", "Picture in picture", "Ctrl+Alt+O"),
            ("share", "Send Explorer selection to phone", "Ctrl+Alt+U"),
            ("stacknext", "Next tab in a window stack", ""),
            ("sheet", "Shortcut sheet", "Ctrl+Alt+Y"),
            ("capture", "Screenshot, text, colors", "Ctrl+Alt+X"),
            ("pickcolor", "Pick a color", ""),
            ("reopen", "Reopen last closed app", "Ctrl+Alt+Z"),
            ("remember", "Remember where this window goes", ""),
            ("scratch", "Scratchpad", "Ctrl+Alt+Q"),
            ("lastdl", "Grab your last download", "Ctrl+Alt+J"),
            ("phonedrop", "Send files from your phone", ""),
            ("start", "Start", "")
        };

        readonly HwndSource _source;
        readonly Dictionary<int, Action> _actions = new();
        readonly HashSet<(uint, uint)> _mine = new();
        int _next = 100;

        public List<string> Failed { get; } = new();

        public Hotkeys()
        {
            var p = new HwndSourceParameters("heft-keys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            _source = new HwndSource(p);
            _source.AddHook(Hook);
        }

        public static string Spec(Settings s, string id)
        {
            if (s.Keys.TryGetValue(id, out var v)) return v;
            foreach (var d in Defaults)
                if (d.id == id) return d.fallback;
            return "";
        }

        public void Clear()
        {
            foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
            _mine.Clear();
            Failed.Clear();
        }

        public bool Add(string spec, Action action, string label)
        {
            if (string.IsNullOrWhiteSpace(spec)) return false;
            if (!TryParse(spec, out uint mods, out uint vk))
            {
                Failed.Add($"{label}: can't read \"{spec}\"");
                return false;
            }
            int id = _next++;
            if (!Native.RegisterHotKey(_source.Handle, id, mods | Native.MOD_NOREPEAT, vk))
            {
                Failed.Add($"{label}: {spec} is taken by another app");
                return false;
            }
            _actions[id] = action;
            _mine.Add((mods, vk));
            return true;
        }

        public bool Free(string spec)
        {
            if (!TryParse(spec, out uint mods, out uint vk)) return false;
            if (_mine.Contains((mods, vk))) return true;
            const int probe = 0xBFF0;
            if (!Native.RegisterHotKey(_source.Handle, probe, mods | Native.MOD_NOREPEAT, vk)) return false;
            Native.UnregisterHotKey(_source.Handle, probe);
            return true;
        }

        public static bool TryParse(string spec, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string part = raw.ToLowerInvariant();
                switch (part)
                {
                    case "ctrl":
                    case "control":
                        mods |= Native.MOD_CONTROL;
                        continue;
                    case "alt":
                        mods |= Native.MOD_ALT;
                        continue;
                    case "shift":
                        mods |= Native.MOD_SHIFT;
                        continue;
                    case "win":
                    case "windows":
                        mods |= 0x0008;
                        continue;
                }

                string name = raw;
                if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
                if (name.Equals("esc", StringComparison.OrdinalIgnoreCase)) name = "Escape";
                if (name.Equals("enter", StringComparison.OrdinalIgnoreCase)) name = "Return";
                if (name == "`" || name == "~") name = "OemTilde";
                if (!Enum.TryParse<Key>(name, true, out var key)) return false;
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            return vk != 0;
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
            if (_actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                handled = true;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("hotkey error " + ex.Message);
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Clear();
            _source.Dispose();
        }
    }
}
