using System.ComponentModel.DataAnnotations;

namespace StrikeballServer.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Login { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Role { get; set; } = "player";

    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    public byte[]? HmacKeyCiphertext { get; set; }

    public byte[]? HmacKeyNonce { get; set; }

    public byte[]? HmacKeyTag { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAtUtc { get; set; }

    public ICollection<GpsPosition> GpsPositions { get; set; } = new List<GpsPosition>();

    public ICollection<DetectedPerson> ReportedDetections { get; set; } = new List<DetectedPerson>();
}