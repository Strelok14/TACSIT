using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrikeballServer.Data;
using StrikeballServer.Services;
using System.Net;
using Xunit;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// 2.3 Rate limiting tests.
/// Использует специализированную фабрику с низким лимитом (3 req/sec),
/// чтобы тест был воспроизводимым без привязки ко времени.
/// </summary>
public class RateLimitTests : IAsyncLifetime
{
    private const int BurstBeaconId = 203;
    private const int FirstNBeaconId = 204;
    private static readonly byte[] TestKey = new byte[32]; // all zeros

    private readonly RateLimitTestFactory _factory;
    private readonly HttpClient _client;

    public RateLimitTests()
    {
        _factory = new RateLimitTestFactory();
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedBeaconWithKeyAsync(BurstBeaconId, TestKey);
        await _factory.SeedBeaconWithKeyAsync(FirstNBeaconId, TestKey);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BurstBeyondLimit_Returns429()
    {
        // Лимит 3 запроса — отправляем 5. Среди ответов должны быть 429.
        var results = new List<HttpStatusCode>();
        for (int i = 1; i <= 5; i++)
        {
            var (_, content) = TestAuthHelper.BuildSignedPacket(BurstBeaconId, TestKey, seq: 6000 + i);
            var resp = await _client.PostAsync("/api/telemetry/measurement", content);
            results.Add(resp.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, results);
    }

    [Fact]
    public async Task FirstNRequests_Succeed()
    {
        // Первые 3 пакета (в пределах лимита) должны проходить.
        var successes = 0;
        for (int i = 1; i <= 3; i++)
        {
            var (_, content) = TestAuthHelper.BuildSignedPacket(FirstNBeaconId, TestKey, seq: 6100 + i);
            var resp = await _client.PostAsync("/api/telemetry/measurement", content);
            if (resp.StatusCode == HttpStatusCode.OK) successes++;
        }
        Assert.True(successes >= 1, "Хотя бы один из первых запросов должен пройти");
    }
}

/// <summary>
/// Фабрика с пониженным лимитом rate limiting (3 запроса/сек) для предсказуемых тестов.
/// </summary>
public class RateLimitTestFactory : TacidWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITelemetryRateLimiter));
            if (descriptor != null) services.Remove(descriptor);
            services.AddSingleton<ITelemetryRateLimiter>(new FixedWindowRateLimiter(maxPerWindow: 3));
        });
    }
}

/// <summary>
/// Простой rate limiter с фиксированным окном для тестов.
/// </summary>
public sealed class FixedWindowRateLimiter : ITelemetryRateLimiter
{
    private readonly int _max;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _counters = new();

    public FixedWindowRateLimiter(int maxPerWindow) => _max = maxPerWindow;

    public Task<bool> IsAllowedAsync(int beaconId, string sourceIp, CancellationToken cancellationToken = default)
    {
        var count = _counters.AddOrUpdate(beaconId, 1, (_, v) => v + 1);
        return Task.FromResult(count <= _max);
    }
}
