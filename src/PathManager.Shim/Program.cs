using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using PathManager.Core.Models;

namespace PathManager.Shim;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                processPath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrEmpty(processPath))
            {
                Console.Error.WriteLine("shim: unable to determine own executable path.");
                return 127;
            }

            var appDir = Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
            var exeName = Path.GetFileNameWithoutExtension(processPath);

            var metaPath = Path.Combine(appDir, exeName + ".exe.meta");
            if (!File.Exists(metaPath))
            {
                metaPath = Path.Combine(appDir, Path.GetFileName(processPath) + ".meta");
            }

            if (!File.Exists(metaPath))
            {
                Console.Error.WriteLine($"shim: metadata sidecar not found for '{exeName}' at '{metaPath}'.");
                return 127;
            }

            var metaJson = File.ReadAllText(metaPath);
            var entry = JsonSerializer.Deserialize<CommandEntry>(metaJson);

            if (entry == null || string.IsNullOrWhiteSpace(entry.Target))
            {
                Console.Error.WriteLine($"shim: invalid metadata for '{exeName}'.");
                return 127;
            }

            if (!File.Exists(entry.Target))
            {
                Console.Error.WriteLine($"{exeName}: target file is missing or inaccessible: '{entry.Target}'.");
                Console.Error.WriteLine($"Hint: run 'pathman why {exeName}' or 'pathman doctor' to diagnose.");
                return 127;
            }

            string executable;
            var argumentsBuilder = new StringBuilder();

            var host = entry.Host?.ToLowerInvariant() ?? "exe";
            switch (host)
            {
                case "exe":
                    executable = entry.Target;
                    AppendArgs(argumentsBuilder, args);
                    break;

                case "cmd":
                case "bat":
                    executable = !string.IsNullOrEmpty(entry.HostPath) && File.Exists(entry.HostPath)
                        ? entry.HostPath
                        : (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe");
                    argumentsBuilder.Append($"/c \"{entry.Target}\"");
                    if (args.Length > 0)
                    {
                        argumentsBuilder.Append(' ');
                        AppendArgs(argumentsBuilder, args);
                    }
                    break;

                case "ps1":
                    executable = !string.IsNullOrEmpty(entry.HostPath) && File.Exists(entry.HostPath)
                        ? entry.HostPath
                        : "pwsh.exe";
                    argumentsBuilder.Append($"-NoProfile -ExecutionPolicy Bypass -File \"{entry.Target}\"");
                    if (args.Length > 0)
                    {
                        argumentsBuilder.Append(' ');
                        AppendArgs(argumentsBuilder, args);
                    }
                    break;

                case "py":
                    executable = !string.IsNullOrEmpty(entry.HostPath) && File.Exists(entry.HostPath)
                        ? entry.HostPath
                        : "python.exe";
                    argumentsBuilder.Append($"\"{entry.Target}\"");
                    if (args.Length > 0)
                    {
                        argumentsBuilder.Append(' ');
                        AppendArgs(argumentsBuilder, args);
                    }
                    break;

                default: // custom host
                    executable = !string.IsNullOrEmpty(entry.HostPath) && File.Exists(entry.HostPath)
                        ? entry.HostPath
                        : entry.Target;
                    argumentsBuilder.Append($"\"{entry.Target}\"");
                    if (args.Length > 0)
                    {
                        argumentsBuilder.Append(' ');
                        AppendArgs(argumentsBuilder, args);
                    }
                    break;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = argumentsBuilder.ToString(),
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false
            };

            Process? childProcess = null;
            var exitEvent = new ManualResetEvent(false);

            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                // Prevent premature termination of shim process so child process can exit gracefully
                e.Cancel = true;
            };

            Console.CancelKeyPress += cancelHandler;

            try
            {
                childProcess = Process.Start(startInfo);
                if (childProcess == null)
                {
                    Console.Error.WriteLine($"{exeName}: failed to start process '{executable}'.");
                    return 127;
                }

                childProcess.WaitForExit();
                return childProcess.ExitCode;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                childProcess?.Dispose();
                exitEvent.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"shim error: {ex.Message}");
            return 127;
        }
    }

    private static void AppendArgs(StringBuilder sb, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var arg = args[i];

            // Quote argument if it contains spaces or quotes
            if (arg.Contains(' ') || arg.Contains('\t') || arg.Contains('"') || arg.Length == 0)
            {
                sb.Append('"');
                sb.Append(arg.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
    }
}
