using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PathManager.Core.Env;

namespace PathManager.Tests;

public class MockEnvironmentProvider : IEnvironmentProvider
{
    public string? UserPath { get; set; } = @"C:\Users\test\bin;C:\Tools";
    public string? MachinePath { get; set; } = @"C:\Windows\System32;C:\Windows;%SystemRoot%\System32\Wbem";
    public string MockPmHome { get; set; }
    public bool MockIsElevated { get; set; } = false;
    public bool BroadcastCalled { get; private set; } = false;

    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExistingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ExecutablesOnPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MockEnvironmentProvider(string? tempHome = null)
    {
        MockPmHome = tempHome ?? Path.Combine(Path.GetTempPath(), "PathManager_Test_" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(MockPmHome); } catch { }
        try { Directory.CreateDirectory(Path.Combine(MockPmHome, "shims")); } catch { }
        try { Directory.CreateDirectory(Path.Combine(MockPmHome, "completions")); } catch { }
        try { Directory.CreateDirectory(Path.Combine(MockPmHome, "snapshots")); } catch { }

        UserPath = $"{Path.Combine(MockPmHome, "shims")};C:\\Users\\test\\bin;C:\\Tools";

        ExistingDirectories.Add(MockPmHome);
        ExistingDirectories.Add(Path.Combine(MockPmHome, "shims"));
        ExistingDirectories.Add(Path.Combine(MockPmHome, "completions"));
        ExistingDirectories.Add(Path.Combine(MockPmHome, "snapshots"));
        ExistingDirectories.Add(@"C:\Windows\System32");
        ExistingDirectories.Add(@"C:\Windows");
    }

    public string? GetUserPath() => UserPath;

    public void SetUserPath(string rawPath)
    {
        UserPath = rawPath;
    }

    public string? GetMachinePath() => MachinePath;

    public string GetPmHome() => MockPmHome;

    public bool IsElevated() => MockIsElevated;

    public void BroadcastSettingChange()
    {
        BroadcastCalled = true;
    }

    public bool FileExists(string path)
    {
        return ExistingFiles.Contains(path) || File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return ExistingDirectories.Contains(path) || Directory.Exists(path);
    }

    public async Task<bool> DirectoryExistsWithTimeoutAsync(string path, int timeoutMs)
    {
        if (path.StartsWith(@"\\slow_unc", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(timeoutMs + 50);
            return false;
        }

        return DirectoryExists(path);
    }

    public string? FindExecutableOnPath(string name)
    {
        if (ExecutablesOnPath.TryGetValue(name, out var exe))
        {
            return exe;
        }

        if (FileExists(name)) return Path.GetFullPath(name);

        return null;
    }

    public string GetLocalAppData() => Path.Combine(GetUserProfile(), "AppData", "Local");

    public string GetUserProfile() => Path.GetDirectoryName(MockPmHome) ?? @"C:\Users\test";

    public string ExpandEnvironmentVariables(string value)
    {
        return value.Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
                    .Replace("%USERPROFILE%", @"C:\Users\test", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(MockPmHome))
            {
                Directory.Delete(MockPmHome, recursive: true);
            }
        }
        catch { }
    }
}
