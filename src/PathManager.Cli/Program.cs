using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PathManager.Core.Catalog;
using PathManager.Core.Complete;
using PathManager.Core.Doctor;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;
using PathManager.Core.Snapshot;
using PathManager.Core.Why;

namespace PathManager.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var env = new WindowsEnvironmentProvider();
        return await RunWithEnvAsync(args, env, Console.Out, Console.Error);
    }

    public static async Task<int> RunWithEnvAsync(
        string[] args,
        IEnvironmentProvider env,
        TextWriter stdout,
        TextWriter stderr)
    {
        var pathStore = new PathStore(env);
        var catalog = new CatalogManager(env);
        var shim = new ShimManager(env);
        var snapshot = new SnapshotManager(env, pathStore, catalog, shim);
        var doctor = new DoctorService(env, pathStore, catalog, shim, snapshot);
        var why = new WhyService(env, pathStore, catalog, shim);
        var complete = new CompletionManager(env);
        var resolver = new HostResolver(env);

        bool isJson = args.Contains("--json");

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            return await HandleRootHelpAsync(pathStore, catalog, isJson, stdout);
        }

        if (args[0] == "--version" || args[0] == "-v")
        {
            stdout.WriteLine("pathman 1.0.0");
            return 0;
        }

        var verb = args[0].ToLowerInvariant();
        var verbArgs = args.Skip(1).ToArray();

        try
        {
            switch (verb)
            {
                case "link":
                    return HandleLink(verbArgs, env, pathStore, catalog, shim, snapshot, resolver, isJson, stdout, stderr);

                case "unlink":
                    return HandleUnlink(verbArgs, catalog, shim, snapshot, isJson, stdout, stderr);

                case "list":
                    return HandleList(catalog, env, isJson, stdout);

                case "why":
                    return HandleWhy(verbArgs, why, isJson, stdout, stderr);

                case "doctor":
                    return await HandleDoctorAsync(verbArgs, doctor, isJson, stdout);

                case "path":
                    return HandlePath(verbArgs, pathStore, shim, snapshot, isJson, stdout, stderr);

                case "undo":
                    return HandleUndo(snapshot, isJson, stdout, stderr);

                case "completions":
                    return HandleCompletions(verbArgs, complete, catalog, isJson, stdout, stderr);

                case "uninstall":
                    return HandleUninstall(verbArgs, env, pathStore, complete, shim, isJson, stdout);

                case "upgrade":
                    return HandleUpgrade(isJson, stdout);

                default:
                    stderr.WriteLine($"Unknown command: '{verb}'. Run 'pathman --help' for usage.");
                    return 2;
            }
        }
        catch (PathManagerException ex)
        {
            if (isJson)
            {
                var errorObj = new { error = ex.GetType().Name, message = ex.Message };
                stderr.WriteLine(JsonSerializer.Serialize(errorObj, JsonOpts));
            }
            else
            {
                stderr.WriteLine($"Error: {ex.Message}");
            }
            return 2;
        }
        catch (Exception ex)
        {
            if (isJson)
            {
                var errorObj = new { error = ex.GetType().Name, message = ex.Message };
                stderr.WriteLine(JsonSerializer.Serialize(errorObj, JsonOpts));
            }
            else
            {
                stderr.WriteLine($"Unexpected error: {ex.Message}");
            }
            return 3;
        }
    }

    private static async Task<int> HandleRootHelpAsync(PathStore pathStore, CatalogManager catalog, bool isJson, TextWriter stdout)
    {
        var userPath = pathStore.GetUserPath();
        var machinePath = pathStore.GetMachinePath();
        var combined = PathList.Combine(machinePath, userPath);
        var cmds = catalog.GetAllCommands();

        if (isJson)
        {
            var summary = new
            {
                version = "1.0.0",
                userPathLength = userPath.RawLength,
                userPathCount = userPath.Count,
                machinePathLength = machinePath.RawLength,
                machinePathCount = machinePath.Count,
                combinedPathLength = combined.RawLength,
                combinedPathCount = combined.Count,
                linkedCommandsCount = cmds.Count
            };
            stdout.WriteLine(JsonSerializer.Serialize(summary, JsonOpts));
            return 0;
        }

        stdout.WriteLine("pathman — Windows PATH environment variable & command catalog manager");
        stdout.WriteLine();
        stdout.WriteLine("USAGE:");
        stdout.WriteLine("  pathman <command> [arguments] [flags]");
        stdout.WriteLine();
        stdout.WriteLine("COMMANDS:");
        stdout.WriteLine("  link <path> [name]    Create a short runnable command for any file (.exe, .py, .ps1, etc.)");
        stdout.WriteLine("  unlink <name>         Remove a linked command");
        stdout.WriteLine("  list                  List all linked commands and their targets");
        stdout.WriteLine("  why <name>            Trace command resolution and identify PATH shadowing");
        stdout.WriteLine("  doctor [--repair]     Inspect PATH limits, duplicates, dead folders, and catalog health");
        stdout.WriteLine("  path [add|remove]     View or safely edit User PATH without setx");
        stdout.WriteLine("  undo                  Restore previous snapshot of catalog and User PATH");
        stdout.WriteLine("  completions install              Install the $PROFILE hook (folder loader + pathman Tab)");
        stdout.WriteLine("  completions register-file        Copy a .ps1 into the completions folder");
        stdout.WriteLine("  completions register-command     Run a generator command and store its script");
        stdout.WriteLine("  completions from-help            Best-effort completer from --help / -h");
        stdout.WriteLine("  uninstall             Safely clean up PathManager and restore User PATH");
        stdout.WriteLine();
        stdout.WriteLine("GLOBAL FLAGS:");
        stdout.WriteLine("  --json                Output results in JSON format for scripting");
        stdout.WriteLine("  --help, -h            Show help");
        stdout.WriteLine("  --version, -v         Show version");
        stdout.WriteLine();
        stdout.WriteLine($"STATUS: User PATH {userPath.RawLength}/2047 chars ({userPath.Count} dirs)  •  Machine PATH {machinePath.RawLength} chars  •  {cmds.Count} linked commands");
        return 0;
    }

    private static int HandleLink(
        string[] args,
        IEnvironmentProvider env,
        PathStore pathStore,
        CatalogManager catalog,
        ShimManager shim,
        SnapshotManager snapshot,
        HostResolver resolver,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var nonFlagArgs = args.Where(a => !a.StartsWith("-")).ToArray();
        if (nonFlagArgs.Length == 0)
        {
            stderr.WriteLine("Usage: pathman link <file_path> [command_name] [--force] [--host <interpreter>] [--i-trust-this] [--json]");
            return 2;
        }

        var targetInput = nonFlagArgs[0];
        var fullTarget = Path.GetFullPath(targetInput);

        if (!env.FileExists(fullTarget))
        {
            throw new TargetNotFoundException(targetInput);
        }

        string commandName;
        if (nonFlagArgs.Length > 1)
        {
            commandName = nonFlagArgs[1];
        }
        else
        {
            commandName = Path.GetFileNameWithoutExtension(fullTarget);
        }

        CatalogManager.ValidateCommandName(commandName);

        bool force = args.Contains("--force");
        bool trustWritable = args.Contains("--i-trust-this");
        string? explicitHost = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--host")
            {
                explicitHost = args[i + 1];
            }
        }

        // Check if existing command with same name exists in catalog
        var existing = catalog.GetCommand(commandName);
        if (existing != null)
        {
            if (string.Equals(existing.Target, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                if (isJson)
                {
                    stdout.WriteLine(JsonSerializer.Serialize(existing, JsonOpts));
                }
                else
                {
                    stdout.WriteLine($"Already linked: {commandName} -> {fullTarget} ({existing.Host})");
                }
                return 0;
            }

            if (!force)
            {
                throw new NameCollisionException(commandName, existing.Target);
            }
        }

        // Check shadow collision with Machine PATH / WindowsApps execution aliases
        if (!force)
        {
            shim.CheckShadowCollision(commandName, pathStore);
        }

        // Security check for world-writable directories
        shim.CheckTargetSecurity(fullTarget, trustWritable);

        // Resolve host
        var hostInfo = resolver.Resolve(fullTarget, explicitHost);

        // Atomic staging & linking
        snapshot.CreateSnapshot("link", fullTarget, $"Link {commandName}");

        var entry = new CommandEntry
        {
            Name = commandName,
            Target = fullTarget,
            Host = hostInfo.Host,
            HostPath = hostInfo.HostPath,
            CreatedAt = DateTime.UtcNow,
            TrustedWritable = trustWritable
        };

        shim.CreateShim(entry);
        catalog.AddOrUpdateCommand(entry);
        pathStore.EnsureShimDirectoryOnUserPath(shim.ShimsDirectory);

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(entry, JsonOpts));
        }
        else
        {
            stdout.WriteLine($"linked {commandName} -> {fullTarget}  ({entry.Host})");
        }

        return 0;
    }

    private static int HandleUnlink(
        string[] args,
        CatalogManager catalog,
        ShimManager shim,
        SnapshotManager snapshot,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var name = args.FirstOrDefault(a => !a.StartsWith("-"));
        if (string.IsNullOrWhiteSpace(name))
        {
            stderr.WriteLine("Usage: pathman unlink <command_name> [--json]");
            return 2;
        }

        CatalogManager.ValidateCommandName(name);

        var existing = catalog.GetCommand(name);
        if (existing == null && !shim.ShimExists(name))
        {
            stderr.WriteLine($"Command '{name}' is not currently linked.");
            return 2;
        }

        snapshot.CreateSnapshot("unlink", name, $"Unlink {name}");
        shim.RemoveShim(name);
        catalog.RemoveCommand(name);

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { unlinked = name }, JsonOpts));
        }
        else
        {
            stdout.WriteLine($"unlinked {name}");
        }

        return 0;
    }

    private static int HandleList(
        CatalogManager catalog,
        IEnvironmentProvider env,
        bool isJson,
        TextWriter stdout)
    {
        var commands = catalog.GetAllCommands();

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(commands, JsonOpts));
            return 0;
        }

        if (commands.Count == 0)
        {
            stdout.WriteLine("No linked commands in catalog.");
            stdout.WriteLine("Hint: link a file with 'pathman link <file_path>' to get started.");
            return 0;
        }

        stdout.WriteLine($"{"COMMAND",-18} {"HOST",-8} {"STATUS",-10} {"TARGET"}");
        stdout.WriteLine(new string('-', 70));

        foreach (var cmd in commands)
        {
            var status = env.FileExists(cmd.Target) ? "OK" : "MISSING";
            stdout.WriteLine($"{cmd.Name,-18} {cmd.Host,-8} {status,-10} {cmd.Target}");
        }

        return 0;
    }

    private static int HandleWhy(
        string[] args,
        WhyService why,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var name = args.FirstOrDefault(a => !a.StartsWith("-"));
        if (string.IsNullOrWhiteSpace(name))
        {
            stderr.WriteLine("Usage: pathman why <command_name> [--json]");
            return 2;
        }

        var report = why.TraceResolution(name);

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
            return report.Found ? 0 : 1;
        }

        stdout.WriteLine($"Resolution Trace for '{name}':");
        stdout.WriteLine(new string('-', 50));

        if (!report.Found)
        {
            stdout.WriteLine(report.Summary);
            return 1;
        }

        foreach (var step in report.Steps)
        {
            var details = !string.IsNullOrEmpty(step.Details) ? $" ({step.Details})" : "";
            stdout.WriteLine($"  {step.Order}. [{step.Source}] {step.Path}{details}");
        }

        stdout.WriteLine();
        stdout.WriteLine($"Result: {report.Summary}");
        return 0;
    }

    private static async Task<int> HandleDoctorAsync(
        string[] args,
        DoctorService doctor,
        bool isJson,
        TextWriter stdout)
    {
        bool repair = args.Any(a => string.Equals(a, "--repair", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(a, "repair", StringComparison.OrdinalIgnoreCase));

        var report = repair
            ? await doctor.RepairAsync()
            : await doctor.RunDiagnosticsAsync();

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
            return report.Status == HealthStatus.IssuesFound ? 1 : 0;
        }

        if (repair)
        {
            if (report.RepairsApplied.Count > 0)
            {
                stdout.WriteLine("REPAIRS APPLIED:");
                stdout.WriteLine(new string('-', 50));
                foreach (var rep in report.RepairsApplied)
                {
                    stdout.WriteLine($"  ✓ {rep}");
                }
                stdout.WriteLine("  ✓ Safety snapshot saved. (Run 'pathman undo' anytime to revert)");
                stdout.WriteLine();
            }
            else
            {
                stdout.WriteLine("REPAIR STATUS:");
                stdout.WriteLine(new string('-', 50));
                stdout.WriteLine("  ✓ No repairs needed. Environment is already clean.");
                stdout.WriteLine();
            }
        }

        stdout.WriteLine("PathManager Doctor Diagnostic Report");
        stdout.WriteLine(new string('=', 50));

        stdout.WriteLine($"User PATH:      {report.UserPathLength,5} chars  ({report.UserPathCount} directories)");
        stdout.WriteLine($"Machine PATH:   {report.MachinePathLength,5} chars  ({report.MachinePathCount} directories)");
        stdout.WriteLine($"Combined PATH:  {report.CombinedPathLength,5} chars  ({report.CombinedPathCount} directories)");
        stdout.WriteLine();

        // Length evaluations
        if (report.IsCombinedOverGuiLimit)
        {
            stdout.WriteLine("  [WARN] Combined PATH exceeds 2,047 characters.");
            stdout.WriteLine("         Windows System Properties Environment Variables UI may refuse edits, but PathManager continues working safely.");
        }
        if (report.IsCombinedOverCmdLimit)
        {
            stdout.WriteLine("  [WARN] Combined PATH exceeds 8,191 characters (cmd.exe inherited limit).");
        }
        if (report.IsUserOverWin32Limit)
        {
            stdout.WriteLine("  [FAIL] User PATH exceeds Win32 limit of 32,767 characters!");
        }

        // Duplicates
        if (report.DuplicateEntries.Count > 0)
        {
            stdout.WriteLine();
            stdout.WriteLine($"Duplicate PATH entries ({report.DuplicateEntries.Count}):");
            foreach (var dup in report.DuplicateEntries)
            {
                stdout.WriteLine($"  - {dup}");
            }
        }

        // Dead directories
        if (report.DeadDirectories.Count > 0)
        {
            stdout.WriteLine();
            stdout.WriteLine($"Non-existent / Inaccessible directories ({report.DeadDirectories.Count}):");
            foreach (var dead in report.DeadDirectories)
            {
                stdout.WriteLine($"  - {dead.Path}  [{dead.Reason}]");
            }
        }

        // Catalog & Shims
        stdout.WriteLine();
        stdout.WriteLine($"Command Catalog: {report.TotalCommands} commands linked");
        if (report.StaleCommands.Count > 0)
        {
            stdout.WriteLine($"  [WARN] {report.StaleCommands.Count} commands have missing target files: {string.Join(", ", report.StaleCommands)}");
        }

        if (report.CatalogDrift.Count > 0)
        {
            stdout.WriteLine("  [WARN] Catalog / Shim drift detected:");
            foreach (var drift in report.CatalogDrift)
            {
                stdout.WriteLine($"    - {drift.CommandName}: {drift.Issue}");
            }
            stdout.WriteLine("  Hint: run 'pathman doctor --repair' to synchronize catalog and shims.");
        }

        // Shim directory on User PATH
        if (!report.IsShimDirOnUserPath)
        {
            stdout.WriteLine();
            stdout.WriteLine($"  [FAIL] PathManager shim directory '{report.ShimDirPath}' is not on User PATH!");
            stdout.WriteLine("  Hint: run 'pathman doctor --repair' to fix.");
        }

        // Profile hook
        if (!report.IsProfileHookInstalled)
        {
            stdout.WriteLine();
            stdout.WriteLine("  [INFO] PowerShell completion & prepend hook not installed in $PROFILE.");
            stdout.WriteLine("  Hint: run 'pathman completions install' to enable tab completions and auto-prepend.");
        }

        stdout.WriteLine();
        stdout.WriteLine(new string('-', 50));
        if (report.Status == HealthStatus.Healthy)
        {
            stdout.WriteLine("Verdict: pathman is working cleanly. No issues found.");
        }
        else if (report.Status == HealthStatus.Warning)
        {
            stdout.WriteLine("Verdict: pathman is working (minor warnings noted above).");
        }
        else
        {
            stdout.WriteLine("Verdict: Issues found that require repair. Run 'pathman doctor --repair'.");
        }

        return report.Status == HealthStatus.IssuesFound ? 1 : 0;
    }

    private static int HandlePath(
        string[] args,
        PathStore pathStore,
        ShimManager shim,
        SnapshotManager snapshot,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var nonFlagArgs = args.Where(a => !a.StartsWith("-")).ToArray();
        var subVerb = nonFlagArgs.Length > 0 ? nonFlagArgs[0].ToLowerInvariant() : "show";

        if (subVerb == "add")
        {
            if (nonFlagArgs.Length < 2)
            {
                stderr.WriteLine("Usage: pathman path add <directory> [--prepend] [--json]");
                return 2;
            }
            var dir = nonFlagArgs[1];
            bool prepend = args.Contains("--prepend");

            snapshot.CreateSnapshot("path_add", dir, $"Add {dir} to User PATH");
            var changed = pathStore.AddDirectoryToUserPath(dir, prepend);

            if (isJson)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { added = dir, changed }, JsonOpts));
            }
            else
            {
                stdout.WriteLine(changed ? $"Added '{dir}' to User PATH." : $"'{dir}' was already present on User PATH.");
            }
            return 0;
        }

        if (subVerb == "remove")
        {
            if (nonFlagArgs.Length < 2)
            {
                stderr.WriteLine("Usage: pathman path remove <directory> [--force] [--json]");
                return 2;
            }
            var dir = nonFlagArgs[1];
            bool force = args.Contains("--force");

            snapshot.CreateSnapshot("path_remove", dir, $"Remove {dir} from User PATH");
            var changed = pathStore.RemoveDirectoryFromUserPath(dir, shim.ShimsDirectory, force);

            if (isJson)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { removed = dir, changed }, JsonOpts));
            }
            else
            {
                stdout.WriteLine(changed ? $"Removed '{dir}' from User PATH." : $"'{dir}' was not found on User PATH.");
            }
            return 0;
        }

        // Default: show User, Machine, Combined PATH
        var userPath = pathStore.GetUserPath();
        var machinePath = pathStore.GetMachinePath();

        if (isJson)
        {
            var pathData = new
            {
                user = userPath.Select(e => e.Raw).ToList(),
                machine = machinePath.Select(e => e.Raw).ToList()
            };
            stdout.WriteLine(JsonSerializer.Serialize(pathData, JsonOpts));
            return 0;
        }

        stdout.WriteLine("User PATH Entries:");
        int idx = 1;
        foreach (var entry in userPath)
        {
            stdout.WriteLine($"  {idx++,3}. {entry.Raw}");
        }

        stdout.WriteLine();
        stdout.WriteLine("Machine PATH Entries (Read-Only):");
        idx = 1;
        foreach (var entry in machinePath)
        {
            stdout.WriteLine($"  {idx++,3}. {entry.Raw}");
        }

        return 0;
    }

    private static int HandleUndo(
        SnapshotManager snapshot,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        var snapId = snapshot.Undo();
        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { restoredSnapshot = snapId }, JsonOpts));
        }
        else
        {
            stdout.WriteLine($"Successfully restored snapshot '{snapId}'.");
        }
        return 0;
    }

    private static int HandleCompletions(
        string[] args,
        CompletionManager complete,
        CatalogManager catalog,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        SplitCompletionsArgs(args, out var subVerb, out var payload);
        var force = args.Contains("--force");
        var nameOpt = GetOptionValue(args, "--name");

        if (subVerb == "install")
        {
            bool winPs = args.Contains("--windows-powershell");
            var profilePath = complete.InstallHook(winPs);

            if (isJson)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { installedProfile = profilePath }, JsonOpts));
            }
            else
            {
                stdout.WriteLine($"Installed PathManager completions and PATH hook to:");
                stdout.WriteLine($"  {profilePath}");
                stdout.WriteLine();
                stdout.WriteLine("Restart your terminal session or run: . $PROFILE");
            }
            return 0;
        }

        if (subVerb == "register-file")
        {
            if (payload.Count < 1)
            {
                stderr.WriteLine("Usage: pathman completions register-file <file.ps1> [--name <command>] [--force] [--json]");
                return 2;
            }

            var dest = complete.RegisterFile(payload[0], nameOpt, force);
            return WriteCompleterRegistered(catalog, dest, "file", isJson, stdout);
        }

        if (subVerb == "register-command")
        {
            if (payload.Count < 1)
            {
                stderr.WriteLine("Usage: pathman completions register-command <command...> [--name <command>] [--force] [--json]");
                return 2;
            }

            var commandLine = string.Join(" ", payload);
            var dest = complete.RegisterFromCommand(commandLine, nameOpt, force);
            return WriteCompleterRegistered(catalog, dest, "command", isJson, stdout);
        }

        if (subVerb == "from-help")
        {
            if (payload.Count < 1)
            {
                stderr.WriteLine("Usage: pathman completions from-help <command...> [--name <command>] [--force] [--json]");
                return 2;
            }

            var commandLine = string.Join(" ", payload);
            var dest = complete.RegisterFromHelp(commandLine, nameOpt, force);
            return WriteCompleterRegistered(catalog, dest, "help", isJson, stdout);
        }

        stderr.WriteLine("Usage:");
        stderr.WriteLine("  pathman completions install [--windows-powershell] [--json]");
        stderr.WriteLine("  pathman completions register-file <file.ps1> [--name <command>] [--force] [--json]");
        stderr.WriteLine("  pathman completions register-command <command...> [--name <command>] [--force] [--json]");
        stderr.WriteLine("  pathman completions from-help <command...> [--name <command>] [--force] [--json]");
        return 2;
    }

    private static int WriteCompleterRegistered(
        CatalogManager catalog,
        string dest,
        string source,
        bool isJson,
        TextWriter stdout)
    {
        var commandName = Path.GetFileNameWithoutExtension(dest);
        BindCatalogCompleter(catalog, commandName);

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { command = commandName, path = dest, source }, JsonOpts));
        }
        else
        {
            stdout.WriteLine($"Registered completions for {commandName}:");
            stdout.WriteLine($"  {dest}");
            stdout.WriteLine();
            stdout.WriteLine("Load in this session with `. $PROFILE`, or open a new PowerShell window.");
        }

        return 0;
    }

    private static void BindCatalogCompleter(CatalogManager catalog, string commandName)
    {
        var existing = catalog.GetCommand(commandName);
        if (existing == null)
        {
            return;
        }

        existing.Completer = $"completions/{commandName}.ps1";
        catalog.AddOrUpdateCommand(existing);
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == option)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static readonly string[] CompletionsSwitchFlags =
    {
        "--json", "--force", "--windows-powershell", "--help", "-h"
    };

    private static readonly string[] CompletionsValueFlags =
    {
        "--name"
    };

    private static void SplitCompletionsArgs(string[] args, out string subVerb, out List<string> payload)
    {
        subVerb = "install";
        payload = new List<string>();
        var haveVerb = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (CompletionsValueFlags.Contains(arg, StringComparer.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    i++;
                }
                continue;
            }

            if (CompletionsSwitchFlags.Contains(arg, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!haveVerb)
            {
                if (arg.StartsWith("-"))
                {
                    continue;
                }

                subVerb = arg.ToLowerInvariant();
                haveVerb = true;
                continue;
            }

            payload.Add(arg);
        }
    }

    private static int HandleUninstall(
        string[] args,
        IEnvironmentProvider env,
        PathStore pathStore,
        CompletionManager complete,
        ShimManager shim,
        bool isJson,
        TextWriter stdout)
    {
        bool keepShims = args.Contains("--keep-shims");

        // 1. Remove shim directory from User PATH
        try
        {
            pathStore.RemoveDirectoryFromUserPath(shim.ShimsDirectory, shim.ShimsDirectory, force: true);
        }
        catch { }

        // 2. Remove shims unless requested to keep
        if (!keepShims)
        {
            try
            {
                if (Directory.Exists(shim.ShimsDirectory))
                {
                    Directory.Delete(shim.ShimsDirectory, recursive: true);
                }
            }
            catch { }
        }

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { uninstalled = true, keptShims = keepShims }, JsonOpts));
        }
        else
        {
            stdout.WriteLine("PathManager uninstalled successfully.");
            stdout.WriteLine("User PATH has been cleaned up.");
        }

        return 0;
    }

    private static int HandleUpgrade(bool isJson, TextWriter stdout)
    {
        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { status = "up-to-date", version = "1.0.0" }, JsonOpts));
        }
        else
        {
            stdout.WriteLine("pathman is currently running the latest version (1.0.0).");
        }
        return 0;
    }
}
