using System.Threading.Tasks;

namespace StrikeballServer.Services
{
    public sealed record BeaconKeyCandidate(int KeyVersion, byte[] KeyBytes, bool IsActive);

    public interface IBeaconKeyStore
    {
        /// <summary>
        /// Получить набор ключей для проверки подписи (активный + предыдущий в переходном окне).
        /// </summary>
        Task<IReadOnlyList<BeaconKeyCandidate>> GetVerificationKeysAsync(int beaconId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Установить/обновить активный ключ маяка.
        /// </summary>
        Task UpsertActiveKeyAsync(int beaconId, int keyVersion, byte[] rawKey, TimeSpan? previousKeyGracePeriod = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Выполнить плановую ротацию ключа и вернуть новую версию.
        /// </summary>
        Task<int> RotateKeyAsync(int beaconId, TimeSpan previousKeyGracePeriod, CancellationToken cancellationToken = default);
    }
}
