using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrikeballServer.Data;
using StrikeballServer.Models;
using StrikeballServer.Services;
using System.Security.Cryptography;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// WebApplicationFactory для интеграционных тестов T.A.C.I.D.
/// Использует SQLite in-memory + заглушки для env vars (ключи, пользователи).
/// </summary>
public class TacidWebApplicationFactory : WebApplicationFactory<Program>
{
    // Тестовые секреты (фиксированные, 32 байта каждый).
    public static readonly byte[] TestMasterKey = new byte[32]; // all zeros
    public static readonly string TestMasterKeyB64 = Convert.ToBase64String(new byte[32]);
    public static readonly string TestJwtSigningKey = new string('T', 64);
    public const string AdminLogin = "admin";
    public const string AdminPassword = "AdminDemo123!";
    public const string ObserverLogin = "observer";
    public const string ObserverPassword = "ObserverDemo123!";
    public const string PlayerLogin = "player1";
    public const string PlayerPassword = "PlayerDemo123!";
    public const int PlayerBeaconId = 100;

    private SqliteConnection? _dbConnection;

    public TacidWebApplicationFactory()
    {
        // Env vars должны быть установлены ДО создания WebApplicationBuilder.
        Environment.SetEnvironmentVariable("TACID_JWT_SIGNING_KEY", TestJwtSigningKey);
        Environment.SetEnvironmentVariable("TACID_MASTER_KEY_B64", TestMasterKeyB64);
        Environment.SetEnvironmentVariable("TACID_ADMIN_LOGIN", AdminLogin);
        Environment.SetEnvironmentVariable("TACID_ADMIN_PASSWORD", AdminPassword);
        Environment.SetEnvironmentVariable("TACID_OBSERVER_LOGIN", ObserverLogin);
        Environment.SetEnvironmentVariable("TACID_OBSERVER_PASSWORD", ObserverPassword);
        Environment.SetEnvironmentVariable("TACID_PLAYER_LOGIN", PlayerLogin);
        Environment.SetEnvironmentVariable("TACID_PLAYER_PASSWORD", PlayerPassword);
        Environment.SetEnvironmentVariable("TACID_PLAYER_BEACON_ID", PlayerBeaconId.ToString());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Заменяем DbContext на SQLite in-memory с общим соединением (shared connection
            // гарантирует, что EnsureCreated и запросы работают в одной in-memory БД).
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(ApplicationDbContext))
                .ToList();
            foreach (var d in descriptors) services.Remove(d);

            _dbConnection = new SqliteConnection("DataSource=:memory:");
            _dbConnection.Open();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_dbConnection));
        });
    }

    /// <summary>
    /// Инициализирует тестовые данные: якоря, маяк, ключ маяка с заданным ID.
    /// Вызывается из каждого тестового класса в InitializeAsync.
    /// </summary>
    public async Task SeedBeaconWithKeyAsync(int beaconId, byte[] rawKey, int keyVersion = 1, int anchorCount = 4)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var keyStore = scope.ServiceProvider.GetRequiredService<IBeaconKeyStore>();

        // Защита от повторного добавления в параллельных тестах.
        if (!await db.Beacons.AnyAsync(b => b.Id == beaconId))
        {
            db.Beacons.Add(new Beacon
            {
                Id = beaconId,
                Name = $"TestBeacon_{beaconId}",
                Status = BeaconStatus.Active,
                BatteryLevel = 100,
                KeyVersion = keyVersion
            });

            for (int i = 1; i <= anchorCount; i++)
            {
                var anchorId = beaconId * 10 + i;
                if (!await db.Anchors.AnyAsync(a => a.Id == anchorId))
                {
                    db.Anchors.Add(new Anchor
                    {
                        Id = anchorId,
                        Name = $"Anchor_{anchorId}",
                        X = (i - 1) % 2 == 0 ? 0.0 : 50.0,
                        Y = i <= 2 ? 0.0 : 50.0,
                        Z = 2.0,
                        Status = AnchorStatus.Active
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        if (!await db.BeaconSecrets.AnyAsync(s => s.BeaconId == beaconId && s.IsActive))
        {
            await keyStore.UpsertActiveKeyAsync(beaconId, keyVersion, rawKey);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dbConnection?.Close();
            _dbConnection?.Dispose();
        }
        base.Dispose(disposing);
    }
}
