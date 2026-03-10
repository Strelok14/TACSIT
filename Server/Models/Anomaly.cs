using System;

namespace StrikeballServer.Models
{
    public class Anomaly
    {
        public long Id { get; set; }
        public int BeaconId { get; set; }
        public string Type { get; set; } = string.Empty; // e.g., "InvalidSignature", "TimestampDrift", "SequenceReplay"
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
