using System.Globalization;
using PerfHud.Settings;

namespace PerfHud.Monitoring;

public enum MetricKind
{
    Percent, Temperature, Fps, FrameTime, Power, ClockGHz, ClockMHz, MemoryGB, MemoryMB, DataRate,
    Latency, Voltage, Current, EnergyWh, Duration, Count, BatteryPercent, Frequency, Text, Generic,
}

public enum TempCategory { None, Cpu, Gpu, Storage, Battery, Other }

public enum Severity { Unavailable, Neutral, Cool, Normal, Warm, Hot, Critical }

public sealed record MetricDefinition(
    string Id, string Name, string Short, MetricKind Kind, string Group,
    bool Graphable = false, TempCategory Temp = TempCategory.None, string Icon = "", string? MaxId = null,
    double Max = double.NaN, string Unit = "", bool NoSeverity = false, string? Description = null)
{
    public bool IsText => Kind == MetricKind.Text;
}

/// <summary>Catalog of every metric PerfHud can display. Add new metrics here (see docs/ARCHITECTURE.md).</summary>
public static class MetricRegistry
{
    public static readonly MetricDefinition[] All =
    {
        // ── FPS ───────────────────────────────────────────────
        new("fps.current", "Current FPS", "FPS", MetricKind.Fps, "FPS", true, Icon: "fps"),
        new("fps.avg", "Average FPS", "AVG", MetricKind.Fps, "FPS", true, Icon: "fps"),
        new("fps.low1", "1% Low FPS", "1% LOW", MetricKind.Fps, "FPS", true, Icon: "fps"),
        new("fps.low01", "0.1% Low FPS", "0.1% LOW", MetricKind.Fps, "FPS", false, Icon: "fps"),
        new("fps.frametime", "Frame Time", "FRAME", MetricKind.FrameTime, "FPS", true, Icon: "frametime"),
        new("fps.app", "Measured App", "APP", MetricKind.Text, "FPS", Icon: "fps"),
        new("fps.api", "Present API", "API", MetricKind.Text, "FPS", Icon: "fps"),

        // ── CPU ───────────────────────────────────────────────
        new("cpu.usage", "CPU Usage", "CPU", MetricKind.Percent, "CPU", true, Icon: "cpu", Max: 100),
        new("cpu.temp", "CPU Temperature", "CPU", MetricKind.Temperature, "CPU", true, TempCategory.Cpu, "temp"),
        new("cpu.power", "CPU Package Power", "CPU PWR", MetricKind.Power, "CPU", true, Icon: "power"),
        new("cpu.clock", "CPU Clock (effective)", "CLOCK", MetricKind.ClockGHz, "CPU", true, Icon: "clock"),
        new("cpu.clock.peak", "CPU Peak Clock (session)", "PEAK", MetricKind.ClockGHz, "CPU", Icon: "clock"),
        new("cpu.clock.base", "CPU Base Clock", "BASE", MetricKind.ClockGHz, "CPU", Icon: "clock"),
        new("cpu.cores", "CPU Cores", "CORES", MetricKind.Count, "CPU", Icon: "cpu"),
        new("cpu.threads", "CPU Threads", "THREADS", MetricKind.Count, "CPU", Icon: "cpu"),
        new("cpu.name", "CPU Name", "CPU", MetricKind.Text, "CPU", Icon: "cpu"),
        new("cpu.procs", "Processes", "PROCS", MetricKind.Count, "CPU", Icon: "cpu"),

        // ── GPU (primary / discrete) ──────────────────────────
        new("gpu.usage", "GPU Usage", "GPU", MetricKind.Percent, "GPU", true, Icon: "gpu", Max: 100),
        new("gpu.temp", "GPU Temperature", "GPU", MetricKind.Temperature, "GPU", true, TempCategory.Gpu, "temp"),
        new("gpu.hotspot", "GPU Hotspot", "HOTSPOT", MetricKind.Temperature, "GPU", true, TempCategory.Gpu, "temp"),
        new("gpu.vram.used", "VRAM Used", "VRAM", MetricKind.MemoryGB, "GPU", true, Icon: "vram", MaxId: "gpu.vram.total"),
        new("gpu.vram.total", "VRAM Total", "VRAM", MetricKind.MemoryGB, "GPU", Icon: "vram"),
        new("gpu.vram.pct", "VRAM Usage", "VRAM", MetricKind.Percent, "GPU", true, Icon: "vram", Max: 100),
        new("gpu.power", "GPU Power", "GPU PWR", MetricKind.Power, "GPU", true, Icon: "power", MaxId: "gpu.power.limit"),
        new("gpu.power.limit", "GPU Power Limit", "LIMIT", MetricKind.Power, "GPU", Icon: "power"),
        new("gpu.clock", "GPU Core Clock", "CORE", MetricKind.ClockMHz, "GPU", true, Icon: "clock"),
        new("gpu.memclock", "GPU Memory Clock", "MEM CLK", MetricKind.ClockMHz, "GPU", Icon: "clock"),
        new("gpu.fan", "GPU Fan", "FAN", MetricKind.Percent, "GPU", Icon: "fan", Max: 100, NoSeverity: true),
        new("gpu.name", "GPU Name", "GPU", MetricKind.Text, "GPU", Icon: "gpu"),
        new("gpu.state", "GPU Power State", "STATE", MetricKind.Text, "GPU", Icon: "gpu"),
        new("gpu.throttle", "GPU Throttle Reason", "THROTTLE", MetricKind.Text, "GPU", Icon: "warn"),
        new("gpu.pstate", "GPU P-State", "P-STATE", MetricKind.Text, "GPU", Icon: "gpu"),
        new("igpu.usage", "iGPU Usage", "iGPU", MetricKind.Percent, "GPU", true, Icon: "gpu", Max: 100),
        new("igpu.mem", "iGPU Shared Memory", "iGPU MEM", MetricKind.MemoryGB, "GPU", Icon: "vram"),
        new("igpu.name", "iGPU Name", "iGPU", MetricKind.Text, "GPU", Icon: "gpu"),

        // ── Memory ───────────────────────────────────────────
        new("ram.used", "RAM Used", "RAM", MetricKind.MemoryGB, "Memory", true, Icon: "ram", MaxId: "ram.total"),
        new("ram.avail", "RAM Available", "AVAIL", MetricKind.MemoryGB, "Memory", Icon: "ram"),
        new("ram.total", "RAM Total", "RAM", MetricKind.MemoryGB, "Memory", Icon: "ram"),
        new("ram.pct", "RAM Usage", "RAM", MetricKind.Percent, "Memory", true, Icon: "ram", Max: 100),
        new("ram.commit", "Committed Memory", "COMMIT", MetricKind.MemoryGB, "Memory", Icon: "ram", MaxId: "ram.commit.limit"),
        new("ram.commit.limit", "Commit Limit", "LIMIT", MetricKind.MemoryGB, "Memory", Icon: "ram"),
        new("ram.speed", "RAM Speed", "SPEED", MetricKind.Generic, "Memory", Icon: "ram", Unit: "MT/s"),
        new("ram.type", "RAM Configuration", "RAM", MetricKind.Text, "Memory", Icon: "ram"),

        // ── Storage ──────────────────────────────────────────
        new("disk.read", "Disk Read", "READ", MetricKind.DataRate, "Storage", true, Icon: "disk"),
        new("disk.write", "Disk Write", "WRITE", MetricKind.DataRate, "Storage", true, Icon: "disk"),
        new("disk.active", "Disk Activity", "DISK", MetricKind.Percent, "Storage", true, Icon: "disk", Max: 100),
        new("disk.temp", "Drive Temperature (hottest)", "SSD", MetricKind.Temperature, "Storage", true, TempCategory.Storage, "temp"),
        new("disk.sys.free", "System Drive Free", "FREE", MetricKind.MemoryGB, "Storage", Icon: "disk"),
        new("disk.sys.total", "System Drive Size", "SIZE", MetricKind.MemoryGB, "Storage", Icon: "disk"),
        new("disk.sys.pct", "System Drive Used", "C:", MetricKind.Percent, "Storage", Icon: "disk", Max: 100),

        // ── Network ──────────────────────────────────────────
        new("net.down", "Download", "DOWN", MetricKind.DataRate, "Network", true, Icon: "down"),
        new("net.up", "Upload", "UP", MetricKind.DataRate, "Network", true, Icon: "up"),
        new("net.ping", "Latency", "PING", MetricKind.Latency, "Network", true, Icon: "net"),
        new("net.adapter", "Active Adapter", "NET", MetricKind.Text, "Network", Icon: "net"),
        new("net.type", "Connection Type", "LINK", MetricKind.Text, "Network", Icon: "net"),
        new("net.speed", "Link Speed", "LINK", MetricKind.Text, "Network", Icon: "net"),
        new("net.ip", "Local IP", "IP", MetricKind.Text, "Network", Icon: "net"),

        // ── Battery ──────────────────────────────────────────
        new("bat.pct", "Battery", "BAT", MetricKind.BatteryPercent, "Battery", true, Icon: "battery", Max: 100),
        new("bat.state", "Battery State", "STATE", MetricKind.Text, "Battery", Icon: "battery"),
        new("bat.source", "Power Source", "POWER", MetricKind.Text, "Battery", Icon: "plug"),
        new("bat.time", "Time Remaining", "LEFT", MetricKind.Duration, "Battery", Icon: "clock"),
        new("bat.health", "Battery Health", "HEALTH", MetricKind.Percent, "Battery", Icon: "battery", Max: 100, NoSeverity: true),
        new("bat.power", "Battery Power Draw", "DRAIN", MetricKind.Power, "Battery", true, Icon: "power"),
        new("bat.rate", "Charge Rate", "CHARGE", MetricKind.Power, "Battery", Icon: "plug"),
        new("bat.voltage", "Battery Voltage", "VOLT", MetricKind.Voltage, "Battery", Icon: "power"),
        new("bat.current", "Battery Current", "AMPS", MetricKind.Current, "Battery", Icon: "power"),
        new("bat.temp", "Battery Temperature", "BAT", MetricKind.Temperature, "Battery", true, TempCategory.Battery, "temp"),
        new("bat.design", "Design Capacity", "DESIGN", MetricKind.EnergyWh, "Battery", Icon: "battery"),
        new("bat.full", "Full Charge Capacity", "FULL", MetricKind.EnergyWh, "Battery", Icon: "battery"),
        new("bat.remaining", "Remaining Capacity", "NOW", MetricKind.EnergyWh, "Battery", Icon: "battery"),
        new("bat.cycles", "Cycle Count", "CYCLES", MetricKind.Count, "Battery", Icon: "battery"),

        // ── Laptop / System ──────────────────────────────────
        new("sys.manufacturer", "Manufacturer", "MAKER", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.model", "Model", "MODEL", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.gpus", "Graphics", "GPU", MetricKind.Text, "Laptop", Icon: "gpu"),
        new("sys.os", "Operating System", "OS", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.osver", "OS Version", "BUILD", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.bios", "BIOS Version", "BIOS", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.board", "Motherboard", "BOARD", MetricKind.Text, "Laptop", Icon: "laptop"),
        new("sys.display", "Resolution", "DISPLAY", MetricKind.Text, "Laptop", Icon: "display"),
        new("sys.refresh", "Refresh Rate", "REFRESH", MetricKind.Frequency, "Laptop", Icon: "display"),
        new("sys.monitor", "Active Monitor", "MONITOR", MetricKind.Text, "Laptop", Icon: "display"),
        new("sys.hdr", "HDR", "HDR", MetricKind.Text, "Laptop", Icon: "display"),
        new("sys.powerplan", "Power Plan", "PLAN", MetricKind.Text, "Laptop", Icon: "plug"),
        new("sys.powermode", "Power Mode", "MODE", MetricKind.Text, "Laptop", Icon: "plug"),
        new("sys.oemmode", "OEM Performance Profile", "OEM", MetricKind.Text, "Laptop", Icon: "fan"),
        new("sys.uptime", "Uptime", "UPTIME", MetricKind.Duration, "Laptop", Icon: "clock"),
        new("time.now", "Clock", "TIME", MetricKind.Text, "Laptop", Icon: "clock"),

        // ── PerfHud itself ───────────────────────────────────
        new("app.cpu", "PerfHud CPU", "HUD CPU", MetricKind.Percent, "PerfHud", true, Icon: "cpu", Max: 100, NoSeverity: true),
        new("app.mem", "PerfHud Memory", "HUD MEM", MetricKind.MemoryMB, "PerfHud", Icon: "ram"),
    };

    private static readonly Dictionary<string, MetricDefinition> ById =
        All.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Groups => All.Select(d => d.Group).Distinct();

    /// <summary>Resolves static metrics and dynamic per-drive ids like "disk.C.free" / "disk.D.temp".</summary>
    public static MetricDefinition? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (ById.TryGetValue(id, out var d)) return d;
        var parts = id.Split('.');
        if (parts.Length == 3 && parts[0] == "disk" && parts[1].Length == 1)
        {
            var l = parts[1].ToUpperInvariant() + ":";
            return parts[2] switch
            {
                "free" => new(id, $"{l} Free", l, MetricKind.MemoryGB, "Storage", Icon: "disk"),
                "total" => new(id, $"{l} Size", l, MetricKind.MemoryGB, "Storage", Icon: "disk"),
                "used" => new(id, $"{l} Used", l, MetricKind.MemoryGB, "Storage", Icon: "disk", MaxId: $"disk.{parts[1]}.total"),
                "pct" => new(id, $"{l} Used %", l, MetricKind.Percent, "Storage", Icon: "disk", Max: 100),
                "read" => new(id, $"{l} Read", $"{l} R", MetricKind.DataRate, "Storage", true, Icon: "disk"),
                "write" => new(id, $"{l} Write", $"{l} W", MetricKind.DataRate, "Storage", true, Icon: "disk"),
                "active" => new(id, $"{l} Activity", l, MetricKind.Percent, "Storage", true, Icon: "disk", Max: 100),
                "temp" => new(id, $"{l} Temperature", l, MetricKind.Temperature, "Storage", true, TempCategory.Storage, "temp"),
                _ => null,
            };
        }
        if (parts.Length == 3 && parts[0] == "cpu" && parts[1] == "core" && int.TryParse(parts[2], out var core))
            return new(id, $"CPU Core {core}", $"C{core}", MetricKind.Percent, "CPU", Icon: "cpu", Max: 100);
        if (id.StartsWith("temp.", StringComparison.OrdinalIgnoreCase))
            return new(id, id[5..], id[5..].ToUpperInvariant(), MetricKind.Temperature, "Temperature", true, TempCategory.Other, "temp");
        return null;
    }

