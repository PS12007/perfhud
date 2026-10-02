# PerfHud

A lightweight, deeply customizable performance overlay for Windows laptops — think NVIDIA's overlay, RTSS and
Xbox Game Bar, but with a drag-and-drop HUD editor, per-game profiles, session history, alerts and a battery-aware
monitoring engine. Native .NET 8 / WPF, no services, no injection, everything stays on your PC.

```
╭─────────────────────────────╮      ╭───────────────────────────────╮
│ ◔ FPS        Cyberpunk2077  │      │ FPS  144                      │
│ 144 FPS                     │      │ CPU  38%  62°C                │
│ ◔ 1% LOW 102   ∿ FRAME 6.9ms│      │ GPU  71%  67°C                │
│ ▁▂▁▁▃▁▁▁▂▁▁▁▁█▁▁▂▁▁▁▁▁▂▁▁▁▁  │      │ RAM  11.2/32 GB               │
│ CPU  38%  62°C              │      ╰───────────────────────────────╯
│ GPU  71%  ⚠ 87°C  THERMAL   │                Minimal
│ VRAM 5.4/8.0 GB             │
│ RAM  11.2/32.0 GB           │
│ ↑ UP 12.4 MB/s  ↓ DOWN 84.2 │
│ BAT  87% • AC               │
╰─────────────────────────────╯
            Gaming
```

## Highlights

* **Overlay** – borderless, transparent, always on top, click-through when locked, works over windowed and
  borderless-fullscreen games. Fade in/out, position anywhere (anchors, per-monitor, follow active window, drag to move),
  scale, opacity, themes, compact/detailed, vertical/horizontal, hide-from-capture.
* **Metrics** – FPS (current/avg/1%/0.1% lows, frame time + per-frame graph), CPU (total + per-thread load, effective
  clock, peak clock, package power, temperature), GPU (usage, temp, VRAM, power + limit, core/memory clock, fan,
  P-state, **thermal-throttle detection**, sleeping-dGPU detection), iGPU, RAM (used/available/commit/speed/type),
  storage (per drive space, read/write, activity, NVMe temperature), network (down/up, ping, adapter, link type & speed),
  **battery** (charge, state, time left/to full, health, design/full/remaining Wh, voltage, current, power draw, charge
  rate, cycles, temperature), laptop info (maker, model, BIOS, board, OS build, display, refresh, HDR, power plan,
  Windows power mode, OEM performance mode).
* **Temperature dashboard** – every accessible sensor, color coded cool → normal → warm → hot → critical with
  configurable thresholds, plus a ⚠ glyph and text tag so color is never the only cue.
* **Graphs** – CPU/GPU load & temp, RAM, VRAM, network, disk, battery drain, FPS, frame time… with 5 s / 10 s / 30 s /
  60 s / 5 min windows. One `StreamGeometry` per graph, decimated per pixel.
* **Presets** – Minimal, Gaming, Full, plus any number of your own layouts. Cycle with a hotkey.
* **HUD editor** – drag-and-drop grid editor with live data: move, resize, duplicate, delete, reorder, undo.
  Components: Number, Big number, Percentage, Progress bar, Graph, Frame-time graph, Gauge, Text/header, Icon, Divider,
  Spacer, Per-core bars, Temperature list, Drive list. Also a one-click **metric picker** for quick custom layouts.
* **App profiles** – e.g. `Cyberpunk2077.exe → Gaming`, `chrome.exe → Minimal`, `Desktop → Hidden`; per profile:
  layout, position, opacity, scale, compact mode, auto-record. Reverts automatically when the app closes/loses focus.
* **Session history** – auto-records when a full-screen game is detected (or manually / per profile): duration, avg
  FPS, exact 1% / 0.1% lows from every frame, avg/max CPU/GPU, max temps, battery used… with charts and CSV/JSON export.
* **Alerts** – GPU > 85 °C, CPU > 90 °C, battery < 20 %, RAM > 90 %, VRAM > 95 %, disk < 10 GB, FPS < 30 … each with
  sustain time, hysteresis, cooldown and any mix of subtle toast / HUD warning line / sound.
* **Battery mode** – on battery PerfHud polls less, redraws less, drops animations, throttles expensive sensors,
  shows its estimated power impact, and never wakes a sleeping hybrid-graphics dGPU.
* **System tray**, **global hotkeys** (with conflict detection), **start with Windows**, **diagnostics** page with
  live monitor status, logs, and *Copy system information*.

## Run it

**Download:** grab `PerfHud-<version>-win-x64.zip` from [Releases](../../releases), extract anywhere, run `PerfHud.exe`.
It needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64). Alternatives:
`PerfHud-<version>-setup.exe` (per-user installer, no admin; also needs the runtime) or
`PerfHud-SelfContained-<version>-win-x64.zip` (no runtime needed, larger).

On first launch the HUD appears in the top-left corner and the settings window opens. Afterwards PerfHud lives in
the **system tray** (double-click = settings, right-click = menu).

**From source:**

```powershell
dotnet run --project src/PerfHud              # development
./build.ps1                                   # release: dist/PerfHud/PerfHud.exe + zip
./build.ps1 -SelfContained -Installer         # + self-contained build + Inno Setup installer (if ISCC is installed)
```

Requires the .NET 8 SDK. Command-line flags: `--startup` (used by autostart; honors *Start minimized* / *Show HUD on
startup*), `--diag` (logs per-monitor cost every 10 s).

## Default hotkeys

