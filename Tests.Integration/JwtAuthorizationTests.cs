using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// 2.4 JWT authorization tests:
/// - Без токена → 401
/// - Токен с неподходящей ролью → 403
/// - observer → доступ к /api/positions
/// - observer → нет доступа к /api/anchors (POST/DELETE — admin only)
/// - admin → полный доступ
/// - Отозванный токен (denylist) → 401
/// - Refresh token rotation
/// </summary>
[Collection("JwtTests")]
public class JwtAuthorizationTests : IClassFixture<TacidWebApplicationFactory>
{
    private readonly TacidWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public JwtAuthorizationTests(TacidWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await _client.GetAsync("/api/positions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ObserverToken_GetPositions_Returns200()
    {
        var token = TestAuthHelper.CreateJwt("observer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/positions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task ObserverToken_GetAnchors_Returns403()
    {
        var token = TestAuthHelper.CreateJwt("observer");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // В текущей политике контроллера якоря доступны только admin.
        var response = await client.GetAsync("/api/anchors");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlayerToken_AccessPositions_Returns200()
    {
        var token = TestAuthHelper.CreateJwt("player", beaconId: TacidWebApplicationFactory.PlayerBeaconId);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/positions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_FullAccess()
    {
        var token = TestAuthHelper.CreateJwt("admin");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Admin может читать якоря.
        var resp = await client.GetAsync("/api/anchors");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Admin может читать маяки.
        var resp2 = await client.GetAsync("/api/beacons");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsRefreshToken()
    {
        var auth = await TestAuthHelper.LoginAsync(_client, TacidWebApplicationFactory.AdminLogin, TacidWebApplicationFactory.AdminPassword);

        Assert.NotNull(auth);
        Assert.True(auth!.Success);
        Assert.NotNull(auth.Token);
        Assert.NotNull(auth.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewPair()
    {
        var auth = await TestAuthHelper.LoginAsync(_client, TacidWebApplicationFactory.AdminLogin, TacidWebApplicationFactory.AdminPassword);
        Assert.NotNull(auth?.RefreshToken);

        var body = new StringContent(
            JsonSerializer.Serialize(new { refreshToken = auth!.RefreshToken },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/auth/refresh", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var refreshed = JsonSerializer.Deserialize<TestAuthHelper.AuthResponseFromApi>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(refreshed);
        Assert.True(refreshed!.Success);
        Assert.NotNull(refreshed.Token);
        Assert.NotNull(refreshed.RefreshToken);
        // Новый refresh token должен отличаться от старого.
        Assert.NotEqual(auth.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithRevokedToken_Returns401()
    {
        var auth = await TestAuthHelper.LoginAsync(_client, TacidWebApplicationFactory.ObserverLogin, TacidWebApplicationFactory.ObserverPassword);
        var oldRefresh = auth!.RefreshToken!;

        // Используем токен один раз.
        var body1 = new StringContent(
            JsonSerializer.Serialize(new { refreshToken = oldRefresh },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json");
        await _client.PostAsync("/api/auth/refresh", body1);

        // Пытаемся использовать повторно — должен быть 401.
        var body2 = new StringContent(
            JsonSerializer.Serialize(new { refreshToken = oldRefresh },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("/api/auth/refresh", body2);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesAccessToken()
    {
        var auth = await TestAuthHelper.LoginAsync(_client, TacidWebApplicationFactory.AdminLogin, TacidWebApplicationFactory.AdminPassword);
        Assert.NotNull(auth?.Token);

        // Авторизуемся и делаем logout.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token!);

        var logoutBody = new StringContent(
            JsonSerializer.Serialize(new { refreshToken = auth.RefreshToken },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json");
        var logoutResp = await client.PostAsync("/api/auth/logout", logoutBody);
        Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);

        // После logout тот же access token должен давать 401.
        var resp = await client.GetAsync("/api/positions");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
