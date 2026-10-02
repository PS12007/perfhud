using System.Diagnostics;
using System.Security.Principal;

namespace PerfHud.Core;

public static class Elevation
{
    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>True if the current user is in "Performance Log Users" (can start ETW sessions without admin).</summary>
    public static bool IsPerformanceLogUser()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            // S-1-5-32-559 = BUILTIN\Performance Log Users
            return new WindowsPrincipal(id).IsInRole(new SecurityIdentifier("S-1-5-32-559"));
        }
        catch { return false; }
    }

    /// <summary>Relaunches PerfHud elevated. Returns false if the user declined UAC.</summary>
    public static bool RestartElevated(string args = "--elevated-restart")
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExePath, args) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Elevated restart cancelled/failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// One-time elevated action: adds the current user to "Performance Log Users" so the FPS counter
    /// (ETW) works without running PerfHud as administrator. Takes effect after sign-out.
    /// </summary>
    public static bool AddCurrentUserToPerformanceLogUsers()
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().Name;
            // Use the well-known SID so this works on non-English Windows.
            var cmd = "$g = (New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-559')).Translate([System.Security.Principal.NTAccount]).Value.Split('\\')[-1]; " +
                      $"Add-LocalGroupMember -Group $g -Member '{user.Replace("'", "''")}' -ErrorAction SilentlyContinue";
            var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{cmd}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            });
            p?.WaitForExit(20000);
            return p?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not add user to Performance Log Users: {ex.Message}");
            return false;
        }
    }
}
