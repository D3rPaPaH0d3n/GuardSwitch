using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

[SupportedOSPlatform("windows")]
public class WindowsTunnelController : ITunnelController
{
    private readonly ILogger<WindowsTunnelController> _log;

    // Standard-Pfad von WireGuard für Windows
    private static readonly string WireGuardExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "WireGuard", "wireguard.exe");

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "WireGuard", "Data", "Configurations");

    public WindowsTunnelController(ILogger<WindowsTunnelController> log)
    {
        _log = log;
    }

    private static string ServiceNameFor(string tunnel) => $"WireGuardTunnel${tunnel}";

    public TunnelStatus GetStatus(string tunnelName)
    {
        var svcName = ServiceNameFor(tunnelName);
        try
        {
            using var sc = new ServiceController(svcName);
            sc.Refresh();
            var status = sc.Status switch
            {
                ServiceControllerStatus.Running => "Running",
                ServiceControllerStatus.Stopped => "Stopped",
                ServiceControllerStatus.StartPending => "Starting",
                ServiceControllerStatus.StopPending => "Stopping",
                _ => sc.Status.ToString()
            };
            return new TunnelStatus(tunnelName, sc.Status == ServiceControllerStatus.Running, status);
        }
        catch (InvalidOperationException)
        {
            // WireGuard hängt den Tunnel-Service erst beim Aktivieren ein und löscht ihn
            // beim Deaktivieren wieder. "Service existiert nicht" heißt also: Tunnel ist
            // entweder gerade nicht aktiv (Config liegt aber vor) oder gar nicht importiert.
            return ConfigExists(tunnelName)
                ? new TunnelStatus(tunnelName, false, "Inactive")
                : new TunnelStatus(tunnelName, false, "NotInstalled");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tunnel-Status für {Tunnel} fehlgeschlagen", tunnelName);
            return new TunnelStatus(tunnelName, false, "Error");
        }
    }

    public async Task<bool> SetActiveAsync(string tunnelName, bool activate, CancellationToken ct)
    {
        try
        {
            var current = GetStatus(tunnelName);

            if (activate)
            {
                if (current.Active) return true;

                var confPath = FindConfigPath(tunnelName);
                if (confPath == null)
                {
                    _log.LogWarning("Config-Datei für Tunnel {Tunnel} nicht gefunden in {Dir}. " +
                                    "Tunnel zuerst in WireGuard importieren.", tunnelName, ConfigDir);
                    return false;
                }

                // wireguard.exe legt den Service an UND startet ihn
                var ok = await RunWireGuardAsync($"/installtunnelservice \"{confPath}\"", ct);
                if (!ok) return false;

                // Auf Running warten - WireGuard kommt in der Regel innerhalb 1-3 s hoch
                return await WaitForServiceStateAsync(tunnelName, expectActive: true, ct);
            }
            else
            {
                // Service nicht da → schon deaktiviert
                if (current.ServiceState is "NotInstalled" or "Inactive") return true;

                var ok = await RunWireGuardAsync($"/uninstalltunnelservice \"{tunnelName}\"", ct);
                if (!ok) return false;

                return await WaitForServiceStateAsync(tunnelName, expectActive: false, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tunnel {Tunnel} konnte nicht auf Active={Active} gesetzt werden",
                          tunnelName, activate);
            return false;
        }
    }

    private static bool ConfigExists(string tunnelName) => FindConfigPath(tunnelName) != null;

    private static string? FindConfigPath(string tunnelName)
    {
        var dpapi = Path.Combine(ConfigDir, tunnelName + ".conf.dpapi");
        if (File.Exists(dpapi)) return dpapi;
        var conf = Path.Combine(ConfigDir, tunnelName + ".conf");
        if (File.Exists(conf)) return conf;
        return null;
    }

    private async Task<bool> RunWireGuardAsync(string args, CancellationToken ct)
    {
        if (!File.Exists(WireGuardExe))
        {
            _log.LogError("wireguard.exe nicht gefunden unter {Path}. Ist WireGuard für Windows installiert?",
                          WireGuardExe);
            return false;
        }

        var psi = new ProcessStartInfo(WireGuardExe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = Process.Start(psi);
        if (p == null)
        {
            _log.LogError("wireguard.exe konnte nicht gestartet werden");
            return false;
        }

        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
        {
            var err = await p.StandardError.ReadToEndAsync(ct);
            var outp = await p.StandardOutput.ReadToEndAsync(ct);
            _log.LogWarning("wireguard.exe {Args} → ExitCode {Code}. Stdout: {Out}. Stderr: {Err}",
                            args, p.ExitCode, outp, err);
            return false;
        }
        return true;
    }

    private async Task<bool> WaitForServiceStateAsync(string tunnelName, bool expectActive, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var s = GetStatus(tunnelName);
            if (s.Active == expectActive) return true;
            try { await Task.Delay(250, ct); }
            catch (OperationCanceledException) { return false; }
        }
        _log.LogWarning("Tunnel {Tunnel} hat nach 15 s nicht den Zustand Active={Active} erreicht",
                        tunnelName, expectActive);
        return false;
    }
}
