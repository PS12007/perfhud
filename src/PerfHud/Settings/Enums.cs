namespace PerfHud.Settings;

public enum HudCorner { TopLeft, TopCenter, TopRight, MiddleLeft, MiddleRight, BottomLeft, BottomCenter, BottomRight, Custom }
public enum HudOrientation { Vertical, Horizontal }
public enum HudAlignment { Left, Center, Right }
public enum TemperatureUnit { Celsius, Fahrenheit }
public enum NetworkUnit { Bytes, Bits }
public enum ColorVisionMode { Standard, ColorBlindSafe }
public enum AutoRecordMode { Off, ProfilesOnly, GamesDetected }
public enum CompareOp { Above, Below }
public enum ProfileTrigger { WhileFocused, WhileRunning }
public enum FpsTargetMode { ForegroundApp, ForegroundThenMostActive }

[Flags]
public enum AlertActions { None = 0, Notification = 1, HudWarning = 2, Sound = 4 }

public enum HotkeyAction
{
    ToggleHud,
    CyclePreset,
    ToggleCompact,
    ToggleGraphs,
    ToggleClickThrough,
    ToggleSettings,
    TogglePause,
    ScreenshotHud,
    ResetPosition,
    ToggleRecording,
    OpenEditor,
}
