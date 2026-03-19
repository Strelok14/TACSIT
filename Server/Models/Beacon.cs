using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrikeballServer.Models;

/// <summary>
/// Маяк - мобильное устройство на игроке
/// </summary>
public class Beacon
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// MAC адрес UWB модуля маяка
    /// </summary>
    [MaxLength(17)]
    public string? MacAddress { get; set; }

    /// <summary>
    /// Уровень батареи (0-100%)
    /// </summary>
    [Range(0, 100)]
    public int BatteryLevel { get; set; } = 100;

    /// <summary>
    /// Время последнего пакета от маяка
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Статус маяка
    /// </summary>
    public BeaconStatus Status { get; set; } = BeaconStatus.Active;

    /// <summary>
    /// Дата регистрации маяка
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Активная версия криптографического ключа маяка
    /// </summary>
    [Range(1, int.MaxValue)]
    public int KeyVersion { get; set; } = 1;

    // Navigation properties
    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
    public ICollection<Position> Positions { get; set; } = new List<Position>();
}

/// <summary>
/// Статус маяка
/// </summary>
public enum BeaconStatus
{
    Active = 1,
    Offline = 2,
    LowBattery = 3,
    Error = 4
}
