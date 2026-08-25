using System;
using System.Collections.Generic;
using System.IO;
using PathManager.Core.Catalog;
using PathManager.Core.Env;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;

namespace PathManager.Core.Why;

public class ResolutionStep
{
    public int Order { get; set; }
    public string Source { get; set; } = string.Empty; // "PathManager Shim", "Machine PATH", "User PATH", "WindowsApps Alias"
    public string Path { get; set; } = string.Empty;
    public bool IsActiveInPwshHook { get; set; }
    public bool IsActiveInRawWindows { get; set; }
    public string? Details { get; set; }
}

public class ResolutionReport
{
    public string CommandName { get; set; } = string.Empty;
    public bool Found => Steps.Count > 0;
    public List<ResolutionStep> Steps { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class WhyService
{
    private readonly IEnvironmentProvider _env;
    private readonly PathStore _pathStore;
    private readonly CatalogManager _catalog;
    private readonly ShimManager _shim;

    public WhyService(IEnvironmentProvider env, PathStore pathStore, CatalogManager catalog, ShimManager shim)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));
    }

    public ResolutionReport TraceResolution(string commandName)
    {
        var report = new ResolutionReport { CommandName = commandName };
        var order = 1;

        // 1. Check PathManager shims
        var catalogCmd = _catalog.GetCommand(commandName);
        var shimExe = Path.Combine(_shim.ShimsDirectory, commandName + ".exe");
        if (catalogCmd != null || File.Exists(shimExe))
        {
            report.Steps.Add(new ResolutionStep
            {
                Order = order++,
                Source = "PathManager Shim",
                Path = shimExe,
                IsActiveInPwshHook = true,
                IsActiveInRawWindows = _pathStore.GetUserPath().Contains(_shim.ShimsDirectory),
                Details = catalogCmd != null ? $"Linked to: {catalogCmd.Target} (host: {catalogCmd.Host})" : "Shim exists on disk"
            });
        }

        // 2. Check WindowsApps App Execution Aliases
        var localAppData = _env.GetLocalAppData();
        var appsDir = Path.Combine(localAppData, "Microsoft", "WindowsApps");
        if (_env.DirectoryExists(appsDir))
        {
            var aliasExe = Path.Combine(appsDir, commandName + ".exe");
            if (_env.FileExists(aliasExe))
            {
                report.Steps.Add(new ResolutionStep
                {
                    Order = order++,
                    Source = "WindowsApps Execution Alias",
                    Path = aliasExe,
                    IsActiveInPwshHook = false,
                    IsActiveInRawWindows = true,
                    Details = "Microsoft Store App Execution Alias"
                });
            }
        }

        // 3. Check Machine PATH
        var machinePath = _pathStore.GetMachinePath();
        CheckPathEntries(machinePath, commandName, "Machine PATH", ref order, report.Steps);

        // 4. Check User PATH
        var userPath = _pathStore.GetUserPath();
        CheckPathEntries(userPath, commandName, "User PATH", ref order, report.Steps);

        if (report.Steps.Count == 0)
        {
            report.Summary = $"Command '{commandName}' was not found on PATH, in PathManager shims, or in WindowsApps aliases.";
        }
        else
        {
            var first = report.Steps[0];
            report.Summary = $"'{commandName}' resolves to [{first.Source}] at '{first.Path}'.";
        }

        return report;
    }

    private void CheckPathEntries(PathList pathList, string commandName, string sourceName, ref int order, List<ResolutionStep> steps)
    {
        var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.PS1";
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in pathList)
        {
            try
            {
                var dir = entry.Expanded;
                if (!_env.DirectoryExists(dir)) continue;

                // Skip the PathManager shim directory if we encounter it
                if (string.Equals(dir.TrimEnd('\\', '/'), _shim.ShimsDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    continue;

                var direct = Path.Combine(dir, commandName);
                if (_env.FileExists(direct))
                {
                    steps.Add(new ResolutionStep
                    {
                        Order = order++,
                        Source = sourceName,
                        Path = direct,
                        IsActiveInPwshHook = true,
                        IsActiveInRawWindows = true
                    });
                    continue;
                }

                foreach (var ext in extensions)
                {
                    var full = Path.Combine(dir, commandName + ext);
                    if (_env.FileExists(full))
                    {
                        steps.Add(new ResolutionStep
                        {
                            Order = order++,
                            Source = sourceName,
                            Path = full,
                            IsActiveInPwshHook = true,
                            IsActiveInRawWindows = true
                        });
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
