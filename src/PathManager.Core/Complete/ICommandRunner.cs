using System;

namespace PathManager.Core.Complete;

public sealed class CommandCapture
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

public interface ICommandRunner
{
    CommandCapture Run(string commandLine, TimeSpan timeout);
}
