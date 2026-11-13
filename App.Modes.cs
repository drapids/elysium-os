using System;
using System.IO;
using System.Linq;
using Heft.Ui;

namespace Heft
{
    public partial class App
    {
        string _modeMinute = "";
        string _modeSig = "";

        void ApplyMode(Mode m, bool quiet = false)
        {
            if (m.Theme.Length > 0) _s.Theme = m.Theme;
            if (m.Accent.Length > 0)
            {
                if (m.Accent is "system" or "wallpaper" or "default") _s.AccentMode = m.Accent;
                else
                {
                    _s.AccentMode = "custom";
                    _s.AccentHex = m.Accent;
                }
            }
            if (m.Focus == "on") _s.FocusActive = true;
            else if (m.Focus == "off") _s.FocusActive = false;
            if (m.Sounds == "on") _s.SoundsOn = true;
            else if (m.Sounds == "off") _s.SoundsOn = false;
            if (m.Audio.Length > 0)
            {
                var prof = _s.AudioProfiles.FirstOrDefault(p => p.Name == m.Audio);
                if (prof != null) _audio.Apply(prof);
            }
            _s.ActiveMode = m.Name;
            Theme.Apply(_s.Theme);
            _s.Touch();
            if (!quiet) Flash(m.Name.Length > 0 ? m.Name.ToLowerInvariant() + " mode" : "mode on");
            _ext?.Emit("mode", m.Name);
        }

        void ModeTick()
        {
            if (_s.Modes.Count == 0) return;
            var due = Modes.Due(_s, DateTime.Now, ref _modeMinute);
            if (due != null && due.Name != _s.ActiveMode) ApplyMode(due);
        }

        void ModeForApp(string path)
        {
            if (_s.Modes.Count == 0) return;
            string exe = Path.GetFileNameWithoutExtension(path);
            var m = _s.Modes.FirstOrDefault(x => x.App.Length > 0 && x.App.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Equals(exe, StringComparison.OrdinalIgnoreCase));
            if (m != null && m.Name != _s.ActiveMode) ApplyMode(m);
        }

        void RegisterModes()
        {
            string sig = string.Join("|", _s.Modes.Select(m => m.Id + "=" + m.Name));
            if (sig == _modeSig) return;
            foreach (var id in _modeSig.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=')[0]))
                _cmds.Remove("mode-" + id);
            _modeSig = sig;
            foreach (var m in _s.Modes)
            {
                var mode = m;
                if (mode.Name.Length == 0) continue;
                _cmds.Add("heft", "mode-" + mode.Id, "Mode: " + mode.Name, "\uE771", () => ApplyMode(mode), "mode profile switch " + mode.Name, false, () => _s.ActiveMode == mode.Name ? "on" : null);
            }
        }

        Mode SnapshotMode(string name)
        {
            return new Mode
            {
                Name = name,
                Theme = _s.Theme,
                Accent = _s.AccentMode == "custom" ? _s.AccentHex : _s.AccentMode,
                Focus = _s.FocusActive ? "on" : "off",
                Audio = _s.ActiveProfile,
                Sounds = _s.SoundsOn ? "on" : "off"
            };
        }
    }
}
