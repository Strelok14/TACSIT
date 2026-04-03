using StackExchange.Redis;
using System.Collections.Concurrent;

namespace StrikeballServer.Services;

/// <summary>
/// Redis token bucket rate limiter для входящей телеметрии.
/// При отказе Redis переключается на in-memory fallback.
/// </summary>
public class RedisTelemetryRateLimiter : ITelemetryRateLimiter
{
    private readonly ILogger<RedisTelemetryRateLimiter> _logger;
    private readonly Lazy<IConnectionMultiplexer?> _redis;

    // Fallback: простое окно запросов за секунду.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _fallbackWindows = new();

    private const string LuaScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local refillRate = tonumber(ARGV[2])
local capacity = tonumber(ARGV[3])
local need = tonumber(ARGV[4])

local data = redis.call('HMGET', key, 'tokens', 'ts')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])

if tokens == nil then tokens = capacity end
if ts == nil then ts = now end

local delta = math.max(0, now - ts)
tokens = math.min(capacity, tokens + (delta * refillRate))
local allowed = 0
if tokens >= need then
    tokens = tokens - need
    allowed = 1
end

redis.call('HMSET', key, 'tokens', tokens, 'ts', now)
redis.call('EXPIRE', key, 60)
return allowed";

    public RedisTelemetryRateLimiter(IConfiguration configuration, ILogger<RedisTelemetryRateLimiter> logger)
    {
        _logger = logger;
        var connection = configuration["Redis:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? "localhost:6379,abortConnect=false";

        _redis = new Lazy<IConnectionMultiplexer?>(() =>
        {
            try
            {
                return ConnectionMultiplexer.Connect(connection);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis не подключен, будет использоваться fallback rate limiter");
                return null;
            }
        }, true);
    }

    public async Task<bool> IsAllowedAsync(int beaconId, string sourceIp, CancellationToken cancellationToken = default)
    {
        return await IsAllowedAsync(beaconId, "telemetry", sourceIp, 20, 15, cancellationToken);
    }

    public async Task<bool> IsAllowedAsync(int subjectId, string channel, string sourceIp, int capacity, double refillPerSecond, CancellationToken cancellationToken = default)
    {
        var redis = _redis.Value;
        if (redis != null && redis.IsConnected)
        {
            try
            {
                var key = $"tacid:rl:{channel}:{subjectId}:{sourceIp}";
                var db = redis.GetDatabase();
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var refillPerMillisecond = refillPerSecond / 1000d;
                var allowed = (int)await db.ScriptEvaluateAsync(
                    LuaScript,
                    new RedisKey[] { key },
                    new RedisValue[] { nowMs, refillPerMillisecond, capacity, 1 });

                return allowed == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis недоступен при rate limiting, включаем fallback для subject {subjectId}", subjectId);
            }
        }

        var bucketKey = $"{subjectId}:{channel}:{sourceIp}";
        var queue = _fallbackWindows.GetOrAdd(bucketKey, _ => new ConcurrentQueue<DateTime>());
        var now = DateTime.UtcNow;
        var border = now.AddSeconds(-1);

        while (queue.TryPeek(out var old) && old < border)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= capacity)
        {
            return false;
        }

        queue.Enqueue(now);
        return true;
    }
}
