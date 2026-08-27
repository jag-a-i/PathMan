using System;
using System.IO;
using System.Threading.Tasks;
using PathManager.Core.Catalog;
using PathManager.Core.Doctor;
using PathManager.Core.Models;
using PathManager.Core.Pathstore;
using PathManager.Core.Shim;
using Xunit;

namespace PathManager.Tests;

public class DoctorTests : IDisposable
{
    private readonly MockEnvironmentProvider _mockEnv;
    private readonly PathStore _pathStore;
    private readonly CatalogManager _catalog;
    private readonly ShimManager _shim;
    private readonly DoctorService _doctor;

    public DoctorTests()
    {
        _mockEnv = new MockEnvironmentProvider();
        _pathStore = new PathStore(_mockEnv);
        _catalog = new CatalogManager(_mockEnv);
        _shim = new ShimManager(_mockEnv);
        _doctor = new DoctorService(_mockEnv, _pathStore, _catalog, _shim);
    }

    public void Dispose()
    {
        _mockEnv.Dispose();
    }

    [Fact]
    public async Task Doctor_DetectsGuiLimitWarning()
    {
        var longUserPath = _shim.ShimsDirectory + ";" + string.Join(";", Enumerable.Range(1, 100).Select(i => $@"C:\Tools\VeryLongPackagePathNameForTesting_{i}\bin"));
        _mockEnv.UserPath = longUserPath;

        var report = await _doctor.RunDiagnosticsAsync();

        Assert.True(report.IsUserOverGuiLimit);
        Assert.Equal(HealthStatus.Warning, report.Status);
    }

    [Fact]
    public async Task Doctor_DetectsDeadDirectoriesWithoutHangingOnUnc()
    {
        _mockEnv.UserPath = @"C:\NonExistentDir123;\\slow_unc\share\bin";

        var report = await _doctor.RunDiagnosticsAsync();

        Assert.NotEmpty(report.DeadDirectories);
        Assert.Contains(report.DeadDirectories, d => d.Path == @"C:\NonExistentDir123");
        Assert.Contains(report.DeadDirectories, d => d.IsUnc);
    }

    [Fact]
    public async Task Doctor_Repair_AddsMissingShimDirToUserPath()
    {
        _mockEnv.UserPath = @"C:\Bin;C:\Tools";

        var before = await _doctor.RunDiagnosticsAsync();
        Assert.False(before.IsShimDirOnUserPath);

        var after = await _doctor.RepairAsync();
        Assert.True(after.IsShimDirOnUserPath);
    }

    [Fact]
    public async Task Doctor_Repair_RemovesDeadDirsAndMachineDuplicates()
    {
        var existingDir = Path.Combine(_mockEnv.MockPmHome, "existing_bin");
        Directory.CreateDirectory(existingDir);
        _mockEnv.ExistingDirectories.Add(existingDir);

        _mockEnv.MachinePath = @"C:\Windows\System32;C:\Tools";
        _mockEnv.UserPath = $@"C:\Tools;C:\NonExistent_DeadDir123;{existingDir};{existingDir}";

        var after = await _doctor.RepairAsync();

        Assert.True(after.IsShimDirOnUserPath);
        Assert.NotEmpty(after.RepairsApplied);
        var userPath = _pathStore.GetUserPath();
        Assert.False(userPath.Contains(@"C:\NonExistent_DeadDir123"));
        Assert.False(userPath.Contains(@"C:\Tools")); // Removed because it exists in MachinePath
        Assert.True(userPath.Contains(existingDir));
        Assert.Equal(2, userPath.Count); // Shim dir + existingDir
    }
}
