using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Elysium
{
    internal sealed class Pocket
    {
        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        class TaskbarList
        {
        }

        [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList
        {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        }

        const int Away = -25000;

        readonly Settings _s;
        ITaskbarList? _tb;

        public event Action? Changed;

        public Pocket(Settings s)
        {
            _s = s;
            try
            {
                _tb = (ITaskbarList)new TaskbarList();
                _tb.HrInit();
            }
            catch
            {
                _tb = null;
            }
        }

        public IReadOnlyList<PocketEntry> Items => _s.Pocketed;

        public void RestoreLeftovers()
        {
            foreach (var e in _s.Pocketed.ToList()) Release(e, false);
            _s.Pocketed.Clear();
            _s.Touch();
        }

        public bool Stash(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return false;
            string cls = Native.ClassName(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;
            if (_s.Pocketed.Any(p => p.Hwnd == hwnd.ToInt64())) return false;

            bool max = Native.IsZoomed(hwnd);
            if (max || Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
            Native.GetWindowRect(hwnd, out var r);
            _s.Pocketed.Add(new PocketEntry { Hwnd = hwnd.ToInt64(), Pid = pid, X = r.Left, Y = r.Top, Max = max, Title = Native.Title(hwnd) });
            Native.SetWindowPos(hwnd, IntPtr.Zero, Away, Away, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            try
            {
                _tb?.DeleteTab(hwnd);
            }
            catch
            {
            }
            _s.Touch();
            Changed?.Invoke();
            return true;
        }

        public void Release(PocketEntry e, bool focus = true, Native.POINT? at = null)
        {
            var h = new IntPtr(e.Hwnd);
            _s.Pocketed.Remove(e);
            _s.Touch();
            Changed?.Invoke();
            if (!Native.IsWindow(h)) return;
            int x = e.X, y = e.Y;
            if (at is Native.POINT p)
            {
                Native.GetWindowRect(h, out var r);
                x = p.X - r.Width / 2;
                y = p.Y - 20;
            }
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            try
            {
                _tb?.AddTab(h);
            }
            catch
            {
            }
            if (e.Max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
            if (focus) Native.Focus(h);
        }

        public void Prune()
        {
            int before = _s.Pocketed.Count;
            _s.Pocketed.RemoveAll(e => !Native.IsWindow(new IntPtr(e.Hwnd)));
            if (_s.Pocketed.Count == before) return;
            _s.Touch();
            Changed?.Invoke();
        }
    }

    internal sealed class Thumb : IDisposable
    {
        IntPtr _id;

        public Thumb(IntPtr dest, IntPtr src)
        {
            if (Native.DwmRegisterThumbnail(dest, src, out _id) != 0) _id = IntPtr.Zero;
        }

        public bool Valid => _id != IntPtr.Zero;

        public void Place(Native.RECT rect, byte opacity = 255)
        {
            if (_id == IntPtr.Zero) return;
            Native.SIZE size = default;
            Native.DwmQueryThumbnailSourceSize(_id, out size);
            var dest = rect;
            if (size.cx > 0 && size.cy > 0)
            {
                double k = Math.Min(rect.Width / (double)size.cx, rect.Height / (double)size.cy);
                int w = (int)(size.cx * k), h = (int)(size.cy * k);
                dest.Left = rect.Left + (rect.Width - w) / 2;
                dest.Top = rect.Top + (rect.Height - h) / 2;
                dest.Right = dest.Left + w;
                dest.Bottom = dest.Top + h;
            }
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = 0x1 | 0x4 | 0x8 | 0x10,
                rcDestination = dest,
                opacity = opacity,
                fVisible = 1,
                fSourceClientAreaOnly = 0
            };
            Native.DwmUpdateThumbnailProperties(_id, ref props);
        }

        public void Hide()
        {
            if (_id == IntPtr.Zero) return;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES { dwFlags = 0x8, fVisible = 0 };
            Native.DwmUpdateThumbnailProperties(_id, ref props);
        }

        public void Dispose()
        {
            if (_id != IntPtr.Zero) Native.DwmUnregisterThumbnail(_id);
            _id = IntPtr.Zero;
        }
    }
}
