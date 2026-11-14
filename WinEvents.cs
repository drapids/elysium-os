using System;
using System.Collections.Generic;

namespace Heft
{
    internal static class WinEvents
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

        static readonly Dictionary<IntPtr, int> Watched = new();
        static Native.WinEventProc? _proc;
        static IntPtr _moveHook, _fgHook, _minHook;
        static IntPtr _fg;
        static int _fgWatchers;

        public static event Action<IntPtr>? Moved;
        public static event Action<IntPtr>? Foreground;
        public static event Action<IntPtr, bool>? Minimized;

        public static IntPtr Current => _fg;

        static void Ensure()
        {
            if (_proc != null) return;
            _proc = OnEvent;
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            _moveHook = Native.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, flags);
            _fgHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, flags);
            _minHook = Native.SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _proc, 0, 0, flags);
            _fg = Native.GetForegroundWindow();
        }

        public static void Start() => Ensure();

        public static void Watch(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            Ensure();
            Watched[h] = Watched.TryGetValue(h, out int n) ? n + 1 : 1;
        }

        public static void Unwatch(IntPtr h)
        {
            if (!Watched.TryGetValue(h, out int n)) return;
            if (n <= 1) Watched.Remove(h);
            else Watched[h] = n - 1;
        }

        public static void FollowForeground(bool on)
        {
            Ensure();
            _fgWatchers = Math.Max(0, _fgWatchers + (on ? 1 : -1));
        }

        static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                switch (evt)
                {
                    case EVENT_OBJECT_LOCATIONCHANGE:
                        if (Watched.ContainsKey(hwnd) || (_fgWatchers > 0 && hwnd == _fg)) Moved?.Invoke(hwnd);
                        break;
                    case EVENT_SYSTEM_FOREGROUND:
                        _fg = hwnd;
                        Foreground?.Invoke(hwnd);
                        break;
                    case EVENT_SYSTEM_MINIMIZESTART:
                        Minimized?.Invoke(hwnd, true);
                        break;
                    case EVENT_SYSTEM_MINIMIZEEND:
                        Minimized?.Invoke(hwnd, false);
                        break;
                }
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("win event handler failed " + ex.Message);
            }
        }

        public static void Dispose()
        {
            foreach (var h in new[] { _moveHook, _fgHook, _minHook })
                if (h != IntPtr.Zero) Native.UnhookWinEvent(h);
            _moveHook = _fgHook = _minHook = IntPtr.Zero;
            _proc = null;
        }
    }
}
