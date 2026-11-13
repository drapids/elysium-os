using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Heft
{
    public sealed class ClosedApp
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime When { get; set; } = DateTime.Now;
    }

    internal sealed class WindowWatch : IDisposable
    {
        const uint EVENT_OBJECT_DESTROY = 0x8001;
        const uint EVENT_OBJECT_SHOW = 0x8002;

        static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "heft", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
            "LockApp", "ApplicationFrameHost", "SystemSettings", "rundll32", "dllhost", "conhost", "taskmgr", "SnippingTool", "ScreenClippingHost"
        };

        readonly Native.WinEventProc _proc;
        readonly IntPtr _hook;
        readonly Dictionary<IntPtr, (string path, string title)> _known = new();
        readonly HashSet<IntPtr> _pending = new();
        readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(320) };
        readonly uint _pid = (uint)Environment.ProcessId;

        public event Action<IntPtr, string>? Opened;
        public event Action<ClosedApp>? Closed;

        public WindowWatch()
        {
            _proc = OnEvent;
            _hook = Native.SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            _settle.Tick += (_, _) => Settle();
            Native.EnumWindows((h, _) =>
            {
                if (IsApp(h, out string path)) _known[h] = (path, Native.Title(h));
                return true;
            }, IntPtr.Zero);
        }

        bool IsApp(IntPtr h, out string path)
        {
            path = "";
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h)) return false;
            if (Native.GetAncestor(h, Native.GA_ROOT) != h) return false;
            if (Native.GetWindow(h, 4) != IntPtr.Zero) return false;
            long style = Native.Style(h);
            if ((style & Native.WS_CAPTION) != Native.WS_CAPTION) return false;
            if ((Native.ExStyle(h) & Native.WS_EX_TOOLWINDOW) != 0) return false;
            if (Native.IsCloaked(h)) return false;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == _pid) return false;
            var p = Native.ProcessPath(pid);
            if (string.IsNullOrEmpty(p)) return false;
            if (Skip.Contains(System.IO.Path.GetFileNameWithoutExtension(p))) return false;
            path = p;
            return true;
        }

        void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
            if (evt == EVENT_OBJECT_SHOW)
            {
                if (_known.ContainsKey(hwnd)) return;
                if (Native.GetAncestor(hwnd, Native.GA_ROOT) != hwnd) return;
                _pending.Add(hwnd);
                if (!_settle.IsEnabled) _settle.Start();
            }
            else if (evt == EVENT_OBJECT_DESTROY)
            {
                _pending.Remove(hwnd);
                if (!_known.Remove(hwnd, out var info)) return;
                bool others = _known.Values.Any(v => v.path.Equals(info.path, StringComparison.OrdinalIgnoreCase));
                if (others) return;
                Closed?.Invoke(new ClosedApp
                {
                    Path = info.path,
                    Name = FileName(info.path),
                    Title = info.title,
                    When = DateTime.Now
                });
            }
        }

        static string FileName(string path)
        {
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(v.FileDescription)) return v.FileDescription!.Trim();
            }
            catch
            {
            }
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        void Settle()
        {
            _settle.Stop();
            var list = _pending.ToList();
            _pending.Clear();
            foreach (var h in list)
            {
                if (_known.ContainsKey(h)) continue;
                if (!IsApp(h, out string path)) continue;
                _known[h] = (path, Native.Title(h));
                try
                {
                    Opened?.Invoke(h, path);
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("window opened handler " + ex.Message);
                }
            }
            if (_known.Count > 400)
                foreach (var dead in _known.Keys.Where(k => !Native.IsWindow(k)).ToList()) _known.Remove(dead);
        }

        public void Retitle(IntPtr h)
        {
            if (_known.TryGetValue(h, out var info)) _known[h] = (info.path, Native.Title(h));
        }

        public void Dispose()
        {
            _settle.Stop();
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        }
    }
}
