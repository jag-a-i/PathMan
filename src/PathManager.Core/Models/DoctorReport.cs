using System.Collections.Generic;

namespace PathManager.Core.Models;

public enum HealthStatus
{
    Healthy,
    Warning,
    IssuesFound
}

public class DeadPathEntry
{
    public string Path { get; set; } = string.Empty;
    public bool IsUnc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CatalogDriftItem
{
    public string CommandName { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
}

public class DoctorReport
{
    public int UserPathLength { get; set; }
    public int UserPathCount { get; set; }
    public int MachinePathLength { get; set; }
    public int MachinePathCount { get; set; }
    public int CombinedPathLength { get; set; }
    public int CombinedPathCount { get; set; }

    public bool IsUserOverGuiLimit => UserPathLength > PathList.GuiLimit;
    public bool IsMachineOverGuiLimit => MachinePathLength > PathList.GuiLimit;
    public bool IsCombinedOverGuiLimit => CombinedPathLength > PathList.GuiLimit;
    public bool IsCombinedOverCmdLimit => CombinedPathLength > PathList.CmdLimit;
    public bool IsUserOverWin32Limit => UserPathLength > PathList.Win32Limit;

    public bool IsShimDirOnUserPath { get; set; }
    public string ShimDirPath { get; set; } = string.Empty;

    public List<string> DuplicateEntries { get; set; } = new();
    public List<DeadPathEntry> DeadDirectories { get; set; } = new();

    public int TotalCommands { get; set; }
    public List<string> StaleCommands { get; set; } = new();
    public List<CatalogDriftItem> CatalogDrift { get; set; } = new();

    public bool IsProfileHookInstalled { get; set; }
    public string? ProfilePath { get; set; }

    public HealthStatus Status
    {
        get
        {
            if (IsUserOverWin32Limit || !IsShimDirOnUserPath || CatalogDrift.Count > 0 || StaleCommands.Count > 0)
                return HealthStatus.IssuesFound;

            if (IsCombinedOverGuiLimit || IsCombinedOverCmdLimit || DuplicateEntries.Count > 0 || DeadDirectories.Count > 0 || !IsProfileHookInstalled)
                return HealthStatus.Warning;

            return HealthStatus.Healthy;
        }
    }
}
