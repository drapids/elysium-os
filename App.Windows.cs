using System;
using System.IO;
using System.Linq;

namespace Heft
{
    public partial class App
    {
        WindowWatch? _watch;
        WindowRules? _rules;

        void InitWindows(Ui.SettingsPages pages)
        {
            var rules = new WindowRules(_s);
            _rules = rules;
            var watch = new WindowWatch();
            _watch = watch;
            watch.Opened += (h, path) =>
            {
                if (!_game) rules.Opened(h, path);
                ModeForApp(path);
                _stacks.Opened(h, path);
                _ext?.Emit("open", Path.GetFileNameWithoutExtension(path), path);
            };
            watch.Closed += Remember;
            _start.Reopen = Reopen;
            pages.CaptureRule = rules.Capture;
            pages.LastWindow = () => rules.LastExe;
        }

        void Remember(ClosedApp c)
        {
            if (_quitting) return;
            _s.RecentlyClosed.RemoveAll(x => x.Path.Equals(c.Path, StringComparison.OrdinalIgnoreCase));
            _s.RecentlyClosed.Insert(0, c);
            if (_s.RecentlyClosed.Count > 12) _s.RecentlyClosed.RemoveRange(12, _s.RecentlyClosed.Count - 12);
            _s.Touch();
        }

        void Reopen(ClosedApp c)
        {
            _s.RecentlyClosed.Remove(c);
            _s.Touch();
            if (!File.Exists(c.Path))
            {
                Flash("can't find " + c.Name + " anymore");
                return;
            }
            Files.Open(c.Path);
            Flash("reopening " + c.Name);
        }

        void ReopenLast()
        {
            var c = _s.RecentlyClosed.FirstOrDefault();
            if (c == null)
            {
                Flash("nothing closed lately");
                return;
            }
            Reopen(c);
        }

        void RememberWindow()
        {
            var r = _rules?.Capture();
            if (r == null)
            {
                Flash("click on a window first");
                return;
            }
            Flash(r.Exe + (r.Place == "max" ? " will open maximized here" : " will open right there") + " from now on");
        }
    }
}
