using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;

namespace PathManager.Core.Catalog;

public class CatalogManager
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly Regex ValidNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    private readonly string _catalogPath;
    private readonly string _lockFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string CatalogPath => _catalogPath;

    public CatalogManager(IEnvironmentProvider env)
    {
        var pmHome = env.GetPmHome();
        Directory.CreateDirectory(pmHome);
        _catalogPath = Path.Combine(pmHome, "state.json");
        _lockFilePath = Path.Combine(pmHome, ".catalog.lock");
    }

    public CatalogManager(string catalogPath)
    {
        _catalogPath = catalogPath;
        var dir = Path.GetDirectoryName(catalogPath) ?? ".";
        Directory.CreateDirectory(dir);
        _lockFilePath = Path.Combine(dir, ".catalog.lock");
    }

    public static void ValidateCommandName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidCommandNameException(name ?? "", "Command name cannot be empty.");
        }

        if (ReservedNames.Contains(name))
        {
            throw new InvalidCommandNameException(name, $"'{name}' is a reserved Windows device name.");
        }

        if (!ValidNameRegex.IsMatch(name))
        {
            throw new InvalidCommandNameException(name, "Name must start with an alphanumeric character and contain only alphanumeric characters, dots, hyphens, or underscores.");
        }

        if (name.Contains(".."))
        {
            throw new InvalidCommandNameException(name, "Name cannot contain '..' path traversal sequences.");
        }
    }

    public CatalogState LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
        {
            return new CatalogState();
        }

        try
        {
            using var fileStream = new FileStream(_catalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var state = JsonSerializer.Deserialize<CatalogState>(fileStream, _jsonOptions);
            return state ?? new CatalogState();
        }
        catch (JsonException ex)
        {
            throw new CatalogPersistFailedException($"Catalog JSON at '{_catalogPath}' is corrupted: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not PathManagerException)
        {
            throw new CatalogPersistFailedException($"Failed to read catalog: {ex.Message}", ex);
        }
    }

    public void SaveCatalog(CatalogState state)
    {
        var dir = Path.GetDirectoryName(_catalogPath) ?? ".";
        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $"state.json.tmp.{Guid.NewGuid():N}");

        FileStream? lockStream = null;
        var maxRetries = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                lockStream = new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException)
            {
                if (i == maxRetries - 1)
                {
                    throw new CatalogLockedException();
                }
                Thread.Sleep(50);
            }
        }

        try
        {
            // Validate all command entries before saving
            foreach (var cmd in state.Commands)
            {
                ValidateCommandName(cmd.Name);
                if (string.IsNullOrWhiteSpace(cmd.Target))
                {
                    throw new CatalogPersistFailedException($"Command '{cmd.Name}' has an empty target path.");
                }
                if (cmd.Target.Contains(".."))
                {
                    throw new CatalogPersistFailedException($"Target path for '{cmd.Name}' contains illegal '..' traversal.");
                }
            }

            // Write to temp file first
            using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(tempStream, state, _jsonOptions);
                tempStream.Flush(flushToDisk: true);
            }

            // Atomic replacement
            if (File.Exists(_catalogPath))
            {
                File.Replace(tempPath, _catalogPath, null);
            }
            else
            {
                File.Move(tempPath, _catalogPath);
            }
        }
        catch (Exception ex) when (ex is not PathManagerException)
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw new CatalogPersistFailedException($"Failed to write catalog: {ex.Message}", ex);
        }
        finally
        {
            if (lockStream != null)
            {
                lockStream.Dispose();
                try { File.Delete(_lockFilePath); } catch { }
            }
        }
    }

    public CommandEntry? GetCommand(string name)
    {
        var state = LoadCatalog();
        return state.Commands.Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdateCommand(CommandEntry entry)
    {
        ValidateCommandName(entry.Name);
        var state = LoadCatalog();
        state.Commands.RemoveAll(c => string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        state.Commands.Add(entry);
        SaveCatalog(state);
    }

    public bool RemoveCommand(string name)
    {
        var state = LoadCatalog();
        var countBefore = state.Commands.Count;
        state.Commands.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (state.Commands.Count < countBefore)
        {
            SaveCatalog(state);
            return true;
        }
        return false;
    }

    public IReadOnlyList<CommandEntry> GetAllCommands()
    {
        return LoadCatalog().Commands;
    }

    public CatalogState RebuildFromShims(string shimsDir)
    {
        var state = new CatalogState();
        if (!Directory.Exists(shimsDir)) return state;

        var metaFiles = Directory.GetFiles(shimsDir, "*.exe.meta");
        foreach (var metaFile in metaFiles)
        {
            try
            {
                var content = File.ReadAllText(metaFile);
                var entry = JsonSerializer.Deserialize<CommandEntry>(content);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Name))
                {
                    state.Commands.Add(entry);
                }
            }
            catch
            {
                // Skip unparseable meta files
            }
        }

        SaveCatalog(state);
        return state;
    }
}
