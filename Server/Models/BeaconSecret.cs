using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrikeballServer.Models;

/// <summary>
/// Зашифрованный секрет маяка для HMAC-подписи.
/// Ключи хранятся только в зашифрованном виде (AES-256-GCM).
/// </summary>
public class BeaconSecret
{
    [Key]
    public long Id { get; set; }

    [Required]
    public int BeaconId { get; set; }

    /// <summary>
    /// Версия ключа для поддержки ротации.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int KeyVersion { get; set; }

    [Required]
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    [Required]
    [MaxLength(12)]
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    [Required]
    [MaxLength(16)]
    public byte[] Tag { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Активный ключ применяется для подписи новых пакетов.
    /// Предыдущий активный ключ может временно оставаться валидным для проверки.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntilUtc { get; set; }

    [ForeignKey(nameof(BeaconId))]
    public Beacon Beacon { get; set; } = null!;
}
