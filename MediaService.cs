using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Ostrovok.Services;

/// <summary>Любой плеер из системного медиа-центра Windows (SMTC): Spotify, Яндекс Музыка, браузеры, AIMP...</summary>
public class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager _mgr;
    GlobalSystemMediaTransportControlsSession _session;

    public string Title = "", Artist = "", Album = "", Player = "";
    public bool IsPlaying;
    public TimeSpan Position, Duration;
    public BitmapImage Cover;
    public bool HasSession => _session != null && !string.IsNullOrEmpty(Title);

    DateTime _posStamp = DateTime.UtcNow;
    public event Action Changed;
    public event Action TrackChanged;

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Attach();
            Attach();
        }
        catch (Exception ex) { AppSettings.Log("SMTC init: " + ex.Message); }
    }

    void Attach()
    {
        try
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = _mgr?.GetCurrentSession();
            if (_session == null) { Title = Artist = ""; IsPlaying = false; Changed?.Invoke(); return; }
            Player = _session.SourceAppUserModelId ?? "";
            _session.MediaPropertiesChanged += OnProps;
            _session.PlaybackInfoChanged += OnPlayback;
            _session.TimelinePropertiesChanged += OnTimeline;
            OnProps(null, null); OnPlayback(null, null); OnTimeline(null, null);
        }
        catch (Exception ex) { AppSettings.Log("SMTC attach: " + ex.Message); }
    }

    async void OnProps(GlobalSystemMediaTransportControlsSession s, object a)
    {
        try
        {
            var p = await _session.TryGetMediaPropertiesAsync();
            bool changed = p.Title != Title || p.Artist != Artist;
            Title = p.Title ?? ""; Artist = p.Artist ?? ""; Album = p.AlbumTitle ?? "";
            Cover = await LoadThumb(p.Thumbnail);
            Changed?.Invoke();
            if (changed) TrackChanged?.Invoke();
        }
        catch (Exception ex) { AppSettings.Log("SMTC props: " + ex.Message); }
    }

    void OnPlayback(GlobalSystemMediaTransportControlsSession s, object a)
    {
        try
        {
            var i = _session?.GetPlaybackInfo();
            IsPlaying = i?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Changed?.Invoke();
        }
        catch { }
    }

    void OnTimeline(GlobalSystemMediaTransportControlsSession s, object a)
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return;
            Position = t.Position; Duration = t.EndTime - t.StartTime;
            _posStamp = DateTime.UtcNow;
        }
        catch { }
    }

    /// <summary>Текущая позиция с интерполяцией (SMTC обновляет её редко).</summary>
    public TimeSpan LivePosition =>
        IsPlaying ? Position + (DateTime.UtcNow - _posStamp) : Position;

    static async Task<BitmapImage> LoadThumb(IRandomAccessStreamReference r)
    {
        if (r == null) return null;
        try
        {
            using var s = await r.OpenReadAsync();
            var bytes = new byte[s.Size];
            using var reader = new DataReader(s);
            await reader.LoadAsync((uint)s.Size);
            reader.ReadBytes(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public async void PlayPause() { try { await _session?.TryTogglePlayPauseAsync(); } catch { Native.PressKey(Native.VK_MEDIA_PLAY); } }
    public async void Next() { try { await _session?.TrySkipNextAsync(); } catch { Native.PressKey(Native.VK_MEDIA_NEXT); } }
    public async void Prev() { try { await _session?.TrySkipPreviousAsync(); } catch { Native.PressKey(Native.VK_MEDIA_PREV); } }
    public async void Seek(TimeSpan t) { try { await _session?.TryChangePlaybackPositionAsync(t.Ticks); } catch { } }
}
