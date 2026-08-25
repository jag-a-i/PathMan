using System;
using System.IO;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;

namespace PathManager.Core.Shim;

public class HostInfo
{
    public string Host { get; }
    public string? HostPath { get; }

    public HostInfo(string host, string? hostPath)
    {
        Host = host;
        HostPath = hostPath;
    }
}

public class HostResolver
{
    private readonly IEnvironmentProvider _env;

    public HostResolver(IEnvironmentProvider env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public HostInfo Resolve(string targetPath, string? explicitHost = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitHost))
        {
            var resolvedHost = _env.FindExecutableOnPath(explicitHost) ?? explicitHost;
            if (!_env.FileExists(resolvedHost))
            {
                throw new HostNotFoundException(Path.GetFileName(targetPath), explicitHost);
            }
            return new HostInfo("custom", Path.GetFullPath(resolvedHost));
        }

        var ext = Path.GetExtension(targetPath).ToLowerInvariant();

        switch (ext)
        {
            case ".exe":
                return new HostInfo("exe", null);

            case ".cmd":
            case ".bat":
                var comSpec = System.Environment.GetEnvironmentVariable("COMSPEC");
                if (string.IsNullOrWhiteSpace(comSpec) || !_env.FileExists(comSpec))
                {
                    comSpec = _env.FindExecutableOnPath("cmd.exe") ?? "cmd.exe";
                }
                return new HostInfo(ext.TrimStart('.'), comSpec);

            case ".ps1":
                var pwsh = FindPowerShell();
                return new HostInfo("ps1", pwsh);

            case ".py":
            case ".pyw":
                var python = FindPython();
                return new HostInfo("py", python);

            default:
                throw new UnsupportedRunnableException(ext);
        }
    }

    public string FindPowerShell()
    {
        // 1. Check for pwsh (PowerShell Core 7+)
        var pwshPath = _env.FindExecutableOnPath("pwsh.exe");
        if (!string.IsNullOrEmpty(pwshPath) && _env.FileExists(pwshPath))
        {
            return pwshPath;
        }

        // 2. Check for Windows PowerShell 5.1
        var powershellPath = _env.FindExecutableOnPath("powershell.exe");
        if (!string.IsNullOrEmpty(powershellPath) && _env.FileExists(powershellPath))
        {
            return powershellPath;
        }

        var system32 = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (_env.FileExists(system32))
        {
            return system32;
        }

        return "powershell.exe";
    }

    public string FindPython()
    {
        // 1. Check for python.exe
        var pythonPath = _env.FindExecutableOnPath("python.exe");
        if (!string.IsNullOrEmpty(pythonPath) && _env.FileExists(pythonPath))
        {
            // Verify it's not a 0-byte WindowsApps Store stub
            try
            {
                var fileInfo = new FileInfo(pythonPath);
                if (fileInfo.Length > 0 && !pythonPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    return pythonPath;
                }
            }
            catch { }
        }

        // 2. Check for py launcher (py.exe)
        var pyPath = _env.FindExecutableOnPath("py.exe");
        if (!string.IsNullOrEmpty(pyPath) && _env.FileExists(pyPath))
        {
            return pyPath;
        }

        // 3. Fallback to python.exe
        return pythonPath ?? "python.exe";
    }
}
