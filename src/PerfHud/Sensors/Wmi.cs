using System.Management;
using PerfHud.Core;

namespace PerfHud.Sensors;

/// <summary>Tiny WMI helper for one-off static queries (hardware info). Never used in hot paths.</summary>
public static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2")
    {
        var result = new List<Dictionary<string, object?>>();
        try
        {
            using var s = new ManagementObjectSearcher(scope, wql);
            s.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (ManagementBaseObject o in s.Get())
            {
                using (o)
                {
                    var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in o.Properties) d[p.Name] = p.Value;
                    result.Add(d);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Once($"wmi-{wql}", LogLevel.Warn, $"WMI query failed ({wql}): {ex.Message}");
        }
        return result;
    }

    public static Dictionary<string, object?>? First(string wql, string scope = @"root\cimv2") => Query(wql, scope).FirstOrDefault();

    public static string? Str(this Dictionary<string, object?>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
        var s = v.ToString()?.Trim();
        return string.IsNullOrEmpty(s) || s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) || s == "None" ? null : s;
    }

    public static double? Num(this Dictionary<string, object?>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
        try { return Convert.ToDouble(v); } catch { return null; }
    }
}
