using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using PathManager.Core.Catalog;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;

namespace PathManager.Core.Shim;

public class ShimManager
{
    private readonly IEnvironmentProvider _env;
    private readonly string _shimsDir;
    private readonly string _templateShimPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string ShimsDirectory => _shimsDir;

    public ShimManager(IEnvironmentProvider env, string? templateShimPath = null)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _shimsDir = Path.Combine(_env.GetPmHome(), "shims");
        Directory.CreateDirectory(_shimsDir);

        if (!string.IsNullOrEmpty(templateShimPath))
        {
            _templateShimPath = templateShimPath;
        }
        else
        {
            // Default template shim location: next to executing pathman assembly, or inside shims dir
            var appDir = AppContext.BaseDirectory;
            var candidate1 = Path.Combine(appDir, "shim.exe");
            var candidate2 = Path.Combine(_shimsDir, "shim.exe");
            _templateShimPath = File.Exists(candidate1) ? candidate1 : candidate2;
        }
    }

    public void CheckShadowCollision(string commandName, PathStore pathStore)
    {
        CatalogManager.ValidateCommandName(commandName);

        // 1. Check Machine PATH
        var machinePath = pathStore.GetMachinePath();
        foreach (var dir in machinePath)
        {
            try
            {
                var expanded = dir.Expanded;
                if (!_env.DirectoryExists(expanded)) continue;

                var direct = Path.Combine(expanded, commandName + ".exe");
                if (_env.FileExists(direct))
                {
                    throw new ShadowCollisionException(commandName, direct);
                }

                var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                foreach (var ext in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var full = Path.Combine(expanded, commandName + ext);
                    if (_env.FileExists(full))
                    {
                        throw new ShadowCollisionException(commandName, full);
                    }
                }
            }
            catch (ShadowCollisionException) { throw; }
            catch { }
        }

        // 2. Check WindowsApps App Execution Aliases (Store stubs like python.exe, winget.exe)
        var localAppData = _env.GetLocalAppData();
        var windowsAppsDir = Path.Combine(localAppData, "Microsoft", "WindowsApps");
        if (_env.DirectoryExists(windowsAppsDir))
        {
            var aliasExe = Path.Combine(windowsAppsDir, commandName + ".exe");
            if (_env.FileExists(aliasExe))
            {
                throw new ShadowCollisionException(commandName, aliasExe);
            }
        }
    }

    public void CheckTargetSecurity(string targetPath, bool trustWritable)
    {
        if (trustWritable) return;

        var fullPath = Path.GetFullPath(targetPath);
        var userProfile = _env.GetUserProfile();

        // Files inside the user's profile are considered trusted by default
        if (fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Check common world-writable locations outside user profile (C:\Temp, C:\Users\Public, etc.)
        var temp = Path.GetTempPath();
        var publicProfile = System.Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

        if (fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(publicProfile, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(@"C:\Temp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InsecureTargetException(fullPath);
        }
    }

    public void CreateShim(CommandEntry entry)
    {
        CatalogManager.ValidateCommandName(entry.Name);
        Directory.CreateDirectory(_shimsDir);

        var shimExePath = Path.Combine(_shimsDir, entry.Name + ".exe");
        var shimMetaPath = Path.Combine(_shimsDir, entry.Name + ".exe.meta");

        try
        {
            // Write metadata sidecar
            var metaJson = JsonSerializer.Serialize(entry, _jsonOptions);
            File.WriteAllText(shimMetaPath, metaJson);

            // Copy template shim.exe
            if (File.Exists(_templateShimPath))
            {
                File.Copy(_templateShimPath, shimExePath, overwrite: true);
            }
            else
            {
                // In case template is not yet deployed, write a marker or ensure directory exists
                // The shim runner is also built as part of the project
            }
        }
        catch (Exception ex)
        {
            throw new ShimWriteFailedException(ex.Message, ex);
        }
    }

    public bool RemoveShim(string commandName)
    {
        CatalogManager.ValidateCommandName(commandName);

        var shimExePath = Path.Combine(_shimsDir, commandName + ".exe");
        var shimMetaPath = Path.Combine(_shimsDir, commandName + ".exe.meta");

        var deleted = false;
        if (File.Exists(shimExePath))
        {
            try { File.Delete(shimExePath); deleted = true; } catch { }
        }
        if (File.Exists(shimMetaPath))
        {
            try { File.Delete(shimMetaPath); deleted = true; } catch { }
        }
        return deleted;
    }

    public bool ShimExists(string commandName)
    {
        var shimExePath = Path.Combine(_shimsDir, commandName + ".exe");
        var shimMetaPath = Path.Combine(_shimsDir, commandName + ".exe.meta");
        return File.Exists(shimExePath) || File.Exists(shimMetaPath);
    }
}
