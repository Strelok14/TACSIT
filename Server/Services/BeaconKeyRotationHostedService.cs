using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;

namespace StrikeballServer.Services;

/// <summary>
/// Плановая ротация ключей: раз в сутки проверяет возраст активных ключей
/// и ротирует те, что старше порога (по умолчанию 30 дней).
/// </summary>
public class BeaconKeyRotationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BeaconKeyRotationHostedService> _logger;
    private readonly TimeSpan _rotationPeriod;
    private readonly TimeSpan _gracePeriod;

    public BeaconKeyRotationHostedService(IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<BeaconKeyRotationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var days = configuration.GetValue<int>("Security:KeyRotationDays", 30);
        var graceDays = configuration.GetValue<int>("Security:PreviousKeyGraceDays", 7);

        _rotationPeriod = TimeSpan.FromDays(Math.Max(1, days));
        _gracePeriod = TimeSpan.FromDays(Math.Max(1, graceDays));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Стартовый небольшой отложенный запуск, чтобы не мешать cold start.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RotateOutdatedKeysAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка плановой ротации ключей");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RotateOutdatedKeysAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var keyStore = scope.ServiceProvider.GetRequiredService<IBeaconKeyStore>();

        var border = DateTime.UtcNow.Subtract(_rotationPeriod);

        var outdatedBeaconIds = await context.BeaconSecrets
            .Where(s => s.IsActive && s.CreatedAtUtc < border)
            .Select(s => s.BeaconId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var beaconId in outdatedBeaconIds)
        {
            var newVersion = await keyStore.RotateKeyAsync(beaconId, _gracePeriod, cancellationToken);
            _logger.LogInformation("Плановая ротация: beacon {beaconId} -> key version {version}", beaconId, newVersion);
        }
    }
}
