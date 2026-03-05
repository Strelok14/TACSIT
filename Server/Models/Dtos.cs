namespace StrikeballServer.Models;

/// <summary>
/// DTO для приема пакета измерений от маяка
/// </summary>
public class MeasurementPacketDto
{
    /// <summary>
    /// ID маяка
    /// </summary>
    public int BeaconId { get; set; }

    /// <summary>
    /// Список измерений расстояний до якорей
    /// </summary>
    public List<AnchorDistanceDto> Distances { get; set; } = new();

    /// <summary>
    /// Timestamp измерения (Unix milliseconds)
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Уровень батареи маяка (0-100%)
    /// </summary>
    public int? BatteryLevel { get; set; }
}

/// <summary>
/// Расстояние до конкретного якоря
/// </summary>
public class AnchorDistanceDto
{
    /// <summary>
    /// ID якоря
    /// </summary>
    public int AnchorId { get; set; }

    /// <summary>
    /// Расстояние в метрах
    /// </summary>
    public double Distance { get; set; }

    /// <summary>
    /// RSSI (опционально)
    /// </summary>
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
