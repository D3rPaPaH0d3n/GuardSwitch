using WgAutoswitch.Shared;

namespace WgAutoswitch.Service;

public interface ITunnelController
{
    TunnelStatus GetStatus(string tunnelName);
    Task<bool> SetActiveAsync(string tunnelName, bool activate, CancellationToken ct);
}
