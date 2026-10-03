using System.Runtime.InteropServices;
using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

/// <summary>Default output device: master volume, mute and the current peak level (a live "is sound playing" meter).</summary>
public sealed class AudioMonitor : MonitorBase
{
    public override string Name => "Audio";
    public override string[] Prefixes => new[] { "audio." };
    public override int GetIntervalMs(MonitorContext ctx) => 500;

    private CoreAudio.IMMDeviceEnumerator? _enum;
    private CoreAudio.IMMDevice? _device;
    private CoreAudio.IAudioEndpointVolume? _volume;
    private CoreAudio.IAudioMeterInformation? _meter;
    private string? _deviceId;
    private int _sinceCheck;

    public override void Initialize(MonitorContext ctx)
    {
        if (!ctx.Settings.Media.Enabled) throw new MonitorUnavailableException("Media tracking is off (Settings → General)");
        _enum = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
        Acquire(ctx.Store);
        Detail = "Core Audio";
    }

    private void Acquire(MetricStore st)
    {
        ReleaseDevice();
        if (_enum == null) return;
        if (_enum.GetDefaultAudioEndpoint(CoreAudio.eRender, CoreAudio.eMultimedia, out var dev) != 0 || dev == null)
        {
            st.SetText("audio.device", "No output device");
            throw new MonitorUnavailableException("No audio output device");
        }
        _device = dev;
        dev.GetId(out _deviceId);
        var iid = CoreAudio.IID_IAudioEndpointVolume;
        if (dev.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out var vol) == 0) _volume = vol as CoreAudio.IAudioEndpointVolume;
        iid = CoreAudio.IID_IAudioMeterInformation;
        if (dev.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out var meter) == 0) _meter = meter as CoreAudio.IAudioMeterInformation;
        st.SetText("audio.device", CoreAudio.FriendlyName(dev) ?? "Speakers");
    }

    public override void Update(MonitorContext ctx)
    {
        var st = ctx.Store;
        if (!ctx.Settings.Media.Enabled) throw new MonitorUnavailableException("Media tracking is off (Settings → General)");

        // Follow default-device switches (headphones plugged in, Bluetooth connected…).
        if (++_sinceCheck >= 6 && _enum != null)
        {
            _sinceCheck = 0;
            if (_enum.GetDefaultAudioEndpoint(CoreAudio.eRender, CoreAudio.eMultimedia, out var cur) == 0 && cur != null)
            {
                cur.GetId(out var id);
                Marshal.ReleaseComObject(cur);
                if (id != _deviceId) Acquire(st);
            }
        }

        if (_volume != null && _volume.GetMasterVolumeLevelScalar(out var level) == 0)
        {
            st.Set("audio.volume", Math.Round(level * 100));
            st.SetText("audio.muted", _volume.GetMute(out var muted) == 0 && muted ? "Muted" : "On");
        }
        else st.SetUnavailable("audio.volume", "Volume not readable");

        if (_meter != null && _meter.GetPeakValue(out var peak) == 0) st.Set("audio.peak", peak * 100);
        else st.SetUnavailable("audio.peak", "Level not readable");
    }

    private void ReleaseDevice()
    {
        foreach (var o in new object?[] { _meter, _volume, _device })
            if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
        _meter = null; _volume = null; _device = null; _deviceId = null;
    }

    protected override void DisposeResources()
    {
        ReleaseDevice();
        if (_enum != null) try { Marshal.ReleaseComObject(_enum); } catch { }
        _enum = null;
    }
}
