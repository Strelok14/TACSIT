using Microsoft.IdentityModel.Tokens;
using StrikeballServer.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// Вспомогательные методы для интеграционных тестов: генерация JWT, построение HMAC-пакетов.
/// </summary>
public static class TestAuthHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Создаёт JWT с заданной ролью для тестовых запросов.</summary>
    public static string CreateJwt(string role, int? beaconId = null, bool expired = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TacidWebApplicationFactory.TestJwtSigningKey));
        var now = DateTime.UtcNow;
        var expiry = expired ? now.AddMinutes(-5) : now.AddMinutes(30);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"test-{role}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, $"test-{role}"),
            new(ClaimTypes.Role, role)
        };

        if (beaconId.HasValue)
            claims.Add(new Claim("beacon_id", beaconId.Value.ToString()));

        var jwt = new JwtSecurityToken(
            issuer: "tacid-server",
            audience: "tacid-clients",
            claims: claims,
            notBefore: now,
            expires: expiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>Строит канонический строку для HMAC (точно как middleware).</summary>
    public static string BuildCanonical(MeasurementPacketDto packet)
    {
        var ordered = packet.Distances.OrderBy(d => d.AnchorId).ToList();
        var sb = new StringBuilder();
        sb.Append(packet.BeaconId).Append('|')
          .Append(packet.Sequence).Append('|')
          .Append(packet.Timestamp).Append('|');
        foreach (var d in ordered)
        {
            sb.Append(d.AnchorId).Append(':')
              .Append(d.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(':').Append(d.Rssi ?? 0).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>Создаёт подписанный HMAC-пакет для telemetry endpoint.</summary>
    public static (MeasurementPacketDto packet, StringContent content) BuildSignedPacket(
        int beaconId, byte[] rawKey, long seq, long? timestampMs = null,
        List<(int anchorId, double dist)>? distances = null, int keyVersion = 1)
    {
        var ts = timestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dist = distances ?? new List<(int, double)>
        {
            (beaconId * 10 + 1, 10.5)
        };

        var packet = new MeasurementPacketDto
        {
            BeaconId = beaconId,
            Sequence = seq,
            Timestamp = ts,
            KeyVersion = keyVersion,
            BatteryLevel = 85,
            Distances = dist.Select(d => new AnchorDistanceDto { AnchorId = d.anchorId, Distance = d.dist }).ToList(),
            Signature = string.Empty // заполним ниже
        };

        var canonical = BuildCanonical(packet);
        using var hmac = new HMACSHA256(rawKey);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        packet.Signature = Convert.ToBase64String(sig);

        var json = JsonSerializer.Serialize(packet, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return (packet, content);
    }

    /// <summary>Выполняет login (сначала /api/auth/login, затем fallback /auth/login).</summary>
    public static async Task<AuthResponseFromApi?> LoginAsync(HttpClient client, string login, string password)
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new { login, password }, JsonOpts),
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/auth/login", body);
        if (!response.IsSuccessStatusCode)
        {
            var fallbackBody = new StringContent(
                JsonSerializer.Serialize(new { login, password }, JsonOpts),
                Encoding.UTF8, "application/json");
            response = await client.PostAsync("/auth/login", fallbackBody);
        }
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AuthResponseFromApi>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>Простой DTO для десериализации ответа /auth/login в тестах.</summary>
    public sealed class AuthResponseFromApi
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public string? Role { get; set; }
        public string? Message { get; set; }
    }
}
