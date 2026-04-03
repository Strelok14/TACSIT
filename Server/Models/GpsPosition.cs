using System.ComponentModel.DataAnnotations;

namespace StrikeballServer.Models;

public class GpsPosition
{
    [Key]
    public long Id { get; set; }

    [Required]
    public int UserId { get; set; }

    public User? User { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? Altitude { get; set; }

    public double? Accuracy { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public long SequenceNumber { get; set; }
}