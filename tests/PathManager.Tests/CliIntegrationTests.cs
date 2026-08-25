using System;
using System.IO;
using System.Threading.Tasks;
using PathManager.Cli;
using Xunit;

namespace PathManager.Tests;

public class CliIntegrationTests : IDisposable
{
    private readonly MockEnvironmentProvider _mockEnv;
    private readonly string _tempScriptPath;

    public CliIntegrationTests()
    {
        _mockEnv = new MockEnvironmentProvider();
        _tempScriptPath = Path.Combine(_mockEnv.MockPmHome, "sample.py");
        File.WriteAllText(_tempScriptPath, "print('hello from test')");
        _mockEnv.ExistingFiles.Add(_tempScriptPath);
    }

    public void Dispose()
    {
        _mockEnv.Dispose();
    }

    [Fact]
    public async Task Cli_LinkAndListAndUnlink_FullWorkflow()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // 1. Link
        var linkExit = await Program.RunWithEnvAsync(new[] { "link", _tempScriptPath, "sampletool" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, linkExit);
        Assert.Contains("linked sampletool", stdout.ToString());

        // 2. List
        stdout.GetStringBuilder().Clear();
        var listExit = await Program.RunWithEnvAsync(new[] { "list" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, listExit);
        Assert.Contains("sampletool", stdout.ToString());

        // 3. Why
        stdout.GetStringBuilder().Clear();
        var whyExit = await Program.RunWithEnvAsync(new[] { "why", "sampletool" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, whyExit);
        Assert.Contains("PathManager Shim", stdout.ToString());

        // 4. Unlink
        stdout.GetStringBuilder().Clear();
        var unlinkExit = await Program.RunWithEnvAsync(new[] { "unlink", "sampletool" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, unlinkExit);
        Assert.Contains("unlinked sampletool", stdout.ToString());

        // 5. List again (empty)
        stdout.GetStringBuilder().Clear();
        await Program.RunWithEnvAsync(new[] { "list" }, _mockEnv, stdout, stderr);
        Assert.Contains("No linked commands", stdout.ToString());
    }

    [Fact]
    public async Task Cli_DoctorJson_ReturnsValidJson()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await Program.RunWithEnvAsync(new[] { "doctor", "--json" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, exit);

        var output = stdout.ToString();
        Assert.Contains("\"UserPathLength\":", output);
        Assert.Contains("\"Status\":", output);
    }

    [Fact]
    public async Task Cli_PathAddAndRemove_WorksSafely()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Add
        var addExit = await Program.RunWithEnvAsync(new[] { "path", "add", @"C:\CustomTestDir" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, addExit);
        Assert.Contains("Added 'C:\\CustomTestDir'", stdout.ToString());

        // Remove
        stdout.GetStringBuilder().Clear();
        var rmExit = await Program.RunWithEnvAsync(new[] { "path", "remove", @"C:\CustomTestDir" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, rmExit);
        Assert.Contains("Removed 'C:\\CustomTestDir'", stdout.ToString());
    }
}
