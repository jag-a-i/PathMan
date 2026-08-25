using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PathManager.Core.Models;

public class PathEntry
{
    public string Raw { get; }
    public string Expanded { get; }
    public bool IsUnc => Raw.StartsWith(@"\\", StringComparison.Ordinal) || Raw.StartsWith("//", StringComparison.Ordinal);

    public PathEntry(string raw)
    {
        Raw = raw.Trim().Trim('"');
        Expanded = System.Environment.ExpandEnvironmentVariables(Raw);
    }

    public override string ToString() => Raw;

    public bool EqualsNormalized(string other)
    {
        var otherTrimmed = other.Trim().Trim('"');
        var otherExpanded = System.Environment.ExpandEnvironmentVariables(otherTrimmed);
        return string.Equals(Raw, otherTrimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Expanded, otherExpanded, StringComparison.OrdinalIgnoreCase);
    }
}

public class PathList : IEnumerable<PathEntry>
{
    public const int GuiLimit = 2047;
    public const int CmdLimit = 8191;
    public const int Win32Limit = 32767;

    private readonly List<PathEntry> _entries = new();

    public IReadOnlyList<PathEntry> Entries => _entries;
    public int Count => _entries.Count;

    public PathList() { }

    public PathList(string? rawPath)
    {
        if (!string.IsNullOrWhiteSpace(rawPath))
        {
            var parts = rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim().Trim('"');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    _entries.Add(new PathEntry(trimmed));
                }
            }
        }
    }

    public PathList(IEnumerable<string> rawEntries)
    {
        foreach (var part in rawEntries)
        {
            var trimmed = part.Trim().Trim('"');
            if (!string.IsNullOrEmpty(trimmed))
            {
                _entries.Add(new PathEntry(trimmed));
            }
        }
    }

    public bool Contains(string directory)
    {
        return _entries.Any(e => e.EqualsNormalized(directory));
    }

    public int IndexOf(string directory)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].EqualsNormalized(directory))
                return i;
        }
        return -1;
    }

    public bool Prepend(string directory)
    {
        var trimmed = directory.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return false;

        // Remove existing instance if present so it moves to front
        _entries.RemoveAll(e => e.EqualsNormalized(trimmed));
        _entries.Insert(0, new PathEntry(trimmed));
        return true;
    }

    public bool Append(string directory)
    {
        var trimmed = directory.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return false;

        if (Contains(trimmed)) return false;
        _entries.Add(new PathEntry(trimmed));
        return true;
    }

    public bool Remove(string directory)
    {
        var trimmed = directory.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return false;

        var countBefore = _entries.Count;
        _entries.RemoveAll(e => e.EqualsNormalized(trimmed));
        return _entries.Count < countBefore;
    }

    public PathList Deduplicate()
    {
        var result = new PathList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _entries)
        {
            var key = entry.Expanded.TrimEnd('\\', '/');
            if (seen.Add(key))
            {
                result._entries.Add(entry);
            }
        }

        return result;
    }

    public List<string> GetDuplicates()
    {
        var duplicates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _entries)
        {
            var key = entry.Expanded.TrimEnd('\\', '/');
            if (!seen.Add(key))
            {
                duplicates.Add(entry.Raw);
            }
        }

        return duplicates;
    }

    public string ToJoinedString()
    {
        return string.Join(";", _entries.Select(e => e.Raw));
    }

    public int RawLength => ToJoinedString().Length;

    public IEnumerator<PathEntry> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static PathList Combine(PathList machinePath, PathList userPath)
    {
        // Windows PATH resolution order: Machine PATH first, then User PATH
        var combinedEntries = new List<string>();
        foreach (var entry in machinePath)
        {
            combinedEntries.Add(entry.Raw);
        }
        foreach (var entry in userPath)
        {
            combinedEntries.Add(entry.Raw);
        }
        return new PathList(combinedEntries);
    }
}
