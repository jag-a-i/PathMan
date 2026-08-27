using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PathManager.Core.Catalog;
using PathManager.Core.Env;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;
using PathManager.Core.Snapshot;

namespace PathManager.Core.Doctor;

public class DoctorService
{
    private readonly IEnvironmentProvider _env;
    private readonly PathStore _pathStore;
    private readonly CatalogManager _catalog;
    private readonly ShimManager _shim;
    private readonly SnapshotManager? _snapshot;

    public DoctorService(
        IEnvironmentProvider env,
        PathStore pathStore,
        CatalogManager catalog,
        ShimManager shim,
        SnapshotManager? snapshot = null)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));
        _snapshot = snapshot;
    }

    public async Task<DoctorReport> RunDiagnosticsAsync()
    {
        var report = new DoctorReport();

        var userPath = _pathStore.GetUserPath();
        var machinePath = _pathStore.GetMachinePath();
        var combinedPath = PathList.Combine(machinePath, userPath);

        report.UserPathLength = userPath.RawLength;
        report.UserPathCount = userPath.Count;
        report.MachinePathLength = machinePath.RawLength;
        report.MachinePathCount = machinePath.Count;
        report.CombinedPathLength = combinedPath.RawLength;
        report.CombinedPathCount = combinedPath.Count;

        report.ShimDirPath = _shim.ShimsDirectory;
        report.IsShimDirOnUserPath = userPath.Contains(_shim.ShimsDirectory);

        // Duplicates
        report.DuplicateEntries = combinedPath.GetDuplicates();

        // Dead directories check
        var deadList = new List<DeadPathEntry>();
        foreach (var entry in combinedPath)
        {
            var exists = await _env.DirectoryExistsWithTimeoutAsync(entry.Expanded, 200);
            if (!exists)
            {
                deadList.Add(new DeadPathEntry
                {
                    Path = entry.Raw,
                    IsUnc = entry.IsUnc,
                    Reason = entry.IsUnc ? "Unreachable or slow network path (>200ms timeout)" : "Directory does not exist"
                });
            }
        }
        report.DeadDirectories = deadList;

        // Catalog & Shims health
        var catalogState = _catalog.LoadCatalog();
        report.TotalCommands = catalogState.Commands.Count;

        foreach (var cmd in catalogState.Commands)
        {
            if (!_env.FileExists(cmd.Target))
            {
                report.StaleCommands.Add(cmd.Name);
            }

            var shimMeta = Path.Combine(_shim.ShimsDirectory, cmd.Name + ".exe.meta");
            if (!File.Exists(shimMeta))
            {
                report.CatalogDrift.Add(new CatalogDriftItem
                {
                    CommandName = cmd.Name,
                    Issue = "Catalog entry exists but shim sidecar metadata (.meta) is missing"
                });
            }
        }

        // Check for orphan shims in shims directory not in catalog
        if (Directory.Exists(_shim.ShimsDirectory))
        {
            var metaFiles = Directory.GetFiles(_shim.ShimsDirectory, "*.exe.meta");
            foreach (var meta in metaFiles)
            {
                var cmdName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta));
                if (!catalogState.Commands.Any(c => string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase)))
                {
                    report.CatalogDrift.Add(new CatalogDriftItem
                    {
                        CommandName = cmdName,
                        Issue = "Shim file exists in directory but is not tracked in catalog"
                    });
                }
            }
        }

        // Profile hook check
        var userProfile = _env.GetUserProfile();
        var pwshProfile = Path.Combine(userProfile, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
        var winPsProfile = Path.Combine(userProfile, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");

        report.ProfilePath = File.Exists(pwshProfile) ? pwshProfile : winPsProfile;
        report.IsProfileHookInstalled = false;

        if (File.Exists(pwshProfile))
        {
            var text = File.ReadAllText(pwshProfile);
            if (text.Contains("PathManager", StringComparison.OrdinalIgnoreCase))
            {
                report.IsProfileHookInstalled = true;
            }
        }
        else if (File.Exists(winPsProfile))
        {
            var text = File.ReadAllText(winPsProfile);
            if (text.Contains("PathManager", StringComparison.OrdinalIgnoreCase))
            {
                report.IsProfileHookInstalled = true;
            }
        }

        return report;
    }

    public async Task<DoctorReport> RepairAsync()
    {
        var repairsApplied = new List<string>();

        // 1. Take safety snapshot before mutating User PATH or Catalog
        _snapshot?.CreateSnapshot("doctor-repair", details: "Automated repair of User PATH and command catalog");

        var initialUserPath = _pathStore.GetUserPath();
        var machinePath = _pathStore.GetMachinePath();
        var machineKeys = new HashSet<string>(machinePath.Select(e => e.Expanded.TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);

        var repairedEntries = new List<string>();
        var seenUserKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var removedMachineDups = new List<string>();
        var removedInternalDups = new List<string>();
        var prunedDeadDirs = new List<string>();

        // Always put shims directory at the very front
        repairedEntries.Add(_shim.ShimsDirectory);
        seenUserKeys.Add(_shim.ShimsDirectory.TrimEnd('\\', '/'));

        if (!initialUserPath.Contains(_shim.ShimsDirectory))
        {
            repairsApplied.Add($"Added PathManager shims directory to User PATH ({_shim.ShimsDirectory})");
        }

        foreach (var entry in initialUserPath)
        {
            if (entry.EqualsNormalized(_shim.ShimsDirectory))
            {
                continue; // Already at front
            }

            var expandedNorm = entry.Expanded.TrimEnd('\\', '/');

            // Check if already covered by Machine PATH
            if (machineKeys.Contains(expandedNorm))
            {
                removedMachineDups.Add(entry.Raw);
                continue;
            }

            // Check if duplicate within User PATH
            if (!seenUserKeys.Add(expandedNorm))
            {
                removedInternalDups.Add(entry.Raw);
                continue;
            }

            // Check if dead local directory
            if (!entry.IsUnc)
            {
                var exists = _env.DirectoryExists(entry.Expanded);
                if (!exists)
                {
                    prunedDeadDirs.Add(entry.Raw);
                    continue;
                }
            }

            repairedEntries.Add(entry.Raw);
        }

        var repairedPathList = new PathList(repairedEntries);
        if (repairedPathList.ToJoinedString() != initialUserPath.ToJoinedString())
        {
            _pathStore.SetUserPath(repairedPathList);

            if (removedMachineDups.Count > 0)
            {
                repairsApplied.Add($"Removed {removedMachineDups.Count} redundant Machine PATH duplicates from User PATH ({string.Join(", ", removedMachineDups.Take(3))}{(removedMachineDups.Count > 3 ? "..." : "")})");
            }
            if (removedInternalDups.Count > 0)
            {
                repairsApplied.Add($"Deduplicated {removedInternalDups.Count} repeat entries within User PATH");
            }
            if (prunedDeadDirs.Count > 0)
            {
                repairsApplied.Add($"Pruned {prunedDeadDirs.Count} non-existent directories from User PATH ({string.Join(", ", prunedDeadDirs.Take(3))}{(prunedDeadDirs.Count > 3 ? "..." : "")})");
            }

            repairsApplied.Add($"Reduced User PATH from {initialUserPath.RawLength} chars ({initialUserPath.Count} dirs) to {repairedPathList.RawLength} chars ({repairedPathList.Count} dirs)");
        }

        // 2. Rebuild catalog from shims directory if drift exists
        var preReport = await RunDiagnosticsAsync();
        if (preReport.CatalogDrift.Count > 0)
        {
            _catalog.RebuildFromShims(_shim.ShimsDirectory);
            repairsApplied.Add($"Rebuilt command catalog state for {preReport.CatalogDrift.Count} drifted items");
        }

        // Run fresh post-repair diagnostics
        var finalReport = await RunDiagnosticsAsync();
        finalReport.RepairsApplied = repairsApplied;
        return finalReport;
    }
}
