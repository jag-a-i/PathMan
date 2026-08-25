using System;
using System.Text.Json.Serialization;

namespace PathManager.Core.Models;

public class AuditEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("snapshotId")]
    public string? SnapshotId { get; set; }
}
