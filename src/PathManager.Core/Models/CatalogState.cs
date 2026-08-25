using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PathManager.Core.Models;

public class CatalogState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("commands")]
    public List<CommandEntry> Commands { get; set; } = new();

    public CatalogState Clone()
    {
        var clone = new CatalogState
        {
            Version = Version,
            Commands = new List<CommandEntry>()
        };
        foreach (var cmd in Commands)
        {
            clone.Commands.Add(cmd.Clone());
        }
        return clone;
    }
}
