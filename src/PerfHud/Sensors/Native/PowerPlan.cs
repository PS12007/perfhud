using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

/// <summary>Reads (never writes) the active power plan and Windows 11 power mode.</summary>
public static class PowerPlan
{
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);

    public static string? ActivePlanName()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var g) != 0) return null;
        try
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return null;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                return PowerReadFriendlyName(IntPtr.Zero, g, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0
                    ? Marshal.PtrToStringUni(buf) : null;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { LocalFree(g); }
    }

    /// <summary>Windows 10/11 power mode slider ("Best performance", "Balanced", "Best power efficiency").</summary>
    public static string? PowerMode()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var o) != 0) return null;
            return o.ToString().ToLowerInvariant() switch
            {
                "ded574b5-45a0-4f42-8737-46345c09c238" => "Best performance",
                "3af9b8d9-7c97-431d-ad78-34a8bfea439f" => "Better performance",
                "961cc777-2547-4f9d-8174-7d86181b8a7a" => "Best power efficiency",
                "00000000-0000-0000-0000-000000000000" => "Balanced",
                _ => "Custom",
            };
        }
        catch (EntryPointNotFoundException) { return null; }
    }
}
