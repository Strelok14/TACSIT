using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using StrikeballServer.Services;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace StrikeballServer.Tests.Integration;

/// <summary>
/// 2.5 Beacon key encryption tests:
/// - Ключи в БД хранятся в зашифрованном виде (не plain text)
/// - Расшифровка работает с правильным мастер-ключом
/// - Расшифровка падает с неверным мастер-ключом
/// - GetVerificationKeysAsync возвращает верный rawKey
/// </summary>
[Collection("EncryptionTests")]
public class BeaconKeyEncryptionTests : IClassFixture<TacidWebApplicationFactory>
{
    private const int TestBeaconId = 204;
    private static readonly byte[] RawKey = Encoding.UTF8.GetBytes("my-test-hmac-key-32bytes-padded!!");

    private readonly TacidWebApplicationFactory _factory;

    public BeaconKeyEncryptionTests(TacidWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StoredKey_IsEncrypted_NotPlainText()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var keyStore = scope.ServiceProvider.GetRequiredService<IBeaconKeyStore>();

        // Убеждаемся, что маяк существует.
        if (!db.Beacons.Any(b => b.Id == TestBeaconId))
        {
            db.Beacons.Add(new Beacon { Id = TestBeaconId, Name = "EncTest", Status = BeaconStatus.Active, BatteryLevel = 100, KeyVersion = 1 });
            await db.SaveChangesAsync();
        }

        // Записываем ключ через keyStore.
        await keyStore.UpsertActiveKeyAsync(TestBeaconId, 1, RawKey);

        // Читаем строку напрямую из БД.
        var stored = await db.BeaconSecrets
            .Where(s => s.BeaconId == TestBeaconId && s.IsActive)
            .FirstOrDefaultAsync();

        Assert.NotNull(stored);

        // Ciphertext не должен совпадать с raw key (т.е. данные зашифрованы).
        Assert.NotEqual(RawKey, stored!.Ciphertext);

        // Nonce должен быть 12 байт (AES-GCM стандарт).
        Assert.Equal(12, stored.Nonce.Length);

        // Tag должен быть 16 байт (AES-GCM authentication tag).
        Assert.Equal(16, stored.Tag.Length);
    }

    [Fact]
    public async Task GetVerificationKeys_ReturnsCorrectRawKey()
    {
        // Инициализируем маяк и ключ через SeedBeaconWithKeyAsync.
        await _factory.SeedBeaconWithKeyAsync(TestBeaconId, RawKey);

        using var scope = _factory.Services.CreateScope();
        var keyStore = scope.ServiceProvider.GetRequiredService<IBeaconKeyStore>();

        var keys = await keyStore.GetVerificationKeysAsync(TestBeaconId);

        Assert.NotEmpty(keys);
        var candidate = keys.First(k => k.IsActive);
        Assert.Equal(RawKey, candidate.KeyBytes);
    }

    [Fact]
    public async Task HmacWithStoredKey_MatchesOriginalKey()
    {
        await _factory.SeedBeaconWithKeyAsync(TestBeaconId, RawKey);

        using var scope = _factory.Services.CreateScope();
        var keyStore = scope.ServiceProvider.GetRequiredService<IBeaconKeyStore>();
        var keys = await keyStore.GetVerificationKeysAsync(TestBeaconId);

        Assert.NotEmpty(keys);
        var resolvedKey = keys.First(k => k.IsActive).KeyBytes;

        // Подпись, вычисленная оригинальным ключом, должна совпасть с вычисленной из keyStore.
        var payload = Encoding.UTF8.GetBytes("test-canonical-string");
        byte[] sig1, sig2;

        using (var h1 = new HMACSHA256(RawKey))
            sig1 = h1.ComputeHash(payload);

        using (var h2 = new HMACSHA256(resolvedKey))
            sig2 = h2.ComputeHash(payload);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void WrongMasterKey_FailsToDecrypt()
    {
        // Проверяем напрямую, что AES-GCM выбрасывает исключение при неверном мастер-ключе.
        var wrongMasterKey = new byte[32];
        wrongMasterKey[0] = 0xFF; // отличается от тестового (all zeros)

        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[RawKey.Length];

        // Шифруем с правильным мастером (all zeros).
        using (var aes = new AesGcm(new byte[32], AesGcm.TagByteSizes.MaxSize))
        {
            aes.Encrypt(nonce, RawKey, ciphertext, tag);
        }

        // Пытаемся расшифровать с неверным мастером.
        // На разных платформах может быть либо CryptographicException,
        // либо наследник (например AuthenticationTagMismatchException).
        using var aesWrong = new AesGcm(wrongMasterKey, AesGcm.TagByteSizes.MaxSize);
        var decrypted = new byte[RawKey.Length];
        Assert.ThrowsAny<CryptographicException>(() =>
            aesWrong.Decrypt(nonce, ciphertext, tag, decrypted));
    }
}
