using System;
using System.IO;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;
using Xunit;

namespace PathManager.Tests;

public class ShimTests : IDisposable
{
    private readonly MockEnvironmentProvider _mockEnv;
    private readonly PathStore _pathStore;
    private readonly ShimManager _shim;
    private readonly HostResolver _resolver;

    public ShimTests()
    {
        _mockEnv = new MockEnvironmentProvider();
        _pathStore = new PathStore(_mockEnv);
        _shim = new ShimManager(_mockEnv);
        _resolver = new HostResolver(_mockEnv);
    }

    public void Dispose()
    {
        _mockEnv.Dispose();
    }

    [Theory]
    [InlineData("test.exe", "exe")]
    [InlineData("script.cmd", "cmd")]
    [InlineData("batch.bat", "bat")]
    [InlineData("task.ps1", "ps1")]
    [InlineData("app.py", "py")]
    public void HostResolver_ResolvesSupportedExtensions(string file, string expectedHost)
    {
        var hostInfo = _resolver.Resolve(file);
        Assert.Equal(expectedHost, hostInfo.Host);
    }

    [Fact]
    public void HostResolver_ThrowsOnUnsupportedExtension()
    {
        Assert.Throws<UnsupportedRunnableException>(() => _resolver.Resolve("doc.pdf"));
    }

    [Fact]
    public void ShimManager_DetectsShadowCollisionOnMachinePath()
    {
        _mockEnv.MachinePath = @"C:\Windows\System32";
        _mockEnv.ExistingFiles.Add(@"C:\Windows\System32\ping.exe");

        Assert.Throws<ShadowCollisionException>(() => _shim.CheckShadowCollision("ping", _pathStore));
    }

    [Fact]
    public void ShimManager_CreateAndRemoveShim_WritesMetadata()
    {
        var entry = new CommandEntry
        {
            Name = "demo",
            Target = @"C:\Users\test\demo.py",
            Host = "py"
        };

        _shim.CreateShim(entry);

        var metaPath = Path.Combine(_shim.ShimsDirectory, "demo.exe.meta");
        Assert.True(File.Exists(metaPath));

        _shim.RemoveShim("demo");
        Assert.False(File.Exists(metaPath));
    }
}
