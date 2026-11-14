using System;
using System.Linq;
using Elysium.Ui;

namespace Elysium
{
    public partial class App
    {
        string _lookSig = "";

        string LookSig()
        {
            return string.Join("|",
                _s.Theme, _s.AccentMode, _s.AccentHex, _s.GlassAlpha.ToString("0.###"), _s.CornerScale.ToString("0.###"), _s.UiFontName,
                _s.AnimScale.ToString("0.###"), _s.BarBlur, _s.BarTint.ToString("0.###"), _s.ThemePack,
                string.Join(",", _s.MonitorTints.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
        }

        void SyncLook()
        {
            string sig = LookSig();
            if (sig == _lookSig) return;
            bool packChanged = _lookSig.Split('|').ElementAtOrDefault(9) != _s.ThemePack;
            _lookSig = sig;
            if (packChanged || (Themes.Active?.Id ?? "") != _s.ThemePack) Themes.Use(_s);
            Sfx.Forget();
            Theme.Apply(_s.Theme);
        }
    }
}
