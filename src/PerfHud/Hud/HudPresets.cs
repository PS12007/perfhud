using PerfHud.Core;
using PerfHud.Settings;

namespace PerfHud.Hud;

/// <summary>Built-in layouts. They're generated in code (always up to date) and can be copied into editable custom layouts.</summary>
public static class HudPresets
{
    public const string MinimalName = "Minimal", GamingName = "Gaming", FullName = "Full", CustomName = "Custom";
    public const string HiddenToken = "(Hidden)";

    public static readonly string[] BuiltInNames = { MinimalName, GamingName, FullName };

    private sealed class B
    {
        public readonly List<HudComponent> Items = new();
        public HudComponent Add(ComponentType t, string metric, int col, int row, int colSpan = 1, int rowSpan = 1,
            string secondary = "", string label = "", bool ratio = false, bool detail = false, double height = 0, string text = "", string icon = "")
        {
            var c = new HudComponent
            {
                Type = t, MetricId = metric, Col = col, Row = row, ColSpan = colSpan, RowSpan = rowSpan, SecondaryMetricId = secondary,
                Label = label, Ratio = ratio, DetailOnly = detail, Height = height, Text = text, Icon = icon,
            };
            Items.Add(c);
            return c;
        }
        public HudLayout Build(string name, bool builtIn) => new() { Name = name, IsBuiltIn = builtIn, Components = new ObservableList<HudComponent>(Items) };
    }

    public static HudLayout Minimal()
    {
        using var quiet = Observable.Quiet();
        var b = new B();
        b.Add(ComponentType.Number, "fps.current", 0, 0, label: "FPS");
        b.Add(ComponentType.Number, "cpu.usage", 0, 1, secondary: "cpu.temp", label: "CPU");
        b.Add(ComponentType.Number, "gpu.usage", 0, 2, secondary: "gpu.temp", label: "GPU");
        b.Add(ComponentType.Number, "ram.used", 0, 3, secondary: "ram.total", label: "RAM", ratio: true);
        return b.Build(MinimalName, true);
    }

    public static HudLayout Gaming()
    {
        using var quiet = Observable.Quiet();
        var b = new B();
        int r = 0;
        b.Add(ComponentType.BigNumber, "fps.current", 0, r++, 2, secondary: "fps.app");
        b.Add(ComponentType.Number, "fps.low1", 0, r, label: "1% LOW");
        b.Add(ComponentType.Number, "fps.frametime", 1, r++, label: "FRAME");
        b.Add(ComponentType.FrameTimeGraph, "fps.frametime", 0, r++, 2, detail: true, height: 30);
        b.Add(ComponentType.Divider, "", 0, r++, 2);
        b.Add(ComponentType.Number, "cpu.usage", 0, r++, 2, secondary: "cpu.temp", label: "CPU");
        b.Add(ComponentType.Number, "gpu.usage", 0, r++, 2, secondary: "gpu.temp", label: "GPU");
        b.Add(ComponentType.Number, "gpu.vram.used", 0, r++, 2, secondary: "gpu.vram.total", label: "VRAM", ratio: true);
        b.Add(ComponentType.Number, "ram.used", 0, r++, 2, secondary: "ram.total", label: "RAM", ratio: true);
        b.Add(ComponentType.Divider, "", 0, r++, 2);
        b.Add(ComponentType.Number, "net.up", 0, r, label: "UP", icon: "up");
        b.Add(ComponentType.Number, "net.down", 1, r++, label: "DOWN", icon: "down");
        b.Add(ComponentType.Number, "bat.pct", 0, r++, 2, secondary: "bat.source", label: "BAT");
        return b.Build(GamingName, true);
    }

