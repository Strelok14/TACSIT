using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrikeballServer.Models;

/// <summary>
/// Сырое измерение расстояния от маяка до якоря
/// </summary>
public class Measurement
{
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// ID маяка
    /// </summary>
    [Required]
    public int BeaconId { get; set; }

    /// <summary>
    /// ID якоря
    /// </summary>
    [Required]
    public int AnchorId { get; set; }

    /// <summary>
    /// Измеренное расстояние в метрах
    /// </summary>
    [Required]
    public double Distance { get; set; }

    /// <summary>
    /// Мощность принятого сигнала (RSSI) в дБм (опционально)
    /// </summary>
    public int? Rssi { get; set; }

    /// <summary>
    /// Время измерения (UTC)
    /// </summary>
    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Sequence исходного пакета (для дедупликации backlog)
    /// </summary>
    public long? PacketSequence { get; set; }

    /// <summary>
    /// Качество измерения (0.0-1.0), опционально
    /// </summary>
    [Range(0.0, 1.0)]
    public double? Quality { get; set; }

    // Navigation properties
    [ForeignKey(nameof(BeaconId))]
    public Beacon Beacon { get; set; } = null!;

    [ForeignKey(nameof(AnchorId))]
    public Anchor Anchor { get; set; } = null!;
}
