using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class NetworkDetector
{
    private readonly ILogger<NetworkDetector> _log;
    private readonly ServiceState _state;

    public NetworkDetector(ILogger<NetworkDetector> log, ServiceState state)
    {
        _log = log;
        _state = state;
    }

    // Drei Zustände statt nur ja/nein: "Unknown" heißt "Check nicht anwendbar oder
    // nicht messbar" (z. B. SSID per LAN-Kabel) und darf weder für noch gegen
    // "zuhause" zählen.
    private enum CheckResult { Yes, No, Unknown }

    // Inconclusive: zu wenig belastbare Checks → wir raten NICHT, der Aufrufer
    // behält den aktuellen Zustand bei.
    public record DetectionResult(bool AtHome, bool Inconclusive, string Reason, int YesVotes, int Applicable);

    public async Task<DetectionResult> DetectAsync(CancellationToken ct)
    {
        var cfg = _state.Config.HomeDetection;
        var retries = Math.Max(0, _state.Config.General.CheckRetries);
        var reasons = new List<string>();
        int yes = 0, applicable = 0, configured = 0;

        void Tally(CheckResult r, string label, string detail = "")
        {
            switch (r)
            {
                case CheckResult.Yes:
                    yes++; applicable++; reasons.Add($"{label} ✓"); break;
                case CheckResult.No:
                    applicable++; reasons.Add($"{label} ✗{detail}"); break;
                default:
                    reasons.Add($"{label} ? (unbekannt{detail})"); break;
            }
        }

        // 1. Gateway-MAC vergleichen (zuverlässigster Check)
        if (!string.IsNullOrWhiteSpace(cfg.GatewayMac))
        {
            configured++;
            var mac = await GetDefaultGatewayMacAsync(retries, ct);
            CheckResult r = mac == null
                ? CheckResult.Unknown // ARP/Gateway nicht ermittelbar → nicht als "nein" werten
                : string.Equals(mac, NormalizeMac(cfg.GatewayMac), StringComparison.OrdinalIgnoreCase)
                    ? CheckResult.Yes : CheckResult.No;
            Tally(r, "Gateway-MAC", mac == null ? "" : $", gefunden: {mac}");
        }

        // 2. WLAN-SSID
        if (!string.IsNullOrWhiteSpace(cfg.Ssid))
        {
            configured++;
            var (ssid, hasWifi) = GetCurrentSsid();
            CheckResult r = !hasWifi
                ? CheckResult.Unknown // kein WLAN-Adapter aktiv (z. B. LAN-Kabel) → nicht werten
                : string.Equals(ssid, cfg.Ssid, StringComparison.Ordinal)
                    ? CheckResult.Yes : CheckResult.No;
            Tally(r, "SSID", hasWifi && ssid != null ? $", aktuell: {ssid}" : "");
        }

        // 3. Reachability eines internen Hosts (mehrfach sampeln gegen Paketverlust)
        if (!string.IsNullOrWhiteSpace(cfg.ReachableHost) && cfg.ReachablePort > 0)
        {
            configured++;
            var reachable = await IsReachableAsync(cfg.ReachableHost, cfg.ReachablePort,
                                                   TimeSpan.FromMilliseconds(800), retries, ct);
            Tally(reachable ? CheckResult.Yes : CheckResult.No,
                  $"{cfg.ReachableHost}:{cfg.ReachablePort}");
        }

        if (configured == 0)
            return new DetectionResult(false, true, "Keine Heimerkennungs-Checks konfiguriert", 0, 0);

        // Mindestanzahl auf die tatsächlich konfigurierten Checks deckeln, damit ein
        // Single-Check-Setup nicht dauerhaft "unterwegs" meldet.
        var min = Math.Max(1, Math.Min(_state.Config.General.MinChecksRequired, configured));
        var reason = string.Join(", ", reasons);

        // Zu wenig belastbare (Yes/No) Antworten → wir wissen es schlicht nicht.
        if (applicable < min)
            return new DetectionResult(false, true,
                $"{reason} (zu wenig belastbare Checks: {applicable}/{min})", yes, applicable);

        var atHome = yes >= min;
        return new DetectionResult(atHome, false, reason, yes, applicable);
    }

    private static string NormalizeMac(string mac) =>
        mac.Replace("-", ":").Replace(" ", "").ToUpperInvariant();

    private async Task<string?> GetDefaultGatewayMacAsync(int retries, CancellationToken ct)
    {
        var gateway = GetDefaultGatewayIp();
        if (gateway == null) return null;

        for (int attempt = 0; attempt <= retries; attempt++)
        {
            // Erst die ARP-Tabelle anstoßen: nach einem Netzwerkwechsel ist der
            // Eintrag oft noch leer, ein Ping zwingt die Auflösung.
            await PopulateArpAsync(gateway, ct);
            var mac = ReadArpMac(gateway);
            if (mac != null) return mac;
            try { await Task.Delay(300, ct); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private static IPAddress? GetDefaultGatewayIp()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var gw = ni.GetIPProperties().GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                     && !g.Address.Equals(IPAddress.Any));
            if (gw != null) return gw.Address;
        }
        return null;
    }

    private async Task PopulateArpAsync(IPAddress gateway, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            await ping.SendPingAsync(gateway, 500).WaitAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Gateway-Ping zum Füllen der ARP-Tabelle fehlgeschlagen");
        }
    }

    private string? ReadArpMac(IPAddress gateway)
    {
        try
        {
            var output = OperatingSystem.IsWindows()
                ? RunCommand("arp", "-a", gateway.ToString())
                : RunCommand("ip", "neigh", "show", gateway.ToString());

            var match = Regex.Match(output, @"([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}");
            return match.Success ? NormalizeMac(match.Value) : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gateway-MAC konnte nicht ermittelt werden");
            return null;
        }
    }

    // Liefert (ssid, hasWifi): hasWifi=false heißt "kein WLAN-Adapter aktiv" → Unknown.
    private (string? Ssid, bool HasWifi) GetCurrentSsid()
    {
        if (!OperatingSystem.IsWindows())
            return GetCurrentSsidLinux();

        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return (null, false);
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);

            // Kein WLAN-Interface vorhanden / verbunden → kein "State"/keine Daten.
            bool hasWifi = false;
            string? ssid = null;
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                    hasWifi = true;
                if (trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase)) continue;
                if (!trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)) continue;
                var idx = trimmed.IndexOf(':');
                if (idx < 0) continue;
                ssid = trimmed[(idx + 1)..].Trim();
                hasWifi = true;
            }
            // SSID leer obwohl Adapter da → nicht verbunden, das ist ein echtes "nein".
            return (ssid, hasWifi);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SSID konnte nicht ermittelt werden");
            return (null, false);
        }
    }

    private (string? Ssid, bool HasWifi) GetCurrentSsidLinux()
    {
        try
        {
            var output = RunCommand("nmcli", "-t", "-f", "ACTIVE,SSID", "dev", "wifi");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var active = line[..idx];
                var ssid = line[(idx + 1)..].Replace("\\:", ":").Trim();
                if (active == "yes")
                    return (string.IsNullOrEmpty(ssid) ? null : ssid, true);
            }

            // Fallback für minimalere Systeme ohne aktives NetworkManager-WLAN-Listing.
            output = RunCommand("iwgetid", "-r").Trim();
            if (!string.IsNullOrWhiteSpace(output)) return (output, true);

            return (null, false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SSID konnte nicht ermittelt werden");
            return (null, false);
        }
    }

    private async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout,
                                              int retries, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            if (await TryConnectAsync(host, port, timeout, ct)) return true;
            if (attempt < retries)
            {
                try { await Task.Delay(200, ct); }
                catch (OperationCanceledException) { return false; }
            }
        }
        return false;
    }

    private static async Task<bool> TryConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, cts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string RunCommand(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi);
        if (p == null) return "";
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return output;
    }
}
