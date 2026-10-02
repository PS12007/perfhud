using PerfHud.Core;
using PerfHud.Monitoring;
using PerfHud.Sensors.Native;
using PerfHud.Settings;

namespace PerfHud.Notifications;

public sealed record AlertEvent(AlertRule Rule, string Title, string Message, Severity Severity);

/// <summary>Evaluates alert rules once per second with a sustain duration, hysteresis and cooldown.</summary>
public sealed class AlertEngine
{
    private sealed class State { public double Since = double.NaN; public double LastFired = double.NegativeInfinity; public bool Active; }

    private readonly MetricStore _store;
    private readonly Func<AppSettings> _settings;
    private readonly Dictionary<AlertRule, State> _state = new();

    public event Action<AlertEvent>? Fired;

    public AlertEngine(MetricStore store, Func<AppSettings> settings)
    {
        _store = store;
        _settings = settings;
    }

    public void Evaluate(bool onBattery)
    {
        var s = _settings();
        double now = MetricSeries.Now;
        foreach (var rule in s.Alerts.ToList())
        {
            if (!_state.TryGetValue(rule, out var st)) _state[rule] = st = new State();
            if (!rule.Enabled || rule.Actions == AlertActions.None || (rule.OnlyOnBattery && !onBattery)) { st.Since = double.NaN; st.Active = false; continue; }

            var v = ToThresholdUnits(MetricRegistry.Get(rule.MetricId), _store.Get(rule.MetricId));
            if (double.IsNaN(v)) { st.Since = double.NaN; continue; }

            // 2% hysteresis so a value hovering at the threshold doesn't flap
            double margin = Math.Abs(rule.Threshold) * 0.02;
            bool breach = rule.Op == CompareOp.Above
                ? v > rule.Threshold - (st.Active ? margin : 0)
                : v < rule.Threshold + (st.Active ? margin : 0);

            if (!breach) { st.Since = double.NaN; st.Active = false; continue; }
            if (double.IsNaN(st.Since)) st.Since = now;
            if (now - st.Since < rule.DurationSeconds) continue;
            st.Active = true;
            if (now - st.LastFired < Math.Max(5, rule.CooldownSeconds)) continue;
            st.LastFired = now;

            var def = MetricRegistry.Get(rule.MetricId);
            string valueText = def != null ? FormatValue(def, _store.Get(rule.MetricId), s) : v.ToString("0.#");
            string title = string.IsNullOrWhiteSpace(rule.Name) ? (def?.Name ?? rule.MetricId) : rule.Name;
            string msg = $"{def?.Name ?? rule.MetricId} is {valueText} ({(rule.Op == CompareOp.Above ? "above" : "below")} {rule.Threshold:0.#})";
            var sev = rule.Op == CompareOp.Above && def?.Kind == MetricKind.Temperature ? Severity.Critical : Severity.Warm;
            Log.Info($"Alert: {title} — {msg}");
            Fired?.Invoke(new AlertEvent(rule, title, msg, sev));
            if (rule.Actions.HasFlag(AlertActions.Sound)) Win32.MessageBeep(0x30);
        }
        foreach (var dead in _state.Keys.Where(k => !s.Alerts.Contains(k)).ToList()) _state.Remove(dead);
    }

    /// <summary>Thresholds are entered in human units: GB for memory/space, MB/s for data rates.</summary>
    public static double ToThresholdUnits(MetricDefinition? d, double v) => d?.Kind switch
    {
        MetricKind.MemoryGB => v / (1024d * 1024 * 1024),
        MetricKind.MemoryMB => v / (1024d * 1024),
        MetricKind.DataRate => v / (1024d * 1024),
        _ => v,
    };

    public static string ThresholdUnit(MetricDefinition? d) => d?.Kind switch
    {
        MetricKind.MemoryGB => "GB",
        MetricKind.MemoryMB => "MB",
        MetricKind.DataRate => "MB/s",
        MetricKind.Percent or MetricKind.BatteryPercent => "%",
        MetricKind.Temperature => "°C",
        MetricKind.Power => "W",
        MetricKind.FrameTime or MetricKind.Latency => "ms",
        MetricKind.Fps => "FPS",
        _ => d?.Unit ?? "",
    };

    private static string FormatValue(MetricDefinition d, double v, AppSettings s)
    {
        var (val, unit) = MetricRegistry.Format(d, v, s);
        return unit is "%" or "°C" or "°F" ? val + unit : $"{val} {unit}".Trim();
    }
}
