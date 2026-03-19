namespace StrikeballServer.Services;

/// <summary>
/// Проверка replay-атак по sequence number и timestamp.
/// </summary>
public interface IReplayProtectionService
{
    Task<bool> AcceptPacketAsync(int beaconId, long sequence, long timestampMs, CancellationToken cancellationToken = default);
}
