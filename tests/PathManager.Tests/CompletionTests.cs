using System;
using System.IO;
using PathManager.Core.Catalog;
using PathManager.Core.Complete;
using PathManager.Core.Exceptions;
using PathManager.Core.Models;
using Xunit;

namespace PathManager.Tests;

public class CompletionTests : IDisposable
{
    private readonly MockEnvironmentProvider _env;
    private readonly CompletionManager _complete;

    public CompletionTests()
    {
        _env = new MockEnvironmentProvider();
        _complete = new CompletionManager(_env);
    }

    public void Dispose()
    {
        _env.Dispose();
    }

    [Fact]
    public void RegisterFile_CopiesPs1IntoCompletionsFolder()
    {
        var source = Path.Combine(_env.MockPmHome, "mytool.ps1");
        File.WriteAllText(source, "Register-ArgumentCompleter -CommandName mytool -ScriptBlock { }");

        var dest = _complete.RegisterFile(source);

        Assert.Equal(Path.Combine(_complete.CompletionsDirectory, "mytool.ps1"), dest);
        Assert.True(File.Exists(dest));
        Assert.Contains("Register-ArgumentCompleter", File.ReadAllText(dest), StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFile_UsesExplicitName()
    {
        var source = Path.Combine(_env.MockPmHome, "generated.ps1");
        File.WriteAllText(source, "# completer");

        var dest = _complete.RegisterFile(source, commandName: "gh");

        Assert.Equal(Path.Combine(_complete.CompletionsDirectory, "gh.ps1"), dest);
    }

    [Fact]
    public void RegisterFile_RefusesOverwriteWithoutForce()
    {
        var source = Path.Combine(_env.MockPmHome, "tool.ps1");
        File.WriteAllText(source, "# v1");
        _complete.RegisterFile(source);

        File.WriteAllText(source, "# v2");
        Assert.Throws<CompleterExistsException>(() => _complete.RegisterFile(source));
        Assert.Contains("# v1", File.ReadAllText(Path.Combine(_complete.CompletionsDirectory, "tool.ps1")), StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFile_ForceOverwrites()
    {
        var source = Path.Combine(_env.MockPmHome, "tool.ps1");
        File.WriteAllText(source, "# v1");
        _complete.RegisterFile(source);
        File.WriteAllText(source, "# v2");

        _complete.RegisterFile(source, force: true);

        Assert.Contains("# v2", File.ReadAllText(Path.Combine(_complete.CompletionsDirectory, "tool.ps1")), StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFile_MissingFile_Throws()
    {
        Assert.Throws<CompleterFileNotFoundException>(
            () => _complete.RegisterFile(Path.Combine(_env.MockPmHome, "missing.ps1")));
    }

    [Fact]
    public void RegisterFile_NonPs1_Throws()
    {
        var source = Path.Combine(_env.MockPmHome, "notes.txt");
        File.WriteAllText(source, "not a completer");
        Assert.Throws<CompleterUnsupportedFileException>(() => _complete.RegisterFile(source));
    }

    [Fact]
    public void RegisterFromCommand_WritesStdoutToCompletionsFolder()
    {
        var runner = new FakeCommandRunner
        {
            ExitCode = 0,
            Stdout = "# generated completer\n"
        };
        var complete = new CompletionManager(_env, runner);

        var dest = complete.RegisterFromCommand("someprogram completions pwsh", commandName: "someprogram");

        Assert.Equal("someprogram completions pwsh", runner.LastCommand);
        Assert.Equal(Path.Combine(complete.CompletionsDirectory, "someprogram.ps1"), dest);
        Assert.Equal("# generated completer\n", File.ReadAllText(dest));
    }

    [Fact]
    public void RegisterFromCommand_InfersNameFromFirstToken()
    {
        var runner = new FakeCommandRunner { Stdout = "# ok" };
        var complete = new CompletionManager(_env, runner);

        var dest = complete.RegisterFromCommand(@"C:\Tools\gh.exe completion powershell");

        Assert.Equal(Path.Combine(complete.CompletionsDirectory, "gh.ps1"), dest);
    }

    [Fact]
    public void RegisterFromCommand_NonZeroExit_Throws()
    {
        var runner = new FakeCommandRunner { ExitCode = 7, Stderr = "boom" };
        var complete = new CompletionManager(_env, runner);

        var ex = Assert.Throws<CompleterCommandFailedException>(
            () => complete.RegisterFromCommand("badtool --completions powershell"));
        Assert.Equal(7, ex.ExitCode);
        Assert.Contains("pathman doctor", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFromHelp_WritesNativeCompleterFromFlags()
    {
        var runner = new FakeCommandRunner
        {
            Stdout = """
                Usage: demo [options]
                  --json      JSON output
                  --force     overwrite
                  -h          help
                """
        };
        var complete = new CompletionManager(_env, runner);

        var dest = complete.RegisterFromHelp("demo");

        Assert.Equal("demo --help", runner.LastCommand);
        var script = File.ReadAllText(dest);
        Assert.Contains("Register-ArgumentCompleter -Native -CommandName 'demo'", script, StringComparison.Ordinal);
        Assert.Contains("'--json'", script, StringComparison.Ordinal);
        Assert.Contains("'--force'", script, StringComparison.Ordinal);
        Assert.Contains("'-h'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFromHelp_FallsBackToDashH_WhenHelpHasNoFlags()
    {
        var runner = new FakeCommandRunner
        {
            OnRun = cmd => cmd.EndsWith(" -h", StringComparison.Ordinal)
                ? new CommandCapture { ExitCode = 0, StandardOutput = "Usage: demo --verbose" }
                : new CommandCapture { ExitCode = 1, StandardError = "nope" }
        };
        var complete = new CompletionManager(_env, runner);

        var dest = complete.RegisterFromHelp("demo");

        Assert.Equal("demo -h", runner.LastCommand);
        Assert.Contains("'--verbose'", File.ReadAllText(dest), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpCompleterGenerator_ExtractsSubcommands()
    {
        var help = """
            Usage: demo <command>
            Commands:
              build    Compile the project
              test     Run tests
            Options:
              --watch  Rebuild on change
            """;

        var suggestions = HelpCompleterGenerator.ExtractSuggestions(help);

        Assert.Contains("--watch", suggestions);
        Assert.Contains("build", suggestions);
        Assert.Contains("test", suggestions);
    }

    [Fact]
    public void InferCommandName_UsesBasenameWithoutExtension()
    {
        Assert.Equal("someprogram", CompletionManager.InferCommandName("someprogram completions pwsh"));
        Assert.Equal("gh", CompletionManager.InferCommandName(@"""C:\Program Files\GitHub CLI\gh.exe"" completion powershell"));
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
        public string? LastCommand { get; private set; }
        public Func<string, CommandCapture>? OnRun { get; set; }

        public CommandCapture Run(string commandLine, TimeSpan timeout)
        {
            LastCommand = commandLine;
            if (OnRun != null)
            {
                return OnRun(commandLine);
            }

            return new CommandCapture
            {
                ExitCode = ExitCode,
                StandardOutput = Stdout,
                StandardError = Stderr
            };
        }
    }
}

public class CompletionCliTests : IDisposable
{
    private readonly MockEnvironmentProvider _mockEnv;

    public CompletionCliTests()
    {
        _mockEnv = new MockEnvironmentProvider();
    }

    public void Dispose()
    {
        _mockEnv.Dispose();
    }

    [Fact]
    public async Task Cli_RegisterFile_CopiesAndBindsCatalogCompleter()
    {
        var script = Path.Combine(_mockEnv.MockPmHome, "hello.py");
        File.WriteAllText(script, "print('hi')");
        _mockEnv.ExistingFiles.Add(script);

        var completer = Path.Combine(_mockEnv.MockPmHome, "hello.ps1");
        File.WriteAllText(completer, "# hello completer");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var linkExit = await PathManager.Cli.Program.RunWithEnvAsync(
            new[] { "link", script, "hello" }, _mockEnv, stdout, stderr);
        Assert.Equal(0, linkExit);

        stdout.GetStringBuilder().Clear();
        var regExit = await PathManager.Cli.Program.RunWithEnvAsync(
            new[] { "completions", "register-file", completer, "--name", "hello" },
            _mockEnv, stdout, stderr);
        Assert.Equal(0, regExit);
        Assert.Contains("Registered completions for hello", stdout.ToString(), StringComparison.Ordinal);

        var dest = Path.Combine(_mockEnv.MockPmHome, "completions", "hello.ps1");
        Assert.True(File.Exists(dest));

        var catalog = new CatalogManager(_mockEnv);
        var entry = catalog.GetCommand("hello");
        Assert.NotNull(entry);
        Assert.Equal("completions/hello.ps1", entry!.Completer);
    }

    [Fact]
    public async Task Cli_RegisterFileJson_ReturnsPath()
    {
        var completer = Path.Combine(_mockEnv.MockPmHome, "tool.ps1");
        File.WriteAllText(completer, "# tool");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await PathManager.Cli.Program.RunWithEnvAsync(
            new[] { "completions", "register-file", completer, "--json" },
            _mockEnv, stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("\"source\": \"file\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("tool.ps1", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_RegisterCommand_CapturesGeneratorStdout()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await PathManager.Cli.Program.RunWithEnvAsync(
            new[] { "completions", "register-command", "Write-Output '# live completer'", "--name", "livecmd" },
            _mockEnv, stdout, stderr);

        Assert.Equal(0, exit);
        var dest = Path.Combine(_mockEnv.MockPmHome, "completions", "livecmd.ps1");
        Assert.True(File.Exists(dest));
        Assert.Contains("# live completer", File.ReadAllText(dest), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_RegisterCommand_KeepsGeneratorFlagsLikeShell()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await PathManager.Cli.Program.RunWithEnvAsync(
            new[] { "completions", "register-command", "Write-Output", "--shell", "powershell", "--name", "atuin" },
            _mockEnv, stdout, stderr);

        Assert.Equal(0, exit);
        var dest = Path.Combine(_mockEnv.MockPmHome, "completions", "atuin.ps1");
        Assert.True(File.Exists(dest));
        var body = File.ReadAllText(dest);
        Assert.Contains("--shell", body, StringComparison.Ordinal);
        Assert.Contains("powershell", body, StringComparison.Ordinal);
    }
}