| Action | Key |
|---|---|
| Toggle HUD | **F9** |
| Cycle layout (preset) | **F10** |
| Compact / detailed | **F11** |
| Toggle graphs | Ctrl+Alt+G |
| Lock / unlock (click-through) | Ctrl+Alt+L |
| Show / hide settings | Ctrl+Alt+S |
| Pause / resume monitoring | Ctrl+Alt+P |
| Screenshot HUD | Ctrl+Alt+C |
| Reset HUD position | Ctrl+Alt+R |
| Start / stop session recording | Ctrl+Alt+H |
| Open HUD editor | Ctrl+Alt+E |

All are rebindable (Settings → Global hotkeys). Hotkeys use `RegisterHotKey`; if another app already owns a
combination PerfHud shows "already used by another application" for that action and everything else keeps working.
Note that while PerfHud runs, F9–F11 are reserved system-wide (e.g. F11 no longer toggles browser full-screen) —
rebind them if that bothers you.

## Sensors and where they come from

| Data | Source | Admin? |
|---|---|---|
| CPU load (total + per thread), effective clock | Windows perf counters (`% Processor Utility`, `% Processor Performance`) — same as Task Manager | no |
| CPU package power | Windows `Energy Meter` counters (Intel RAPL) | no |
| CPU temperature | LibreHardwareMonitor (kernel driver, opt-in) · Lenovo GameZone WMI · ACPI thermal zones | **yes** on most laptops |
| NVIDIA GPU: usage, temp, VRAM, power/limit, clocks, fan, P-state, throttle reasons | NVML (ships with the driver) | no |
| AMD GPU | LibreHardwareMonitorLib (ADL) | no |
| Any GPU: usage per engine, dedicated/shared memory, iGPU | Windows GPU perf counters | no |
| dGPU sleep state (Optimus) | SetupAPI device power data — doesn't wake the GPU | no |
| RAM, commit, module speed/type | `GlobalMemoryStatusEx`, `GetPerformanceInfo`, SMBIOS via WMI | no |
| Drives: space, read/write, activity | `DriveInfo`, LogicalDisk perf counters | no |
| NVMe temperature | `IOCTL_STORAGE_QUERY_PROPERTY` (temperature property) | no |
| SATA SMART temperature | LibreHardwareMonitorLib | yes |
| Network rate, adapter, link type/speed | `NetworkInterface` statistics (auto-follows adapter changes) | no |
| Ping | ICMP to your default gateway (or a host you set) | no |
| Battery: charge, rate, voltage, current, capacity, health, cycles, temp | `IOCTL_BATTERY_*` + `GetSystemPowerStatus` | no |
| Display, refresh rate, HDR, monitor name | `QueryDisplayConfig` | no |
| Power plan / Windows power mode | PowrProf (read-only) | no |
| OEM performance profile (Lenovo Quiet/Balanced/Performance) | Lenovo GameZone WMI (read-only) | yes |
| FPS / frame times | ETW present events (PresentMon technique) — see [docs/FPS.md](docs/FPS.md) | admin **or** *Performance Log Users* |

Anything unavailable shows **N/A**, and Settings explains why (hover-free: Overview, Sensors and Diagnostics pages).
Nothing is ever faked or estimated without being labeled.

### Getting the admin-only sensors without running as admin

* **FPS:** Settings → Overview → *Enable FPS without admin (one-time)* → sign out and in once.
* **CPU temperature & OEM sensors:** Settings → General → *Start elevated* registers a logon scheduled task with
  highest privileges (one UAC prompt now, none later). Or use *Restart as administrator* for the current session.

## Limitations (honest list)

* **Exclusive full-screen** games bypass the compositor; no non-injected overlay can draw over them. Use borderless /
  windowed full-screen (most modern games use flip-model "fullscreen optimizations" and work fine).
* **FPS** is the application's present rate with ~1 s latency (ETW buffering); OpenGL/Vulkan coverage is best-effort.
  Requires admin or *Performance Log Users* (one-time). Details in [docs/FPS.md](docs/FPS.md).
* **CPU temperature** on many laptops (including Lenovo Legion/LOQ) is only exposed to administrators.
* **GPU hotspot** temperature isn't exposed by NVML; it's shown when LibreHardwareMonitor provides it (AMD).
* **Laptop GPU fans** are usually EC-controlled and not reported by the GPU (shown as N/A with that reason).
* **Battery temperature** is rarely implemented by laptop firmware.
* **Blur** (experimental) uses an undocumented compositor API; on some Windows builds it fills the square window bounds.
  The default look uses a translucent panel with a soft shadow instead.
* The LibreHardwareMonitor **kernel driver** option is off by default: some anti-cheat systems refuse to start games
  while hardware-access drivers are loaded.

## Privacy

All data stays local. No telemetry, analytics, update checks or crash uploads. The only network traffic is the optional
ping to your own router. Your IP address is hidden on the HUD unless you enable it; app names can be hidden too.

## Configuration

* Settings: `%APPDATA%\PerfHud\settings.json` — fully documented in [docs/CONFIGURATION.md](docs/CONFIGURATION.md)
* Logs: `%LOCALAPPDATA%\PerfHud\logs` · Sessions: `%LOCALAPPDATA%\PerfHud\sessions` · Screenshots: `Pictures\PerfHud`

## Architecture & adding sensors

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). In short: declare the metric in `MetricRegistry`, publish it from a
`MonitorBase` subclass (`ctx.Store.Set("my.metric", value, "reason if null")`), register the monitor in `AppHost` —
it's instantly available in the editor, picker, alerts and HUD. Each monitor fails independently with backoff.

## License

MIT. Uses [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) and
NVIDIA NVML (part of the NVIDIA driver).
