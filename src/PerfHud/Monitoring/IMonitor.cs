using PerfHud.Settings;

namespace PerfHud.Monitoring;

public enum MonitorState { Starting, Ok, Degraded, Unavailable, Failing, Disabled, Paused }

/// <summary>Which worker thread runs the monitor. Slow monitors (WMI, ping, SMART) must not delay fast ones.</summary>
public enum MonitorLane { Fast, Slow }

/// <summary>Thrown from Initialize/Update when a monitor cannot work on this machine (not an error, just N/A).</summary>
public sealed class MonitorUnavailableException(string reason) : Exception(reason);

public sealed class MonitorContext
{
    public required MetricStore Store { get; init; }
    public required Func<AppSettings> SettingsAccessor { get; init; }
    public required SensorHub Sensors { get; init; }
    public AppSettings Settings => SettingsAccessor();

    /// <summary>True when on battery and battery optimization is enabled.</summary>
    public bool PowerSaving { get; set; }
    public bool OnBattery { get; set; }
}

/// <summary>
/// A hardware monitor. Implementations publish metrics to <see cref="MetricStore"/>.
/// Each monitor fails independently: exceptions are caught, logged (rate-limited), and retried with backoff.
/// </summary>
public interface IMonitor : IDisposable
{
    string Name { get; }
    /// <summary>Metric id prefix(es) this monitor owns (for invalidation on failure).</summary>
    string[] Prefixes { get; }
    MonitorLane Lane { get; }
    /// <summary>Expensive monitors are slowed further / skipped in battery-saving mode.</summary>
    bool IsExpensive { get; }
    int GetIntervalMs(MonitorContext ctx);
    void Initialize(MonitorContext ctx);
    void Update(MonitorContext ctx);
    /// <summary>Release native resources so the next Initialize starts fresh (after sleep/resume, driver reset...).</summary>
    void Reset();
    /// <summary>Optional status detail shown in Diagnostics (e.g. which source is used).</summary>
    string? Detail { get; }
}

public abstract class MonitorBase : IMonitor
{
    public abstract string Name { get; }
    public abstract string[] Prefixes { get; }
    public virtual MonitorLane Lane => MonitorLane.Fast;
    public virtual bool IsExpensive => false;
    public string? Detail { get; protected set; }

    public virtual int GetIntervalMs(MonitorContext ctx) => ctx.Settings.Performance.SensorIntervalMs;
    public virtual void Initialize(MonitorContext ctx) { }
    public abstract void Update(MonitorContext ctx);
    public virtual void Reset() => DisposeResources();
    protected virtual void DisposeResources() { }
    public void Dispose() { DisposeResources(); GC.SuppressFinalize(this); }
}