    public static HudLayout Full()
    {
        using var quiet = Observable.Quiet();
        var b = new B();
        int r = 0;
        // Frame rate
        b.Add(ComponentType.BigNumber, "fps.current", 0, r, secondary: "fps.app");
        b.Add(ComponentType.Number, "fps.avg", 1, r, label: "AVG");
        b.Add(ComponentType.Number, "fps.low1", 2, r, label: "1% LOW");
        b.Add(ComponentType.Number, "fps.low01", 3, r++, label: "0.1% LOW");
        b.Add(ComponentType.FrameTimeGraph, "fps.frametime", 0, r++, 4, detail: true, height: 34);

        // CPU | GPU
        b.Add(ComponentType.Text, "", 0, r, 2, secondary: "cpu.name", text: "CPU", icon: "cpu");
        b.Add(ComponentType.Text, "", 2, r++, 2, secondary: "gpu.name", text: "GPU", icon: "gpu");
        b.Add(ComponentType.Number, "cpu.usage", 0, r, label: "LOAD");
        b.Add(ComponentType.Number, "cpu.temp", 1, r, label: "TEMP");
        b.Add(ComponentType.Number, "gpu.usage", 2, r, label: "LOAD");
        b.Add(ComponentType.Number, "gpu.temp", 3, r++, label: "TEMP");
        b.Add(ComponentType.Number, "cpu.clock", 0, r, label: "CLOCK");
        b.Add(ComponentType.Number, "cpu.power", 1, r, label: "POWER");
        b.Add(ComponentType.Number, "gpu.clock", 2, r, label: "CORE");
        b.Add(ComponentType.Number, "gpu.power", 3, r++, label: "POWER");
        b.Add(ComponentType.Number, "cpu.clock.peak", 0, r, label: "PEAK");
        b.Add(ComponentType.Number, "cpu.threads", 1, r, secondary: "cpu.cores", label: "C/T");
        b.Add(ComponentType.Number, "gpu.memclock", 2, r, label: "MEM");
        b.Add(ComponentType.Number, "gpu.hotspot", 3, r++, label: "HOTSPOT");
        b.Add(ComponentType.Graph, "cpu.usage", 0, r, 2, detail: true, height: 28);
        b.Add(ComponentType.Graph, "gpu.usage", 2, r++, 2, detail: true, height: 28);
        b.Add(ComponentType.Graph, "cpu.temp", 0, r, 2, detail: true, height: 28);
        b.Add(ComponentType.Graph, "gpu.temp", 2, r++, 2, detail: true, height: 28);
        b.Add(ComponentType.CoreGrid, "cpu.cores", 0, r, 2, detail: true, height: 26);
        b.Add(ComponentType.ProgressBar, "gpu.vram.used", 2, r++, 2, secondary: "gpu.vram.total", label: "VRAM", ratio: true);
        b.Add(ComponentType.Number, "gpu.fan", 2, r, label: "FAN");
        b.Add(ComponentType.Number, "gpu.state", 3, r++, label: "STATE");

        // Memory | Network
        b.Add(ComponentType.Text, "", 0, r, 2, secondary: "ram.type", text: "MEMORY", icon: "ram");
        b.Add(ComponentType.Text, "", 2, r++, 2, secondary: "net.adapter", text: "NETWORK", icon: "net");
        b.Add(ComponentType.ProgressBar, "ram.used", 0, r, 2, secondary: "ram.total", label: "RAM", ratio: true);
        b.Add(ComponentType.Number, "net.down", 2, r, label: "DOWN", icon: "down");
        b.Add(ComponentType.Number, "net.up", 3, r++, label: "UP", icon: "up");
        b.Add(ComponentType.Graph, "ram.pct", 0, r, 2, detail: true, height: 28);
        b.Add(ComponentType.Graph, "net.down", 2, r++, 2, detail: true, height: 28);
        b.Add(ComponentType.Number, "ram.avail", 0, r, label: "FREE");
        b.Add(ComponentType.Number, "gpu.vram.pct", 1, r, label: "VRAM");
        b.Add(ComponentType.Number, "net.ping", 2, r, label: "PING");
        b.Add(ComponentType.Number, "net.type", 3, r++, label: "LINK");
        b.Add(ComponentType.ProgressBar, "ram.commit", 0, r, 2, secondary: "ram.commit.limit", label: "COMMIT", ratio: true, detail: true);
        b.Add(ComponentType.Graph, "net.up", 2, r++, 2, detail: true, height: 22);

        // Storage | Battery
        b.Add(ComponentType.Text, "", 0, r, 2, text: "STORAGE", icon: "disk");
        b.Add(ComponentType.Text, "", 2, r++, 2, secondary: "bat.state", text: "BATTERY", icon: "battery");
        int batRow = r;
        b.Add(ComponentType.DriveList, "disk.drives", 0, r, 2, 3);
        b.Add(ComponentType.Number, "bat.pct", 2, r, secondary: "bat.source", label: "CHARGE");
        b.Add(ComponentType.Number, "bat.time", 3, r++, label: "LEFT");
        b.Add(ComponentType.Number, "bat.power", 2, r, label: "DRAIN");
        b.Add(ComponentType.Number, "bat.rate", 3, r++, label: "CHARGE");
        b.Add(ComponentType.Number, "bat.health", 2, r, label: "HEALTH");
        b.Add(ComponentType.Number, "bat.voltage", 3, r++, label: "VOLT");
        b.Add(ComponentType.Number, "disk.read", 0, r, label: "READ");
        b.Add(ComponentType.Number, "disk.write", 1, r, label: "WRITE");
        b.Add(ComponentType.Number, "bat.full", 2, r, secondary: "bat.design", label: "CAP", ratio: true);
        b.Add(ComponentType.Number, "bat.current", 3, r++, label: "AMPS");
        b.Add(ComponentType.Graph, "disk.active", 0, r, 2, detail: true, height: 22);
        b.Add(ComponentType.Graph, "bat.power", 2, r++, 2, detail: true, height: 22);
        _ = batRow;

        // Temperatures | Laptop
        b.Add(ComponentType.Text, "", 0, r, 2, text: "TEMPERATURES", icon: "temp");
        b.Add(ComponentType.Text, "", 2, r++, 2, text: "LAPTOP", icon: "laptop");
        var sysIds = new[] { "sys.model", "cpu.name", "sys.gpus", "ram.type", "sys.os", "sys.osver", "sys.bios", "sys.board",
            "sys.display", "sys.refresh", "sys.monitor", "sys.hdr", "sys.powerplan", "sys.powermode", "sys.oemmode", "bat.source" };
        b.Add(ComponentType.SensorList, "temps.all", 0, r, 2, sysIds.Length);
        foreach (var id in sysIds) b.Add(ComponentType.Number, id, 2, r++, 2);
        return b.Build(FullName, true);
    }

