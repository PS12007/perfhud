# Architecture

PerfHud is a single WPF (.NET 8) process with no services, no drivers of its own and no network dependencies.

```
src/PerfHud
├── App.xaml(.cs)            Entry point: single instance, crash guards, theme
├── Core/                    AppHost (composition root), logging, paths, elevation, startup, system-info report
├── Settings/                AppSettings model (observable), JSON persistence, themes, enums
├── Monitoring/              Monitor framework + metric catalog
│   ├── IMonitor.cs          IMonitor / MonitorBase / MonitorContext
│   ├── MonitoringService.cs Scheduler: 2 lanes (fast/slow), backoff, pause, reset, battery scaling, self-throttle
│   ├── MetricStore.cs       Thread-safe latest values + N/A reasons + history series
│   ├── MetricRegistry.cs    Every metric: id, label, kind, formatting, severity rules
│   ├── SensorHub.cs         State shared between monitors (GPU adapters, LHM snapshot, power source)
│   └── Monitors/            Cpu, Gpu, Memory, Disk, Network(+Latency), Battery, Temperature, Fps, SystemInfo, Self
├── Sensors/                 Data sources
│   ├── Native/              P/Invoke: PDH, NVML, ETW, DXGI, SetupAPI, battery & storage IOCTLs, DisplayConfig, power
│   ├── LhmProvider.cs       Optional LibreHardwareMonitorLib integration
│   ├── VendorWmiProvider.cs Lenovo GameZone WMI + ACPI thermal zones
│   └── Wmi.cs               One-off WMI queries (static hardware info)
├── Hud/                     Overlay
│   ├── HudLayout.cs         Layout model: grid of components
│   ├── HudPresets.cs        Built-in Minimal / Gaming / Full layouts
│   ├── HudStyle.cs          Resolved brushes/fonts from settings
│   ├── HudWindow.cs         Transparent, topmost, click-through window; positioning; screenshots
│   ├── HudController.cs     Effective state (settings + profile), rebuild/refresh, HUD actions
│   └── Elements/            One class per component type (Number, Graph, Gauge, lists…)
├── Rendering/               Sparkline, BarFill, GaugeArc, CoreBars (OnRender, no per-point visuals), vector icons
├── Hotkeys/                 RegisterHotKey-based global hotkeys with conflict reporting
├── Profiles/                Per-app profiles (foreground hook + light process scan)
├── Storage/                 Session recorder (CSV + JSON summary, frame-time histogram), history store
├── Notifications/           Alert engine (sustain, hysteresis, cooldown), toast window
├── SystemTray/              Shell_NotifyIcon wrapper + hidden message window
└── UI/                      Settings window (pages), HUD editor, history viewer, theme, controls
```

## Data flow

```
 Sensors (PDH, NVML, ETW, IOCTL, WMI, LHM)
        │  read by
 Monitors (background lanes, isolated failures)
        │  publish
 MetricStore  ──► HudController (UI timer) ──► HudWindow elements
        │    ──► AlertEngine (1 s)          ──► toasts / HUD banner / sound
        │    ──► SessionRecorder (1 s)      ──► %LOCALAPPDATA%\PerfHud\sessions
        └──► Settings window live pages, tray tooltip
```

* Monitors never touch UI objects. The UI never calls sensors. The store is the only contract between them.
* A missing value is `NaN` + a human-readable **reason** (`store.GetReason(id)`), shown as `N/A` on the HUD and explained in Settings.

## Reliability rules

* Each monitor runs inside `try/catch` in `MonitoringService.Run`:
  * `MonitorUnavailableException` → "N/A on this machine", re-checked every 60 s (hardware/permissions can change).
  * any other exception → logged once per 10 min (rate-limited), exponential backoff, full `Reset()` after 3 failures.
* Sleep/resume, display changes, GPU adapter changes and AC changes trigger targeted resets or immediate re-polls.
* The UI dispatcher has a last-chance handler: a UI glitch is logged and the HUD keeps running.

## Adding a new sensor / metric

1. **Declare the metric** in `Monitoring/MetricRegistry.cs`:
   ```csharp
   new("fan.cpu", "CPU Fan", "FAN", MetricKind.Generic, "Laptop", Graphable: true, Icon: "fan", Unit: "RPM"),
   ```
   The kind controls formatting and severity colors. It immediately becomes available in the HUD editor,
   the metric picker, alerts and (if added to `SessionRecorder.Columns`) session recording.

2. **Publish values** from a monitor. Either extend an existing one or add a new class:
   ```csharp
   public sealed class FanMonitor : MonitorBase
   {
       public override string Name => "Fans";
       public override string[] Prefixes => new[] { "fan." };
       public override MonitorLane Lane => MonitorLane.Slow;      // slow/blocking sources go on the slow lane

       public override void Initialize(MonitorContext ctx)
       {
           if (!SomeApi.IsPresent) throw new MonitorUnavailableException("No fan sensor exposed by this laptop");
       }

       public override void Update(MonitorContext ctx)
       {
           double? rpm = SomeApi.ReadRpm();
           ctx.Store.Set("fan.cpu", rpm, "Fan speed not reported");   // null → N/A with that reason
       }

       protected override void DisposeResources() { /* release handles; Reset() calls this */ }
   }
   ```
3. **Register it** in `Core/AppHost.Start()` with `Monitoring.Add(new FanMonitor());`.

That's it — no UI code is required. To display it by default, add a component to a preset in `Hud/HudPresets.cs`.

### New component types

Add a value to `ComponentType`, a `HudElement` subclass in `Hud/Elements`, a case in `HudElementFactory.Create`
and a name/icon/help text in `UI/HudEditorWindow.cs`.

## Performance notes

* All charts draw a single `StreamGeometry`, decimated to one min/max pair per pixel column.
* HUD elements only touch WPF properties when the text/brush actually changes.
* Animation frame rate is capped at 30 fps; animations are disabled in battery mode if configured.
* Throwaway model objects (built-in presets, clones) are created inside `Observable.Quiet()` so they never trigger
  settings saves or HUD rebuilds.
* `SelfMonitor` measures PerfHud's own CPU use and slows every lane if it exceeds the configured limit.
* Measured on the development laptop (i7-14700HX, RTX 5060 Laptop): ~0.2 % of total CPU and ~110 MB private memory
  with the Gaming layout at the default 500 ms refresh.
