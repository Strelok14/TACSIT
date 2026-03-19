using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using System.Security.Cryptography;

namespace StrikeballServer.Services;

/// <summary>
/// Безопасное хранилище ключей маяков:
/// - хранит ключи только в зашифрованном виде (AES-256-GCM)
/// - поддерживает active/previous ключ для бесшовной ротации
/// - при отказе БД может временно читать fallback-ключ из переменной окружения
/// </summary>
public class BeaconKeyStore : IBeaconKeyStore
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BeaconKeyStore> _logger;
    private readonly byte[] _masterKey;

    public BeaconKeyStore(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<BeaconKeyStore> logger)
    {
        _context = context;
        _logger = logger;

        var masterKeyB64 = configuration["Security:MasterKeyB64"]
            ?? Environment.GetEnvironmentVariable("TACID_MASTER_KEY_B64");

        if (string.IsNullOrWhiteSpace(masterKeyB64))
        {
            throw new InvalidOperationException("Не задан мастер-ключ TACID_MASTER_KEY_B64 (Base64 32 bytes)");
        }

        _masterKey = Convert.FromBase64String(masterKeyB64);
        if (_masterKey.Length != 32)
        {
            throw new InvalidOperationException("Мастер-ключ должен иметь длину 32 байта (AES-256)");
        }
    }

    public async Task<IReadOnlyList<BeaconKeyCandidate>> GetVerificationKeysAsync(int beaconId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _context.BeaconSecrets
            .Where(s => s.BeaconId == beaconId && (s.IsActive || s.ValidUntilUtc == null || s.ValidUntilUtc > now))
            .OrderByDescending(s => s.KeyVersion)
            .Take(2)
            .ToListAsync(cancellationToken);

        var keys = new List<BeaconKeyCandidate>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var keyBytes = Decrypt(row.Ciphertext, row.Nonce, row.Tag);
                keys.Add(new BeaconKeyCandidate(row.KeyVersion, keyBytes, row.IsActive));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка расшифровки ключа маяка {beaconId} v{version}", beaconId, row.KeyVersion);
            }
        }

        // Graceful fallback: если в БД нет ключа, пытаемся взять временный из env.
        if (keys.Count == 0)
        {
            var fallback = Environment.GetEnvironmentVariable($"BEACON_KEY_{beaconId}");
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                _logger.LogWarning("Используется временный fallback-ключ из ENV для маяка {beaconId}. Рекомендуется перенести ключ в БД", beaconId);
                keys.Add(new BeaconKeyCandidate(1, Convert.FromBase64String(fallback), true));
            }
        }

        return keys;
    }

    public async Task UpsertActiveKeyAsync(int beaconId, int keyVersion, byte[] rawKey, TimeSpan? previousKeyGracePeriod = null, CancellationToken cancellationToken = default)
    {
        if (rawKey.Length < 16)
        {
            throw new ArgumentException("Ключ HMAC слишком короткий", nameof(rawKey));
        }

        var grace = previousKeyGracePeriod ?? TimeSpan.FromDays(7);
        var activeKeys = await _context.BeaconSecrets
            .Where(s => s.BeaconId == beaconId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var oldKey in activeKeys)
        {
            oldKey.IsActive = false;
            oldKey.ValidUntilUtc = DateTime.UtcNow.Add(grace);
        }

        var existing = await _context.BeaconSecrets
            .FirstOrDefaultAsync(s => s.BeaconId == beaconId && s.KeyVersion == keyVersion, cancellationToken);

        var (ciphertext, nonce, tag) = Encrypt(rawKey);
        if (existing == null)
        {
            existing = new BeaconSecret
            {
                BeaconId = beaconId,
                KeyVersion = keyVersion,
                Ciphertext = ciphertext,
                Nonce = nonce,
                Tag = tag,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                ValidUntilUtc = null
            };

            await _context.BeaconSecrets.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.Ciphertext = ciphertext;
            existing.Nonce = nonce;
            existing.Tag = tag;
            existing.IsActive = true;
            existing.ValidUntilUtc = null;
        }

        var beacon = await _context.Beacons.FindAsync(new object[] { beaconId }, cancellationToken);
        if (beacon != null)
        {
            beacon.KeyVersion = keyVersion;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RotateKeyAsync(int beaconId, TimeSpan previousKeyGracePeriod, CancellationToken cancellationToken = default)
    {
        var beacon = await _context.Beacons.FindAsync(new object[] { beaconId }, cancellationToken);
        if (beacon == null)
        {
            throw new InvalidOperationException($"Маяк {beaconId} не найден");
        }

        var newVersion = Math.Max(1, beacon.KeyVersion + 1);
        var newRawKey = RandomNumberGenerator.GetBytes(32);

        await UpsertActiveKeyAsync(beaconId, newVersion, newRawKey, previousKeyGracePeriod, cancellationToken);

        _logger.LogInformation("Выполнена ротация ключа маяка {beaconId}: новая версия {version}", beaconId, newVersion);
        return newVersion;
    }

    private (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_masterKey, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    private byte[] Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_masterKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
