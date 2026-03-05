using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrikeballServer.Models;

/// <summary>
/// Якорь - стационарная базовая станция с известными координатами
/// </summary>
public class Anchor
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Координата X в метрах
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Координата Y в метрах
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Координата Z в метрах (высота)
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// MAC адрес UWB модуля якоря
    /// </summary>
    [MaxLength(17)]
    public string? MacAddress { get; set; }

    /// <summary>
    /// Калибровочное смещение для коррекции измерений (в метрах)
    /// </summary>
    public double CalibrationOffset { get; set; } = 0.0;

    /// <summary>
    /// Статус якоря
    /// </summary>
    public AnchorStatus Status { get; set; } = AnchorStatus.Active;

    /// <summary>
    /// Дата и время создания записи
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Дата и время последнего обновления
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
}

/// <summary>
/// Статус якоря
/// </summary>
public enum AnchorStatus
{
    Active = 1,
    Inactive = 2,
    Error = 3,
    Maintenance = 4
}
