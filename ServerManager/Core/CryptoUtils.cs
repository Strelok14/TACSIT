using System.Security.Cryptography;
using System.Text;

namespace TacidManager.Core;

/// <summary>
/// Утилиты генерации и валидации криптографических ключей для T.A.C.I.D.
/// Все ключи генерируются через CSPRNG (RandomNumberGenerator).
/// </summary>
internal static class CryptoUtils
{
    // ─── Генерация ────────────────────────────────────────────────────────

    /// <summary>
    /// Генерирует JWT Signing Key: случайные байты → URL-safe Base64.
    /// По умолчанию 48 байт (384 бит), что с запасом перекрывает требование HMAC-SHA256.
    /// </summary>
    public static string GenerateJwtSigningKey(int byteLength = 48)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        // URL-safe Base64 без padding для удобства ENV-переменных
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Генерирует Master Key (AES-256): ровно 32 байта → standard Base64.
    /// Совместим с BeaconKeyStore (требует length == 32).
    /// </summary>
    public static string GenerateMasterKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Генерирует ключ маяка для прошивки и записи в БД через POST /api/security/beacons/{id}/key.
    /// 32 байта (256 бит) → Base64 для совместимости с HMAC-SHA256.
    /// </summary>
    public static string GenerateBeaconKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    // ─── Валидация ────────────────────────────────────────────────────────

    /// <summary>
    /// JWT key должен быть не менее 32 байт в кодировке UTF-8 (256 бит для HMAC-SHA256).
    /// </summary>
    public static bool IsValidJwtKey(string key)
        => !string.IsNullOrWhiteSpace(key) && Encoding.UTF8.GetByteCount(key) >= 32;

    /// <summary>
    /// Master key: валидный Base64, декодируется в ровно 32 байта.
    /// </summary>
    public static bool IsValidMasterKey(string b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return false;
        try
        {
            var bytes = Convert.FromBase64String(NormalizePadding(b64));
            return bytes.Length == 32;
        }
        catch
        {
            return false;
        }
    }

    // ─── Вспомогательные ──────────────────────────────────────────────────

    /// <summary>
    /// Восстанавливает padding Base64, если он был убран (URL-safe ключи).
    /// </summary>
    public static string NormalizePadding(string b64)
    {
        var pad = (4 - b64.Length % 4) % 4;
        return pad == 0 ? b64 : b64 + new string('=', pad);
    }
}
