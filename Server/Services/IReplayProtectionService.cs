namespace StrikeballServer.Services;

/// <summary>
/// Проверка replay-атак по sequence number и timestamp.
/// </summary>
public interface IReplayProtectionService
{
    Task<bool> AcceptPacketAsync(int beaconId, long sequence, long timestampMs, CancellationToken cancellationToken = default);

    Task<bool> AcceptPacketAsync(int subjectId, string channel, long sequence, long timestampMs, CancellationToken cancellationToken = default);
}
