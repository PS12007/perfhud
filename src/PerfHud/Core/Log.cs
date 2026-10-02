using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace PerfHud.Core;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Small, allocation-light file logger. Writes happen on a background thread.
/// Repeated messages with the same key are rate-limited so a failing sensor can't flood the log.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 2048);
    private static readonly ConcurrentDictionary<string, (DateTime last, int suppressed)> RateLimits = new();
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(10);
    private static Thread? _writer;
    private static string? _file;

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static string CurrentFile => _file ?? Path.Combine(AppPaths.LogDir, $"perfhud-{DateTime.Now:yyyyMMdd}.log");

    public static void Start()
    {
        if (_writer != null) return;
        _file = Path.Combine(AppPaths.LogDir, $"perfhud-{DateTime.Now:yyyyMMdd}.log");
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "PerfHud.Log", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
        CleanupOld();
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, ex == null ? msg : $"{msg}: {ex}");

    /// <summary>Logs at most once per rate window for a given key; counts suppressed repeats.</summary>
    public static void Once(string key, LogLevel level, string msg)
    {
        var now = DateTime.UtcNow;
        if (RateLimits.TryGetValue(key, out var st) && now - st.last < RateWindow)
        {
            RateLimits[key] = (st.last, st.suppressed + 1);
            return;
        }
        var suffix = st.suppressed > 0 ? $" (repeated {st.suppressed}x since last report)" : "";
        RateLimits[key] = (now, 0);
        Write(level, msg + suffix);
    }

    public static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        Queue.TryAdd(line);
    }

    private static void WriterLoop()
    {
        var sb = new StringBuilder();
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            sb.Clear().AppendLine(line);
            while (Queue.TryTake(out var more)) sb.AppendLine(more);
            try
            {
                var file = Path.Combine(AppPaths.LogDir, $"perfhud-{DateTime.Now:yyyyMMdd}.log");
                _file = file;
                if (File.Exists(file) && new FileInfo(file).Length > 5 * 1024 * 1024) continue; // hard cap per day
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch { /* logging must never crash the app */ }
        }
    }

    public static void Flush()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (Queue.Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
    }

    private static void CleanupOld()
    {
        try
        {
            foreach (var f in Directory.GetFiles(AppPaths.LogDir, "perfhud-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch { }
    }
}