    public static HudLayout CreateDefaultCustom()
    {
        using var quiet = Observable.Quiet();
        var b = new B();
        b.Add(ComponentType.BigNumber, "fps.current", 0, 0, secondary: "fps.app");
        b.Add(ComponentType.Number, "fps.low1", 1, 0, label: "1% LOW");
        b.Add(ComponentType.Number, "fps.frametime", 2, 0, label: "FRAME");
        b.Add(ComponentType.Gauge, "cpu.usage", 0, 1, secondary: "cpu.temp", label: "CPU");
        b.Add(ComponentType.Gauge, "gpu.usage", 1, 1, secondary: "gpu.temp", label: "GPU");
        b.Add(ComponentType.Gauge, "ram.pct", 2, 1, label: "RAM");
        b.Add(ComponentType.Graph, "cpu.temp", 0, 2, 3, detail: true, height: 28);
        b.Add(ComponentType.Number, "bat.pct", 0, 3, label: "BAT");
        b.Add(ComponentType.Number, "bat.power", 1, 3, label: "DRAIN");
        b.Add(ComponentType.Number, "bat.time", 2, 3, label: "LEFT");
        return b.Build(CustomName, false);
    }

    /// <summary>All layout names in cycle order: built-ins, then user layouts.</summary>
    public static List<string> AllNames(AppSettings s) => BuiltInNames.Concat(s.Layouts.Select(l => l.Name)).Distinct().ToList();

    public static HudLayout Resolve(string? name, AppSettings s)
    {
        return name switch
        {
            MinimalName => Minimal(),
            GamingName => Gaming(),
            FullName => Full(),
            _ => s.Layouts.FirstOrDefault(l => l.Name == name) ?? Gaming(),
        };
    }

    public static HudLayout? FindEditable(string name, AppSettings s) => s.Layouts.FirstOrDefault(l => l.Name == name);
}
