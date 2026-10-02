using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerfHud.Core;

namespace PerfHud.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON. Saves are debounced and atomic (write temp + replace).</summary>
public sealed class SettingsService : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Timer _saveTimer;
    private int _dirty;

    public AppSettings Current { get; private set; }

    /// <summary>Raised (on the thread that changed the value) after any settings change.</summary>
    public event Action<object, string?>? Changed;

    public SettingsService()
    {
        Current = Load();
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        Observable.AnyChanged += OnAnyChanged;
    }

    private void OnAnyChanged(object sender, string? prop)
    {
        Interlocked.Exchange(ref _dirty, 1);
        _saveTimer.Change(600, Timeout.Infinite);
        Changed?.Invoke(sender, prop);
    }

    private static AppSettings Load()
    {
        Observable.SuppressGlobal = true;
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings file was unreadable; starting from defaults (a backup was kept)", ex);
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
        }
        finally { Observable.SuppressGlobal = false; }

        var d = new AppSettings();
        Observable.SuppressGlobal = true;
        d.Normalize();
        Observable.SuppressGlobal = false;
        return d;
    }

    public void SaveNow()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0 && File.Exists(AppPaths.SettingsFile)) return;
        try
        {
            string json;
            lock (this) json = JsonSerializer.Serialize(Current, JsonOptions);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex)
        {
            Log.Once("settings-save", LogLevel.Error, $"Failed to save settings: {ex.Message}");
        }
    }

    public string Export() => JsonSerializer.Serialize(Current, JsonOptions);

    /// <summary>Replaces the current settings in place (keeps object identity for bindings by copying JSON).</summary>
    public bool Import(string json, out string? error)
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? throw new InvalidDataException("Empty settings");
            s.Normalize();
            Current = s;
            Interlocked.Exchange(ref _dirty, 1);
            SaveNow();
            Changed?.Invoke(s, null);
            error = null;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public void ResetToDefaults()
    {
        var s = new AppSettings();
        s.Normalize();
        Current = s;
        Interlocked.Exchange(ref _dirty, 1);
        SaveNow();
        Changed?.Invoke(s, null);
    }

    public void Dispose()
    {
        Observable.AnyChanged -= OnAnyChanged;
        SaveNow();
        _saveTimer.Dispose();
    }
}
