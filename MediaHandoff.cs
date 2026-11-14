using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Elysium
{
    internal sealed class MediaHandoff
    {
        static readonly string[] MusicApps = { "spotify", "applemusic", "apple music", "itunes", "zunemusic", "music.ui", "tidal", "deezer", "amazonmusic", "amazon music", "foobar", "musicbee", "aimp", "winamp", "youtube music", "soundcloud", "qobuz", "pandora" };

        GlobalSystemMediaTransportControlsSessionManager? _m;
        bool _busy, _starting;
        string _paused = "";
        long _quietSince;
        bool _userWon;

        static bool IsMusic(string id)
        {
            string low = id.ToLowerInvariant();
            return MusicApps.Any(m => low.Contains(m));
        }

        static bool Playing(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                return false;
            }
        }

        public async Task Tick(bool on)
        {
            if (!on)
            {
                _paused = "";
                _userWon = false;
                return;
            }
            if (_busy) return;
            _busy = true;
            try
            {
                if (_m == null)
                {
                    if (_starting) return;
                    _starting = true;
                    _m = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _starting = false;
                }
                var sessions = _m.GetSessions().ToList();
                var music = sessions.Where(s => IsMusic(s.SourceAppUserModelId ?? "")).ToList();
                var others = sessions.Where(s => !IsMusic(s.SourceAppUserModelId ?? "")).ToList();
                bool otherPlaying = others.Any(Playing);
                long now = Environment.TickCount64;
                if (otherPlaying)
                {
                    _quietSince = 0;
                    var song = music.FirstOrDefault(Playing);
                    if (song == null) return;
                    if (_paused.Length > 0)
                    {
                        _userWon = true;
                        _paused = "";
                        return;
                    }
                    if (_userWon) return;
                    _paused = song.SourceAppUserModelId ?? "";
                    await song.TryPauseAsync();
                    PhysicsEngine.Log("handoff paused " + _paused);
                    return;
                }
                _userWon = false;
                if (_paused.Length == 0) return;
                if (_quietSince == 0)
                {
                    _quietSince = now;
                    return;
                }
                if (now - _quietSince < 2500) return;
                var back = music.FirstOrDefault(s => (s.SourceAppUserModelId ?? "") == _paused);
                _paused = "";
                _quietSince = 0;
                if (back != null && !Playing(back)) await back.TryPlayAsync();
            }
            catch (Exception ex)
            {
                _starting = false;
                PhysicsEngine.Log("handoff failed " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
