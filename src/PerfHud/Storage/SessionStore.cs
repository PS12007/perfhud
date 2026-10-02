using System.Globalization;
using System.Text.Json;
using PerfHud.Core;

namespace PerfHud.Storage;

public sealed class SessionSamples
{
    public List<string> Columns { get; } = new();
    public List<double> Time { get; } = new();
    public Dictionary<string, List<double>> Values { get; } = new();
}

/// <summary>Reads, exports and prunes recorded sessions in %LOCALAPPDATA%\PerfHud\sessions.</summary>
public static class SessionStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static List<SessionSummary> List()
    {
        var list = new List<SessionSummary>();
        foreach (var f in Directory.EnumerateFiles(AppPaths.SessionDir, "*.json"))
        {
            try
            {
                var s = JsonSerializer.Deserialize<SessionSummary>(File.ReadAllText(f), Json);
                if (s != null) list.Add(s);
            }
            catch (Exception ex) { Log.Once($"session-read-{f}", LogLevel.Warn, $"Unreadable session {f}: {ex.Message}"); }
        }
        return list.OrderByDescending(s => s.Start).ToList();
    }

    public static SessionSamples LoadSamples(string id)
    {
        var r = new SessionSamples();
        var path = Path.Combine(AppPaths.SessionDir, id + ".csv");
        if (!File.Exists(path)) return r;
        using var reader = new StreamReader(path);
        var header = reader.ReadLine()?.Split(',');
        if (header == null) return r;
        for (int i = 2; i < header.Length; i++) { r.Columns.Add(header[i]); r.Values[header[i]] = new List<double>(); }
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(',');
            if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
            r.Time.Add(t);
            for (int i = 2; i < header.Length; i++)
            {
                double v = i < parts.Length && double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : double.NaN;
                r.Values[header[i]].Add(v);
            }
        }
        return r;
    }

    public static void ExportCsv(SessionSummary s, string target)
    {
        var src = Path.Combine(AppPaths.SessionDir, s.Id + ".csv");
        using var w = new StreamWriter(target);
        w.WriteLine($"# PerfHud session: {s.Name}; app={s.App}; start={s.Start:s}; duration_s={s.DurationSeconds:0}; avg_fps={s.AvgFps}; low1={s.Low1Fps}; battery_used={s.BatteryUsed}");
        if (File.Exists(src)) w.Write(File.ReadAllText(src));
    }

    public static void ExportJson(SessionSummary s, string target)
    {
        var samples = LoadSamples(s.Id);
        var rows = new List<Dictionary<string, object?>>();
        for (int i = 0; i < samples.Time.Count; i++)
        {
            var d = new Dictionary<string, object?> { ["t"] = samples.Time[i] };
            foreach (var c in samples.Columns)
            {
                var v = samples.Values[c][i];
                d[c] = double.IsNaN(v) ? null : v;
            }
            rows.Add(d);
        }
        File.WriteAllText(target, JsonSerializer.Serialize(new { summary = s, samples = rows }, Json));
    }

    public static void Delete(string id)
    {
        foreach (var ext in new[] { ".json", ".csv" })
        {
            try { File.Delete(Path.Combine(AppPaths.SessionDir, id + ext)); } catch { }
        }
    }

    public static void Prune(int retentionDays)
    {
        if (retentionDays <= 0) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(AppPaths.SessionDir))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-retentionDays)) File.Delete(f);
        }
        catch { }
    }
}
