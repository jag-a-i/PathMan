using System;
using PathManager.Core.Env;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;

namespace PathManager.Core.Pathstore;

public class PathStore
{
    private readonly IEnvironmentProvider _env;

    public PathStore(IEnvironmentProvider env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public PathList GetUserPath()
    {
        var raw = _env.GetUserPath();
        return new PathList(raw);
    }

    public PathList GetMachinePath()
    {
        var raw = _env.GetMachinePath();
        return new PathList(raw);
    }

    public PathList GetCombinedPath()
    {
        var machine = GetMachinePath();
        var user = GetUserPath();
        return PathList.Combine(machine, user);
    }

    public void SetUserPath(PathList pathList)
    {
        var raw = pathList.ToJoinedString();
        if (raw.Length > PathList.Win32Limit)
        {
            throw new PathHardLimitException(raw.Length);
        }

        try
        {
            _env.SetUserPath(raw);
            _env.BroadcastSettingChange();
        }
        catch (Exception ex) when (ex is not PathManagerException)
        {
            throw new PathWriteFailedException(ex.Message, ex);
        }
    }

    public bool EnsureShimDirectoryOnUserPath(string shimDir)
    {
        var userPath = GetUserPath();
        if (!userPath.Contains(shimDir))
        {
            userPath.Prepend(shimDir);
            SetUserPath(userPath);
            return true;
        }
        return false;
    }

    public bool AddDirectoryToUserPath(string directory, bool prepend = false)
    {
        var userPath = GetUserPath();
        bool changed;
        if (prepend)
        {
            changed = userPath.Prepend(directory);
        }
        else
        {
            changed = userPath.Append(directory);
        }

        if (changed)
        {
            SetUserPath(userPath);
        }
        return changed;
    }

    public bool RemoveDirectoryFromUserPath(string directory, string shimDir, bool force = false)
    {
        var userPath = GetUserPath();
        if (!force && directory.Trim().Trim('"').Equals(shimDir.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove the PathManager shim directory from User PATH. Pass --force if you really want to remove it.");
        }

        var changed = userPath.Remove(directory);
        if (changed)
        {
            SetUserPath(userPath);
        }
        return changed;
    }
}
