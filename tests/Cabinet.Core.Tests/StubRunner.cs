using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class StubRunner(ProcessResult result) : IProcessRunner
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
        args is [Prefixes.PathsMode]
            ? new ProcessResult(0, SessionFiles.Printed(env ?? new Dictionary<string, string>()), "")
            : result;
}
