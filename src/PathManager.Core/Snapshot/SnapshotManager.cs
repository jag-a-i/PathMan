using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PathManager.Core.Catalog;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;

namespace PathManager.Core.Snapshot;

public class SnapshotManager
{
    private readonly IEnvironmentProvider _env;
    private readonly PathStore _pathStore;
    private readonly CatalogManager _catalog;
    private readonly ShimManager _shim;
    private readonly string _snapshotsDir;
    private readonly string _auditLogPath;

    public string SnapshotsDirectory => _snapshotsDir;
    public string AuditLogPath => _auditLogPath;

    public SnapshotManager(IEnvironmentProvider env, PathStore pathStore, CatalogManager catalog, ShimManager shim)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));

        var pmHome = _env.GetPmHome();
        _snapshotsDir = Path.Combine(pmHome, "snapshots");
        _auditLogPath = Path.Combine(pmHome, "audit.jsonl");

        Directory.CreateDirectory(_snapshotsDir);
    }

    public string CreateSnapshot(string action, string? target = null, string? details = null)
    {
        var timestampStr = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var snapDir = Path.Combine(_snapshotsDir, timestampStr);
        Directory.CreateDirectory(snapDir);

        // 1. Save copy of current catalog state.json
        if (File.Exists(_catalog.CatalogPath))
        {
            File.Copy(_catalog.CatalogPath, Path.Combine(snapDir, "state.json"), overwrite: true);
        }
        else
        {
            File.WriteAllText(Path.Combine(snapDir, "state.json"), "{\"version\":1,\"commands\":[]}");
        }

        // 2. Save copy of current User PATH
        var userPath = _pathStore.GetUserPath();
        File.WriteAllText(Path.Combine(snapDir, "UserPath.txt"), userPath.ToJoinedString());

        // 3. Append to audit log
        var audit = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            Action = action,
            Target = target,
            Details = details,
            SnapshotId = timestampStr
        };
        var line = JsonSerializer.Serialize(audit);
        File.AppendAllText(_auditLogPath, line + System.Environment.NewLine);

        return timestampStr;
    }

    public string Undo()
    {
        if (!Directory.Exists(_snapshotsDir))
        {
            throw new NothingToUndoException();
        }

        var dirs = Directory.GetDirectories(_snapshotsDir).OrderByDescending(d => d).ToList();
        if (dirs.Count == 0)
        {
            throw new NothingToUndoException();
        }

        var latestSnapDir = dirs[0];
        var stateJsonPath = Path.Combine(latestSnapDir, "state.json");
        var userPathTxtPath = Path.Combine(latestSnapDir, "UserPath.txt");

        if (!File.Exists(stateJsonPath) || !File.Exists(userPathTxtPath))
        {
            throw new UndoConflictException($"Corrupted snapshot folder '{Path.GetFileName(latestSnapDir)}'.");
        }

        var restoredPathContent = File.ReadAllText(userPathTxtPath);
        var restoredCatalogContent = File.ReadAllText(stateJsonPath);

        // Restore User PATH
        _pathStore.SetUserPath(new PathList(restoredPathContent));

        // Restore catalog
        var restoredState = JsonSerializer.Deserialize<CatalogState>(restoredCatalogContent);
        if (restoredState != null)
        {
            _catalog.SaveCatalog(restoredState);

            // Re-sync shims
            if (Directory.Exists(_shim.ShimsDirectory))
            {
                var existingMetaFiles = Directory.GetFiles(_shim.ShimsDirectory, "*.exe.meta");
                var activeNames = restoredState.Commands.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var meta in existingMetaFiles)
                {
                    var cmdName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta));
                    if (!activeNames.Contains(cmdName))
                    {
                        _shim.RemoveShim(cmdName);
                    }
                }

                foreach (var cmd in restoredState.Commands)
                {
                    _shim.CreateShim(cmd);
                }
            }
        }

        // Delete used snapshot directory
        try { Directory.Delete(latestSnapDir, recursive: true); } catch { }

        // Log undo
        var audit = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            Action = "undo",
            Details = $"Restored snapshot {Path.GetFileName(latestSnapDir)}"
        };
        File.AppendAllText(_auditLogPath, JsonSerializer.Serialize(audit) + System.Environment.NewLine);

        return Path.GetFileName(latestSnapDir);
    }
}
