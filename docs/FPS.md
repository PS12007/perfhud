# How PerfHud measures FPS

## Method

PerfHud uses the same technique as Intel/Microsoft **PresentMon** (which also powers tools like CapFrameX and
the frame-time data in several vendor overlays): it opens a real-time **Event Tracing for Windows (ETW)** session
and listens to the events Windows' graphics stack already emits whenever an application presents a frame:

| Provider | Events | Covers |
|---|---|---|
| Microsoft-Windows-DXGI | Present_Start, PresentMultiplaneOverlay_Start | Direct3D 10/11/12 (most games) |
| Microsoft-Windows-D3D9 | Present_Start | Direct3D 9 games |
| Microsoft-Windows-DxgKrnl | Present_Info, PresentHistory(Detailed)_Start | best-effort for OpenGL / Vulkan |

Event-ID filtering is applied **in the kernel** (EVENT_FILTER_TYPE_EVENT_ID), so PerfHud only receives the handful of
events it needs — the overhead is negligible even at very high frame rates.

From the timestamps of consecutive presents of the measured process PerfHud computes:

* **Current FPS / frame time** – mean over the most recent second.
* **Average FPS** – frames ÷ time over the statistics window (default 30 s).
* **1% low / 0.1% low** – the average of the slowest 1% / 0.1% of frame times in the window, converted to FPS
  (0.1% low needs ≥ 1000 frames).
* **Frame-time graph** – every individual frame, decimated per pixel.
* **Sessions** – an exact frame-time histogram (0.05 ms bins) so session-wide lows are computed from every frame.

The measured app is the **foreground window's process**; if it isn't rendering, PerfHud (by default) falls back to the
busiest rendering process, ignoring the desktop compositor and shell.

## Why this is safe

* Nothing is injected into any process, no hooks are installed in games, no graphics APIs are intercepted.
* ETW is a passive, documented OS facility. Anti-cheat systems don't treat it as tampering.

## Requirements

Windows only lets administrators **or members of the built-in "Performance Log Users" group** start ETW sessions.
PerfHud runs as a normal user, so you have two options:

1. **Recommended:** Settings → Overview (or Performance) → *Enable FPS without admin (one-time)*. This adds your account
   to *Performance Log Users* (one UAC prompt). **Sign out and back in once**; FPS then works forever without admin.
2. Run PerfHud elevated (Settings → Diagnostics → *Restart as administrator*, or General → *Start elevated*).

Until then the FPS tiles show `N/A` with a hint, and everything else works normally.

## Limitations

* **Present rate, not displayed rate.** Like PresentMon's `MsBetweenPresents`, PerfHud measures how fast the app submits
  frames. With V-Sync/G-Sync caps this matches what you see; frames dropped by the compositor aren't distinguished.
* **~1 second latency.** ETW delivers real-time events in buffers flushed once per second, so numbers lag slightly.
  Timestamps are exact, so statistics are unaffected.
* **OpenGL / Vulkan** are covered through kernel present events on a best-effort basis; some drivers present through
  DXGI interop (then it's exact), others may not emit per-frame kernel events.
* **Exclusive full-screen** games bypass the desktop compositor, so *no* non-injected overlay (including PerfHud and
  Game Bar's legacy widgets) can draw on top of them. FPS measurement still works; switch the game to borderless /
  windowed full-screen to see the HUD. Modern Windows 11 games almost always use flip-model "fullscreen optimizations",
  which behave like borderless and show the HUD fine.
