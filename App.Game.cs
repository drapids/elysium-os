using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Elysium
{
    public partial class App
    {
        bool _game;
        readonly HashSet<IntPtr> _gameWins = new();

        static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "zen", "librewolf", "vlc", "mpv", "mpc-hc64", "mpc-be64", "potplayermini64", "wmplayer", "Microsoft.Media.Player", "powerpnt", "obs64", "discord", "spotify", "code", "devenv", "notepad", "winword", "excel", "photoshop", "afterfx", "premiere", "resolve", "TouchDesigner"
        };
        int _gameVotes;
        int _gameHeld;
        string _gameApp = "";

        public bool Gaming => _game;

        static bool Ours(IntPtr hwnd)
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        static string ExeOf(IntPtr hwnd)
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch
            {
                return "";
            }
        }

        void CheckGame()
        {
            if (!_s.GameMode)
            {
                if (_game) LeaveGame();
                _gameVotes = 0;
                return;
            }
            var fg = Native.GetForegroundWindow();
            bool full = fg != IntPtr.Zero && !Ours(fg) && !Native.IsZoomed(fg) && CursorLock.IsFullscreen(fg, out _);
            if (full && !_gameWins.Contains(fg))
            {
                string exe = ExeOf(fg);
                bool skip = exe.Equals("explorer", StringComparison.OrdinalIgnoreCase) || NotGames.Contains(exe) || _s.GameSkip.Exists(x => x.Equals(exe, StringComparison.OrdinalIgnoreCase));
                if (!skip && (EdgeGuard.IsGameWindow(fg) || EdgeGuard.CursorHidden() || EdgeGuard.ForeignClip()))
                {
                    if (_gameWins.Count > 40) _gameWins.RemoveWhere(h => !Native.IsWindow(h));
                    _gameWins.Add(fg);
                }
                else full = false;
            }
            if (full == _game)
            {
                _gameVotes = 0;
                return;
            }
            if (++_gameVotes < (full ? 3 : 1)) return;
            _gameVotes = 0;
            if (full) EnterGame(fg);
            else LeaveGame();
        }

        void EnterGame(IntPtr hwnd)
        {
            _game = true;
            _gameHeld = 0;
            _gameApp = ExeOf(hwnd);
            Sfx.Quiet = true;
            _mouse.Quiet = true;
            _spot.Off();
            SyncFeatures();
            _activity.Slow = true;
            _start.Hold(true);
            _state.Interval = TimeSpan.FromMilliseconds(2000);
            PhysicsEngine.Log("game mode on " + _gameApp);
            _ext?.Emit("game", true, _gameApp);
        }

        void LeaveGame()
        {
            if (!_game) return;
            _game = false;
            Sfx.Quiet = false;
            _mouse.Quiet = false;
            SyncFeatures();
            _activity.Slow = false;
            _start.Hold(false);
            _state.Interval = TimeSpan.FromMilliseconds(500);
            _ext?.Emit("game", false, _gameApp);
            if (_gameHeld > 0)
            {
                int n = _gameHeld;
                _gameHeld = 0;
                _toasts.Show(new Note
                {
                    Title = "while you were playing",
                    Body = n == 1 ? "1 notification came in" : n + " notifications came in",
                    Glyph = "\uE7FC"
                });
            }
            _gameApp = "";
            Tick();
        }

        static readonly string[] UrgentApps = { "defender", "security", "securityhealth", "malwarebytes", "norton", "avast", "avg", "bitdefender", "kaspersky", "mcafee", "eset", "sophos", "webroot", "windows update" };
        static readonly string[] UrgentWords = { "threat", "malware", "virus", "ransomware", "trojan", "quarantin", "blocked a", "battery is low", "low battery", "critical battery", "incoming call", "is calling", "alarm", "unusual sign-in", "sign-in attempt", "security alert", "password changed" };

        static bool Urgent(Note n)
        {
            string app = (n.App + " " + n.AppId).ToLowerInvariant();
            if (UrgentApps.Any(a => app.Contains(a))) return true;
            string text = (n.Title + " " + n.Body).ToLowerInvariant();
            return UrgentWords.Any(w => text.Contains(w));
        }

        bool HoldToast(Note n)
        {
            if (!_game || !_s.GameHoldToasts) return false;
            if (Urgent(n)) return false;
            _gameHeld++;
            return true;
        }
    }
}
