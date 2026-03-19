using Microsoft.EntityFrameworkCore;
using StrikeballServer.Models;

namespace StrikeballServer.Data;

/// <summary>
/// Контекст базы данных для системы позиционирования
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // DbSets for all entities
    public DbSet<Anchor> Anchors { get; set; } = null!;
    public DbSet<Beacon> Beacons { get; set; } = null!;
    public DbSet<Measurement> Measurements { get; set; } = null!;
    public DbSet<Position> Positions { get; set; } = null!;
    public DbSet<Anomaly> Anomalies { get; set; } = null!;
    public DbSet<BeaconSecret> BeaconSecrets { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Anchor configuration
        modelBuilder.Entity<Anchor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.X).IsRequired();
            entity.Property(e => e.Y).IsRequired();
            entity.Property(e => e.Z).IsRequired();
            entity.Property(e => e.MacAddress).HasMaxLength(17);
            entity.Property(e => e.CalibrationOffset).HasDefaultValue(0.0);
            entity.Property(e => e.Status).HasDefaultValue(AnchorStatus.Active);
            entity.HasIndex(e => e.MacAddress).IsUnique();
        });

        // Beacon configuration
        modelBuilder.Entity<Beacon>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.MacAddress).HasMaxLength(17);
            entity.Property(e => e.BatteryLevel).HasDefaultValue(100);
            entity.Property(e => e.Status).HasDefaultValue(BeaconStatus.Active);
            entity.Property(e => e.KeyVersion).HasDefaultValue(1);
            entity.HasIndex(e => e.MacAddress).IsUnique();
        });

        // Measurement configuration
        modelBuilder.Entity<Measurement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Distance).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.PacketSequence).IsRequired(false);
            
            entity.HasOne(e => e.Beacon)
                .WithMany(b => b.Measurements)
                .HasForeignKey(e => e.BeaconId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Anchor)
                .WithMany(a => a.Measurements)
                .HasForeignKey(e => e.AnchorId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for faster queries by beacon and timestamp
            entity.HasIndex(e => new { e.BeaconId, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.BeaconId, e.PacketSequence }).IsUnique();
        });

        // Position configuration
        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.X).IsRequired();
            entity.Property(e => e.Y).IsRequired();
            entity.Property(e => e.Z).IsRequired();
            entity.Property(e => e.Confidence).HasDefaultValue(1.0);
            entity.Property(e => e.Method).HasMaxLength(20).HasDefaultValue("TWR");
            entity.Property(e => e.Timestamp).IsRequired();

            entity.HasOne(e => e.Beacon)
                .WithMany(b => b.Positions)
                .HasForeignKey(e => e.BeaconId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for faster queries by beacon and timestamp
            entity.HasIndex(e => new { e.BeaconId, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
        });

        // Anomaly configuration
        modelBuilder.Entity<Anomaly>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.HasIndex(e => e.BeaconId);
            entity.HasIndex(e => e.Timestamp);
        });

        // BeaconSecret configuration
        modelBuilder.Entity<BeaconSecret>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyVersion).IsRequired();
            entity.Property(e => e.Ciphertext).IsRequired();
            entity.Property(e => e.Nonce).IsRequired();
            entity.Property(e => e.Tag).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(false);
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasOne(e => e.Beacon)
                .WithMany()
                .HasForeignKey(e => e.BeaconId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.BeaconId, e.KeyVersion }).IsUnique();
            entity.HasIndex(e => new { e.BeaconId, e.IsActive });
        });

        // Seed initial data (optional)
        SeedData(modelBuilder);
    }

    /// <summary>
    /// Заполнение начальных данных для тестирования
    /// </summary>
    private void SeedData(ModelBuilder modelBuilder)
    {
        // Seed 4 anchors in a square configuration (for testing)
        modelBuilder.Entity<Anchor>().HasData(
            new Anchor
            {
                Id = 1,
                Name = "Anchor_1",
                X = 0.0,
                Y = 0.0,
                Z = 2.0,
                Status = AnchorStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Anchor
            {
                Id = 2,
                Name = "Anchor_2",
                X = 50.0,
                Y = 0.0,
                Z = 2.0,
                Status = AnchorStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Anchor
            {
                Id = 3,
                Name = "Anchor_3",
                X = 50.0,
                Y = 50.0,
                Z = 2.0,
                Status = AnchorStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Anchor
            {
                Id = 4,
                Name = "Anchor_4",
                X = 0.0,
                Y = 50.0,
                Z = 2.0,
                Status = AnchorStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );

        // Seed 2 test beacons
        modelBuilder.Entity<Beacon>().HasData(
            new Beacon
            {
                Id = 1,
                Name = "Player_1",
                BatteryLevel = 100,
                Status = BeaconStatus.Active,
                LastSeen = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new Beacon
            {
                Id = 2,
                Name = "Player_2",
                BatteryLevel = 100,
                Status = BeaconStatus.Active,
                LastSeen = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}
