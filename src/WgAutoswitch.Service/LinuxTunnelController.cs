using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class LinuxTunnelController : ITunnelController
{
    private readonly ILogger<LinuxTunnelController> _log;

    public LinuxTunnelController(ILogger<LinuxTunnelController> log)
    {
        _log = log;
    }

    public TunnelStatus GetStatus(string tunnelName)
    {
        try
        {
            if (!ConnectionExists(tunnelName))
                return new TunnelStatus(tunnelName, false, "NotImported");

            var active = IsConnectionActive(tunnelName);
            return new TunnelStatus(tunnelName, active, active ? "Active" : "Inactive");
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
            if (current.ServiceState == "NotImported")
            {
                _log.LogWarning("NetworkManager-Verbindung {Tunnel} nicht gefunden. WireGuard zuerst importieren.", tunnelName);
                return false;
            }

            if (current.Active == activate) return true;

            var ok = activate
                ? await RunNmcliAsync("connection", "up", tunnelName, ct)
                : await RunNmcliAsync("connection", "down", tunnelName, ct);

            if (!ok) return false;

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (GetStatus(tunnelName).Active == activate) return true;
                try { await Task.Delay(250, ct); }
                catch (OperationCanceledException) { return false; }
            }

            _log.LogWarning("Tunnel {Tunnel} hat nach 15 s nicht den Zustand Active={Active} erreicht",
                            tunnelName, activate);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tunnel {Tunnel} konnte nicht auf Active={Active} gesetzt werden",
                          tunnelName, activate);
            return false;
        }
    }

    private static bool ConnectionExists(string tunnelName)
    {
        var (code, output, _) = RunNmcliSync("-t", "-f", "NAME", "connection", "show");
        return code == 0 && output.Split('\n').Any(line => line.Trim() == tunnelName);
    }

    private static bool IsConnectionActive(string tunnelName)
    {
        var (code, output, _) = RunNmcliSync("-t", "-f", "NAME", "connection", "show", "--active");
        return code == 0 && output.Split('\n').Any(line => line.Trim() == tunnelName);
    }

    private async Task<bool> RunNmcliAsync(string arg1, string arg2, string arg3, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("nmcli")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add(arg1);
        psi.ArgumentList.Add(arg2);
        psi.ArgumentList.Add(arg3);

        using var p = Process.Start(psi);
        if (p == null)
        {
            _log.LogError("nmcli konnte nicht gestartet werden");
            return false;
        }

        await p.WaitForExitAsync(ct);
        if (p.ExitCode == 0) return true;

        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        _log.LogWarning("nmcli {A1} {A2} {A3} -> ExitCode {Code}. Stdout: {Out}. Stderr: {Err}",
                        arg1, arg2, arg3, p.ExitCode, stdout, stderr);
        return false;
    }

    private static (int Code, string Output, string Error) RunNmcliSync(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("nmcli")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "nmcli konnte nicht gestartet werden");
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(3000);
            return (p.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
