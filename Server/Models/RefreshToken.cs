using System.ComponentModel.DataAnnotations;

namespace StrikeballServer.Models;

/// <summary>
/// Refresh-токен для обновления JWT-сессии без повторного ввода пароля.
/// Хранится в БД, имеет фиксированный срок жизни (30 дней) и может быть отозван.
/// </summary>
public class RefreshToken
{
    [Key]
    public long Id { get; set; }

    /// <summary>Идентификатор пользователя (Login из env).</summary>
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Значение токена — криптографически случайная строка (64 байта hex).</summary>
    [Required]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiryUtc { get; set; }

    /// <summary>Отозван ли токен — при logout или обновлении пары.</summary>
    public bool IsRevoked { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
