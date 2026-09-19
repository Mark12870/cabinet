using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class UnusedRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null,
        IReadOnlySet<string>? blankEnvironment = null,
        bool inheritStdin = false,
        CancellationToken cancellationToken = default) =>
        args switch
        {
            [Prefixes.PathsMode] =>
                new ProcessResult(0, SessionFiles.Printed(env ?? new Dictionary<string, string>()), ""),
            [Prefixes.SessionMode] => new ProcessResult(1, "", ""),
            _ => throw new NotSupportedException(
                $"this operation should run no process, got '{file}'"),
        };
}
