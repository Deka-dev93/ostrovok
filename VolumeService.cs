using NAudio.CoreAudioApi;

namespace Ostrovok.Services;

/// <summary>Громкость системы (устройство вывода по умолчанию).</summary>
public class VolumeService
{
    MMDeviceEnumerator _en;
    MMDevice _dev;

    MMDevice Dev()
    {
        try
        {
            _en ??= new MMDeviceEnumerator();
            var d = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_dev == null || _dev.ID != d.ID) { _dev?.Dispose(); _dev = d; }
            else d.Dispose();
            return _dev;
        }
        catch { return null; }
    }

    public double Level
    {
        get { try { return Dev()?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; } catch { return 0; } }
        set { try { var d = Dev(); if (d != null) d.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1); } catch { } }
    }

    public bool Muted
    {
        get { try { return Dev()?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
        set { try { var d = Dev(); if (d != null) d.AudioEndpointVolume.Mute = value; } catch { } }
    }

    public string Name
    {
        get
        {
            try
            {
                var n = Dev()?.FriendlyName ?? "Нет устройства";
                int i = n.IndexOf('(');
                return (i > 0 ? n[..i] : n).Trim();
            }
            catch { return "—"; }
        }
    }
}
