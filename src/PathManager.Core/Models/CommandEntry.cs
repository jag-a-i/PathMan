using System;
using System.Text.Json.Serialization;

namespace PathManager.Core.Models;

public class CommandEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = "exe";

    [JsonPropertyName("hostPath")]
    public string? HostPath { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("completer")]
    public string? Completer { get; set; }

    [JsonPropertyName("trustedWritable")]
    public bool TrustedWritable { get; set; } = false;

    public CommandEntry Clone()
    {
        return new CommandEntry
        {
            Name = Name,
            Target = Target,
            Host = Host,
            HostPath = HostPath,
            CreatedAt = CreatedAt,
            Completer = Completer,
            TrustedWritable = TrustedWritable
        };
    }
}