    // ── Formatting ─────────────────────────────────────────

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string NA = "N/A";

    public static (string value, string unit) Format(MetricDefinition d, double v, AppSettings s)
    {
        if (double.IsNaN(v)) return (NA, "");
        switch (d.Kind)
        {
            case MetricKind.Percent:
            case MetricKind.BatteryPercent:
                return (Math.Clamp(v, 0, 999).ToString("0", Inv), "%");
            case MetricKind.Temperature:
                return s.General.TemperatureUnit == TemperatureUnit.Fahrenheit
                    ? ((v * 9 / 5 + 32).ToString("0", Inv), "°F")
                    : (v.ToString("0", Inv), "°C");
            case MetricKind.Fps: return (v.ToString("0", Inv), "FPS");
            case MetricKind.FrameTime: return (v.ToString(v < 100 ? "0.0" : "0", Inv), "ms");
            case MetricKind.Power: return (Math.Abs(v).ToString(Math.Abs(v) < 100 ? "0.0" : "0", Inv), "W");
            case MetricKind.ClockGHz: return ((v / 1000).ToString("0.00", Inv), "GHz");
            case MetricKind.ClockMHz: return (v.ToString("0", Inv), "MHz");
            case MetricKind.MemoryGB:
                {
                    var gb = v / (1024d * 1024 * 1024);
                    return (gb.ToString(gb < 100 ? "0.0" : "0", Inv), "GB");
                }
            case MetricKind.MemoryMB: return ((v / (1024d * 1024)).ToString("0", Inv), "MB");
            case MetricKind.DataRate:
                return d.Group == "Network" && s.General.NetworkUnit == NetworkUnit.Bits ? FormatBits(v * 8) : FormatBytesRate(v);
            case MetricKind.Latency: return (v.ToString("0", Inv), "ms");
            case MetricKind.Voltage: return (v.ToString("0.00", Inv), "V");
            case MetricKind.Current: return (v.ToString("0.00", Inv), "A");
            case MetricKind.EnergyWh: return (v.ToString("0.0", Inv), "Wh");
            case MetricKind.Duration: return (FormatDuration(v), "");
            case MetricKind.Count: return (v.ToString("0", Inv), "");
            case MetricKind.Frequency: return (v.ToString("0", Inv), "Hz");
            default: return (v.ToString(Math.Abs(v) < 10 ? "0.0#" : "0.#", Inv), d.Unit);
        }
    }

