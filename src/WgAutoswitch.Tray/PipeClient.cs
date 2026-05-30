using System.IO.Pipes;
using System.Text.Json;
using WgAutoswitch.Shared;

namespace WgAutoswitch.Tray;

public class PipeClient
{
    public async Task<CommandResponse> SendAsync(Command cmd, CancellationToken ct)
    {
        // Kurze Timeouts + mehrere Versuche statt eines langen Timeouts: die Lücke
        // zwischen zwei Pipe-Verbindungen ist nur Millisekunden lang, also lieber
        // schnell erneut versuchen, als das Tray-UI (pollt alle 3 s) sekundenlang
        // zu blockieren. Worst-Case ~3,65 s, wenn der Service wirklich weg ist.
        const int maxAttempts = 4;
        const int connectTimeoutMs = 800;
        Exception? lastEx = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                try { await Task.Delay(150, ct); }
                catch (OperationCanceledException) { break; }
            }
            try
            {
                using var pipe = new NamedPipeClientStream(".", Paths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(connectTimeoutMs, ct);

                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);

                await writer.WriteLineAsync(JsonSerializer.Serialize(cmd));
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line))
                    return new CommandResponse(false, "Leere Antwort vom Service", null);

                return JsonSerializer.Deserialize<CommandResponse>(line)
                       ?? new CommandResponse(false, "Antwort nicht parsebar", null);
            }
            catch (OperationCanceledException)
            {
                return new CommandResponse(false, "Abgebrochen", null);
            }
            catch (TimeoutException ex)
            {
                lastEx = ex;
                // Retry — the server may be momentarily between accept calls.
            }
            catch (IOException ex)
            {
                lastEx = ex;
                // Broken pipe / no instances available — retry.
            }
            catch (Exception ex)
            {
                return new CommandResponse(false, ex.Message, null);
            }
        }
        return lastEx is TimeoutException
            ? new CommandResponse(false, "Service läuft nicht oder ist nicht erreichbar", null)
            : new CommandResponse(false, lastEx?.Message ?? "Verbindung fehlgeschlagen", null);
    }
}
