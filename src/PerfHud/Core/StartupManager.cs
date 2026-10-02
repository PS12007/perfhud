using System.Diagnostics;
using Microsoft.Win32;

namespace PerfHud.Core;

/// <summary>
/// "Start with Windows" without a service: the per-user Run key (normal), or — if the user wants admin-only sensors
/// without a UAC prompt at every logon — a per-user logon scheduled task with highest privileges (created once, elevated).
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PerfHud";
    private const string TaskName = "PerfHud (elevated sensors)";

    public static string Command => $"\"{AppPaths.ExePath}\" --startup";

    public static bool IsRunKeySet()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Contains(Path.GetFileName(AppPaths.ExePath), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsTaskSet() => RunSchtasks($"/Query /TN \"{TaskName}\"", elevated: false) == 0;

    /// <summary>Applies the desired state. Returns an error message for the UI, or null.</summary>
    public static string? Apply(bool enabled, bool elevated)
    {
        try
        {
            if (!enabled)
            {
                SetRunKey(false);
                if (IsTaskSet()) RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevated: !Elevation.IsAdmin);
                return null;
            }
            if (elevated)
            {
                var xmlPath = Path.Combine(AppPaths.DataDir, "startup-task.xml");
                File.WriteAllText(xmlPath, TaskXml(), System.Text.Encoding.Unicode);
                int rc = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", elevated: !Elevation.IsAdmin);
                if (rc != 0) return "Could not create the elevated startup task (UAC was declined?). Using the normal startup entry instead.";
                SetRunKey(false);
                return null;
            }
            SetRunKey(true);
            if (IsTaskSet()) RunSchtasks($"/Delete /TN \"{TaskName}\" /F", elevated: !Elevation.IsAdmin);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Startup registration failed", ex);
            return ex.Message;
        }
        finally
        {
            if (enabled && elevated && !IsTaskSet()) SetRunKey(true);
        }
    }

    private static void SetRunKey(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, Command);
        else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
    }

    private static int RunSchtasks(string args, bool elevated)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = elevated,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (elevated) psi.Verb = "runas";
        else { psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; }
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static string TaskXml()
    {
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Starts PerfHud at logon with access to admin-only sensors.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{esc(user)}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id="Author"><UserId>{esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author"><Exec><Command>{esc(AppPaths.ExePath)}</Command><Arguments>--startup</Arguments></Exec></Actions>
</Task>
""";
    }
}
