using System;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using Xunit;

namespace PathManager.Tests;

public class PathStoreTests : IDisposable
{
    private readonly MockEnvironmentProvider _mockEnv;
    private readonly PathStore _pathStore;

    public PathStoreTests()
    {
        _mockEnv = new MockEnvironmentProvider();
        _pathStore = new PathStore(_mockEnv);
    }

    public void Dispose()
    {
        _mockEnv.Dispose();
    }

    [Fact]
    public void PathStore_GetUserPath_ReturnsPathList()
    {
        _mockEnv.UserPath = @"C:\Bin;%USERPROFILE%\tools;C:\Scripts";
        var list = _pathStore.GetUserPath();

        Assert.Equal(3, list.Count);
        Assert.Equal(@"%USERPROFILE%\tools", list.Entries[1].Raw);
    }

    [Fact]
    public void PathStore_SetUserPath_TriggersBroadcast()
    {
        var list = new PathList(@"C:\NewBin;C:\Tools");
        _pathStore.SetUserPath(list);

        Assert.True(_mockEnv.BroadcastCalled);
        Assert.Equal(@"C:\NewBin;C:\Tools", _mockEnv.UserPath);
    }

    [Fact]
    public void PathStore_SetUserPath_ThrowsHardLimitIfExceeds32767()
    {
        var hugeString = string.Join(";", Enumerable.Repeat(@"C:\VeryLongPath\Directory_Entry_For_Testing_Path_Limits_12345", 700));
        var list = new PathList(hugeString);

        Assert.Throws<PathHardLimitException>(() => _pathStore.SetUserPath(list));
    }

    [Fact]
    public void PathStore_EnsureShimDirectoryOnUserPath_PrependsIfMissing()
    {
        _mockEnv.UserPath = @"C:\Bin;C:\Tools";
        var shimDir = @"C:\Users\test\AppData\Local\PathManager\shims";

        var changed = _pathStore.EnsureShimDirectoryOnUserPath(shimDir);

        Assert.True(changed);
        var updated = _pathStore.GetUserPath();
        Assert.Equal(shimDir, updated.Entries[0].Raw);

        // Calling again is a no-op
        var changedAgain = _pathStore.EnsureShimDirectoryOnUserPath(shimDir);
        Assert.False(changedAgain);
    }
}
