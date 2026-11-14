using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Elysium
{
    internal sealed class NowPlaying
    {
        GlobalSystemMediaTransportControlsSessionManager? _manager;
        GlobalSystemMediaTransportControlsSession? _session;
        bool _starting;
        string _artKey = "";

        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Source { get; private set; } = "";
        public bool Playing { get; private set; }
        public bool HasSession { get; private set; }
        public BitmapSource? Art { get; private set; }
        public double Position { get; private set; }
        public double Duration { get; private set; }

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

                _session = _manager.GetCurrentSession();
                if (_session == null)
                {
                    HasSession = false;
                    Playing = false;
                    Title = "";
                    Artist = "";
                    Art = null;
                    return;
                }

                var props = await _session.TryGetMediaPropertiesAsync();
                var info = _session.GetPlaybackInfo();
                HasSession = true;
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                Source = _session.SourceAppUserModelId ?? "";
                Playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                try
                {
                    var tl = _session.GetTimelineProperties();
                    Duration = (tl.EndTime - tl.StartTime).TotalSeconds;
                    double pos = tl.Position.TotalSeconds;
                    if (Playing && tl.LastUpdatedTime.Year > 2000) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                    Position = Math.Clamp(pos, 0, Math.Max(Duration, 0));
                }
                catch
                {
                    Duration = 0;
                    Position = 0;
                }

                string key = Title + "|" + Artist;
                if (key != _artKey)
                {
                    _artKey = key;
                    Art = null;
                    if (props?.Thumbnail != null)
                    {
                        try
                        {
                            using var stream = await props.Thumbnail.OpenReadAsync();
                            using var s = stream.AsStream();
                            var ms = new MemoryStream();
                            await s.CopyToAsync(ms);
                            ms.Position = 0;
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = 96;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            Art = bmp;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
                _starting = false;
                HasSession = false;
            }
        }

        public async Task Toggle()
        {
            try
            {
                if (_session != null) await _session.TryTogglePlayPauseAsync();
                else Native.Tap(Native.VK_MEDIA_PLAY_PAUSE);
            }
            catch
            {
            }
        }

        public async Task Next()
        {
            try
            {
                if (_session != null) await _session.TrySkipNextAsync();
                else Native.Tap(Native.VK_MEDIA_NEXT);
            }
            catch
            {
            }
        }

        public async Task Previous()
        {
            try
            {
                if (_session != null) await _session.TrySkipPreviousAsync();
                else Native.Tap(Native.VK_MEDIA_PREV);
            }
            catch
            {
            }
        }
    }
}
