using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrikeballServer.Models;

/// <summary>
/// Вычисленная позиция маяка в 3D пространстве
/// </summary>
public class Position
{
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// ID маяка
    /// </summary>
    [Required]
    public int BeaconId { get; set; }

    /// <summary>
    /// Вычисленная координата X в метрах
    /// </summary>
    [Required]
    public double X { get; set; }

    /// <summary>
    /// Вычисленная координата Y в метрах
    /// </summary>
    [Required]
    public double Y { get; set; }

    /// <summary>
    /// Вычисленная координата Z в метрах (высота)
    /// </summary>
    [Required]
    public double Z { get; set; }

    /// <summary>
    /// Уверенность в точности позиции (0.0-1.0)
    /// 1.0 = отличная геометрия якорей, малый шум
    /// 0.0 = плохая геометрия, высокий шум
    /// </summary>
    [Range(0.0, 1.0)]
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// Метод, использованный для вычисления (TWR, TDoA и т.д.)
    /// </summary>
    [MaxLength(20)]
    public string Method { get; set; } = "TWR";

    /// <summary>
    /// Время вычисления позиции (UTC)
    /// </summary>
    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Количество якорей, использованных для вычисления
    /// </summary>
    public int AnchorsUsed { get; set; }

    /// <summary>
    /// Оценочная ошибка в метрах (опционально)
    /// </summary>
    public double? EstimatedError { get; set; }

    // Navigation properties
    [ForeignKey(nameof(BeaconId))]
    public Beacon Beacon { get; set; } = null!;
}
