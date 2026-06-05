using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public class MainWorker : BackgroundService
{
    private readonly ILogger<MainWorker> _log;
    private readonly ServiceState _state;
    private readonly NetworkDetector _detector;
    private readonly ITunnelController _controller;

    // Hysterese: zähle aufeinanderfolgende identische Detection-Ergebnisse
    private bool? _lastSeen;
    private int _consecutiveCount;
    // null = wir haben noch nie aktiv geschaltet, also beim ersten stabilen
    // Resultat zwingend einmal anwenden (sonst bleibt der Tunnel beim Erst-
    // Start "unterwegs" hängen, falls er gerade aus ist).
    private bool? _appliedAtHome;

    // Backoff, falls eine Tunnel-Schaltung fehlschlägt (z. B. wireguard.exe kommt
    // nicht hoch). Verhindert hektisches Wieder-und-wieder-Versuchen in jedem Tick.
    private DateTime _nextApplyRetry = DateTime.MinValue;
    private int _applyFailures;

    public MainWorker(ILogger<MainWorker> log, ServiceState state,
                      NetworkDetector detector, ITunnelController controller)
    {
        _log = log;
        _state = state;
        _detector = detector;
        _controller = controller;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("GuardSwitch gestartet");

        // Sofort reagieren bei Netzwerk-Änderungen (kein dummes Polling auf alles)
        NetworkChange.NetworkAvailabilityChanged += (s, e) => TriggerImmediate();
        NetworkChange.NetworkAddressChanged += (s, e) => TriggerImmediate();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Fehler im Hauptloop");
            }

            try
            {
                var interval = TimeSpan.FromSeconds(Math.Max(2, _state.Config.General.CheckIntervalSeconds));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _wakeToken.Token);
                await Task.Delay(interval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Entweder Stop oder Wake-Up
                if (stoppingToken.IsCancellationRequested) break;
                var old = _wakeToken;
                _wakeToken = new CancellationTokenSource();
                old.Dispose();

                // Settle: nach einem Netzwerk-Event kurz warten, bis ARP/Gateway/DHCP
                // stehen. Misst man sofort, kommen die meisten Fehl-"unterwegs"-Meldungen
                // zustande. Weitere Events während des Wartens entprellen sich so von selbst.
                var settle = TimeSpan.FromSeconds(Math.Max(0, _state.Config.General.SettleDelaySeconds));
                if (settle > TimeSpan.Zero)
                {
                    try { await Task.Delay(settle, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }

    private CancellationTokenSource _wakeToken = new();
    private void TriggerImmediate()
    {
        // Sofort reagieren - wie vom User gewünscht
        try { _wakeToken.Cancel(); } catch { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Tunnel-Status immer aktualisieren - auch wenn Auto-Modus aus ist,
        // damit das Tray etwas anzeigen kann
        foreach (var tunnel in _state.Config.Tunnels)
        {
            _state.CurrentTunnels[tunnel.Name] = _controller.GetStatus(tunnel.Name);
        }

        if (!_state.Config.General.Enabled || !_state.AutoModeEnabled)
        {
            _state.LastDetectionReason = !_state.Config.General.Enabled
                ? "Per Konfiguration deaktiviert"
                : "Per Tray pausiert";
            _state.NotifyChanged();
            return;
        }

        var detection = await _detector.DetectAsync(ct);
        _state.LastDetectionReason = detection.Reason;

        // Unklares Ergebnis → nichts umschalten, aktuellen Zustand halten und die
        // Hysterese-Zählung zurücksetzen, damit kein gemischter Verlauf entsteht.
        if (detection.Inconclusive)
        {
            _lastSeen = null;
            _consecutiveCount = 0;
            _state.NotifyChanged();
            return;
        }

        _state.LastAtHome = detection.AtHome;

        // Hysterese
        if (_lastSeen == detection.AtHome)
        {
            _consecutiveCount++;
        }
        else
        {
            _lastSeen = detection.AtHome;
            _consecutiveCount = 1;
        }

        // Asymmetrisch: schnell den Tunnel AN (unterwegs erkannt, Schutz), aber
        // langsam/sicher den Tunnel AUS (zuhause erkannt).
        var threshold = detection.AtHome
            ? Math.Max(1, _state.Config.General.HysteresisCountHome)
            : Math.Max(1, _state.Config.General.HysteresisCountAway);
        var stable = _consecutiveCount >= threshold;

        var needsApply = stable && detection.AtHome != _appliedAtHome;
        if (needsApply && DateTime.UtcNow >= _nextApplyRetry)
        {
            // Aktion: zuhause -> alle Tunnel AUS, unterwegs -> alle Tunnel AN
            var shouldBeActive = !detection.AtHome;
            _log.LogInformation("Wechsle Tunnel-Zustand: AtHome={AtHome}, ShouldBeActive={Active}. Grund: {Reason}",
                                detection.AtHome, shouldBeActive, detection.Reason);

            bool allOk = true;
            foreach (var tunnel in _state.Config.Tunnels)
            {
                var ok = await _controller.SetActiveAsync(tunnel.Name, shouldBeActive, ct);
                allOk &= ok;
                _state.CurrentTunnels[tunnel.Name] = _controller.GetStatus(tunnel.Name);
            }

            if (allOk)
            {
                // Nur bei tatsächlichem Erfolg als angewendet verbuchen.
                _appliedAtHome = detection.AtHome;
                _applyFailures = 0;
                _nextApplyRetry = DateTime.MinValue;
                _state.LastChange = DateTime.Now;
                _state.LastChangeReason = detection.AtHome
                    ? $"Heimnetz erkannt → Tunnel AUS ({detection.Reason})"
                    : $"Heimnetz verlassen → Tunnel AN ({detection.Reason})";
            }
            else
            {
                // Fehlschlag NICHT als erledigt verbuchen → wird erneut versucht,
                // aber mit wachsendem Abstand (max. 60 s), um Thrashing zu vermeiden.
                _applyFailures++;
                var backoff = TimeSpan.FromSeconds(Math.Min(60, 5 * _applyFailures));
                _nextApplyRetry = DateTime.UtcNow + backoff;
                _log.LogWarning("Tunnel-Wechsel fehlgeschlagen (Versuch {N}), erneuter Versuch in {Sec}s",
                                _applyFailures, backoff.TotalSeconds);
            }
        }

        _state.NotifyChanged();
    }
}
