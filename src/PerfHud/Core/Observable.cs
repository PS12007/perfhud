using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PerfHud.Core;

/// <summary>
/// Base for settings/model objects. Every property change also raises the global
/// <see cref="AnyChanged"/> event, which the settings service (persistence) and the HUD (live preview) observe.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised for any change on any Observable model instance (sender, property name).</summary>
    public static event Action<object, string?>? AnyChanged;

    [JsonIgnore] public static bool SuppressGlobal { get; set; }

    [ThreadStatic] private static int _quiet;

    /// <summary>
    /// Suppresses global change notifications on this thread for the scope (use when building throwaway objects such as
    /// built-in presets or clones, which aren't part of the saved settings and must not trigger rebuilds/saves).
    /// </summary>
    public static IDisposable Quiet()
    {
        _quiet++;
        return new QuietScope();
    }

    private sealed class QuietScope : IDisposable
    {
        private bool _done;
        public void Dispose() { if (!_done) { _done = true; _quiet--; } }
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (!SuppressGlobal && _quiet == 0) AnyChanged?.Invoke(this, name);
    }

    public static void RaiseGlobal(object sender, string? name)
    {
        if (!SuppressGlobal && _quiet == 0) AnyChanged?.Invoke(sender, name);
    }
}

/// <summary>ObservableCollection that reports structural changes through <see cref="Observable.AnyChanged"/>.</summary>
public class ObservableList<T> : ObservableCollection<T>
{
    public ObservableList() { }
    public ObservableList(IEnumerable<T> items) : base(items) { }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        Observable.RaiseGlobal(this, "Items");
    }
}
