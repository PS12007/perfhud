using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PerfHud.Monitoring.Monitors;

/// <summary>Throughput of the active adapter. Reacts to adapter changes (Wi-Fi ↔ Ethernet, VPN, dock).</summary>
public sealed class NetworkMonitor : MonitorBase
{
    public override string Name => "Network";
    public override string[] Prefixes => new[] { "net.down", "net.up", "net.adapter", "net.type", "net.ip", "net.speed" };

    private NetworkInterface? _active;
    private long _lastRx, _lastTx, _lastTick;
    private volatile bool _rescan = true;
    private long _lastScan;

    /// <summary>Default gateway of the active adapter (used by the latency monitor).</summary>
    public static volatile IPAddress? Gateway;

    public override void Initialize(MonitorContext ctx)
    {
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvail;
        _rescan = true;
    }

    private void OnChanged(object? s, EventArgs e) => _rescan = true;
    private void OnAvail(object? s, NetworkAvailabilityEventArgs e) => _rescan = true;

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        long now = Environment.TickCount64;
        if (_rescan || _active == null || now - _lastScan > 30_000)
        {
            _rescan = false;
            _lastScan = now;
            var prev = _active?.Id;
            _active = PickActive();
            if (_active?.Id != prev) _lastTick = 0;
            if (_active == null)
            {
                store.SetText("net.adapter", "Disconnected");
                store.SetText("net.type", "Offline");
                store.Set("net.down", 0);
                store.Set("net.up", 0);
                store.SetText("net.ip", null);
                store.SetText("net.speed", null);
                Gateway = null;
                return;
            }
            var props = _active.GetIPProperties();
            Gateway = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            store.SetText("net.adapter", _active.Description.Length > 40 ? _active.Name : _active.Description);
            store.SetText("net.type", _active.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
                NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel => "VPN",
                NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Cellular",
                _ => _active.NetworkInterfaceType.ToString(),
            });
            var speed = _active.Speed;
            store.SetText("net.speed", speed > 0 ? speed >= 1_000_000_000 ? $"{speed / 1e9:0.#} Gbps" : $"{speed / 1e6:0} Mbps" : null);
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            store.SetText("net.ip", ctx.Settings.Privacy.ShowLocalIp ? ip?.ToString() : (ip != null ? "Hidden" : null));
        }
        if (_active == null) return;

        IPInterfaceStatistics st;
        try { st = _active.GetIPStatistics(); }
        catch (NetworkInformationException) { _rescan = true; return; }

        long rx = st.BytesReceived, tx = st.BytesSent;
        if (_lastTick != 0 && now > _lastTick)
        {
            double secs = (now - _lastTick) / 1000.0;
            store.Set("net.down", Math.Max(0, (rx - _lastRx) / secs));
            store.Set("net.up", Math.Max(0, (tx - _lastTx) / secs));
        }
        _lastRx = rx; _lastTx = tx; _lastTick = now;
    }

    private static NetworkInterface? PickActive()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)))
                .ToList();
            // Prefer physical adapters over virtual switches; VPN tunnels win if present (they carry the traffic).
            return candidates
                .OrderByDescending(n => n.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel ? 2 : 0)
                .ThenByDescending(n => n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || n.Description.Contains("Hyper-V") ? 0 : 1)
                .ThenByDescending(n => n.GetIPStatistics().BytesReceived)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    protected override void DisposeResources()
    {
        NetworkChange.NetworkAddressChanged -= OnChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvail;
        _active = null;
    }
}

/// <summary>ICMP latency to the default gateway (stays on the local network) or a user-chosen host.</summary>
public sealed class LatencyMonitor : MonitorBase
{
    public override string Name => "Latency";
    public override string[] Prefixes => new[] { "net.ping" };
    public override MonitorLane Lane => MonitorLane.Slow;
    public override bool IsExpensive => true;
    public override int GetIntervalMs(MonitorContext ctx) => Math.Max(2000, ctx.Settings.Performance.SensorIntervalMs * 2);

    private Ping? _ping;
    private int _failures;

    public override void Update(MonitorContext ctx)
    {
        var p = ctx.Settings.Performance;
        if (!p.PingEnabled) { ctx.Store.SetUnavailable("net.ping", "Latency probe disabled in settings"); return; }
        _ping ??= new Ping();
        string? host = string.IsNullOrWhiteSpace(p.PingHost) ? NetworkMonitor.Gateway?.ToString() : p.PingHost.Trim();
        if (host == null) { ctx.Store.SetUnavailable("net.ping", "No default gateway"); return; }
        try
        {
            var r = _ping.Send(host, 1000);
            if (r.Status == IPStatus.Success) { ctx.Store.Set("net.ping", r.RoundtripTime); _failures = 0; }
            else if (++_failures >= 2) ctx.Store.SetUnavailable("net.ping", $"{host} not responding ({r.Status})");
        }
        catch (PingException ex)
        {
            if (++_failures >= 2) ctx.Store.SetUnavailable("net.ping", ex.InnerException?.Message ?? ex.Message);
        }
    }

    protected override void DisposeResources() { _ping?.Dispose(); _ping = null; }
}
