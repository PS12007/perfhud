using System.Text;
using PerfHud.Hotkeys;
using PerfHud.Monitoring;

namespace PerfHud.Core;

/// <summary>Builds the "Copy system information" diagnostic summary (no IP addresses, no personal data).</summary>
public static class SystemInfoReport
{
    public static string Build(AppHost app)
    {
        var s = app.Store;
        var sb = new StringBuilder();
        string T(string id) => s.GetText(id) ?? "N/A";
        string V(string id)
        {
            var d = MetricRegistry.Get(id);
            if (d == null) return "N/A";
            var (v, u) = MetricRegistry.Format(d, s.Get(id), app.Settings.Current);
            return v == MetricRegistry.NA ? $"N/A ({s.GetReason(id) ?? "no data"})" : $"{v} {u}".Trim();
        }

        sb.AppendLine($"PerfHud {typeof(AppHost).Assembly.GetName().Version} — diagnostic summary ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine($"Elevated: {Elevation.IsAdmin} · Performance Log Users: {Elevation.IsPerformanceLogUser()} · .NET {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine("== System ==");
        sb.AppendLine($"Manufacturer: {T("sys.manufacturer")}");
        sb.AppendLine($"Model:        {T("sys.model")}");
        sb.AppendLine($"OS:           {T("sys.os")} {T("sys.osver")}");
        sb.AppendLine($"BIOS:         {T("sys.bios")} · Board: {T("sys.board")}");
        sb.AppendLine($"CPU:          {T("cpu.name")} ({V("cpu.cores")} cores / {V("cpu.threads")} threads, base {V("cpu.clock.base")})");
        sb.AppendLine($"GPU(s):       {T("sys.gpus")} · dGPU state {T("gpu.state")}");
        sb.AppendLine($"RAM:          {T("ram.type")}");
        sb.AppendLine($"Display:      {T("sys.monitor")} {T("sys.display")} @ {V("sys.refresh")} · HDR {T("sys.hdr")}");
        sb.AppendLine($"Power:        plan {T("sys.powerplan")} · mode {T("sys.powermode")} · OEM {T("sys.oemmode")} · source {T("bat.source")}");
        sb.AppendLine();
        sb.AppendLine("== Live readings ==");
        foreach (var id in new[] { "cpu.usage", "cpu.temp", "cpu.clock", "cpu.power", "gpu.usage", "gpu.temp", "gpu.power", "gpu.vram.used", "ram.used", "ram.pct",
                     "disk.temp", "bat.pct", "bat.power", "bat.health", "bat.voltage", "bat.temp", "fps.current", "app.cpu", "app.mem" })
            sb.AppendLine($"{id,-16} {V(id)}");
        sb.AppendLine();
        sb.AppendLine("== Monitors ==");
        foreach (var m in app.Monitoring.Statuses)
            sb.AppendLine($"{m.Name,-18} {m.State,-12} {m.LastDurationMs,4:0} ms  {m.Message}");
        sb.AppendLine($"Sensor providers: {app.Temps.ProviderStatus}");
        sb.AppendLine($"FPS events processed: {app.Fps.EventsReceived:N0}");
        sb.AppendLine();
        sb.AppendLine("== Hotkeys ==");
        foreach (var h in app.Settings.Current.Hotkeys)
        {
            var (st, detail) = app.Hotkeys.GetStatus(h.Action);
            sb.AppendLine($"{h.Action,-20} {h.Gesture,-14} {st} {(st == HotkeyState.Registered ? "" : detail)}");
        }
        sb.AppendLine();
        sb.AppendLine($"Config: {AppPaths.SettingsFile}");
        sb.AppendLine($"Logs:   {AppPaths.LogDir}");
        return sb.ToString();
    }
}