    /// <summary>Formats a value in the "units" of another value (for "11.2/32 GB" style ratios).</summary>
    public static string FormatBare(MetricDefinition d, double v, AppSettings s) => Format(d, v, s).value;

    public static (string, string) FormatBytesRate(double bps)
    {
        if (bps < 1024) return (bps.ToString("0", Inv), "B/s");
        if (bps < 1024 * 1024) return ((bps / 1024).ToString(bps < 100 * 1024 ? "0.0" : "0", Inv), "KB/s");
        if (bps < 1024d * 1024 * 1024) return ((bps / 1024 / 1024).ToString("0.0", Inv), "MB/s");
        return ((bps / 1024 / 1024 / 1024).ToString("0.00", Inv), "GB/s");
    }

    public static (string, string) FormatBits(double bitsPerSec)
    {
        if (bitsPerSec < 1000) return (bitsPerSec.ToString("0", Inv), "b/s");
        if (bitsPerSec < 1e6) return ((bitsPerSec / 1e3).ToString("0", Inv), "Kb/s");
        if (bitsPerSec < 1e9) return ((bitsPerSec / 1e6).ToString("0.0", Inv), "Mb/s");
        return ((bitsPerSec / 1e9).ToString("0.00", Inv), "Gb/s");
    }

    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return NA;
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m";
        return $"{t.Seconds}s";
    }

    // ── Severity (drives color + warning glyphs) ───────────

    public static Severity Evaluate(MetricDefinition d, double v, AppSettings s)
    {
        if (double.IsNaN(v)) return Severity.Unavailable;
        if (d.NoSeverity) return Severity.Neutral;
        var t = s.Thresholds;
        switch (d.Kind)
        {
            case MetricKind.Temperature:
                {
                    var th = d.Temp switch
                    {
                        TempCategory.Cpu => t.Cpu,
                        TempCategory.Gpu => t.Gpu,
                        TempCategory.Storage => t.Storage,
                        TempCategory.Battery => t.Battery,
                        _ => t.Other,
                    };
                    if (v >= th.Critical) return Severity.Critical;
                    if (v >= th.Hot) return Severity.Hot;
                    if (v >= th.Warm) return Severity.Warm;
                    if (v < th.Warm - 20) return Severity.Cool;
                    return Severity.Normal;
                }
            case MetricKind.Percent:
                {
                    bool mem = d.Id is "ram.pct" or "gpu.vram.pct" || d.Id.EndsWith(".pct");
                    double warn = mem ? t.MemoryWarn : t.UsageWarn, crit = mem ? t.MemoryCritical : t.UsageCritical;
                    if (v >= crit) return Severity.Critical;
                    if (v >= warn) return Severity.Warm;
                    return Severity.Normal;
                }
            case MetricKind.Fps:
                if (v < t.FpsCritical) return Severity.Critical;
                if (v < t.FpsWarn) return Severity.Warm;
                return Severity.Normal;
            case MetricKind.FrameTime:
                if (v <= 0) return Severity.Neutral;
                var fps = 1000 / v;
                if (fps < t.FpsCritical) return Severity.Critical;
                if (fps < t.FpsWarn) return Severity.Warm;
                return Severity.Normal;
            case MetricKind.BatteryPercent:
                if (v <= t.BatteryCritical) return Severity.Critical;
                if (v <= t.BatteryWarn) return Severity.Warm;
                return Severity.Normal;
            default:
                return Severity.Neutral;
        }
    }

    /// <summary>Short non-color cue shown next to a value when something is wrong (accessibility: never color-only).</summary>
    public static string? WarningTag(MetricDefinition d, Severity sev, MetricStore store)
    {
        if (d.Id == "gpu.temp" || d.Id == "gpu.usage" || d.Id == "gpu.clock")
        {
            var thr = store.GetText("gpu.throttle");
            if (!string.IsNullOrEmpty(thr)) return thr;
        }
        if (sev < Severity.Hot && !(sev == Severity.Warm && d.Kind is MetricKind.BatteryPercent or MetricKind.Fps)) return null;
        return d.Kind switch
        {
            MetricKind.Temperature => sev == Severity.Critical ? "CRITICAL" : "HOT",
            MetricKind.BatteryPercent => sev == Severity.Critical ? "CRITICAL" : "LOW",
            MetricKind.Fps => sev == Severity.Critical ? "LOW" : null,
            MetricKind.Percent => sev == Severity.Critical ? "HIGH" : null,
            _ => null,
        };
    }
}
