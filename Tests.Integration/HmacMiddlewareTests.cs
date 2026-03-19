using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrikeballServer.Models;
using Xunit;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// 2.1 HMAC middleware tests:
/// - Корректный HMAC → 200 OK
/// - Неверный HMAC → 401
/// - Отсутствие подписи → 401
/// - Старый timestamp (>5 сек) → 401
/// </summary>
[Collection("HmacTests")]
public class HmacMiddlewareTests : IClassFixture<TacidWebApplicationFactory>, IAsyncLifetime
{
    private const int TestBeaconId = 201;
    private static readonly byte[] TestKey = new byte[32]; // all zeros
    private static long _seq = 1000;

    private readonly TacidWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HmacMiddlewareTests(TacidWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static long NextSeq() => System.Threading.Interlocked.Increment(ref _seq);

    public async Task InitializeAsync()
    {
        await _factory.SeedBeaconWithKeyAsync(TestBeaconId, TestKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ValidHmac_Returns200()
    {
        var (_, content) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: NextSeq());
        var response = await _client.PostAsync("/api/telemetry/measurement", content);

        // 200 означает: middleware пропустил, контроллер обработал успешно.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WrongHmac_Returns401()
    {
        var wrongKey = new byte[32];
        wrongKey[0] = 0xFF; // отличается от TestKey

        var (_, content) = TestAuthHelper.BuildSignedPacket(TestBeaconId, wrongKey, seq: NextSeq());
        var response = await _client.PostAsync("/api/telemetry/measurement", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingSignature_Returns401()
    {
        var packet = new MeasurementPacketDto
        {
            BeaconId = TestBeaconId,
            Sequence = NextSeq(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            KeyVersion = 1,
            BatteryLevel = 90,
            Distances = new List<AnchorDistanceDto> { new() { AnchorId = TestBeaconId * 10 + 1, Distance = 10.0 } },
            Signature = "" // намеренно пустая
        };

        var json = JsonSerializer.Serialize(packet, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/telemetry/measurement", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaleTimestamp_Returns401()
    {
        // Timestamp старше окна backlog (по умолчанию 120000 мс).
        var oldTs = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var (_, content) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: NextSeq(), timestampMs: oldTs);
        var response = await _client.PostAsync("/api/telemetry/measurement", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
