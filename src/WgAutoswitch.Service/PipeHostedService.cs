using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class PipeHostedService : BackgroundService
{
    private readonly ILogger<PipeHostedService> _log;
    private readonly ServiceState _state;
    private readonly TunnelController _controller;

    public PipeHostedService(ILogger<PipeHostedService> log, ServiceState state, TunnelController controller)
    {
        _log = log;
        _state = state;
        _controller = controller;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Two concurrent accept loops: while one instance is serving a client,
        // the other is already waiting for the next connection. This eliminates
        // the brief gap between connections that caused "Pipe Broken" errors.
        return Task.WhenAll(AcceptLoopAsync(stoppingToken), AcceptLoopAsync(stoppingToken));
    }

    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ServeOneClientAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Pipe-Server Fehler");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ServeOneClientAsync(CancellationToken ct)
    {
        // ACL: alle authentifizierten User auf der Maschine dürfen lesen/schreiben.
        // Damit funktioniert die Pipe vom User-Tray gegen den LocalSystem-Service.
        var ps = new PipeSecurity();
        var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        ps.AddAccessRule(new PipeAccessRule(sid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        using var server = NamedPipeServerStreamAcl.Create(
            Paths.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 8192,
            outBufferSize: 8192,
            pipeSecurity: ps);

        await server.WaitForConnectionAsync(ct);
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) return;

            var cmd = JsonSerializer.Deserialize<Command>(line);
            var resp = await HandleAsync(cmd, ct);
            await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
        }
        finally
        {
            try { server.Disconnect(); } catch { }
        }
    }

    private async Task<CommandResponse> HandleAsync(Command? cmd, CancellationToken ct)
    {
        try
        {
            switch (cmd)
            {
                case GetStatusCommand:
                    return new CommandResponse(true, null, _state.Snapshot());

                case SetAutoModeCommand sac:
                    _state.AutoModeEnabled = sac.Enabled;
                    _log.LogInformation("Auto-Modus: {State}", sac.Enabled ? "AN" : "PAUSIERT");
                    _state.NotifyChanged();
                    return new CommandResponse(true, null, _state.Snapshot());

                case ManualTunnelCommand mtc:
                    var ok = await _controller.SetActiveAsync(mtc.TunnelName, mtc.Activate, ct);
                    if (ok) _state.CurrentTunnels[mtc.TunnelName] = _controller.GetStatus(mtc.TunnelName);
                    _state.NotifyChanged();
                    return new CommandResponse(ok, ok ? null : "Tunnel-Steuerung fehlgeschlagen", _state.Snapshot());

                case ReloadConfigCommand:
                    _state.ReloadConfig();
                    return new CommandResponse(true, null, _state.Snapshot());

                default:
                    return new CommandResponse(false, "Unbekanntes Kommando", null);
            }
        }
        catch (Exception ex)
        {
            return new CommandResponse(false, ex.Message, null);
        }
    }
}
