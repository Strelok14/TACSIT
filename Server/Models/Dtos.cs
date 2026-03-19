namespace StrikeballServer.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// DTO для приема пакета измерений от маяка
/// </summary>
public class MeasurementPacketDto
{
    /// <summary>
    /// ID маяка
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "BeaconId должен быть положительным")]
    public int BeaconId { get; set; }

    /// <summary>
    /// Список измерений расстояний до якорей
    /// </summary>
    [Required(ErrorMessage = "Список измерений обязателен")]
    [MinLength(1, ErrorMessage = "Нужно минимум одно измерение")]
    public List<AnchorDistanceDto> Distances { get; set; } = new();

    /// <summary>
    /// Timestamp измерения (Unix milliseconds)
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "Timestamp должен быть положительным")]
    public long Timestamp { get; set; }

    /// <summary>
    /// Уровень батареи маяка (0-100%)
    /// </summary>
    [Range(0, 100, ErrorMessage = "Уровень батареи должен быть в диапазоне 0..100")]
    public int? BatteryLevel { get; set; }
    
    /// <summary>
    /// Последовательный номер пакета (для защиты от replay)
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "Sequence должен быть положительным")]
    public long Sequence { get; set; }

    /// <summary>
    /// Версия ключа, которой подписан пакет (для ротации ключей)
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "KeyVersion должен быть положительным")]
    public int KeyVersion { get; set; } = 1;

    /// <summary>
    /// HMAC подпись пакета в Base64
    /// </summary>
    [Required(ErrorMessage = "Signature обязательна")]
    [MinLength(32, ErrorMessage = "Signature слишком короткая")]
    public string Signature { get; set; } = string.Empty;
}

/// <summary>
/// Расстояние до конкретного якоря
/// </summary>
public class AnchorDistanceDto
{
    /// <summary>
    /// ID якоря
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "AnchorId должен быть положительным")]
    public int AnchorId { get; set; }

    /// <summary>
    /// Расстояние в метрах
    /// </summary>
    [Range(0.01, 200.0, ErrorMessage = "Distance должен быть в диапазоне 0.01..200 м")]
    public double Distance { get; set; }

    /// <summary>
    /// RSSI (опционально)
    /// </summary>
    [Range(-130, 0, ErrorMessage = "RSSI должен быть в диапазоне -130..0 дБм")]
    public int? Rssi { get; set; }
}

/// <summary>
/// DTO для отправки позиции маяка
/// </summary>
public class PositionDto
{
    public int BeaconId { get; set; }
    public string BeaconName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Confidence { get; set; }
    public string Method { get; set; } = "TWR";
    public DateTime Timestamp { get; set; }
    public int AnchorsUsed { get; set; }
}

/// <summary>
/// DTO для списка всех позиций (для real-time обновлений)
/// </summary>
public class AllPositionsDto
{
    public List<PositionDto> Positions { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TotalBeacons { get; set; }
}

/// <summary>
/// DTO для /auth/login
/// </summary>
public class AuthRequestDto
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Ответ /auth/login и /auth/refresh
/// </summary>
public class AuthResponseDto
{
    public bool Success { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshExpiresAtUtc { get; set; }
}

/// <summary>
/// Запрос /auth/refresh — передаётся действующий refresh-токен.
/// </summary>
public class RefreshRequestDto
{
    [Required(ErrorMessage = "RefreshToken обязателен")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Запрос /auth/logout — опционально передаётся refresh-токен для принудительного отзыва.
/// </summary>
public class LogoutRequestDto
{
    public string? RefreshToken { get; set; }
}
