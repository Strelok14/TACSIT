using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace StrikeballServer.Services
{
    /// <summary>
    /// Простое хранение ключей маяков: сначала пытается взять из environment переменной BEACON_KEY_{id},
    /// затем из секции конфигурации "BeaconKeys" (appsettings).
    /// Ожидается, что ключ хранится в base64 (raw secret bytes encoded).
    /// </summary>
    public class BeaconKeyStore : IBeaconKeyStore
    {
        private readonly IConfiguration _config;

        public BeaconKeyStore(IConfiguration config)
        {
            _config = config;
        }

        public Task<string?> GetKeyAsync(int beaconId)
        {
            var envName = $"BEACON_KEY_{beaconId}";
            var key = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrEmpty(key)) return Task.FromResult<string?>(key);

            // Try config section BeaconKeys:{id}
            key = _config[$"BeaconKeys:{beaconId}"];
            if (!string.IsNullOrEmpty(key)) return Task.FromResult<string?>(key);

            return Task.FromResult<string?>(null);
        }
    }
}
