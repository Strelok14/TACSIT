using System.Threading.Tasks;

namespace StrikeballServer.Services
{
    public interface IBeaconKeyStore
    {
        /// <summary>
        /// Получить секретный ключ (base64) для маяка по его ID
        /// </summary>
        Task<string?> GetKeyAsync(int beaconId);
    }
}
