using System;
using System.Threading.Tasks;

namespace PathManager.Core.Env;

public interface IEnvironmentProvider
{
    string? GetUserPath();
    void SetUserPath(string rawPath);
    string? GetMachinePath();
    string GetPmHome();
    bool IsElevated();
    void BroadcastSettingChange();
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Task<bool> DirectoryExistsWithTimeoutAsync(string path, int timeoutMs);
    string? FindExecutableOnPath(string name);
    string GetLocalAppData();
    string GetUserProfile();
    string ExpandEnvironmentVariables(string value);
}
