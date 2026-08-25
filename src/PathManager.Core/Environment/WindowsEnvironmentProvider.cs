using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PathManager.Core.Env;

public class WindowsEnvironmentProvider : IEnvironmentProvider
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    public string? GetUserPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: false);
            if (key == null) return null;
            return key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch
        {
            return null;
        }
    }

    public void SetUserPath(string rawPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Environment");

        // Always write as REG_EXPAND_SZ so %USERPROFILE% or %SystemRoot% remain expand-string format
        key.SetValue("Path", rawPath, RegistryValueKind.ExpandString);
    }

    public string? GetMachinePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", writable: false);
            if (key == null) return null;
            return key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch
        {
            return null;
        }
    }

    public string GetPmHome()
    {
        var env = System.Environment.GetEnvironmentVariable("PM_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(GetUserProfile(), "AppData", "Local");
        }

        return Path.Combine(localAppData, "PathManager");
    }

    public bool IsElevated()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void BroadcastSettingChange()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "Environment",
                    SMTO_ABORTIFHUNG,
                    1000,
                    out _);
            }
        }
        catch
        {
            // Ignore broadcast failure in non-interactive / headless contexts
        }
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public async Task<bool> DirectoryExistsWithTimeoutAsync(string path, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // If local path, check directly
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal))
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        // For UNC paths, probe in a background task with strict timeout
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var task = Task.Run(() =>
            {
                try { return Directory.Exists(path); } catch { return false; }
            }, cts.Token);

            var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token));
            if (completedTask == task)
            {
                return await task;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public string? FindExecutableOnPath(string name)
    {
        if (File.Exists(name)) return Path.GetFullPath(name);

        var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.PS1";
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);

        var pathVal = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        var directories = pathVal.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in directories)
        {
            try
            {
                var trimmedDir = dir.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmedDir) || !Directory.Exists(trimmedDir))
                    continue;

                // Check direct name
                var direct = Path.Combine(trimmedDir, name);
                if (File.Exists(direct)) return Path.GetFullPath(direct);

                // Check with extensions
                foreach (var ext in extensions)
                {
                    var withExt = Path.Combine(trimmedDir, name + ext);
                    if (File.Exists(withExt)) return Path.GetFullPath(withExt);
                }
            }
            catch
            {
                // Ignore inaccessible directories
            }
        }

        return null;
    }

    public string GetLocalAppData()
    {
        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
    }

    public string GetUserProfile()
    {
        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    }

    public string ExpandEnvironmentVariables(string value)
    {
        return System.Environment.ExpandEnvironmentVariables(value);
    }
}
