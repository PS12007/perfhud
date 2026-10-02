using System.Management;
using PerfHud.Core;
using PerfHud.Sensors.Native;

namespace PerfHud.Sensors;

/// <summary>
/// Lenovo Legion/LOQ "GameZone" WMI interface (the one Lenovo Vantage uses). Read-only use:
/// CPU/GPU temperature and the current performance (fan) mode. Windows restricts this class to administrators.
/// </summary>
public sealed class LenovoGameZoneProvider : IDisposable
{
    private ManagementObject? _gz;
    public string Status { get; private set; } = "Not started";

    public bool TryOpen(out string reason)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            _gz = s.Get().Cast<ManagementObject>().FirstOrDefault();
            if (_gz == null) { reason = "Lenovo GameZone interface not present"; Status = reason; return false; }
            // Probe once: throws "Access denied" when not elevated.
            _ = Invoke("GetCPUTemp");
            Status = "Active (Lenovo GameZone)";
            reason = "";
            return true;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            reason = "Lenovo GameZone sensors require administrator rights";
        }
        catch (Exception ex)
        {
            reason = $"Lenovo GameZone unavailable: {ex.Message}";
        }
        Status = reason;
        Dispose();
        return false;
    }

    private uint? Invoke(string method)
    {
        if (_gz == null) return null;
        using var r = _gz.InvokeMethod(method, null, null);
        var d = r?["Data"];
        return d == null ? null : Convert.ToUInt32(d);
    }

    public double? CpuTemp() => Valid(Invoke("GetCPUTemp"));
    public double? GpuTemp() => Valid(Invoke("GetGPUTemp"));

    public string? PerformanceMode()
    {
        try
        {
            return Invoke("GetSmartFanMode") switch
            {
                1 => "Quiet",
                2 => "Balanced",
                3 => "Performance",
                224 => "Extreme",
                255 => "Custom",
                null => null,
                var v => $"Mode {v}",
            };
        }
        catch { return null; }
    }

    private static double? Valid(uint? v) => v is > 0 and < 130 ? v : null;

    public void Dispose()
    {
        _gz?.Dispose();
        _gz = null;
    }
}

/// <summary>ACPI thermal zones: via perf counters (no admin, if the firmware exposes them) or WMI (admin).</summary>
public sealed class AcpiThermalProvider : IDisposable
{
    private PdhQuery? _q;
    private PdhCounter? _c;
    private readonly List<(string, double)> _buf = new();
    private bool _useWmi;
    public string Status { get; private set; } = "Not started";

    public bool TryOpen(out string reason)
    {
        if (PdhQuery.CounterExists(@"\Thermal Zone Information(*)\Temperature"))
        {
            _q = new PdhQuery();
            _c = _q.Add(@"\Thermal Zone Information(*)\Temperature");
            _q.Collect();
            _c.Values(_buf);
            if (_buf.Count > 0) { Status = "Active (perf counters)"; reason = ""; return true; }
            _q.Dispose(); _q = null;
        }
        if (Elevation.IsAdmin)
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                if (s.Get().Count > 0) { _useWmi = true; Status = "Active (WMI)"; reason = ""; return true; }
            }
            catch { }
        }
        reason = "Firmware exposes no ACPI thermal zones to Windows";
        Status = reason;
        return false;
    }

    public IEnumerable<(string Name, double Celsius)> Read()
    {
        var list = new List<(string, double)>();
        if (_q != null && _c != null)
        {
            _q.Collect();
            _c.Values(_buf);
            foreach (var (n, k) in _buf)
                if (k > 200 && k < 420) list.Add((Clean(n), k - 273.15));
        }
        else if (_useWmi)
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    var t = Convert.ToDouble(o["CurrentTemperature"]) / 10 - 273.15;
                    if (t > -20 && t < 140) list.Add((Clean(o["InstanceName"]?.ToString() ?? "TZ"), t));
                }
            }
        }
        return list;
    }

    private static string Clean(string n)
    {
        var i = n.LastIndexOf('.');
        n = i >= 0 ? n[(i + 1)..] : n;
        return "ACPI " + n.Replace("_0", "").Trim('_');
    }

    public void Dispose() { _q?.Dispose(); _q = null; }
}
