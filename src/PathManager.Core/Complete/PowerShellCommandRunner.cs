using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using PathManager.Core.Exceptions;

namespace PathManager.Core.Complete;

public sealed class PowerShellCommandRunner : ICommandRunner
{
    public CommandCapture Run(string commandLine, TimeSpan timeout)
    {
        var shell = ResolveShell();
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(commandLine);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new CompleterCommandFailedException(commandLine, -1, ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new CompleterCommandFailedException(commandLine, -1, $"Timed out after {timeout.TotalSeconds:0}s.");
        }

        stdoutTask.Wait(timeout);
        stderrTask.Wait(timeout);

        return new CommandCapture
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutTask.Result,
            StandardError = stderrTask.Result
        };
    }

    private static string ResolveShell()
    {
        foreach (var name in new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" })
        {
            var found = FindOnPath(name);
            if (found != null)
            {
                return found;
            }
        }

        throw new CompleterCommandFailedException(
            "pwsh",
            -1,
            "Neither pwsh nor Windows PowerShell was found on PATH.");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
