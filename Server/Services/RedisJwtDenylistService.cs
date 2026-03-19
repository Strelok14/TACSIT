using StackExchange.Redis;
using System.Collections.Concurrent;

namespace StrikeballServer.Services;

/// <summary>
/// JWT denylist на базе Redis SET с TTL.
/// При недоступности Redis переключается на in-memory fallback (graceful degradation).
/// In-memory fallback обеспечивает безопасность в рамках одной ноды, но не в кластере.
/// </summary>
public class RedisJwtDenylistService : IJwtDenylistService
{
    private readonly ILogger<RedisJwtDenylistService> _logger;
    private readonly Lazy<IConnectionMultiplexer?> _redis;

    // In-memory fallback: JTI → время истечения
    private readonly ConcurrentDictionary<string, DateTime> _fallback = new();

    public RedisJwtDenylistService(IConfiguration configuration, ILogger<RedisJwtDenylistService> logger)
    {
        _logger = logger;
        var conn = configuration["Redis:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? "localhost:6379,abortConnect=false";

        _redis = new Lazy<IConnectionMultiplexer?>(() =>
        {
            try
            {
                return ConnectionMultiplexer.Connect(conn);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis недоступен для JWT denylist, использую in-memory fallback");
                return null;
            }
        }, isThreadSafe: true);
    }

    public async Task AddAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var redis = _redis.Value;
        if (redis is { IsConnected: true })
        {
            try
            {
                await redis.GetDatabase().StringSetAsync($"tacid:jti:deny:{jti}", "1", ttl);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis ошибка при добавлении JTI в denylist, fallback");
            }
        }

        _fallback[jti] = DateTime.UtcNow.Add(ttl);

        // Ленивая очистка устаревших записей, чтобы не накапливалась память.
        foreach (var key in _fallback.Keys.ToList())
        {
            if (_fallback.TryGetValue(key, out var exp) && exp < DateTime.UtcNow)
                _fallback.TryRemove(key, out _);
        }
    }

    public async Task<bool> IsDeniedAsync(string jti, CancellationToken cancellationToken = default)
    {
        var redis = _redis.Value;
        if (redis is { IsConnected: true })
        {
            try
            {
                return await redis.GetDatabase().KeyExistsAsync($"tacid:jti:deny:{jti}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis ошибка при проверке JTI denylist, fallback");
            }
        }

        return _fallback.TryGetValue(jti, out var expiry) && expiry > DateTime.UtcNow;
    }
}
