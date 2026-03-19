using System.Net;
using Xunit;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// 2.2 Replay protection tests:
/// - Пакет seq=N попадает успешно
/// - Повторная отправка того же seq → 409 Conflict
/// - Понижение seq (меньше максимального) → 409 Conflict
/// </summary>
[Collection("ReplayTests")]
public class ReplayProtectionTests : IClassFixture<TacidWebApplicationFactory>, IAsyncLifetime
{
    private const int TestBeaconId = 202;
    private static readonly byte[] TestKey = new byte[32]; // all zeros
    private static long _seqBase = 5000;

    private readonly TacidWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReplayProtectionTests(TacidWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static long NextBaseSeq() => System.Threading.Interlocked.Add(ref _seqBase, 10);

    public async Task InitializeAsync()
    {
        await _factory.SeedBeaconWithKeyAsync(TestBeaconId, TestKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FirstPacket_Accepted()
    {
        var seq = NextBaseSeq();
        var (_, content) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: seq);
        var response = await _client.PostAsync("/api/telemetry/measurement", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DuplicatePacket_Returns409()
    {
        var seq = NextBaseSeq();
        // Сначала отправляем пакет и убеждаемся, что прошёл.
        var (_, content1) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: seq);
        var first = await _client.PostAsync("/api/telemetry/measurement", content1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Второй запрос с тем же seq → Replay.
        var (_, content2) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: seq);
        var second = await _client.PostAsync("/api/telemetry/measurement", content2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task LowerSeqAfterHigher_Returns409()
    {
        var baseSeq = NextBaseSeq();
        // Отправляем seq=5010.
        var (_, content1) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: baseSeq + 5);
        var first = await _client.PostAsync("/api/telemetry/measurement", content1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Пытаемся отправить seq=5005 (ниже максимума) → Replay.
        var (_, content2) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: baseSeq + 1);
        var second = await _client.PostAsync("/api/telemetry/measurement", content2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task IncreasedSeq_Accepted()
    {
        var baseSeq = NextBaseSeq();
        // Серия пакетов с возрастающими seq должна проходить нормально.
        for (long i = baseSeq; i <= baseSeq + 3; i++)
        {
            var (_, content) = TestAuthHelper.BuildSignedPacket(TestBeaconId, TestKey, seq: i);
            var resp = await _client.PostAsync("/api/telemetry/measurement", content);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }
}
