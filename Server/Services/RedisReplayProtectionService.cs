using StackExchange.Redis;
using System.Collections.Concurrent;

namespace StrikeballServer.Services;

/// <summary>
/// Replay-защита через Redis Sorted Set.
/// При недоступности Redis включается in-memory fallback (graceful degradation).
/// </summary>
public class RedisReplayProtectionService : IReplayProtectionService
{
    private readonly ILogger<RedisReplayProtectionService> _logger;
    private readonly string _redisConnectionString;
    private readonly Lazy<IConnectionMultiplexer?> _redis;
    private readonly ConcurrentDictionary<int, long> _fallbackLastSeq = new();

    private const string ReplayLua = @"
local zkey = KEYS[1]
local seq = tonumber(ARGV[1])
local maxValue = redis.call('ZREVRANGE', zkey, 0, 0)
if maxValue[1] ~= nil then
  local maxSeq = tonumber(maxValue[1])
  if seq <= maxSeq then
    return 0
  end
end
redis.call('ZADD', zkey, seq, tostring(seq))
redis.call('EXPIRE', zkey, 3600)
return 1";

    public RedisReplayProtectionService(IConfiguration configuration, ILogger<RedisReplayProtectionService> logger)
    {
        _logger = logger;
        _redisConnectionString = configuration["Redis:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? "localhost:6379,abortConnect=false";

        _redis = new Lazy<IConnectionMultiplexer?>(ConnectRedis, isThreadSafe: true);
    }

    public async Task<bool> AcceptPacketAsync(int beaconId, long sequence, long timestampMs, CancellationToken cancellationToken = default)
    {
        return await AcceptPacketAsync(beaconId, "telemetry", sequence, timestampMs, cancellationToken);
    }

    public async Task<bool> AcceptPacketAsync(int subjectId, string channel, long sequence, long timestampMs, CancellationToken cancellationToken = default)
    {
        var redis = _redis.Value;
        if (redis != null && redis.IsConnected)
        {
            try
            {
                var db = redis.GetDatabase();
                var result = (int)await db.ScriptEvaluateAsync(
                    ReplayLua,
                    new RedisKey[] { $"tacid:replay:{channel}:{subjectId}" },
                    new RedisValue[] { sequence });

                return result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis недоступен при replay-проверке, включаем fallback для subject {subjectId}", subjectId);
            }
        }

        // Graceful degradation: локальная защита от replay.
        var fallbackKey = HashCode.Combine(subjectId, channel);
        var last = _fallbackLastSeq.GetOrAdd(fallbackKey, -1);
        if (sequence <= last)
        {
            return false;
        }

        _fallbackLastSeq[fallbackKey] = sequence;
        return true;
    }

    private IConnectionMultiplexer? ConnectRedis()
    {
        try
        {
            return ConnectionMultiplexer.Connect(_redisConnectionString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis не подключен, будет использоваться fallback replay-защита в памяти");
            return null;
        }
    }
}
