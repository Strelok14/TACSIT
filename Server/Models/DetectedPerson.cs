using System.ComponentModel.DataAnnotations;

namespace StrikeballServer.Models;

public class DetectedPerson
{
    [Key]
    public long Id { get; set; }

    [Required]
    public int UserId { get; set; }

    public User? User { get; set; }

    public int? TargetUserId { get; set; }

    public User? TargetUser { get; set; }

    public bool IsAlly { get; set; }

    [MaxLength(64)]
    public string Label { get; set; } = "person";

    [Required]
    public string SkeletonData { get; set; } = "{}";

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public double? Altitude { get; set; }

    public double? Accuracy { get; set; }

    public long SequenceNumber { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}