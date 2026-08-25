using System;
using System.IO;
using PathManager.Core.Catalog;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using Xunit;

namespace PathManager.Tests;

public class CatalogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _catalogPath;
    private readonly CatalogManager _catalog;

    public CatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pm_cat_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _catalogPath = Path.Combine(_tempDir, "state.json");
        _catalog = new CatalogManager(_catalogPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Catalog_SaveAndLoad_RoundTripsAccurately()
    {
        var entry = new CommandEntry
        {
            Name = "mytool",
            Target = @"C:\Tools\mytool.py",
            Host = "py",
            HostPath = @"C:\Python\python.exe"
        };

        _catalog.AddOrUpdateCommand(entry);

        var loaded = _catalog.GetCommand("mytool");
        Assert.NotNull(loaded);
        Assert.Equal("mytool", loaded.Name);
        Assert.Equal(@"C:\Tools\mytool.py", loaded.Target);
        Assert.Equal("py", loaded.Host);
        Assert.Equal(@"C:\Python\python.exe", loaded.HostPath);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT2")]
    public void ValidateCommandName_RejectsReservedWindowsDeviceNames(string reserved)
    {
        var ex = Assert.Throws<InvalidCommandNameException>(() => CatalogManager.ValidateCommandName(reserved));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("tool;rm")]
    [InlineData("../escape")]
    [InlineData("tool<name>")]
    [InlineData("!invalid")]
    [InlineData("")]
    public void ValidateCommandName_RejectsIllegalCharactersAndTraversals(string invalid)
    {
        Assert.Throws<InvalidCommandNameException>(() => CatalogManager.ValidateCommandName(invalid));
    }

    [Fact]
    public void Catalog_RemoveCommand_DeletesEntryAndUpdatesFile()
    {
        _catalog.AddOrUpdateCommand(new CommandEntry { Name = "cmd1", Target = @"C:\a.exe" });
        _catalog.AddOrUpdateCommand(new CommandEntry { Name = "cmd2", Target = @"C:\b.exe" });

        Assert.Equal(2, _catalog.GetAllCommands().Count);

        var removed = _catalog.RemoveCommand("cmd1");
        Assert.True(removed);
        Assert.Single(_catalog.GetAllCommands());
        Assert.Null(_catalog.GetCommand("cmd1"));
    }
}
