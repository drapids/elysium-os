using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Heft
{
    internal sealed class NowPlaying
    {
        GlobalSystemMediaTransportControlsSessionManager? _manager;
        bool _starting;

        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public bool Playing { get; private set; }
        public bool HasSession { get; private set; }

        public async Task Refresh()
        {
            try
            {
                if (_manager == null)
                {
                    if (_starting) return;
                    _starting = true;
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _starting = false;
                }

                var session = _manager.GetCurrentSession();
                if (session == null)
                {
                    HasSession = false;
                    Playing = false;
                    Title = "";
                    Artist = "";
                    return;
                }

                var props = await session.TryGetMediaPropertiesAsync();
                var info = session.GetPlaybackInfo();
                HasSession = true;
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                Playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                _starting = false;
                HasSession = false;
            }
        }
    }
}
