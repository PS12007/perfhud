# Configuration

Everything is editable in the Settings window and applies instantly. Settings are saved automatically
(debounced, written atomically) — you never need to edit files by hand, but you can.

| What | Where |
|---|---|
| Settings | `%APPDATA%\PerfHud\settings.json` |
| Logs (14 days, ≤5 MB/day) | `%LOCALAPPDATA%\PerfHud\logs\` |
| Recorded sessions (`.csv` samples + `.json` summary) | `%LOCALAPPDATA%\PerfHud\sessions\` |
| HUD screenshots | `%USERPROFILE%\Pictures\PerfHud\` |
| Startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PerfHud` (or scheduled task *PerfHud (elevated sensors)*) |

If `settings.json` is unreadable PerfHud keeps a `settings.json.broken-<timestamp>` copy and starts with defaults.
Settings → Diagnostics has **Export / Import / Reset everything**.

## settings.json reference

```jsonc
{
  "general": {
    "startWithWindows": false,
    "startElevated": false,        // logon task with highest privileges → admin-only sensors without UAC prompts
    "startMinimized": true,        // don't open the settings window on launch
    "showHudOnStartup": true,
    "language": "en",
    "hudRefreshMs": 500,           // HUD redraw interval
    "temperatureUnit": "Celsius",  // Celsius | Fahrenheit
    "networkUnit": "Bytes"         // Bytes | Bits
  },
  "appearance": {
    "themeName": "Midnight",
    "background": "#0A0D12", "panel": "#11161D", "accent": "#00D9FF",
    "success": "#42E88A", "warning": "#FFC857", "hot": "#FF8C42", "danger": "#FF4D6D",
    "cool": "#4CC9F0", "text": "#F2F5F7", "muted": "#7C8795",
    "backgroundOpacity": 0.8,      // panel only
    "opacity": 1.0,                // whole HUD
    "scale": 1.0, "fontScale": 1.0, "cornerRadius": 10,
    "animationSpeed": 1.0,         // 0 = off
    "labelFont": "Segoe UI", "valueFont": "Bahnschrift",
    "blur": false, "shadow": true, "border": true,
    "highContrast": false, "colorVision": "Standard",   // Standard | ColorBlindSafe
    "showIcons": true, "showLabels": true
  },
  "hud": {
    "enabled": true,
    "activePreset": "Gaming",      // Minimal | Gaming | Full | any custom layout name
    "corner": "TopLeft",           // TopLeft … BottomRight, Custom
    "offsetX": 16, "offsetY": 16,  // margin in DIPs
    "customX": 40, "customY": 40,  // physical px relative to the monitor (corner = Custom; set by dragging)
    "monitor": "",                 // "" primary, "*active" follow foreground window, or "\\\\.\\DISPLAY2"
    "orientation": "Vertical",     // Vertical | Horizontal (transposes the layout grid)
    "alignment": "Left",
    "compact": false, "showGraphs": true,
    "clickThrough": true,          // false = unlocked: drag to move, Ctrl+wheel to scale
    "respectTaskbar": true,
    "hideFromCapture": false       // exclude overlay from screenshots/recordings/streams
  },
  "performance": {
    "sensorIntervalMs": 1000,
    "graphHistorySeconds": 60,     // 5 | 10 | 30 | 60 | 300
    "fpsEnabled": true,
    "fpsStatsWindowSeconds": 30,   // window for avg / 1% / 0.1% lows
    "fpsTarget": "ForegroundThenMostActive",
    "cpuLimitPercent": 1.5,        // PerfHud slows itself down above this
    "adaptiveThrottle": true,
    "pingEnabled": true,
    "pingHost": ""                 // "" = default gateway
  },
  "battery": {
    "optimizationEnabled": true,
    "intervalMultiplier": 2.0,
    "reduceAnimations": true,
    "disableExpensiveSensors": true
  },
  "thresholds": {
    "cpu":     { "warm": 70, "hot": 85, "critical": 95 },
    "gpu":     { "warm": 70, "hot": 80, "critical": 88 },
    "storage": { "warm": 50, "hot": 60, "critical": 70 },
    "battery": { "warm": 40, "hot": 45, "critical": 55 },
    "other":   { "warm": 60, "hot": 75, "critical": 90 },
    "usageWarn": 75, "usageCritical": 92, "memoryWarn": 80, "memoryCritical": 92,
    "fpsWarn": 60, "fpsCritical": 30, "batteryWarn": 20, "batteryCritical": 10
  },
  "sensors": {
    "useLibreHardwareMonitor": true,
    "allowKernelDriver": false,    // opt-in; see README "Anti-cheat"
    "avoidWakingDiscreteGpu": true,
    "useVendorWmi": true,
    "diskTemperatures": true
  },
  "profiles": {
    "enabled": true,
    "desktopAction": "",           // "" nothing, "(Hidden)", or a layout name
    "items": [
      { "enabled": true, "exeName": "Cyberpunk2077.exe", "trigger": "WhileFocused", "preset": "Gaming",
        "hide": false, "corner": "TopRight", "opacity": 0.9, "scale": 1.1, "compact": null, "autoRecord": true },
      { "exeName": "chrome.exe", "preset": "Minimal" }
    ]
  },
  "history": { "autoRecord": "GamesDetected", "sampleIntervalSeconds": 1, "retentionDays": 90 },
  "privacy": { "showLocalIp": false, "showProcessNames": true },
  "hotkeys": [ { "action": "ToggleHud", "gesture": "F9" } /* … */ ],
  "alerts": [
    { "enabled": true, "name": "GPU hot", "metricId": "gpu.temp", "op": "Above", "threshold": 85,
      "durationSeconds": 3, "cooldownSeconds": 120, "onlyOnBattery": false, "actions": "Notification, HudWarning" }
  ],
  "layouts": [ /* custom layouts, see below */ ]
}
```

Alert thresholds use human units: °C, %, FPS, ms, W, **GB** for memory/disk space and **MB/s** for data rates.

## Layouts

A layout is a grid. Each component has a type, a metric id and a position:

```jsonc
{
  "name": "My overlay",
  "components": [
    { "type": "BigNumber", "metricId": "fps.current", "secondaryMetricId": "fps.app", "col": 0, "row": 0, "colSpan": 2 },
    { "type": "Number", "metricId": "cpu.usage", "secondaryMetricId": "cpu.temp", "label": "CPU", "col": 0, "row": 1 },
    { "type": "Graph", "metricId": "gpu.temp", "col": 0, "row": 2, "colSpan": 2, "height": 28, "graphSeconds": 30, "detailOnly": true },
    { "type": "ProgressBar", "metricId": "ram.used", "secondaryMetricId": "ram.total", "ratio": true, "col": 0, "row": 3, "colSpan": 2 }
  ]
}
```

Component types: `Number, BigNumber, Percentage, ProgressBar, Graph, FrameTimeGraph, Gauge, Text, Icon, Divider,
Spacer, CoreGrid, SensorList, DriveList`. Optional fields: `label, text, icon, color ("#RRGGBB" or "" for automatic),
fontScale, height, width, graphSeconds, detailOnly (hidden in compact mode), showLabel, showIcon, ratio`.

Metric ids are listed in Settings → Metric picker and in `src/PerfHud/Monitoring/MetricRegistry.cs`
(e.g. `cpu.usage`, `cpu.temp`, `cpu.power`, `gpu.vram.used`, `bat.power`, `net.ping`, `sys.model`).
Per-drive metrics are dynamic: `disk.c.free`, `disk.d.temp`, `disk.c.read`, …
