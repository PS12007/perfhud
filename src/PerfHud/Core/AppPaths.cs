using System.IO;

namespace PerfHud.Core;

/// <summary>Well-known file locations. Everything stays local to the user profile.</summary>
public static class AppPaths
{
    public const string AppName = "PerfHud";

    /// <summary>%APPDATA%\PerfHud — settings (roams with the profile).</summary>
    public static string ConfigDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName));

    /// <summary>%LOCALAPPDATA%\PerfHud — logs, sessions, caches (machine-local).</summary>
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName));

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string LogDir => Ensure(Path.Combine(DataDir, "logs"));
    public static string SessionDir => Ensure(Path.Combine(DataDir, "sessions"));

    public static string ScreenshotDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppName));

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PerfHud.exe");

    private static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* reported when used */ }
        return dir;
    }
}
